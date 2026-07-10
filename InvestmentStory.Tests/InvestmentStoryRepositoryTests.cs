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

    [Fact]
    public void SaveSettings_PersistsUiPreferences()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_settings_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();

            var settings = repository.GetSettings();
            settings.ThemeMode = "Dark";
            settings.IsSidebarCollapsed = true;
            settings.StockListDisplayMode = "配当";
            settings.LastDashboardCompositionMode = "Broker";
            settings.LastOpenedPage = "銘柄一覧";

            repository.SaveSettings(settings);

            var reloaded = new InvestmentStoryRepository(databasePath);
            var loaded = reloaded.GetSettings();

            Assert.Equal("Dark", loaded.ThemeMode);
            Assert.True(loaded.IsSidebarCollapsed);
            Assert.Equal("配当", loaded.StockListDisplayMode);
            Assert.Equal("Broker", loaded.LastDashboardCompositionMode);
            Assert.Equal("銘柄一覧", loaded.LastOpenedPage);
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
