using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.Tests;

public sealed class KnownGainCoverageServiceTests
{
    [Fact]
    public void PartialSubtotalExcludesUnknownReferenceAndUnverifiedTradeBasis()
    {
        static StockSnapshot Row(int id, decimal gain, bool known = true, bool reference = false) => new()
        {
            Position = new StockPosition
            {
                Stock = new Stock { Id = id, Ticker = $"S{id}" },
                CurrentHolding = new CurrentHolding { CurrentShares = 1m }
            },
            HasKnownCurrentMarketValueJpy = true,
            HasKnownPurchaseCostJpy = known,
            IsReferencePurchaseCostJpy = reference,
            PurchaseTotalJpy = 1000m,
            CurrentMarketValueJpy = 1000m + gain,
            UnrealizedGainJpy = gain
        };
        var rows = new[] { Row(1, 100m), Row(2, -200m), Row(3, 500m, false), Row(4, 300m, reference: true) };
        var trades = new[] { new BrokerTrade { StockId = 2, TradeType = "TransferIn" } };

        var result = new KnownGainCoverageService().Build(rows, trades);

        Assert.Equal(4, result.ActiveCount);
        Assert.Equal(1, result.ConfirmedCount);
        Assert.Equal(100m, result.ConfirmedGainJpy);
        Assert.Equal(1, result.ReferenceCount);
        Assert.Equal(300m, result.ReferenceGainJpy);
        Assert.Equal(1, result.UnverifiedTradeCount);
        Assert.Equal(1, result.UnknownCostCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void DashboardShowsOnlyCoveredGainAndExplainsIncompleteTotal()
    {
        var exact = new StockSnapshot
        {
            Position = new StockPosition
            {
                Stock = new Stock { Id = 1, Ticker = "ABC" },
                CurrentHolding = new CurrentHolding { CurrentShares = 1m }
            },
            HasKnownPurchaseCostJpy = true, HasKnownCurrentMarketValueJpy = true,
            PurchaseTotalJpy = 1000m, CurrentMarketValueJpy = 1250m, UnrealizedGainJpy = 250m
        };
        var unknown = new StockSnapshot
        {
            Position = new StockPosition
            {
                Stock = new Stock { Id = 2, Ticker = "XYZ" },
                CurrentHolding = new CurrentHolding { CurrentShares = 1m }
            },
            HasKnownPurchaseCostJpy = false, HasKnownCurrentMarketValueJpy = true,
            CurrentMarketValueJpy = 3000m
        };
        var dashboard = new DashboardViewModel();
        dashboard.Update(new DashboardSummary { UnknownPurchaseCostPositionCount = 1 },
            [exact, unknown], []);

        Assert.Equal("+250円", dashboard.TotalUnrealizedGain);
        Assert.Contains("1/2件", dashboard.TotalUnrealizedGainRate);
        Assert.Contains("全体損益ではありません", dashboard.UnrealizedGainRateExplanation);
        Assert.Equal("算出不可", dashboard.TotalReturn);
    }
}
