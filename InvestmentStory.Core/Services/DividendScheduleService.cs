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

        var existing = existingPayments
            .Where(x => !string.Equals(x.DividendStatus, DividendConstants.Replaced, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var profiles = taxProfiles.ToList();
        var schedules = new List<DividendPayment>();
        var generatedKeys = new HashSet<ScheduleKey>();
        var created = 0;
        var updated = 0;
        var paymentDue = 0;

        foreach (var position in positions.Where(IsScheduleTarget))
        {
            var actualHistory = GetActualHistory(position, existing, asOf);
            var months = ResolveDividendMonths(position, actualHistory, existing);
            if (months.Count == 0)
            {
                continue;
            }

            var accountType = ResolveAccountType(position);
            var profile = ResolveTaxProfile(position, profiles, accountType);
            var paymentYear = asOf.Year;
            var reliableActuals = actualHistory
                .Where(x => IsPlausibleDividendPerShare(position, x.DividendPerShare, months.Count))
                .ToList();

            foreach (var month in months)
            {
                var paymentDate = ResolvePaymentDate(actualHistory, existing, position.Stock.Id, paymentYear, month);
                if (HasActualPaymentForMonth(position.Stock.Id, existing, paymentDate.Year, paymentDate.Month))
                {
                    continue;
                }

                var perPaymentDividend = ResolveDividendPerShare(position, reliableActuals, month, months.Count);
                if (perPaymentDividend <= 0m)
                {
                    continue;
                }

                var status = paymentDate.Date < asOf.Date ? DividendConstants.PaymentDue : DividendConstants.Estimated;
                var existingSchedule = existing
                    .Where(x => x.StockId == position.Stock.Id)
                    .Where(IsGeneratedSchedule)
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
                schedule.Source = actualHistory.Count > 0
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
                schedule.IsNisa = DividendConstants.IsNisaAccount(accountType);
                schedule.IsForeignStock = !IsJpy(position.Stock.Currency);
                schedule.TaxProfileId = profile.Id == 0 ? null : profile.Id;
                schedule.UpdatedAt = DateTime.Now;
                schedule.Memo = actualHistory.Count > 0
                    ? "過去の配当実績から推定した配当予定です。実際の入金額は証券会社CSVを正とします。"
                    : "年間配当情報から推定した配当予定です。実際の入金額は証券会社CSVを正とします。";

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
                generatedKeys.Add(new ScheduleKey(position.Stock.Id, paymentDate.Year, paymentDate.Month));
            }
        }

        var obsoleteScheduleIds = existing
            .Where(IsGeneratedSchedule)
            .Where(x => x.PaymentDate.Year == asOf.Year)
            .Where(x => !generatedKeys.Contains(new ScheduleKey(x.StockId, x.PaymentDate.Year, x.PaymentDate.Month)))
            .Select(x => x.Id)
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        return new DividendScheduleUpdateResult
        {
            Schedules = schedules,
            CreatedCount = created,
            UpdatedCount = updated,
            PaymentDueCount = paymentDue,
            ObsoleteScheduleIds = obsoleteScheduleIds
        };
    }

    private static bool IsScheduleTarget(StockPosition position) =>
        !position.IsMutualFund &&
        position.CurrentHolding.CurrentShares > 0m &&
        position.CurrentHolding.AnnualDividendPerShare > 0m &&
        !IsNoDividendStatus(position.CurrentHolding.DividendStatus);

    private static bool IsNoDividendStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Contains("配当なし", StringComparison.Ordinal) ||
               status.Contains("なし", StringComparison.Ordinal);
    }

    private static IReadOnlyList<DividendPayment> GetActualHistory(
        StockPosition position,
        IReadOnlyList<DividendPayment> existing,
        DateTime asOf)
    {
        var from = asOf.AddYears(-5);
        return existing
            .Where(x => x.StockId == position.Stock.Id)
            .Where(x => DividendConstants.IsActual(x.DividendStatus))
            .Where(x => x.PaymentDate >= from && x.PaymentDate <= asOf.AddYears(1))
            .OrderByDescending(x => x.PaymentDate)
            .ToList();
    }

    private static IReadOnlyList<int> ResolveDividendMonths(
        StockPosition position,
        IReadOnlyList<DividendPayment> actualHistory,
        IReadOnlyList<DividendPayment> existing)
    {
        var configuredMonths = ParseMonths(position.CurrentHolding.DividendMonths);
        if (configuredMonths.Count > 0)
        {
            return configuredMonths;
        }

        var frequencyMonths = FrequencyToMonths(position.CurrentHolding.DividendFrequency);
        if (frequencyMonths.Count > 0)
        {
            return frequencyMonths;
        }

        var historyMonths = actualHistory
            .Select(x => x.PaymentDate.Month)
            .Distinct()
            .Order()
            .ToList();
        if (historyMonths.Count > 0)
        {
            return historyMonths;
        }

        return existing
            .Where(x => x.StockId == position.Stock.Id)
            .Where(IsGeneratedSchedule)
            .Select(x => x.PaymentDate.Month)
            .Distinct()
            .Order()
            .ToList();
    }

    private static DateTime ResolvePaymentDate(
        IReadOnlyList<DividendPayment> actualHistory,
        IReadOnlyList<DividendPayment> existing,
        int stockId,
        int year,
        int month)
    {
        var sameMonthDay = actualHistory
            .Where(x => x.PaymentDate.Month == month)
            .OrderByDescending(x => x.PaymentDate)
            .Select(x => x.PaymentDate.Day)
            .FirstOrDefault();
        if (sameMonthDay > 0)
        {
            return SafeDate(year, month, sameMonthDay);
        }

        var commonHistoryDay = actualHistory
            .Where(x => x.PaymentDate != default)
            .GroupBy(x => x.PaymentDate.Day)
            .OrderByDescending(x => x.Count())
            .ThenByDescending(x => x.Max(y => y.PaymentDate))
            .Select(x => x.Key)
            .FirstOrDefault();
        if (commonHistoryDay > 0)
        {
            return SafeDate(year, month, commonHistoryDay);
        }

        var existingDay = existing
            .Where(x => x.StockId == stockId)
            .Where(IsGeneratedSchedule)
            .Where(x => x.PaymentDate.Month == month)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => x.PaymentDate.Day)
            .FirstOrDefault();

        return SafeDate(year, month, existingDay > 0 ? existingDay : 20);
    }

    private static DateTime SafeDate(int year, int month, int day) =>
        new(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));

    private static decimal ResolveDividendPerShare(
        StockPosition position,
        IReadOnlyList<DividendPayment> reliableActuals,
        int month,
        int paymentCount)
    {
        var sameMonthActual = reliableActuals
            .Where(x => x.PaymentDate.Month == month)
            .OrderByDescending(x => x.PaymentDate)
            .FirstOrDefault();
        if (sameMonthActual is not null)
        {
            return sameMonthActual.DividendPerShare;
        }

        var latestActual = reliableActuals
            .OrderByDescending(x => x.PaymentDate)
            .FirstOrDefault();
        if (latestActual is not null)
        {
            return latestActual.DividendPerShare;
        }

        var annualDividend = position.CurrentHolding.AnnualDividendPerShare;
        if (!IsPlausibleAnnualDividendPerShare(position, annualDividend))
        {
            return 0m;
        }

        return paymentCount <= 0 ? 0m : annualDividend / paymentCount;
    }

    private static bool IsPlausibleDividendPerShare(StockPosition position, decimal dividendPerShare, int paymentCount)
    {
        if (dividendPerShare <= 0m)
        {
            return false;
        }

        var currency = NormalizeCurrency(position.Stock.Currency);
        if (currency == "USD" && dividendPerShare > 50m)
        {
            return false;
        }

        if (currency == "JPY" && dividendPerShare > 10000m)
        {
            return false;
        }

        var price = position.CurrentHolding.CurrentPrice;
        if (price <= 0m)
        {
            return true;
        }

        if (dividendPerShare > price * 0.20m)
        {
            return false;
        }

        var annualizedDividend = dividendPerShare * Math.Max(paymentCount, 1);
        return annualizedDividend / price <= 0.40m;
    }

    private static bool IsPlausibleAnnualDividendPerShare(StockPosition position, decimal annualDividendPerShare)
    {
        if (annualDividendPerShare <= 0m)
        {
            return false;
        }

        var currency = NormalizeCurrency(position.Stock.Currency);
        if (currency == "USD" && annualDividendPerShare > 200m)
        {
            return false;
        }

        if (currency == "JPY" && annualDividendPerShare > 50000m)
        {
            return false;
        }

        var price = position.CurrentHolding.CurrentPrice;
        return price <= 0m || annualDividendPerShare / price <= 0.40m;
    }

    private static bool HasActualPaymentForMonth(
        int stockId,
        IReadOnlyList<DividendPayment> existing,
        int year,
        int month) =>
        existing.Any(x =>
            x.StockId == stockId &&
            DividendConstants.IsActual(x.DividendStatus) &&
            x.PaymentDate.Year == year &&
            x.PaymentDate.Month == month);

    private static bool IsGeneratedSchedule(DividendPayment payment) =>
        DividendConstants.IsUnconfirmed(payment.DividendStatus) &&
        (string.Equals(payment.Source, DividendConstants.SourceEstimatedFromHistory, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(payment.Source, DividendConstants.SourceEstimatedFromAnnualDividend, StringComparison.OrdinalIgnoreCase));

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
                   TotalDomesticTaxRate = DividendConstants.IsNisaAccount(accountType) ? 0m : 20.315m,
                   ForeignWithholdingTaxRate = currency == "USD" ? 10m : 0m,
                   IsDomesticTaxExempt = DividendConstants.IsNisaAccount(accountType),
                   IsForeignTaxExempt = currency == "JPY"
               };
    }

    private static string ResolveAccountType(StockPosition position)
    {
        var accountType = DividendConstants.NormalizeAccountType(position.Stock.AccountType);
        if (accountType != DividendConstants.AccountUnknown)
        {
            return accountType;
        }

        accountType = DividendConstants.NormalizeAccountType(position.Stock.CustodyType);
        return accountType == DividendConstants.AccountUnknown
            ? DividendConstants.AccountSpecific
            : accountType;
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

    private readonly record struct ScheduleKey(int StockId, int Year, int Month);
}

public sealed class DividendScheduleUpdateResult
{
    public IReadOnlyList<DividendPayment> Schedules { get; init; } = Array.Empty<DividendPayment>();
    public IReadOnlyList<int> ObsoleteScheduleIds { get; init; } = Array.Empty<int>();
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int PaymentDueCount { get; init; }
}
