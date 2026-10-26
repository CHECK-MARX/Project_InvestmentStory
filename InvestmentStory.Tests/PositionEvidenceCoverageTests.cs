using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class PositionEvidenceCoverageTests
{
    private static readonly DateTime AsOf = new(2026, 10, 25);

    [Fact]
    public void CoverageSeparatesEstimatedExcludedAndFreshnessWithoutTreatingUnknownAsZero()
    {
        var snapshots = new[]
        {
            Snapshot("CONF", 100m, true, false, 120m, true, AsOf, 120m),
            Snapshot("EST", 200m, true, true, 250m, true, AsOf.AddDays(-8), 250m),
            Snapshot("UNK", 0m, false, false, 300m, true, DateTime.MinValue, 300m),
            Snapshot("MISSING", 0m, false, false, 0m, false, DateTime.MinValue, 0m)
        };

        var coverage = PositionEvidenceCoverage.Build(snapshots, AsOf);

        Assert.Equal(1, coverage.ConfirmedCostCount);
        Assert.Equal(1, coverage.EstimatedCostCount);
        Assert.Equal(250m, coverage.EstimatedCostMarketValueJpy);
        Assert.Equal(2, coverage.UnconfirmedCostCount);
        Assert.Null(coverage.ExcludedMarketValueJpy);
        Assert.Equal(1, coverage.UnknownMarketValueCount);
        Assert.Equal(1, coverage.MissingPriceCount);
        Assert.Equal(1, coverage.DelayedPriceCount);
        Assert.Equal(1, coverage.UnknownPriceTimeCount);
    }

    [Fact]
    public void StockRowUsesTheSameCostAndPriceEvidenceRules()
    {
        var estimated = Snapshot("EST", 200m, true, true, 250m, true, AsOf.AddDays(-8), 250m);
        var unknown = Snapshot("UNK", 0m, false, false, 300m, true, DateTime.MinValue, 300m);
        var row = new StockRowViewModel([estimated, unknown]);

        Assert.Equal("未確認", row.CostQuality);
        Assert.Equal("時刻不明", row.PriceFreshness);
        Assert.Equal("未確認", row.CostBasisJpy);
    }

    private static StockSnapshot Snapshot(string ticker, decimal cost, bool knownCost,
        bool referenceCost, decimal value, bool knownValue, DateTime priceDate, decimal price) => new()
    {
        Position = new StockPosition
        {
            Stock = new Stock { Ticker = ticker, Name = ticker, Currency = "JPY" },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 1m,
                CurrentPrice = price,
                CurrentPriceAcquiredAt = priceDate
            }
        },
        PurchaseTotalJpy = cost,
        HasKnownPurchaseCostJpy = knownCost,
        IsReferencePurchaseCostJpy = referenceCost,
        CurrentMarketValueJpy = value,
        HasKnownCurrentMarketValueJpy = knownValue
    };
}
