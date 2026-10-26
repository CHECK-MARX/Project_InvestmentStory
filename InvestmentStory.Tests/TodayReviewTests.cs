using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class TodayReviewTests
{
    private readonly DateTime _today = new(2026, 10, 24);
    private readonly TodayReviewService _service = new();
    private static DividendPayment Payment(int id, int stockId, DateTime date, string status, decimal net) => new()
    {
        Id = id, StockId = stockId, Ticker = $"S{stockId}", PaymentDate = date,
        DividendStatus = status, NetAmountJpy = net, GrossAmountJpy = net,
        NetAmount = net, GrossAmount = net, Currency = "JPY", Broker = "証券A",
        AccountType = "Specific"
    };
    private static StockSnapshot Holding(int id, decimal value, bool knownCost, bool knownValue = true, decimal shares = 1m) => new()
    {
        Position = new StockPosition
        {
            Stock = new Stock { Id = id, Ticker = $"S{id}" },
            CurrentHolding = new CurrentHolding { CurrentShares = shares }
        },
        HasKnownPurchaseCostJpy = knownCost, HasKnownCurrentMarketValueJpy = knownValue,
        CurrentMarketValueJpy = value
    };

    [Fact]
    public void MatchesDividendActualsAndKeepsPriorYearOverdueWithoutDuplication()
    {
        var plan = Payment(1, 1, new DateTime(2026, 8, 10), DividendConstants.Planned, 100m);
        var actual = Payment(2, 1, new DateTime(2026, 8, 12), DividendConstants.Actual, 100m);
        plan.MatchedActualDividendId = actual.Id;
        var old = Payment(3, 2, new DateTime(2025, 12, 10), DividendConstants.Planned, 200m);
        var result = _service.Build([], [plan, actual, old], [], new CsvReviewSummary(0, 0, 0), _today);
        var card = result.Cards.Single(x => x.Kind == TodayReviewKind.Dividend);
        Assert.Equal(1, card.Count);
        Assert.Equal(new ReminderService().Build([plan, actual, old], [], _today)
            .Count(x => x.Kind == ReminderKind.DividendOverdue), card.Count);
        Assert.Contains("200円", card.Impact);
    }

    [Fact]
    public void CostCardOnlyCountsActiveUnknownsAndDoesNotInventImpact()
    {
        var result = _service.Build([
            Holding(1, 100m, false), Holding(2, 200m, false, knownValue: false),
            Holding(3, 900m, false, shares: 0m), Holding(4, 50m, true)],
            [], [], new CsvReviewSummary(0, 0, 0), _today);
        var card = result.Cards.Single(x => x.Kind == TodayReviewKind.Cost);
        Assert.Equal(2, card.Count);
        Assert.Contains("算出不可", card.Impact);
        Assert.DoesNotContain("S3", card.Detail);
    }

    [Fact]
    public void LaterSuccessClearsOnlySameApiTargetFailure()
    {
        var logs = new[]
        {
            new ApiFetchLogEntry { Id = 1, ApiType = "Price", Provider = "Provider", Symbol = "ABC", IsSuccess = false, FetchedAt = _today.AddHours(-2) },
            new ApiFetchLogEntry { Id = 2, ApiType = "Price", Provider = "Provider", Symbol = "abc", IsSuccess = true, FetchedAt = _today.AddHours(-1) },
            new ApiFetchLogEntry { Id = 3, ApiType = "FX", Provider = "Provider", Symbol = "USDJPY", IsSuccess = false, FetchedAt = _today }
        };
        var result = _service.Build([], [], logs, new CsvReviewSummary(0, 0, 0), _today);
        var card = result.Cards.Single(x => x.Kind == TodayReviewKind.Update);
        Assert.Equal(1, card.Count);
        Assert.Equal(new ReminderService().Build([], logs, _today)
            .Count(x => x.Kind == ReminderKind.UpdateFailure), card.Count);
        Assert.Contains("USDJPY", card.Detail);
        Assert.DoesNotContain("ABC", card.Detail);
        Assert.Equal("3", Assert.Single(card.Items).SourceKey);
    }

    [Fact]
    public void DrilldownKeepsExactPositionAndDividendKeysAndExcludesFutureDue()
    {
        var holding = Holding(7, 500m, false);
        holding.Position.Stock.Broker = "証券A";
        holding.Position.Stock.AccountType = "Specific";
        var overdue = Payment(31, 7, _today.AddDays(-2), DividendConstants.Planned, 100m);
        var future = Payment(32, 7, _today.AddDays(2), DividendConstants.PaymentDue, 100m);
        var result = _service.Build([holding], [overdue, future], [],
            new CsvReviewSummary(0, 0, 0), _today);
        var cost = result.Cards.Single(x => x.Kind == TodayReviewKind.Cost);
        Assert.Equal(SecurityIdentityService.BuildPositionKey(holding.Position), Assert.Single(cost.Items).SourceKey);
        var dividend = result.Cards.Single(x => x.Kind == TodayReviewKind.Dividend);
        Assert.Equal("31", Assert.Single(dividend.Items).SourceKey);
        Assert.Equal(1, dividend.Count);
    }

    [Fact]
    public void CsvSummarySeparatesUnresolvedTradesFromHistoricalReviewRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryReview_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "test.db");
            new DatabaseInitializer().Initialize(path);
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO CsvImportReviewRows
                    (Broker,Account,Ticker,TradeDate,TradeType,Quantity,SignedQuantity,UnitPrice,Currency,Source,Reason,Resolution)
                    VALUES ('A','特定','AAA','2026-10-01','Buy',1,1,10,'USD','csv','要確認',''),
                           ('A','特定','BBB','2026-10-02','Buy',1,1,10,'USD','csv','要確認','既存と同一');
                    INSERT INTO CsvImportReviewHoldings
                    (Broker,Account,Ticker,SecurityName,AssetType,Currency,SnapshotDate,Shares,Reason)
                    VALUES ('A','特定','CCC','CCC','Stock','USD','2026-10-01',1,'要確認');
                    INSERT INTO CsvImportReviewDividends
                    (PaymentDate,Broker,SecurityName,Product,Quantity,GrossAmount,TaxAmount,Source,Reason)
                    VALUES ('2026-10-01','A','DDD','Stock',1,10,1,'csv','要確認');
                    """;
                command.ExecuteNonQuery();
            }
            var repository = new InvestmentStoryRepository(path);
            var summary = repository.GetCsvReviewSummary();
            var records = repository.GetCsvReviewRecords();
            Assert.Equal(new CsvReviewSummary(1, 1, 1), summary);
            Assert.Equal(3, records.Count);
            Assert.Contains(records, x => x.SourceKey.StartsWith("trade:") && x.Status == "未解決");
            Assert.DoesNotContain(records, x => x.Security == "BBB");
            var card = _service.Build([], [], [], summary, _today, records).Cards.Single(x => x.Kind == TodayReviewKind.Csv);
            Assert.Equal(3, card.Count);
            Assert.Contains("要確認履歴", card.Detail);
            Assert.Equal(3, card.Items.Count);
        }
        finally { Directory.Delete(directory, true); }
    }
}
