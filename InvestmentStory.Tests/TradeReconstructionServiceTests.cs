using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class TradeReconstructionServiceTests
{
    [Fact]
    public void Reconstruct_CalculatesRealizedGainFromAverageCost()
    {
        var trades = new[]
        {
            new BrokerTradeRecord
            {
                TradeDate = new DateTime(2026, 1, 1),
                SettlementDate = new DateTime(2026, 1, 3),
                TradeType = "現物買付",
                Quantity = 10m,
                SignedQuantity = 10m,
                UnitPrice = 24775m,
                ExchangeRate = 1m,
                Currency = "JPY"
            },
            new BrokerTradeRecord
            {
                TradeDate = new DateTime(2026, 2, 1),
                SettlementDate = new DateTime(2026, 2, 3),
                TradeType = "現物売却",
                Quantity = 5m,
                SignedQuantity = -5m,
                UnitPrice = 28800m,
                ExchangeRate = 1m,
                Currency = "JPY"
            }
        };

        var result = TradeReconstructionService.Reconstruct(trades);

        Assert.Equal(5m, result.CurrentShares);
        Assert.Equal(24775m, result.AverageAcquisitionPrice);
        Assert.Equal(24775m, result.AverageAcquisitionPriceJpy);
        Assert.Equal(20125m, result.RealizedGainJpy);
    }

    [Fact]
    public void Reconstruct_KeepsAverageCostAfterPartialSellAndReweightsLaterBuy()
    {
        var trades = new[]
        {
            Buy(new DateTime(2026, 1, 1), 10m, 100m),
            Sell(new DateTime(2026, 2, 1), 5m, 150m),
            Buy(new DateTime(2026, 3, 1), 5m, 200m)
        };

        var result = TradeReconstructionService.Reconstruct(trades);

        Assert.Equal(10m, result.CurrentShares);
        Assert.Equal(150m, result.AverageAcquisitionPrice);
        Assert.Equal(250m, result.RealizedGainJpy);
    }

    private static BrokerTradeRecord Buy(DateTime date, decimal quantity, decimal unitPrice) =>
        new()
        {
            TradeDate = date,
            SettlementDate = date.AddDays(2),
            TradeType = "現物買付",
            Quantity = quantity,
            SignedQuantity = quantity,
            UnitPrice = unitPrice,
            ExchangeRate = 1m,
            Currency = "JPY"
        };

    private static BrokerTradeRecord Sell(DateTime date, decimal quantity, decimal unitPrice) =>
        new()
        {
            TradeDate = date,
            SettlementDate = date.AddDays(2),
            TradeType = "現物売却",
            Quantity = quantity,
            SignedQuantity = -quantity,
            UnitPrice = unitPrice,
            ExchangeRate = 1m,
            Currency = "JPY"
        };
}
