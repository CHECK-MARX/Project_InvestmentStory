using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class DividendDashboardServiceTests
{
    private readonly DividendDashboardService _service = new();
    private readonly DateTime _asOf = new(2026, 7, 17);

    [Fact]
    public void Build_AlwaysCreatesJanuaryThroughDecemberInAscendingOrder()
    {
        var result = _service.Build(Array.Empty<DividendPayment>(), 2026, 1_200_000m, _asOf);

        Assert.Equal(Enumerable.Range(1, 12), result.Months.Select(x => x.Month));
        Assert.All(result.Months, month =>
        {
            Assert.Equal(0m, month.ActualJpy);
            Assert.Equal(0m, month.UpcomingJpy);
            Assert.Equal(0m, month.ForecastJpy);
            Assert.Equal(100_000m, month.GoalJpy);
        });
    }

    [Fact]
    public void Build_ClassifiesActualUpcomingEstimatedAndOverdue()
    {
        var payments = new[]
        {
            Payment(1, new DateTime(2026, 1, 10), DividendConstants.Actual, 1_000m),
            Payment(2, new DateTime(2026, 9, 10), DividendConstants.Planned, 500m),
            Payment(3, new DateTime(2026, 10, 10), DividendConstants.Estimated, 300m),
            Payment(4, new DateTime(2026, 3, 10), DividendConstants.Planned, 200m)
        };

        var result = _service.Build(payments, 2026, 12_000m, _asOf);

        Assert.Equal(new[]
        {
            DividendDashboardService.StatusActual,
            DividendDashboardService.StatusOverdue,
            DividendDashboardService.StatusUpcoming,
            DividendDashboardService.StatusEstimated
        }, result.Entries.Select(x => x.DisplayStatus));
        Assert.Equal(new[] { "実績", "未照合", "予定", "推定" }, result.Entries.Select(x => x.DataQuality));
        Assert.Equal(1_000m, result.ActualNetJpy);
        Assert.Equal(800m, result.UpcomingNetJpy);
        Assert.Equal(2_000m, result.YearEndForecastJpy);
        Assert.Equal(1, result.UnmatchedCount);
    }

    [Fact]
    public void Build_ExcludesPlannedPaymentMatchedToActualFromForecast()
    {
        var actual = Payment(10, new DateTime(2026, 6, 15), DividendConstants.Actual, 5_000m);
        var matchedPlan = Payment(11, new DateTime(2026, 6, 20), DividendConstants.Planned, 5_000m);

        var result = _service.Build(new[] { actual, matchedPlan }, 2026, 0m, _asOf);

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.IsActual);
        Assert.Equal(5_000m, result.YearEndForecastJpy);
        Assert.Equal(5_000m, result.Months[5].ForecastJpy);
        Assert.Equal(0, result.UnmatchedCount);
    }

    [Fact]
    public void Build_UsesMatchedActualPointerWithoutChangingSourceData()
    {
        var actual = Payment(20, new DateTime(2026, 4, 1), DividendConstants.Actual, 2_500m);
        var plan = Payment(21, new DateTime(2026, 4, 1), DividendConstants.Planned, 2_000m);
        plan.MatchedActualDividendId = actual.Id;

        var result = _service.Build(new[] { actual, plan }, 2026, 0m, _asOf);

        Assert.Single(result.Entries);
        Assert.Equal(DividendConstants.Planned, plan.DividendStatus);
        Assert.Equal(actual.Id, plan.MatchedActualDividendId);
    }

    [Fact]
    public void Build_AggregatesTaxesRankingAndBiasFromSameVisibleEntries()
    {
        var first = Payment(31, new DateTime(2026, 1, 10), DividendConstants.Actual, 700m, "AAA");
        first.GrossAmountJpy = 1_000m;
        first.ForeignTaxAmountJpy = 100m;
        first.DomesticTaxAmountJpy = 200m;
        var second = Payment(32, new DateTime(2026, 1, 20), DividendConstants.Planned, 300m, "AAA");
        second.GrossAmountJpy = 400m;
        second.ForeignTaxAmountJpy = 40m;
        second.DomesticTaxAmountJpy = 60m;
        var third = Payment(33, new DateTime(2026, 6, 10), DividendConstants.Estimated, 500m, "BBB");
        third.GrossAmountJpy = 700m;
        third.ForeignTaxAmountJpy = 70m;
        third.DomesticTaxAmountJpy = 130m;

        var result = _service.Build(new[] { first, second, third }, 2026, 12_000m, new DateTime(2025, 12, 31));

        Assert.Equal(2_100m, result.GrossTotalJpy);
        Assert.Equal(210m, result.ForeignTaxTotalJpy);
        Assert.Equal(390m, result.DomesticTaxTotalJpy);
        Assert.Equal(1_500m, result.YearEndForecastJpy);
        Assert.Equal(2, result.Rankings.Count);
        Assert.Equal("AAA", result.Rankings[0].Ticker);
        Assert.Equal(1_000m, result.Rankings[0].NetJpy);
        Assert.Equal(2, result.Rankings[0].PaymentCount);
        Assert.Equal(1, result.Bias.MaximumMonth);
        Assert.Equal(1_000m, result.Bias.MaximumMonthJpy);
        Assert.Equal(10, result.Bias.ZeroDividendMonthCount);
        Assert.Equal(100m, result.Bias.TopTwoMonthConcentrationRate);
        Assert.Equal(
            result.GrossTotalJpy - result.ForeignTaxTotalJpy - result.DomesticTaxTotalJpy,
            result.YearEndForecastJpy);
        Assert.Equal(result.YearEndForecastJpy, result.Months.Sum(month => month.ForecastJpy));
        Assert.Equal(result.YearEndForecastJpy, result.Months[^1].CumulativeForecastJpy);
        Assert.Equal(result.ActualNetJpy, result.Months[^1].CumulativeActualJpy);
    }

    [Fact]
    public void Build_DeduplicatesRepeatedScheduleRowsButKeepsSeparateBrokerAccounts()
    {
        var same1 = Payment(41, new DateTime(2026, 12, 20), DividendConstants.Planned, 800m);
        var same2 = Payment(42, new DateTime(2026, 12, 20), DividendConstants.Estimated, 800m);
        same2.SourcePriority = 10;
        var separateAccount = Payment(43, new DateTime(2026, 12, 20), DividendConstants.Planned, 800m);
        separateAccount.AccountType = AccountTypes.NisaGrowth;

        var result = _service.Build(new[] { same1, same2, separateAccount }, 2026, 0m, _asOf);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(1_600m, result.YearEndForecastJpy);
    }

    private static DividendPayment Payment(
        int id,
        DateTime date,
        string status,
        decimal netJpy,
        string ticker = "AAA") => new()
    {
        Id = id,
        StockId = ticker == "AAA" ? 1 : 2,
        Ticker = ticker,
        StockName = $"{ticker}社",
        Broker = "SBI証券",
        AccountType = AccountTypes.Specific,
        Currency = "JPY",
        PaymentDate = date,
        DividendStatus = status,
        Source = DividendConstants.SourceApi,
        SourcePriority = 50,
        NetAmount = netJpy,
        NetAmountJpy = netJpy,
        JpyAmount = netJpy,
        GrossAmount = netJpy,
        GrossAmountJpy = netJpy,
        UpdatedAt = date
    };
}
