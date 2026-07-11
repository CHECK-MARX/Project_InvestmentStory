using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
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

    [Fact]
    public void SavePosition_PersistsMutualFundHolding()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_fund_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();

            var position = new StockPosition
            {
                Stock = new Stock
                {
                    AssetType = AssetTypes.MutualFund,
                    Name = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                    Ticker = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                    Country = "日本",
                    Currency = "JPY",
                    Broker = "SBI証券",
                    Sector = "投資信託",
                    DataSource = "SBI投信保有CSV"
                },
                Purchase = new Purchase
                {
                    Shares = 411318m,
                    UnitPrice = 29499m,
                    ExchangeRate = 1m,
                    ExchangeRateSource = "SBI投信保有CSV",
                    ExchangeRateInputType = "CSV"
                },
                Split = new StockSplit { SplitRatio = 1m },
                CurrentHolding = new CurrentHolding
                {
                    CurrentShares = 411318m,
                    CurrentPrice = 40579m,
                    CurrentExchangeRate = 1m,
                    CurrentPriceSource = "SBI投信保有CSV",
                    CurrentPriceAcquiredAt = new DateTime(2026, 7, 11),
                    DividendStatus = "再投資"
                },
                MutualFund = new MutualFundHolding
                {
                    FundName = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                    FundCode = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                    UnitsHeld = 411318m,
                    UnitBase = 10000m,
                    AverageCostNav = 29499m,
                    CurrentNav = 40579m,
                    AcquisitionAmount = 1_213_346m,
                    MarketValue = 1_669_087m,
                    UnrealizedGainLoss = 455_741m,
                    NavDate = new DateTime(2026, 7, 11),
                    NavSource = "SBI投信保有CSV",
                    DistributionMethod = "再投資",
                    AccountType = "NISA"
                }
            };

            repository.SavePosition(position);

            var loaded = repository.GetPositions().Single(x => x.Stock.AssetType == AssetTypes.MutualFund);
            var snapshot = new InvestmentCalculator().CreateSnapshot(loaded);

            Assert.True(loaded.IsMutualFund);
            Assert.Equal(411318m, loaded.MutualFund.UnitsHeld);
            Assert.Equal(29499m, loaded.MutualFund.AverageCostNav);
            Assert.Equal(40579m, loaded.MutualFund.CurrentNav);
            Assert.Equal(1_213_346m, snapshot.PurchaseTotalJpy);
            Assert.Equal(1_669_087m, snapshot.CurrentMarketValueJpy);
            Assert.Equal(455_741m, snapshot.UnrealizedGainJpy);
            Assert.Equal(37.56m, snapshot.UnrealizedGainRateJpy, precision: 2);
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
