using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DividendDashboardService
{
    public const string StatusActual = "入金済み";
    public const string StatusUpcoming = "入金予定";
    public const string StatusOverdue = "予定日経過（CSV未照合）";
    public const string StatusEstimated = "推定";

    public DividendDashboardAnalysis Build(
        IEnumerable<DividendPayment> source,
        int year,
        decimal annualGoalJpy,
        DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(source);

        var all = source.ToList();
        var actuals = all
            .Where(x => x.PaymentDate.Year == year)
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus))
            .ToList();

        var schedules = all
            .Where(x => x.PaymentDate.Year == year)
            .Where(x => DividendConstants.IsUnconfirmed(x.DividendStatus))
            .Where(x => x.ReplacedByDividendId is null && x.MatchedActualDividendId is null)
            .Where(x => !actuals.Any(actual => IsSameDividendIdentity(x, actual)))
            .GroupBy(BuildScheduleKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.SourcePriority)
                .ThenByDescending(x => x.UpdatedAt)
                .First())
            .ToList();

        var entries = actuals.Select(CreateActualEntry)
            .Concat(schedules.Select(x => CreateScheduleEntry(x, asOf)))
            .OrderBy(x => x.Payment.PaymentDate)
            .ThenBy(x => x.Payment.Ticker)
            .ToList();

        var actualNet = entries.Where(x => x.IsActual).Sum(x => x.NetJpy);
        var upcomingNet = entries.Where(x => x.IsUpcoming).Sum(x => x.NetJpy);
        var forecast = entries.Sum(x => x.NetJpy);
        var monthlyGoal = annualGoalJpy > 0m ? annualGoalJpy / 12m : 0m;
        var months = BuildMonths(entries, monthlyGoal);
        var rankings = BuildRankings(entries, forecast);

        return new DividendDashboardAnalysis
        {
            Year = year,
            ActualNetJpy = actualNet,
            UpcomingNetJpy = upcomingNet,
            YearEndForecastJpy = forecast,
            AnnualGoalJpy = annualGoalJpy,
            AchievementRate = annualGoalJpy > 0m ? forecast / annualGoalJpy * 100m : 0m,
            RemainingJpy = Math.Max(0m, annualGoalJpy - forecast),
            GrossTotalJpy = entries.Sum(x => x.GrossJpy),
            ForeignTaxTotalJpy = entries.Sum(x => x.ForeignTaxJpy),
            DomesticTaxTotalJpy = entries.Sum(x => x.DomesticTaxJpy),
            UnmatchedCount = entries.Count(x => x.IsUnmatched),
            Entries = entries,
            Months = months,
            Rankings = rankings,
            Bias = BuildBias(months)
        };
    }

    private static DividendDashboardEntry CreateActualEntry(DividendPayment payment) => new()
    {
        Payment = payment,
        ScheduleStatus = DividendScheduleStatus.Paid,
        DisplayStatus = StatusActual,
        DataQuality = "実績",
        IsActual = true,
        GrossJpy = ToJpy(payment.GrossAmountJpy, payment.GrossAmount, payment),
        ForeignTaxJpy = ToJpy(payment.ForeignTaxAmountJpy, payment.ForeignTaxAmount, payment),
        DomesticTaxJpy = ToJpy(payment.DomesticTaxAmountJpy, payment.DomesticTaxAmount, payment),
        NetJpy = NetJpy(payment)
    };

    private static DividendDashboardEntry CreateScheduleEntry(DividendPayment payment, DateTime asOf)
    {
        var overdue = string.Equals(payment.DividendStatus, DividendConstants.PaymentDue, StringComparison.OrdinalIgnoreCase)
                      || payment.PaymentDate.Date < asOf.Date;
        var estimated = !overdue && string.Equals(
            payment.DividendStatus, DividendConstants.Estimated, StringComparison.OrdinalIgnoreCase);

        return new DividendDashboardEntry
        {
            Payment = payment,
            ScheduleStatus = overdue
                ? DividendScheduleStatus.OverdueUnmatched
                : estimated ? DividendScheduleStatus.Estimated : DividendScheduleStatus.Expected,
            DisplayStatus = overdue ? StatusOverdue : estimated ? StatusEstimated : StatusUpcoming,
            DataQuality = overdue ? "未照合" : estimated ? "推定" : "予定",
            IsUpcoming = !overdue,
            IsUnmatched = overdue,
            IsEstimated = estimated,
            GrossJpy = ToJpy(payment.GrossAmountJpy, payment.GrossAmount, payment),
            ForeignTaxJpy = ToJpy(payment.ForeignTaxAmountJpy, payment.ForeignTaxAmount, payment),
            DomesticTaxJpy = ToJpy(payment.DomesticTaxAmountJpy, payment.DomesticTaxAmount, payment),
            NetJpy = NetJpy(payment)
        };
    }

    private static IReadOnlyList<DividendDashboardMonth> BuildMonths(
        IReadOnlyList<DividendDashboardEntry> entries,
        decimal monthlyGoal)
    {
        var result = new List<DividendDashboardMonth>(12);
        var cumulativeActual = 0m;
        var cumulativeForecast = 0m;

        for (var month = 1; month <= 12; month++)
        {
            var monthEntries = entries.Where(x => x.Payment.PaymentDate.Month == month).ToList();
            var actual = monthEntries.Where(x => x.IsActual).Sum(x => x.NetJpy);
            var upcoming = monthEntries.Where(x => x.IsUpcoming).Sum(x => x.NetJpy);
            var unmatched = monthEntries.Where(x => x.IsUnmatched).Sum(x => x.NetJpy);
            var forecast = monthEntries.Sum(x => x.NetJpy);
            cumulativeActual += actual;
            cumulativeForecast += forecast;
            result.Add(new DividendDashboardMonth
            {
                Month = month,
                ActualJpy = actual,
                UpcomingJpy = upcoming,
                UnmatchedJpy = unmatched,
                ForecastJpy = forecast,
                GoalJpy = monthlyGoal,
                DifferenceJpy = forecast - monthlyGoal,
                CumulativeActualJpy = cumulativeActual,
                CumulativeForecastJpy = cumulativeForecast,
                CumulativeGoalJpy = monthlyGoal * month,
                Entries = monthEntries
            });
        }

        return result;
    }

    private static IReadOnlyList<DividendDashboardRanking> BuildRankings(
        IReadOnlyList<DividendDashboardEntry> entries,
        decimal totalNetJpy) =>
        entries
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Payment.Ticker)
                ? $"STOCK:{x.Payment.StockId}"
                : x.Payment.Ticker.Trim().ToUpperInvariant())
            .Select(group => new DividendDashboardRanking
            {
                Ticker = group.Select(x => x.Payment.Ticker).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "-",
                StockName = group.Select(x => x.Payment.StockName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "-",
                GrossJpy = group.Sum(x => x.GrossJpy),
                NetJpy = group.Sum(x => x.NetJpy),
                CompositionRate = totalNetJpy > 0m ? group.Sum(x => x.NetJpy) / totalNetJpy * 100m : 0m,
                PaymentCount = group.Count(),
                ActualJpy = group.Where(x => x.IsActual).Sum(x => x.NetJpy),
                UpcomingJpy = group.Where(x => !x.IsActual).Sum(x => x.NetJpy),
                DataQuality = string.Join(" / ", group.Select(x => x.DataQuality).Distinct().OrderBy(QualityOrder))
            })
            .OrderByDescending(x => x.NetJpy)
            .ThenBy(x => x.Ticker)
            .ToList();

    private static DividendBiasAnalysis BuildBias(IReadOnlyList<DividendDashboardMonth> months)
    {
        var ordered = months.Select(x => x.ForecastJpy).OrderBy(x => x).ToArray();
        var total = ordered.Sum();
        var median = ordered.Length == 0
            ? 0m
            : (ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) / 2m;
        var maximum = months.OrderByDescending(x => x.ForecastJpy).ThenBy(x => x.Month).First();
        var minimum = months.OrderBy(x => x.ForecastJpy).ThenBy(x => x.Month).First();
        var topTwo = months.OrderByDescending(x => x.ForecastJpy).Take(2).Sum(x => x.ForecastJpy);

        return new DividendBiasAnalysis
        {
            MaximumMonth = maximum.Month,
            MaximumMonthJpy = maximum.ForecastJpy,
            MinimumMonth = minimum.Month,
            MinimumMonthJpy = minimum.ForecastJpy,
            MonthlyAverageJpy = total / 12m,
            MonthlyMedianJpy = median,
            ZeroDividendMonthCount = months.Count(x => x.ForecastJpy == 0m),
            TopTwoMonthConcentrationRate = total > 0m ? topTwo / total * 100m : 0m
        };
    }

    private static int QualityOrder(string quality) => quality switch
    {
        "実績" => 0,
        "予定" => 1,
        "推定" => 2,
        "未照合" => 3,
        _ => 4
    };

    private static string BuildScheduleKey(DividendPayment payment) => string.Join('|',
        payment.StockId,
        Normalize(payment.Broker),
        NormalizeAccount(payment.AccountType),
        NormalizeCurrency(payment.Currency),
        payment.PaymentDate.ToString("yyyyMMdd"),
        Math.Round(NetJpy(payment), 0));

    private static bool IsSameDividendIdentity(DividendPayment planned, DividendPayment actual)
    {
        if (planned.StockId != actual.StockId
            || !string.Equals(Normalize(planned.Broker), Normalize(actual.Broker), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(NormalizeAccount(planned.AccountType), NormalizeAccount(actual.AccountType), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(NormalizeCurrency(planned.Currency), NormalizeCurrency(actual.Currency), StringComparison.OrdinalIgnoreCase)
            || Math.Abs((planned.PaymentDate.Date - actual.PaymentDate.Date).TotalDays) > 10)
        {
            return false;
        }

        var plannedJpy = NetJpy(planned);
        var actualJpy = NetJpy(actual);
        if (plannedJpy > 0m && actualJpy > 0m)
        {
            return IsClose(plannedJpy, actualJpy, 10m);
        }

        return IsClose(planned.NetAmount, actual.NetAmount,
            NormalizeCurrency(actual.Currency) == "JPY" ? 10m : 0.05m);
    }

    private static bool IsClose(decimal left, decimal right, decimal absoluteTolerance)
    {
        var difference = Math.Abs(left - right);
        if (difference <= absoluteTolerance)
        {
            return true;
        }

        var denominator = Math.Max(Math.Abs(left), Math.Abs(right));
        return denominator > 0m && difference / denominator <= 0.01m;
    }

    private static decimal NetJpy(DividendPayment payment)
    {
        if (payment.NetAmountJpy != 0m) return payment.NetAmountJpy;
        if (payment.JpyAmount != 0m) return payment.JpyAmount;
        return ToJpy(0m, payment.NetAmount, payment);
    }

    private static decimal ToJpy(decimal explicitJpy, decimal nativeAmount, DividendPayment payment)
    {
        if (explicitJpy != 0m) return explicitJpy;
        if (NormalizeCurrency(payment.Currency) == "JPY") return nativeAmount;
        return nativeAmount * (payment.ExchangeRate > 0m ? payment.ExchangeRate : 1m);
    }

    private static string Normalize(string value) => value?.Trim() ?? string.Empty;

    private static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "円" ? "JPY" : normalized;
    }

    private static string NormalizeAccount(string accountType) =>
        DividendConstants.NormalizeAccountType(accountType);
}
