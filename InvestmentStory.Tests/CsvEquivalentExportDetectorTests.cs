using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class CsvEquivalentExportDetectorTests
{
    [Fact]
    public void ReorderedWholeExport_WithDifferentSourceRows_IsFlagged()
    {
        var first = Statement("受渡し.csv", Enumerable.Range(1, 10).ToArray());
        var second = Statement("約定.csv", Enumerable.Range(1, 10).Reverse().ToArray());

        Assert.True(CsvEquivalentExportDetector.AreEquivalentTransactions(first, second));
    }

    [Fact]
    public void ChangedTransactionOrSmallIdenticalExport_IsNotFlagged()
    {
        var first = Statement("受渡し.csv", Enumerable.Range(1, 10).ToArray());
        var changed = Statement("約定.csv", Enumerable.Range(1, 9).Append(11).ToArray());
        var smallA = Statement("小口A.csv", new[] { 1 });
        var smallB = Statement("小口B.csv", new[] { 1 });

        Assert.False(CsvEquivalentExportDetector.AreEquivalentTransactions(first, changed));
        Assert.False(CsvEquivalentExportDetector.AreEquivalentTransactions(smallA, smallB));
    }

    private static BrokerStatementImport Statement(string file, int[] days) => new()
    {
        Trades = days.Select((day, index) => new BrokerTradeRecord
        {
            TradeDate = new DateTime(2026, 1, day),
            SettlementDate = new DateTime(2026, 1, day),
            Broker = "野村証券", Account = "特定", Product = "株式", TradeType = "Buy",
            Ticker = "1234", Name = "Example", Quantity = 1m, SignedQuantity = 1m,
            UnitPrice = 100m, SettlementAmountJpy = 100m, Source = "Csv",
            SourceFile = file, SourceRowNumber = index + 2
        }).ToArray(),
        SourceRowAudits = new Dictionary<string, CsvSourceRowAudit>
        {
            [file] = new() { InputRows = days.Length, ParsedRows = days.Length }
        }
    };
}
