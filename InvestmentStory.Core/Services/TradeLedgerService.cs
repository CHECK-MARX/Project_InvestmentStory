using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class TradeLedgerService
{
    public IReadOnlyList<BrokerTrade> BuildLedger(int stockId, IEnumerable<BrokerTradeRecord> trades)
    {
        var ordered = trades
            .OrderBy(x => x.TradeDate)
            .ThenBy(x => x.SettlementDate)
            .ThenBy(x => x.Source)
            .ToList();
        var result = new List<BrokerTrade>();
        var currentQuantity = 0m;
        var remainingCost = 0m;
        var remainingCostJpy = 0m;

        foreach (var trade in ordered)
        {
            var signedQuantity = trade.SignedQuantity != 0m
                ? trade.SignedQuantity
                : InferSignedQuantity(trade);
            var quantity = trade.Quantity > 0m ? trade.Quantity : Math.Abs(signedQuantity);
            var averageCost = currentQuantity > 0m ? remainingCost / currentQuantity : 0m;
            var averageCostJpy = currentQuantity > 0m ? remainingCostJpy / currentQuantity : 0m;
            var realized = 0m;
            var realizedJpy = 0m;

            if (signedQuantity > 0m)
            {
                currentQuantity += signedQuantity;
                remainingCost += signedQuantity * trade.UnitPrice;
                remainingCostJpy += signedQuantity * ToJpyUnitPrice(trade);
            }
            else if (signedQuantity < 0m)
            {
                var sellQuantity = Math.Min(Math.Abs(signedQuantity), currentQuantity);
                if (trade.ProfitLossJpy != 0m)
                {
                    realizedJpy = trade.ProfitLossJpy;
                    realized = trade.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) || trade.ExchangeRate <= 0m
                        ? realizedJpy
                        : realizedJpy / trade.ExchangeRate;
                }
                else if (sellQuantity > 0m)
                {
                    realized = (trade.UnitPrice - averageCost) * sellQuantity;
                    realizedJpy = (ToJpyUnitPrice(trade) - averageCostJpy) * sellQuantity;
                }

                remainingCost -= averageCost * sellQuantity;
                remainingCostJpy -= averageCostJpy * sellQuantity;
                currentQuantity -= sellQuantity;
                if (currentQuantity <= 0m)
                {
                    currentQuantity = 0m;
                    remainingCost = 0m;
                    remainingCostJpy = 0m;
                }
            }

            result.Add(new BrokerTrade
            {
                StockId = stockId,
                TradeDate = trade.TradeDate,
                SettlementDate = trade.SettlementDate == DateTime.MinValue ? trade.TradeDate : trade.SettlementDate,
                Broker = trade.Broker,
                AccountType = AccountTypeNormalizer.Normalize(trade.Account),
                CustodyType = trade.Account,
                TradeType = NormalizeTradeType(trade.TradeType),
                Quantity = quantity,
                SignedQuantity = signedQuantity,
                UnitPrice = trade.UnitPrice,
                Currency = NormalizeCurrency(trade.Currency),
                ExchangeRate = trade.ExchangeRate <= 0m ? 1m : trade.ExchangeRate,
                SettlementAmountJpy = trade.SettlementAmountJpy,
                FeeJpy = trade.FeeJpy,
                RealizedGainLoss = realized,
                RealizedGainLossJpy = realizedJpy,
                AfterTradeQuantity = currentQuantity,
                AfterTradeAverageCost = currentQuantity > 0m ? remainingCost / currentQuantity : 0m,
                Source = string.IsNullOrWhiteSpace(trade.Source) ? "取引履歴CSV" : trade.Source
            });
        }

        return result;
    }

    private static decimal InferSignedQuantity(BrokerTradeRecord trade)
    {
        if (trade.TradeType.Contains("売", StringComparison.Ordinal))
        {
            return -Math.Abs(trade.Quantity);
        }

        return Math.Abs(trade.Quantity);
    }

    private static decimal ToJpyUnitPrice(BrokerTradeRecord trade)
    {
        if (trade.UnitPrice <= 0m)
        {
            return 0m;
        }

        return NormalizeCurrency(trade.Currency) == "JPY"
            ? trade.UnitPrice
            : trade.ExchangeRate > 0m ? trade.UnitPrice * trade.ExchangeRate : 0m;
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "円" ? "JPY" : normalized;
    }

    private static string NormalizeTradeType(string tradeType)
    {
        if (tradeType.Contains("買", StringComparison.Ordinal))
        {
            return "買付";
        }

        if (tradeType.Contains("売", StringComparison.Ordinal))
        {
            return "売却";
        }

        if (tradeType.Contains("入庫", StringComparison.Ordinal))
        {
            return "入庫";
        }

        if (tradeType.Contains("出庫", StringComparison.Ordinal))
        {
            return "出庫";
        }

        if (tradeType.Contains("分配", StringComparison.Ordinal) || tradeType.Contains("再投資", StringComparison.Ordinal))
        {
            return "分配金再投資";
        }

        return string.IsNullOrWhiteSpace(tradeType) ? "その他" : tradeType;
    }
}
