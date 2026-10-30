using System.Globalization;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MarketAnomalyService
{
    public IReadOnlyList<MarketAnomalyCandidate> EvaluateTrades(int stockId, IReadOnlyList<BrokerTrade> trades)
    {
        var result = new List<MarketAnomalyCandidate>();
        var ordered = trades.OrderBy(x => x.TradeDate).ThenBy(x => x.SettlementDate).ThenBy(x => x.Id).ToList();
        var hasUnverifiedCost = ordered.Any(x => x.TradeType is "TransferIn" or "StockSplitCandidate");
        var oneAccount = ordered.Select(x => (x.Broker, x.AccountType, x.CustodyType)).Distinct().Count() == 1;
        BrokerTrade? previous = null;
        foreach (var trade in ordered)
        {
            var source = string.IsNullOrWhiteSpace(trade.SourceFile)
                ? trade.Source
                : $"{Path.GetFileName(trade.SourceFile)}:{trade.SourceRowNumber}";
            if (trade.TradeType == "StockSplitCandidate" && trade.SignedQuantity > 0m)
            {
                var before = trade.AfterTradeQuantity - trade.SignedQuantity;
                if (before > 0m)
                {
                    result.Add(new MarketAnomalyCandidate
                    {
                        StockId = stockId,
                        Category = "数量・分割候補",
                        PreviousValue = before.ToString(CultureInfo.InvariantCulture),
                        IncomingValue = trade.AfterTradeQuantity.ToString(CultureInfo.InvariantCulture),
                        Evidence = $"{trade.TradeDate:yyyy/MM/dd} {source} / 無償入庫 {trade.SignedQuantity:N4}株 / 数量比 {trade.AfterTradeQuantity / before:N2}",
                        SuggestedCheck = "株式分割・併合の公表内容、口座移管、元CSVの数量を確認してください。確認しても取引数量は自動修正しません。",
                        ObservedAt = trade.TradeDate
                    });
                }
            }

            // Only compare a source-reported sign where the cost basis can be reconstructed
            // without a currency conversion, transfer, or cross-account allocation.
            if (oneAccount && !hasUnverifiedCost && previous is not null &&
                trade.SignedQuantity < 0m && trade.Currency == "JPY" &&
                previous.AfterTradeAverageCost > 0m &&
                previous.AfterTradeQuantity >= -trade.SignedQuantity &&
                trade.UnitPrice > 0m && trade.RealizedGainLossJpy != 0m)
            {
                var theoretical = (trade.UnitPrice - previous.AfterTradeAverageCost) * -trade.SignedQuantity;
                var minimum = Math.Max(1000m, trade.UnitPrice * -trade.SignedQuantity * .05m);
                if (Math.Abs(theoretical) > minimum && Math.Abs(trade.RealizedGainLossJpy) > minimum &&
                    Math.Sign(theoretical) != Math.Sign(trade.RealizedGainLossJpy))
                {
                    result.Add(new MarketAnomalyCandidate
                    {
                        StockId = stockId,
                        Category = "損益符号不一致",
                        PreviousValue = theoretical.ToString(CultureInfo.InvariantCulture),
                        IncomingValue = trade.RealizedGainLossJpy.ToString(CultureInfo.InvariantCulture),
                        Evidence = $"{trade.TradeDate:yyyy/MM/dd} {source} / 平均原価 {previous.AfterTradeAverageCost:N2}円 / 売値 {trade.UnitPrice:N2}円 / 売却 {-trade.SignedQuantity:N4}株 / CSV損益 {trade.RealizedGainLossJpy:N0}円",
                        SuggestedCheck = "CSVの損益符号、手数料・税、取得原価と取引区分を確認してください。確認してもCSV値は自動修正しません。",
                        ObservedAt = trade.TradeDate
                    });
                }
            }
            previous = trade;
        }
        return result;
    }

    public IReadOnlyList<MarketAnomalyCandidate> EvaluateQuote(StockPosition position, MarketDataQuote quote)
    {
        var result = new List<MarketAnomalyCandidate>();
        var stock = position.Stock;
        var holding = position.CurrentHolding;
        var observedAt = quote.PriceAcquiredAt ?? DateTime.Now;
        var oldPrice = holding.CurrentPrice;
        var previousSource = ApiDiagnosticRedactor.Redact(holding.CurrentPriceSource, []);
        var incomingSource = ApiDiagnosticRedactor.Redact(quote.Source, []);
        if (quote.CurrentPrice is > 0m && oldPrice > 0m &&
            (holding.CurrentPriceAcquiredAt == DateTime.MinValue ||
             observedAt > holding.CurrentPriceAcquiredAt) &&
            (quote.CurrentPrice.Value >= oldPrice * 1.5m ||
             quote.CurrentPrice.Value <= oldPrice * .5m))
        {
            var ratio = quote.CurrentPrice.Value / oldPrice;
            result.Add(new MarketAnomalyCandidate
            {
                StockId = stock.Id,
                Category = "株価急変",
                PreviousValue = oldPrice.ToString(CultureInfo.InvariantCulture),
                IncomingValue = quote.CurrentPrice.Value.ToString(CultureInfo.InvariantCulture),
                Evidence = $"前回 {holding.CurrentPriceAcquiredAt:yyyy/MM/dd HH:mm} {previousSource} / " +
                    $"今回 {observedAt:yyyy/MM/dd HH:mm} {incomingSource} / 倍率 {ratio:N2}",
                SuggestedCheck = "株式分割・併合、価格単位、取得元の変更を確認してください。",
                ObservedAt = observedAt
            });
        }

        var currentCurrency = stock.Currency.Trim().ToUpperInvariant();
        var incomingCurrency = quote.Currency.Trim().ToUpperInvariant();
        if (currentCurrency.Length > 0 && incomingCurrency.Length > 0 &&
            !string.Equals(currentCurrency, incomingCurrency, StringComparison.Ordinal))
        {
            result.Add(new MarketAnomalyCandidate
            {
                StockId = stock.Id,
                Category = "通貨不一致",
                PreviousValue = currentCurrency,
                IncomingValue = incomingCurrency,
                Evidence = $"登録通貨 {currentCurrency} / API通貨 {incomingCurrency} / " +
                    $"銘柄 {stock.Ticker} / 取得元 {incomingSource}",
                SuggestedCheck = "ティッカー、取引市場、価格の通貨と為替換算を確認してください。",
                ObservedAt = observedAt
            });
        }
        return result;
    }
}
