using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DividendReconciliationService
{
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
            .Where(x => IsSameDividendIdentity(x, actualPayment))
            .OrderByDescending(x => x.SourcePriority)
            .ThenByDescending(x => x.UpdatedAt)
            .ToList();

        var replaceTargets = scheduleCandidates.Count == 1 &&
                             IsExactScheduleMatch(scheduleCandidates[0], actualPayment)
            ? scheduleCandidates
            : new List<DividendPayment>();

        return new DividendReconciliationDecision
        {
            DuplicateActual = duplicateActual,
            ReplaceTargets = replaceTargets,
            ReviewCandidates = replaceTargets.Count == 0 ? scheduleCandidates : Array.Empty<DividendPayment>()
        };
    }

    private static bool IsExactScheduleMatch(DividendPayment planned, DividendPayment actual)
    {
        if (planned.PaymentDate.Date != actual.PaymentDate.Date ||
            (planned.Quantity > 0m && actual.Quantity > 0m && planned.Quantity != actual.Quantity))
            return false;

        var plannedJpy = planned.NetAmountJpy > 0m ? planned.NetAmountJpy : planned.JpyAmount;
        var actualJpy = actual.NetAmountJpy > 0m ? actual.NetAmountJpy : actual.JpyAmount;
        return plannedJpy > 0m && actualJpy > 0m
            ? plannedJpy == actualJpy
            : planned.NetAmount > 0m && actual.NetAmount > 0m &&
              planned.NetAmount == actual.NetAmount;
    }

    private static bool IsManualActual(DividendPayment payment) =>
        DividendConstants.IsActual(payment.DividendStatus) &&
        string.Equals(payment.Source, DividendConstants.SourceManual, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameActualIdentity(DividendPayment left, DividendPayment right) =>
        !(left.SourceRowNumber > 0 && right.SourceRowNumber > 0 &&
          string.Equals(left.SourceFile, right.SourceFile, StringComparison.OrdinalIgnoreCase) &&
          left.SourceRowNumber != right.SourceRowNumber) &&
        left.PaymentDate.Date == right.PaymentDate.Date &&
        left.Quantity == right.Quantity &&
        left.GrossAmount == right.GrossAmount &&
        left.NetAmount == right.NetAmount &&
        left.IsNetOnly == right.IsNetOnly &&
        IsSameDividendIdentity(left, right);

    private static bool IsSameDividendIdentity(DividendPayment left, DividendPayment right)
    {
        if (left.StockId != right.StockId)
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

    private static string Normalize(string value) => value.Trim();

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
