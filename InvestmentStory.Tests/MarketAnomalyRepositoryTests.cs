using InvestmentStory.Core.Models;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class MarketAnomalyRepositoryTests
{
    [Fact]
    public void RepeatedDetectionKeepsOneCandidateAndPreservesEveryReviewDecision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_anomaly_{Guid.NewGuid():N}.db");
        try
        {
            var database = new InvestmentStoryRepository(path);
            database.Initialize();
            var stockId = database.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "ABC", Name = "Example", Currency = "USD" },
                CurrentHolding = new CurrentHolding { CurrentShares = 1m, CurrentPrice = 100m }
            });
            var candidate = new MarketAnomalyCandidate
            {
                StockId = stockId,
                Category = "株価急変",
                PreviousValue = "100",
                IncomingValue = "200",
                Evidence = "前回API → 今回API",
                SuggestedCheck = "分割の有無",
                ObservedAt = new DateTime(2026, 10, 27)
            };
            var repository = new MarketAnomalyRepository(path);

            repository.RecordCandidates([candidate, candidate]);
            var stored = Assert.Single(repository.GetCandidates(stockId, includeResolved: false));
            Assert.Equal("Pending", stored.Status);

            repository.Review(stored.Id, "Confirmed", "分割なし。出所を再確認した。");
            repository.Review(stored.Id, "Pending", "別市場の価格の可能性が出たため再調査");
            repository.RecordCandidates([candidate]);

            var reopened = Assert.Single(new MarketAnomalyRepository(path).GetCandidates(stockId));
            Assert.Equal(stored.Id, reopened.Id);
            Assert.Equal("Pending", reopened.Status);
            Assert.Equal(new[] { "Confirmed", "Pending" },
                repository.GetDecisions(stored.Id).Select(x => x.Decision));
            Assert.Contains("別市場", repository.GetDecisions(stored.Id)[1].Reason);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SeparateTradesWithSameAmountsKeepSeparateCandidates()
    {
        var first = new MarketAnomalyCandidate
        {
            StockId = 3, Category = "数量・分割候補", PreviousValue = "100",
            IncomingValue = "200", Evidence = "trade.csv:14", ObservedAt = new DateTime(2026, 4, 1)
        };
        var second = new MarketAnomalyCandidate
        {
            StockId = 3, Category = "数量・分割候補", PreviousValue = "100",
            IncomingValue = "200", Evidence = "trade.csv:25", ObservedAt = new DateTime(2026, 8, 1)
        };
        Assert.NotEqual(MarketAnomalyRepository.ComputeFingerprint(first),
            MarketAnomalyRepository.ComputeFingerprint(second));
    }
}
