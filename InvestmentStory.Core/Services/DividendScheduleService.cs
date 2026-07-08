using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DividendScheduleService
{
    private readonly DividendTaxCalculator _taxCalculator = new();

    public DividendScheduleUpdateResult BuildSchedules(
        IEnumerable<StockPosition> positions,
        IEnumerable<DividendPayment> existingPayments,
        IEnumerable<TaxProfile> taxProfiles,
        DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(existingPayments);
        ArgumentNullException.ThrowIfNull(taxProfiles);

        var existing = existingPayments.ToList();
        var profiles = taxProfiles.ToList();
        var schedules = new List<DividendPayment>();
        var created = 0;
        var updated = 0;
        var paymentDue = 0;

        foreach (var position in positions.Where(IsScheduleTarget))
        {
            var months = ResolveDividendMonths(position, existing, asOf);
            if (months.Count == 0)
            {
                continue;
            }

            var accountType = ResolveAccountType(position.Stock.Broker);
            var perPaymentDividend = ResolveDividendPerShare(position, existing, months.Count);
            var profile = ResolveTaxProfile(position, profiles, accountType);
            var paymentYear = asOf.Year;

            foreach (var month in months)
            {
                var paymentDate = new DateTime(paymentYear, month, Math.Min(20, DateTime.DaysInMonth(paymentYear, month)));
                var status = paymentDate.Date < asOf.Date ? DividendConstants.PaymentDue : DividendConstants.Estimated;
                var existingSchedule = existing
                    .Where(x => x.StockId == position.Stock.Id)
                    .Where(x => DividendConstants.IsUnconfirmed(x.DividendStatus))
                    .FirstOrDefault(x => x.PaymentDate.Year == paymentDate.Year && x.PaymentDate.Month == paymentDate.Month);

                var schedule = existingSchedule ?? new DividendPayment();
                var calculation = _taxCalculator.Calculate(new DividendTaxInput
                {
                    Quantity = position.CurrentHolding.CurrentShares,
                    DividendPerShare = perPaymentDividend,
                    Currency = position.Stock.Currency,
                    ExchangeRate = position.CurrentHolding.CurrentExchangeRate,
                    TaxProfile = profile
                });

                schedule.StockId = position.Stock.Id;
                schedule.StockName = position.Stock.Name;
                schedule.Ticker = position.Stock.Ticker;
                schedule.Broker = position.Stock.Broker;
                schedule.AccountType = accountType;
                schedule.TaxAccountType = accountType;
                schedule.PaymentDate = paymentDate;
                schedule.FiscalYear = paymentDate.Year;
                schedule.FiscalQuarter = $"Q{((paymentDate.Month - 1) / 3) + 1}";
                schedule.DividendStatus = status;
                schedule.Source = HasHistory(position.Stock.Id, existing)
                    ? DividendConstants.SourceEstimatedFromHistory
                    : DividendConstants.SourceEstimatedFromAnnualDividend;
                schedule.SourcePriority = 20;
                schedule.Quantity = position.CurrentHolding.CurrentShares;
                schedule.DividendPerShare = perPaymentDividend;
                schedule.GrossAmount = calculation.GrossAmount;
                schedule.ForeignTaxAmount = calculation.ForeignTaxAmount;
                schedule.DomesticTaxAmount = calculation.DomesticTaxAmount;
                schedule.TotalTaxAmount = calculation.TotalTaxAmount;
                schedule.TaxAmount = calculation.TotalTaxAmount;
                schedule.NetAmount = calculation.NetAmount;
                schedule.Currency = NormalizeCurrency(position.Stock.Currency);
                schedule.ExchangeRate = NormalizeExchangeRate(position.Stock.Currency, position.CurrentHolding.CurrentExchangeRate);
                schedule.ExchangeRateAcquiredAt = position.CurrentHolding.ExchangeRateAcquiredAt == default
                    ? asOf
                    : position.CurrentHolding.ExchangeRateAcquiredAt;
                schedule.ExchangeRateSource = string.IsNullOrWhiteSpace(position.CurrentHolding.ExchangeRateSource)
                    ? "現在保有情報"
                    : position.CurrentHolding.ExchangeRateSource;
                schedule.ExchangeRateInputType = string.IsNullOrWhiteSpace(position.CurrentHolding.ExchangeRateInputType)
                    ? "手入力"
                    : position.CurrentHolding.ExchangeRateInputType;
                schedule.GrossAmountJpy = calculation.GrossAmountJpy;
                schedule.ForeignTaxAmountJpy = calculation.ForeignTaxAmountJpy;
                schedule.DomesticTaxAmountJpy = calculation.DomesticTaxAmountJpy;
                schedule.TotalTaxAmountJpy = calculation.TotalTaxAmountJpy;
                schedule.NetAmountJpy = calculation.NetAmountJpy;
                schedule.JpyAmount = calculation.NetAmountJpy;
                schedule.IsTaxEstimated = true;
                schedule.IsNisa = accountType == DividendConstants.AccountNisa;
                schedule.IsForeignStock = !IsJpy(position.Stock.Currency);
                schedule.TaxProfileId = profile.Id == 0 ? null : profile.Id;
                schedule.UpdatedAt = DateTime.Now;
                schedule.Memo = "配当予定の概算です。実際の入金額は証券会社CSVを正とします。";

                if (schedule.Id == 0)
                {
                    schedule.CreatedAt = DateTime.Now;
                    created++;
                }
                else
                {
                    updated++;
                }

                if (status == DividendConstants.PaymentDue)
                {
                    paymentDue++;
                }

                schedules.Add(schedule);
            }
        }

        return new DividendScheduleUpdateResult
        {
            Schedules = schedules,
            CreatedCount = created,
            UpdatedCount = updated,
            PaymentDueCount = paymentDue
        };
    }

    private static bool IsScheduleTarget(StockPosition position) =>
        position.CurrentHolding.CurrentShares > 0m &&
        position.CurrentHolding.AnnualDividendPerShare > 0m &&
        !string.Equals(position.CurrentHolding.DividendStatus, "配当なし", StringComparison.Ordinal);

    private static IReadOnlyList<int> ResolveDividendMonths(
        StockPosition position,
        IReadOnlyList<DividendPayment> existing,
        DateTime asOf)
    {
        var configuredMonths = ParseMonths(position.CurrentHolding.DividendMonths);
        if (configuredMonths.Count > 0)
        {
            return configuredMonths;
        }

        var historyMonths = existing
            .Where(x => x.StockId == position.Stock.Id)
            .Where(x => DividendConstants.IsActual(x.DividendStatus))
            .Where(x => x.PaymentDate >= asOf.AddYears(-2))
            .Select(x => x.PaymentDate.Month)
            .ToList();

        var existingScheduleMonths = existing
            .Where(x => x.StockId == position.Stock.Id)
            .Where(x => DividendConstants.IsUnconfirmed(x.DividendStatus))
            .Select(x => x.PaymentDate.Month)
            .ToList();

        var inferredMonths = historyMonths
            .Concat(existingScheduleMonths)
            .Distinct()
            .Order()
            .ToList();
        if (inferredMonths.Count > 0)
        {
            return inferredMonths;
        }

        return FrequencyToMonths(position.CurrentHolding.DividendFrequency);
    }

    private static decimal ResolveDividendPerShare(
        StockPosition position,
        IReadOnlyList<DividendPayment> existing,
        int paymentCount)
    {
        var latestActual = existing
            .Where(x => x.StockId == position.Stock.Id)
            .Where(x => DividendConstants.IsActual(x.DividendStatus))
            .Where(x => x.DividendPerShare > 0m)
            .OrderByDescending(x => x.PaymentDate)
            .FirstOrDefault();
        if (latestActual is not null)
        {
            return latestActual.DividendPerShare;
        }

        return paymentCount <= 0 ? 0m : position.CurrentHolding.AnnualDividendPerShare / paymentCount;
    }

    private static bool HasHistory(int stockId, IReadOnlyList<DividendPayment> existing) =>
        existing.Any(x => x.StockId == stockId && DividendConstants.IsActual(x.DividendStatus));

    private static TaxProfile ResolveTaxProfile(StockPosition position, IReadOnlyList<TaxProfile> taxProfiles, string accountType)
    {
        var currency = NormalizeCurrency(position.Stock.Currency);
        return taxProfiles.FirstOrDefault(x =>
                   string.Equals(x.Currency, currency, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.AccountType, accountType, StringComparison.OrdinalIgnoreCase)) ??
               taxProfiles.FirstOrDefault(x =>
                   string.Equals(x.Currency, currency, StringComparison.OrdinalIgnoreCase)) ??
               new TaxProfile
               {
                   Currency = currency,
                   Country = position.Stock.Country,
                   AccountType = accountType,
                   TotalDomesticTaxRate = accountType == DividendConstants.AccountNisa ? 0m : 20.315m,
                   ForeignWithholdingTaxRate = currency == "USD" ? 10m : 0m,
                   IsDomesticTaxExempt = accountType == DividendConstants.AccountNisa,
                   IsForeignTaxExempt = currency == "JPY"
               };
    }

    private static string ResolveAccountType(string broker)
    {
        _ = broker;
        return DividendConstants.AccountSpecific;
    }

    private static IReadOnlyList<int> ParseMonths(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<int>();
        }

        return value.Split(new[] { ',', '/', '、', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x.Trim().Replace("月", string.Empty, StringComparison.Ordinal), out var month) ? month : 0)
            .Where(x => x is >= 1 and <= 12)
            .Distinct()
            .Order()
            .ToList();
    }

    private static IReadOnlyList<int> FrequencyToMonths(string frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency))
        {
            return Array.Empty<int>();
        }

        if (frequency.Contains("4", StringComparison.Ordinal))
        {
            return new[] { 3, 6, 9, 12 };
        }

        if (frequency.Contains("2", StringComparison.Ordinal))
        {
            return new[] { 6, 12 };
        }

        if (frequency.Contains("1", StringComparison.Ordinal))
        {
            return new[] { 12 };
        }

        return Array.Empty<int>();
    }

    private static decimal NormalizeExchangeRate(string currency, decimal exchangeRate) =>
        IsJpy(currency) ? 1m : exchangeRate <= 0m ? 1m : exchangeRate;

    private static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "円" ? "JPY" : normalized;
    }

    private static bool IsJpy(string currency) => NormalizeCurrency(currency) == "JPY";
}

public sealed class DividendScheduleUpdateResult
{
    public IReadOnlyList<DividendPayment> Schedules { get; init; } = Array.Empty<DividendPayment>();
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int PaymentDueCount { get; init; }
}
