using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DividendReconciliationService
{
    public IReadOnlyList<DividendPayment> FindActualCandidates(
        DividendPayment planned, IEnumerable<DividendPayment> payments)
    {
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(payments);
        if (!DividendConstants.IsUnconfirmed(planned.DividendStatus)) return [];
        return payments.Where(x => x.Id > 0 && DividendConstants.IsVisibleActual(x.DividendStatus))
            .Where(x => IsSameSecurity(planned, x) &&
                        string.Equals(Normalize(planned.Broker), Normalize(x.Broker), StringComparison.OrdinalIgnoreCase))
            .Where(x =>
            {
                var plannedAccount = NormalizeAccount(planned.AccountType);
                var actualAccount = NormalizeAccount(x.AccountType);
                var plannedTaxAccount = NormalizeAccount(planned.TaxAccountType);
                var actualTaxAccount = NormalizeAccount(x.TaxAccountType);
                return (plannedAccount == DividendConstants.AccountUnknown ||
                        actualAccount == DividendConstants.AccountUnknown || plannedAccount == actualAccount) &&
                       (plannedTaxAccount == DividendConstants.AccountUnknown ||
                        actualTaxAccount == DividendConstants.AccountUnknown ||
                        plannedTaxAccount == actualTaxAccount);
            })
            .Where(x => Math.Abs((planned.PaymentDate.Date - x.PaymentDate.Date).TotalDays) <= 30 ||
                        planned.RecordDate != DateTime.MinValue && planned.RecordDate.Date == x.RecordDate.Date ||
                        planned.ExDividendDate != DateTime.MinValue && planned.ExDividendDate.Date == x.ExDividendDate.Date)
            .OrderBy(x => Math.Abs((planned.PaymentDate.Date - x.PaymentDate.Date).TotalDays))
            .ThenBy(x => x.Id)
            .ToArray();
    }

    public DividendReconciliationDecision ReconcileActual(
        IEnumerable<DividendPayment> existingPayments,
        DividendPayment actualPayment)
    {
        ArgumentNullException.ThrowIfNull(existingPayments);
        ArgumentNullException.ThrowIfNull(actualPayment);

        var existing = existingPayments.ToList();
        var duplicateActual = existing
            .Where(x => DividendConstants.IsActual(x.DividendStatus))
            .Where(x => IsSameActualIdentity(x, actualPayment))
            .OrderByDescending(x => x.SourcePriority)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefault();

        var scheduleCandidates = existing
            .Where(x => x.Id != duplicateActual?.Id)
            .Where(x => DividendConstants.IsUnconfirmed(x.DividendStatus) || IsManualActual(x))
            .Where(x => IsLikelySamePlannedEvent(x, actualPayment))
            .OrderByDescending(x => x.SourcePriority)
            .ThenByDescending(x => x.UpdatedAt)
            .ToList();

        var replaceTargets = scheduleCandidates.Count == 1 &&
                             CanReplaceAutomatically(scheduleCandidates[0], actualPayment)
            ? scheduleCandidates
            : new List<DividendPayment>();

        return new DividendReconciliationDecision
        {
            DuplicateActual = duplicateActual,
            ReplaceTargets = replaceTargets,
            ReviewCandidates = replaceTargets.Count == 0 ? scheduleCandidates : Array.Empty<DividendPayment>()
        };
    }

    private static bool IsLikelySamePlannedEvent(DividendPayment planned, DividendPayment actual)
    {
        if (!IsSameSecurity(planned, actual) ||
            !string.Equals(Normalize(planned.Broker), Normalize(actual.Broker), StringComparison.OrdinalIgnoreCase))
            return false;

        var plannedAccount = NormalizeAccount(planned.AccountType);
        var actualAccount = NormalizeAccount(actual.AccountType);
        if (plannedAccount != DividendConstants.AccountUnknown &&
            actualAccount != DividendConstants.AccountUnknown && plannedAccount != actualAccount)
            return false;

        if (Math.Abs((planned.PaymentDate.Date - actual.PaymentDate.Date).TotalDays) > 10 ||
            (planned.Quantity > 0m && actual.Quantity > 0m && planned.Quantity != actual.Quantity))
            return false;

        var plannedJpy = planned.NetAmountJpy > 0m ? planned.NetAmountJpy : planned.JpyAmount;
        var actualJpy = actual.NetAmountJpy > 0m ? actual.NetAmountJpy : actual.JpyAmount;
        if (plannedJpy <= 0m || actualJpy <= 0m)
            return false;
        return Math.Abs(plannedJpy - actualJpy) / Math.Max(plannedJpy, actualJpy) <= 0.30m;
    }

    private static bool CanReplaceAutomatically(DividendPayment planned, DividendPayment actual)
    {
        // An unknown account can match more than one taxable/NISA holding. A unique,
        // exact manual actual may still be replaced by the broker's CSV actual.
        var plannedAccount = NormalizeAccount(planned.AccountType);
        var actualAccount = NormalizeAccount(actual.AccountType);
        if (plannedAccount == DividendConstants.AccountUnknown ||
            actualAccount == DividendConstants.AccountUnknown ||
            plannedAccount != actualAccount)
            return plannedAccount == DividendConstants.AccountUnknown &&
                   actualAccount == DividendConstants.AccountUnknown &&
                   IsManualActual(planned) &&
                   string.Equals(actual.Source, DividendConstants.SourceCsv, StringComparison.OrdinalIgnoreCase) &&
                   planned.PaymentDate.Date == actual.PaymentDate.Date &&
                   NormalizeCurrency(planned.Currency) == NormalizeCurrency(actual.Currency) &&
                   planned.GrossAmount > 0m && planned.GrossAmount == actual.GrossAmount &&
                   planned.NetAmount > 0m && planned.NetAmount == actual.NetAmount &&
                   planned.NetAmountJpy > 0m && planned.NetAmountJpy == actual.NetAmountJpy;
        var plannedJpy = planned.NetAmountJpy > 0m ? planned.NetAmountJpy : planned.JpyAmount;
        var actualJpy = actual.NetAmountJpy > 0m ? actual.NetAmountJpy : actual.JpyAmount;
        if (planned.PaymentDate.Date == actual.PaymentDate.Date && plannedJpy == actualJpy)
            return true;

        // Broker settlement dates and converted yen amounts can differ from a generated
        // estimate. Manual plans and records without quantities still need review.
        var generated = planned.Source == DividendConstants.SourceEstimatedFromHistory ||
                        planned.Source == DividendConstants.SourceEstimatedFromAnnualDividend ||
                        planned.Source.StartsWith(DividendConstants.SourcePublicCalendar, StringComparison.OrdinalIgnoreCase);
        return generated && DividendConstants.IsUnconfirmed(planned.DividendStatus) &&
               planned.Quantity > 0m && actual.Quantity > 0m;
    }

    private static bool IsManualActual(DividendPayment payment) =>
        DividendConstants.IsActual(payment.DividendStatus) &&
        string.Equals(payment.Source, DividendConstants.SourceManual, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameActualIdentity(DividendPayment left, DividendPayment right)
    {
        if (left.SourceRowNumber > 0 && right.SourceRowNumber > 0 &&
            string.Equals(left.SourceFile, right.SourceFile, StringComparison.OrdinalIgnoreCase) &&
            left.SourceRowNumber != right.SourceRowNumber)
            return false;

        if (left.PaymentDate.Date != right.PaymentDate.Date ||
            left.Quantity != right.Quantity ||
            !string.Equals(Normalize(left.Broker), Normalize(right.Broker), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(NormalizeAccount(left.AccountType), NormalizeAccount(right.AccountType), StringComparison.OrdinalIgnoreCase) ||
            !IsSameSecurity(left, right))
            return false;

        // Broker CSV exports may describe one foreign dividend in USD and in rounded JPY.
        // Only the same broker/account/security/date/quantity can be reconciled this way.
        if (string.Equals(left.Source, DividendConstants.SourceCsv, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(right.Source, DividendConstants.SourceCsv, StringComparison.OrdinalIgnoreCase) &&
            (left.SourceRowNumber > 0 || right.SourceRowNumber > 0))
        {
            var leftJpy = left.NetAmountJpy > 0m ? left.NetAmountJpy : left.JpyAmount;
            var rightJpy = right.NetAmountJpy > 0m ? right.NetAmountJpy : right.JpyAmount;
            return leftJpy > 0m && rightJpy > 0m && IsClose(leftJpy, rightJpy, 10m);
        }

        return left.StockId == right.StockId &&
               left.GrossAmount == right.GrossAmount &&
               left.NetAmount == right.NetAmount &&
               left.IsNetOnly == right.IsNetOnly &&
               IsSameDividendIdentity(left, right);
    }

    private static bool IsSameDividendIdentity(DividendPayment left, DividendPayment right)
    {
        if (!IsSameSecurity(left, right))
        {
            return false;
        }

        if (!string.Equals(Normalize(left.Broker), Normalize(right.Broker), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(NormalizeAccount(left.AccountType), NormalizeAccount(right.AccountType), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(NormalizeCurrency(left.Currency), NormalizeCurrency(right.Currency), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Math.Abs((left.PaymentDate.Date - right.PaymentDate.Date).TotalDays) > 10)
        {
            return false;
        }

        var leftJpy = left.NetAmountJpy > 0m ? left.NetAmountJpy : left.JpyAmount;
        var rightJpy = right.NetAmountJpy > 0m ? right.NetAmountJpy : right.JpyAmount;
        if (leftJpy > 0m && rightJpy > 0m)
        {
            return IsClose(leftJpy, rightJpy, 10m);
        }

        return IsClose(left.NetAmount, right.NetAmount, NormalizeCurrency(right.Currency) == "JPY" ? 10m : 0.05m);
    }

    private static bool IsClose(decimal left, decimal right, decimal absoluteTolerance)
    {
        var diff = Math.Abs(left - right);
        if (diff <= absoluteTolerance)
        {
            return true;
        }

        var denominator = Math.Max(Math.Abs(left), Math.Abs(right));
        return denominator > 0m && diff / denominator <= 0.01m;
    }

    private static bool IsSameSecurity(DividendPayment left, DividendPayment right) =>
        left.StockId == right.StockId ||
        (!string.IsNullOrWhiteSpace(left.Ticker) &&
         !string.IsNullOrWhiteSpace(right.Ticker) &&
         string.Equals(SecuritySymbolNormalizer.NormalizeTicker(left.Ticker),
             SecuritySymbolNormalizer.NormalizeTicker(right.Ticker), StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value) => SecuritySymbolNormalizer.NormalizeBroker(value ?? string.Empty);

    private static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "円" ? "JPY" : normalized;
    }

    private static string NormalizeAccount(string accountType) =>
        DividendConstants.NormalizeAccountType(accountType);
}

public sealed class DividendReconciliationDecision
{
    public DividendPayment? DuplicateActual { get; init; }
    public IReadOnlyList<DividendPayment> ReplaceTargets { get; init; } = Array.Empty<DividendPayment>();
    public IReadOnlyList<DividendPayment> ReviewCandidates { get; init; } = Array.Empty<DividendPayment>();
}
