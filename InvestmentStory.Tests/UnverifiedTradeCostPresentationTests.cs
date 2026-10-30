using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Tests;

public sealed class UnverifiedTradeCostPresentationTests
{
    [Fact]
    public void DashboardAndTradeRow_DoNotPresentUnverifiedRealizedGainAsKnown()
    {
        var dashboard = new DashboardViewModel();
        dashboard.Update(new DashboardSummary
        {
            RealizedGainLossJpy = 12_345m,
            TotalReturnJpy = 23_456m,
            HasUnverifiedTradeCost = true
        }, Array.Empty<StockSnapshot>(), Array.Empty<PortfolioSnapshot>());

        Assert.Contains("算出不可", dashboard.RealizedGainLoss);
        Assert.Equal("算出不可", dashboard.TotalReturn);
        Assert.False(dashboard.RecordSnapshotCommand.CanExecute(null));

        var row = new BrokerTradeRowViewModel(new BrokerTrade
        {
            TradeType = "売却", SignedQuantity = -2m,
            RealizedGainLossJpy = 5_000m, AfterTradeAverageCost = 100m
        }, costUnverified: true);
        Assert.Equal("算出不可", row.RealizedGainLossJpy);
        Assert.Equal("原価要確認", row.AfterTradeAverageCost);
    }
}
