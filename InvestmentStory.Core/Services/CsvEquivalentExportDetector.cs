using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class CsvEquivalentExportDetector
{
    // A whole-export match is only used to stop a batch for review. Small exports
    // may contain genuinely distinct, identical transactions, so leave those alone.
    public static bool AreEquivalentTransactions(BrokerStatementImport first, BrokerStatementImport second)
    {
        if (first.Holdings.Count != 0 || second.Holdings.Count != 0) return false;
        if (first.Trades.Count + first.Dividends.Count < 10 ||
            second.Trades.Count + second.Dividends.Count < 10) return false;
        if (first.IgnoredRowCount != second.IgnoredRowCount) return false;
        if (first.SourceRowAudits.Values.SingleOrDefault() is not { } firstAudit ||
            second.SourceRowAudits.Values.SingleOrDefault() is not { } secondAudit ||
            firstAudit.InputRows != secondAudit.InputRows ||
            firstAudit.ParsedRows != secondAudit.ParsedRows ||
            firstAudit.PendingRows != secondAudit.PendingRows ||
            firstAudit.ExcludedRows != secondAudit.ExcludedRows) return false;

        return SameMultiset(first.Trades.Select(TradeKey), second.Trades.Select(TradeKey)) &&
               SameMultiset(first.Dividends.Select(DividendKey), second.Dividends.Select(DividendKey)) &&
               SameMultiset(first.UnresolvedTrades.Select(TradeKey), second.UnresolvedTrades.Select(TradeKey)) &&
               SameMultiset(first.UnresolvedDividends.Select(row => JsonSerializer.Serialize(row)),
                   second.UnresolvedDividends.Select(row => JsonSerializer.Serialize(row)));
    }

    private static bool SameMultiset(IEnumerable<string> first, IEnumerable<string> second) =>
        first.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
            second.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

    private static string TradeKey(BrokerTradeRecord row) => JsonSerializer.Serialize(new
    {
        row.TradeDate, row.SettlementDate, row.Broker, row.Account, row.Product,
        row.TradeType, row.Ticker, row.Name, row.Quantity, row.SignedQuantity,
        row.UnitPrice, row.SettlementAmountJpy, row.FeeJpy, row.ExchangeRate,
        row.Currency, row.ProfitLossJpy, row.Source
    });

    private static string DividendKey(BrokerDividendRecord row) => JsonSerializer.Serialize(new
    {
        row.PaymentDate, row.Broker, row.Account, row.Product, row.Ticker,
        row.Name, row.Quantity, row.GrossAmount, row.TaxAmount, row.NetAmount,
        row.Currency, row.ExchangeRate, row.NetAmountJpy, row.Source
    });
}
