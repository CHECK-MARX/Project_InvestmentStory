using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class TradeReconstructionService
{
    public static TradeReconstructionResult Reconstruct(IReadOnlyList<BrokerTradeRecord> trades)
    {
        var currentShares = 0m;
        var remainingCost = 0m;
        var remainingCostJpy = 0m;
        var realizedGain = 0m;
        var realizedGainJpy = 0m;
        var incompleteSellCount = 0;

        foreach (var trade in trades.OrderBy(x => x.TradeDate).ThenBy(x => x.SettlementDate))
        {
            if (trade.SignedQuantity > 0m)
            {
                currentShares += trade.SignedQuantity;
                if (trade.UnitPrice > 0m)
                {
                    remainingCost += trade.SignedQuantity * trade.UnitPrice;
                    remainingCostJpy += trade.SignedQuantity * ToJpyUnitPrice(trade);
                }

                continue;
            }

            if (trade.SignedQuantity >= 0m)
            {
                continue;
            }

            var sellQuantity = Math.Abs(trade.SignedQuantity);
            if (currentShares <= 0m)
            {
                incompleteSellCount++;
                continue;
            }

            var averageCost = currentShares > 0m ? remainingCost / currentShares : 0m;
            var averageCostJpy = currentShares > 0m ? remainingCostJpy / currentShares : 0m;
            var matchedQuantity = Math.Min(sellQuantity, currentShares);
            if (trade.UnitPrice > 0m && averageCost > 0m)
            {
                realizedGain += (trade.UnitPrice - averageCost) * matchedQuantity;
            }

            var sellUnitPriceJpy = ToJpyUnitPrice(trade);
            if (sellUnitPriceJpy > 0m && averageCostJpy > 0m)
            {
                realizedGainJpy += (sellUnitPriceJpy - averageCostJpy) * matchedQuantity;
            }

            remainingCost -= averageCost * matchedQuantity;
            remainingCostJpy -= averageCostJpy * matchedQuantity;
            currentShares -= matchedQuantity;
            if (sellQuantity > matchedQuantity)
            {
                incompleteSellCount++;
            }

            if (currentShares <= 0m)
            {
                currentShares = 0m;
                remainingCost = 0m;
                remainingCostJpy = 0m;
            }
        }

        return new TradeReconstructionResult
        {
            CurrentShares = currentShares,
            AverageAcquisitionPrice = currentShares > 0m ? remainingCost / currentShares : 0m,
            AverageAcquisitionPriceJpy = currentShares > 0m ? remainingCostJpy / currentShares : 0m,
            RealizedGain = realizedGain,
            RealizedGainJpy = realizedGainJpy,
            IncompleteSellCount = incompleteSellCount
        };
    }

    private static decimal ToJpyUnitPrice(BrokerTradeRecord trade)
    {
        if (trade.UnitPrice <= 0m)
        {
            return 0m;
        }

        return trade.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? trade.UnitPrice
            : trade.ExchangeRate > 0m ? trade.UnitPrice * trade.ExchangeRate : 0m;
    }
}

public sealed class TradeReconstructionResult
{
    public decimal CurrentShares { get; init; }
    public decimal AverageAcquisitionPrice { get; init; }
    public decimal AverageAcquisitionPriceJpy { get; init; }
    public decimal RealizedGain { get; init; }
    public decimal RealizedGainJpy { get; init; }
    public int IncompleteSellCount { get; init; }
}
