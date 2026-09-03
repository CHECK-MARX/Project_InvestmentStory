using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class DividendPurchasePlanSimulationServiceTests
{
    private readonly DividendPurchasePlanSimulationService _service = new();

    [Fact]
    public void PlannedPurchase_ReceivesOnlyDividendsWhoseRightsDateHasNotPassed()
    {
        var item = CreateUsNisaItem(plannedShares: 10m);
        var result = Simulate(item, new DateTime(2026, 7, 14), QuarterlyHistory(item.StockId, item.Ticker));

        var holding = Assert.Single(result.Holdings);
        Assert.Equal(2_880m, holding.TargetYearAdditionalNetDividendJpy);
        Assert.Equal(2_880m, holding.MissedNetDividendJpy);
        Assert.Equal(5_760m, holding.NextYearAdditionalNetDividendJpy);
        Assert.Equal(17_280m, holding.PostAddNextYearNetDividendJpy);
        Assert.Equal(14_400m, holding.TargetYearPlannedNetDividendJpy);
        Assert.Equal(DividendPlanDataQuality.Estimated, holding.DataQuality);
        Assert.Equal(DividendPlanEligibility.Estimated, holding.EligibilityStatus);

        Assert.Equal(1_440m, result.Months[8].ExistingAdditionalNetDividendJpy);
        Assert.Equal(1_440m, result.Months[11].ExistingAdditionalNetDividendJpy);
        Assert.Equal(0m, result.Months[2].ExistingAdditionalNetDividendJpy);
        Assert.Equal(1_440m, result.Months[2].MissedNetDividendJpy);
        Assert.Equal(2_880m, result.Summary.TargetYearDividendIncreaseJpy);
        Assert.Equal(2_880m, result.Summary.MissedTargetYearNetDividendJpy);
    }

    [Fact]
    public void CurrentAndPlannedAmounts_AreTaxedAndAggregatedConsistently()
    {
        var item = CreateUsNisaItem(plannedShares: 10m);
        var result = Simulate(item, new DateTime(2026, 7, 14), QuarterlyHistory(item.StockId, item.Ticker));

        Assert.Equal(11_520m, result.Summary.CurrentTargetYearNetDividendJpy);
        Assert.Equal(14_400m, result.Summary.PlannedTargetYearNetDividendJpy);
        Assert.Equal(17_280m, result.Summary.NextYearAnnualNetDividendJpy);
        Assert.Equal(160_000m, result.Summary.PlannedInvestmentJpy);
        Assert.Equal(3.6m, result.Summary.AdditionalInvestmentYieldRate, 4);
        Assert.Equal(3.6m, result.Summary.CurrentYieldRate, 4);
        Assert.Equal(5.76m, result.Summary.YieldOnCostRate, 4);
        Assert.Equal(3.6m, result.Summary.PostAddPortfolioYieldRate, 4);
        Assert.Equal(27.7778m, result.Summary.AdditionalInvestmentPaybackYears, 4);
        Assert.Equal(
            result.Summary.CurrentTargetYearNetDividendJpy + result.Summary.TargetYearDividendIncreaseJpy,
            result.Summary.PlannedTargetYearNetDividendJpy);
        Assert.Equal(
            result.Months.Sum(month => month.CurrentNetDividendJpy),
            result.Summary.CurrentTargetYearNetDividendJpy);
        Assert.Equal(
            result.Months.Sum(month => month.AdditionalNetDividendJpy),
            result.Summary.TargetYearDividendIncreaseJpy);
    }

    [Fact]
    public void MissingSchedule_IsReportedWithoutInventingTargetYearPayments()
    {
        var item = CreateUsNisaItem(plannedShares: 10m, frequency: string.Empty, months: string.Empty);

        var result = Simulate(item, new DateTime(2026, 7, 14), Array.Empty<DividendPayment>());

        var holding = Assert.Single(result.Holdings);
        Assert.Equal(DividendPlanDataQuality.Missing, holding.DataQuality);
        Assert.Equal(DividendPlanEligibility.Missing, holding.EligibilityStatus);
        Assert.Equal(0m, holding.TargetYearAdditionalNetDividendJpy);
        Assert.All(result.Months, month => Assert.Equal(0m, month.AdditionalNetDividendJpy));
    }

    [Fact]
    public void MonthlyRows_UseOneTwelfthOfAnnualTargetAndPreserveCumulativeTotal()
    {
        var item = CreateUsNisaItem(plannedShares: 10m);
        var result = _service.Simulate(new DividendPurchasePlanInput
        {
            TargetYear = 2026,
            PlannedPurchaseDate = new DateTime(2026, 7, 14),
            TargetAnnualNetDividendJpy = 1_200_000m,
            PlanItems = new[] { item },
            DividendPayments = QuarterlyHistory(item.StockId, item.Ticker)
        });

        Assert.Equal(12, result.Months.Count);
        Assert.All(result.Months, month => Assert.Equal(100_000m, month.TargetNetDividendJpy));
        Assert.Equal(result.Summary.PlannedTargetYearNetDividendJpy, result.Months[^1].CumulativeNetDividendJpy);
        Assert.Equal(result.Summary.CurrentTargetYearNetDividendJpy, result.Months[^1].CurrentCumulativeNetDividendJpy);
        Assert.Equal(
            result.Months.Sum(month => month.CurrentNetDividendJpy),
            result.Months[^1].CurrentCumulativeNetDividendJpy);
        Assert.Equal(
            result.Months.Sum(month => month.PlannedNetDividendJpy),
            result.Months[^1].CumulativeNetDividendJpy);
    }

    [Fact]
    public void InvalidLegacyYear_IsNormalizedBeforeBuildingAllMonthlyOutputs()
    {
        var item = CreateUsNisaItem(plannedShares: 10m);
        var result = _service.Simulate(new DividendPurchasePlanInput
        {
            TargetYear = 2000,
            PlannedPurchaseDate = new DateTime(2000, 7, 14),
            TargetAnnualNetDividendJpy = 1_200_000m,
            PlanItems = new[] { item },
            DividendPayments = QuarterlyHistory(item.StockId, item.Ticker)
        });

        Assert.Equal(12, result.Months.Count);
        Assert.All(result.Months, month => Assert.Equal(DateTime.Today.Year, month.Year));
        Assert.Equal(Enumerable.Range(1, 12), result.Months.Select(month => month.Month));
    }

    [Fact]
    public void CompositionRows_CompareCurrentAndPostPurchaseSharesFromTheSameResult()
    {
        var item = CreateUsNisaItem(plannedShares: 10m);

        var result = Simulate(item, new DateTime(2026, 7, 14), QuarterlyHistory(item.StockId, item.Ticker));

        var composition = Assert.Single(result.Composition);
        Assert.Equal(11_520m, composition.CurrentAnnualNetDividendJpy);
        Assert.Equal(17_280m, composition.AnnualNetDividendJpy);
        Assert.Equal(100m, composition.CurrentCompositionRate);
        Assert.Equal(100m, composition.CompositionRate);
        Assert.Equal(0m, composition.CompositionRateChange);
    }

    [Fact]
    public void NewStockPayments_AreSeparatedFromExistingAdditionalPayments()
    {
        var item = CreateUsNisaItem(plannedShares: 10m, isNewStock: true);

        var result = Simulate(item, new DateTime(2026, 7, 14), QuarterlyHistory(item.StockId, item.Ticker));

        Assert.Equal(0m, result.Months[8].ExistingAdditionalNetDividendJpy);
        Assert.Equal(1_440m, result.Months[8].NewPurchaseNetDividendJpy);
    }

    [Fact]
    public void NoPurchasePlan_UsesSameTargetYearBasisForBeforeAfterAndFollowingYear()
    {
        var item = CreateUsNisaItem(plannedShares: 0m);

        var result = Simulate(item, new DateTime(2026, 7, 14), QuarterlyHistory(item.StockId, item.Ticker));

        Assert.Equal(result.Summary.CurrentTargetYearNetDividendJpy, result.Summary.PlannedTargetYearNetDividendJpy);
        Assert.Equal(result.Summary.CurrentTargetYearNetDividendJpy, result.Summary.NextYearAnnualNetDividendJpy);
        Assert.Equal(0m, result.Summary.TargetYearDividendIncreaseJpy);
        Assert.Equal(0m, result.Summary.PlannedInvestmentJpy);
    }

    [Fact]
    public void PurchaseOnLastRightsDate_IsEligibleForThatDividend()
    {
        var item = CreateUsNisaItem(plannedShares: 10m);
        var history = QuarterlyHistory(item.StockId, item.Ticker);

        var result = Simulate(item, new DateTime(2026, 8, 31), history);

        var september = result.Months[8];
        Assert.Equal(1_440m, september.ExistingAdditionalNetDividendJpy);
        Assert.Contains(september.Events, item => item.IsEligible);
    }

    [Fact]
    public void PartiallyAuthoritativeSchedule_IsReportedAsEstimated()
    {
        var item = CreateUsNisaItem(plannedShares: 10m);
        var oneKnownPayment = new[] { History(item.StockId, item.Ticker, 3, 20, 1) };

        var result = Simulate(item, new DateTime(2026, 1, 1), oneKnownPayment);

        var holding = Assert.Single(result.Holdings);
        Assert.Equal(DividendPlanDataQuality.Estimated, holding.DataQuality);
        Assert.Equal(DividendPlanEligibility.Estimated, holding.EligibilityStatus);
        Assert.Equal(4, result.Months.SelectMany(month => month.Events).Count(item => item.AdditionalNetDividendJpy > 0m));
    }

    [Fact]
    public void PublishedTargetYearCalendar_IsReportedAsAcquiredAndKeepsExactDates()
    {
        var calendar = new[] { 3, 6, 9, 12 }
            .Select(month => new DividendCalendarEvent
            {
                EventKey = $"2026-{month:00}-01|1|USD",
                DeclarationDate = new DateTime(2026, month, 1).AddDays(-35),
                ExDividendDate = new DateTime(2026, month, 1),
                RecordDate = new DateTime(2026, month, 2),
                PaymentDate = new DateTime(2026, month, 20),
                AmountPerShare = 1m,
                Currency = "USD",
                Source = "Nasdaq",
                DataQuality = DividendPlanDataQuality.Acquired,
                AcquiredAt = new DateTime(2026, 1, 1),
                IsConfirmed = true
            })
            .ToList();
        var item = CreateUsNisaItem(plannedShares: 10m, dividendEvents: calendar);

        var result = Simulate(item, new DateTime(2026, 7, 14), Array.Empty<DividendPayment>());

        var holding = Assert.Single(result.Holdings);
        Assert.Equal(DividendPlanDataQuality.Acquired, holding.DataQuality);
        Assert.Equal(DividendPlanEligibility.Eligible, holding.EligibilityStatus);
        var september = Assert.Single(result.Months[8].Events);
        Assert.Equal(new DateTime(2026, 9, 1), september.ExDividendDate);
        Assert.Equal(new DateTime(2026, 9, 2), september.RecordDate);
        Assert.Equal(new DateTime(2026, 9, 20), september.PaymentDate);
        Assert.Equal("Nasdaq", september.Source);
    }

    [Fact]
    public void BrokerJpyPerShareForUsdPosition_IsNotConvertedToJpyTwice()
    {
        var item = CreateUsNisaItem(
            plannedShares: 0m,
            frequency: "1",
            months: "10");
        var history = new[]
        {
            new DividendPayment
            {
                StockId = item.StockId,
                Ticker = item.Ticker,
                Broker = item.Broker,
                AccountType = item.AccountType,
                PaymentDate = new DateTime(2025, 10, 20),
                DividendPerShare = 160m,
                Currency = "JPY",
                ExchangeRate = 1m,
                Source = DividendConstants.SourceCsv
            }
        };

        var result = Simulate(item, new DateTime(2026, 1, 1), history);

        // 160 JPYを160 USDとして再度為替換算すると46万円超になる。
        // 通貨不一致時はポジションの年間配当4 USDを使用する。
        Assert.Equal(11_520m, result.Summary.CurrentTargetYearNetDividendJpy);
        Assert.All(result.Months.SelectMany(x => x.Events), x => Assert.True(x.CurrentNetDividendJpy < 20_000m));
    }

    [Fact]
    public void LegacyStockIdActualPayment_IsMatchedByNormalizedPositionAndMergedWithCalendar()
    {
        var calendar = new[]
        {
            new DividendCalendarEvent
            {
                EventKey = "8151-2026-05",
                PaymentDate = new DateTime(2026, 5, 29),
                AmountPerShare = 30m,
                Currency = "JPY",
                Source = "Yahoo Finance",
                DataQuality = DividendPlanDataQuality.Acquired,
                IsConfirmed = true
            },
            new DividendCalendarEvent
            {
                EventKey = "8151-2026-12",
                PaymentDate = new DateTime(2026, 12, 10),
                AmountPerShare = 39m,
                Currency = "JPY",
                Source = "Yahoo Finance",
                DataQuality = DividendPlanDataQuality.Acquired,
                IsConfirmed = true
            }
        };
        var item = new DividendGrowthPlanItem
        {
            StockId = 84,
            PlanKey = "8151|野村証券|Specific",
            CanonicalKey = "8151",
            PositionKey = "8151|野村証券|Specific",
            Ticker = "8151",
            Name = "東陽テクニカ",
            Broker = "野村証券",
            AccountType = AccountTypes.Specific,
            Country = "Japan",
            Currency = "JPY",
            CurrentShares = 7_000m,
            CurrentPrice = 1_900m,
            ExchangeRate = 1m,
            AnnualDividendPerShare = 69m,
            CurrentCostJpy = 8_246_000m,
            CurrentMarketValueJpy = 13_300_000m,
            DividendFrequency = "2",
            DividendEvents = calendar
        };
        var history = new[]
        {
            new DividendPayment
            {
                StockId = 98,
                Ticker = "8151",
                Broker = "野村証券",
                AccountType = AccountTypes.Unknown,
                PaymentDate = new DateTime(2026, 6, 9),
                Quantity = 6_000m,
                DividendPerShare = 30m,
                Currency = "JPY",
                NetAmountJpy = 143_433m,
                JpyAmount = 143_433m,
                Source = DividendConstants.SourceCsv
            }
        };

        var result = Simulate(item, new DateTime(2026, 1, 1), history);
        var events = result.Months.SelectMany(x => x.Events).ToList();

        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, x => x.PaymentDate.Month == 5);
        var paid = Assert.Single(events, x => x.IsPaid);
        Assert.Equal(new DateTime(2026, 6, 9), paid.PaymentDate);
        Assert.Equal(143_433m, paid.CurrentNetDividendJpy);
    }

    [Fact]
    public void LegacyStockIdFallback_DoesNotCrossKnownAccountBoundary()
    {
        var item = CreateUsNisaItem(plannedShares: 0m, frequency: "1", months: "6");
        var history = new[]
        {
            new DividendPayment
            {
                StockId = 999,
                Ticker = item.Ticker,
                Broker = item.Broker,
                AccountType = AccountTypes.Specific,
                PaymentDate = new DateTime(2026, 6, 20),
                Currency = "USD",
                NetAmountJpy = 50_000m,
                Source = DividendConstants.SourceCsv
            }
        };

        var result = Simulate(item, new DateTime(2026, 1, 1), history);

        Assert.DoesNotContain(result.Months.SelectMany(x => x.Events), x => x.IsPaid);
    }

    [Fact]
    public void FrequencyWithoutDates_DoesNotCreateFabricatedMonthlySchedule()
    {
        var item = CreateUsNisaItem(
            plannedShares: 0m,
            frequency: "4",
            months: string.Empty,
            dividendEvents: Array.Empty<DividendCalendarEvent>());

        var result = Simulate(item, new DateTime(2026, 1, 1), Array.Empty<DividendPayment>());

        Assert.Empty(result.Months.SelectMany(x => x.Events));
    }

    private DividendPurchasePlanResult Simulate(
        DividendGrowthPlanItem item,
        DateTime purchaseDate,
        IReadOnlyList<DividendPayment> history) =>
        _service.Simulate(new DividendPurchasePlanInput
        {
            PlanName = "2026 summer",
            TargetYear = 2026,
            PlannedPurchaseDate = purchaseDate,
            TargetAnnualNetDividendJpy = 1_200_000m,
            PlanItems = new[] { item },
            DividendPayments = history
        });

    private static DividendGrowthPlanItem CreateUsNisaItem(
        decimal plannedShares,
        string frequency = "4",
        string months = "3,6,9,12",
        bool isNewStock = false,
        IReadOnlyList<DividendCalendarEvent>? dividendEvents = null) => new()
    {
        StockId = 77,
        PlanKey = isNewStock ? "New:TRMD" : "TRMD|SBI|NisaGrowth",
        CanonicalKey = "TRMD",
        PositionKey = "TRMD|SBI|NisaGrowth",
        Ticker = "TRMD",
        Name = "TORM plc",
        Broker = "SBI",
        AccountType = AccountTypes.NisaGrowth,
        Country = "United States",
        Currency = "USD",
        CurrentShares = isNewStock ? 0m : 20m,
        CurrentPrice = 100m,
        ExchangeRate = 160m,
        AnnualDividendPerShare = 4m,
        CurrentCostJpy = isNewStock ? 0m : 200_000m,
        CurrentMarketValueJpy = isNewStock ? 0m : 320_000m,
        DividendFrequency = frequency,
        DividendMonths = months,
        DividendEvents = dividendEvents ?? Array.Empty<DividendCalendarEvent>(),
        PlannedAdditionalShares = plannedShares,
        PlannedBroker = "SBI",
        PlannedAccountType = AccountTypes.NisaGrowth,
        IsNewStock = isNewStock
    };

    private static IReadOnlyList<DividendPayment> QuarterlyHistory(int stockId, string ticker) =>
        new[]
        {
            History(stockId, ticker, 3, 20, 1),
            History(stockId, ticker, 6, 20, 1),
            History(stockId, ticker, 9, 20, 1),
            History(stockId, ticker, 12, 20, 1)
        };

    private static DividendPayment History(int stockId, string ticker, int month, int paymentDay, int exDay) => new()
    {
        StockId = stockId,
        Ticker = ticker,
        Broker = "SBI",
        PaymentDate = new DateTime(2025, month, paymentDay),
        ExDividendDate = new DateTime(2025, month, exDay),
        RecordDate = new DateTime(2025, month, Math.Min(exDay + 1, DateTime.DaysInMonth(2025, month)))
    };
}
