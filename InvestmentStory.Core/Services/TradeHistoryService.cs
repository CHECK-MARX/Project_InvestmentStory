using System.Globalization;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class TradeHistoryService
{
    public static IReadOnlyList<BrokerTradeRecord> Merge(
        IEnumerable<BrokerTrade> existing,
        IEnumerable<BrokerTradeRecord> incoming,
        string ticker)
    {
        var imported = existing
            .Where(x => !(x.TradeType == "InitialPosition" && x.Source == "HoldingsSnapshot"))
            .Select(x => new BrokerTradeRecord
            {
                TradeDate = x.TradeDate,
                SettlementDate = x.SettlementDate,
                Broker = x.Broker,
                Account = x.CustodyType,
                Ticker = ticker,
                TradeType = x.TradeType,
                Quantity = x.Quantity,
                SignedQuantity = x.SignedQuantity,
                UnitPrice = x.UnitPrice,
                Currency = x.Currency,
                ExchangeRate = x.ExchangeRate,
                SettlementAmountJpy = x.SettlementAmountJpy,
                FeeJpy = x.FeeJpy,
                Source = x.Source
            });

        // Broker event identity does not depend on export filename or source label.
        return imported.Concat(incoming)
            .GroupBy(x => string.Join("|",
                x.TradeDate.Date.ToString("yyyyMMdd"),
                (x.SettlementDate == DateTime.MinValue ? x.TradeDate : x.SettlementDate).Date.ToString("yyyyMMdd"),
                PositionIdentityService.NormalizeBroker(x.Broker),
                AccountTypeNormalizer.Normalize(x.Account),
                NormalizeType(x.TradeType),
                NormalizeDecimal(x.SignedQuantity),
                NormalizeDecimal(x.Quantity),
                NormalizeDecimal(decimal.Round(x.UnitPrice, 8, MidpointRounding.AwayFromZero)),
                NormalizeDecimal(x.SettlementAmountJpy),
                PositionIdentityService.NormalizeCurrency(x.Currency)), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.TradeDate)
            .ThenBy(x => x.SettlementDate)
            .ToList();
    }

    private static string NormalizeType(string type) =>
        type.Contains("買", StringComparison.Ordinal) || type.Equals("Buy", StringComparison.OrdinalIgnoreCase) ? "Buy" :
        type.Contains("売", StringComparison.Ordinal) || type.Equals("Sell", StringComparison.OrdinalIgnoreCase) ? "Sell" :
        type.Contains("入庫", StringComparison.Ordinal) ||
        type.Equals("TransferIn", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("StockSplit", StringComparison.OrdinalIgnoreCase) ? "TransferIn" :
        type;

    private static string NormalizeDecimal(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
