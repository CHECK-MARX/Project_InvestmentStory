using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class InvestmentStoryRepositoryTests
{
    [Fact]
    public void Initialize_CreatesSampleData()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_test_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);

            repository.Initialize();

            var positions = repository.GetPositions();
            var dividends = repository.GetDividendPayments();
            var goal = repository.GetGoal(DateTime.Today.Year);

            Assert.Equal(3, positions.Count);
            Assert.Contains(positions, x => x.Stock.Ticker == "NVDA" && x.CurrentHolding.CurrentShares == 50m);
            Assert.Contains(positions, x => x.Stock.Ticker == "TSLA" && x.Split.SplitRatio == 3m);
            Assert.Contains(positions, x => x.Stock.Ticker == "AMD" && x.Purchase.UnitPrice == 150m);
            Assert.Contains(positions, x => x.Stock.Ticker == "NVDA" &&
                x.Purchase.ExchangeRate == 160m &&
                x.Purchase.ExchangeRateSource == "手入力" &&
                x.CurrentHolding.CurrentExchangeRate == 160m &&
                x.CurrentHolding.ExchangeRateInputType == "手入力");
            Assert.NotEmpty(dividends);
            Assert.All(dividends, x =>
            {
                Assert.Equal(160m, x.ExchangeRate);
                Assert.Equal("手入力", x.ExchangeRateInputType);
                Assert.True(x.JpyAmount > 0m);
            });
            Assert.NotNull(goal);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
