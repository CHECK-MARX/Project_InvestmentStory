using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public void SavePosition_UpsertsMutualFundHoldingByPositionIdentity()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_fund_upsert_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();

            var first = CreateMutualFundPosition(
                unitsHeld: 411_318m,
                averageCostNav: 29_499m,
                currentNav: 40_579m,
                acquisitionAmount: 1_213_346m,
                marketValue: 1_669_087m,
                unrealizedGainLoss: 455_741m);
            var second = CreateMutualFundPosition(
                unitsHeld: 411_318m,
                averageCostNav: 29_499m,
                currentNav: 41_000m,
                acquisitionAmount: 1_213_346m,
                marketValue: 1_686_405m,
                unrealizedGainLoss: 473_059m);

            var firstId = repository.SavePosition(first);
            var secondId = repository.SavePosition(second);

            var funds = repository.GetPositions()
                .Where(x => x.Stock.AssetType == AssetTypes.MutualFund && x.Stock.Ticker == "FUND:SBI-V-SP500")
                .ToList();

            Assert.Equal(firstId, secondId);
            var loaded = Assert.Single(funds);
            Assert.Equal(411_318m, loaded.MutualFund.UnitsHeld);
            Assert.Equal(41_000m, loaded.MutualFund.CurrentNav);
            Assert.Equal(1_686_405m, loaded.MutualFund.MarketValue);
            Assert.Equal(473_059m, loaded.MutualFund.UnrealizedGainLoss);
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
    public void SavePosition_UpsertsMutualFundHoldingByCanonicalFundCode_WhenDisplayNameChanges()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_fund_canonical_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();

            var first = CreateMutualFundPosition(
                unitsHeld: 411_318m,
                averageCostNav: 29_499m,
                currentNav: 40_579m,
                acquisitionAmount: 1_213_346m,
                marketValue: 1_669_087m,
                unrealizedGainLoss: 455_741m);
            first.Stock.Name = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド";
            first.Stock.Ticker = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド";
            first.MutualFund.FundName = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド";
            first.MutualFund.FundCode = "SBI-V-SP500";

            var second = CreateMutualFundPosition(
                unitsHeld: 420_000m,
                averageCostNav: 30_000m,
                currentNav: 41_000m,
                acquisitionAmount: 1_260_000m,
                marketValue: 1_722_000m,
                unrealizedGainLoss: 462_000m);
            second.Stock.Name = "SBI V S&P500 Index Fund";
            second.Stock.Ticker = "SBI V S&P500 Index Fund";
            second.MutualFund.FundName = "SBI V S&P500 Index Fund";
            second.MutualFund.FundCode = "SBI-V-SP500";

            var firstId = repository.SavePosition(first);
            var secondId = repository.SavePosition(second);

            var loadedFunds = repository.GetPositions()
                .Where(x => x.Stock.AssetType == AssetTypes.MutualFund &&
                            x.MutualFund.FundCode == "SBI-V-SP500")
                .ToList();

            Assert.Equal(firstId, secondId);
            var loaded = Assert.Single(loadedFunds);
            Assert.Equal(420_000m, loaded.MutualFund.UnitsHeld);
            Assert.Equal(41_000m, loaded.MutualFund.CurrentNav);
            Assert.Equal(1_722_000m, loaded.MutualFund.MarketValue);
            Assert.StartsWith("FUND:", loaded.Stock.CanonicalSecurityKey, StringComparison.Ordinal);
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
    public void SavePosition_UpsertsMutualFundHolding_WhenStoredCanonicalKeyUsesLegacyFormat()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_fund_legacy_key_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();

            var first = CreateMutualFundPosition(
                unitsHeld: 411_318m,
                averageCostNav: 29_499m,
                currentNav: 40_579m,
                acquisitionAmount: 1_213_346m,
                marketValue: 1_669_087m,
                unrealizedGainLoss: 455_741m);
            var second = CreateMutualFundPosition(
                unitsHeld: 411_318m,
                averageCostNav: 29_499m,
                currentNav: 41_000m,
                acquisitionAmount: 1_213_346m,
                marketValue: 1_686_405m,
                unrealizedGainLoss: 473_059m);

            var firstId = repository.SavePosition(first);
            SetStockCanonicalKey(databasePath, firstId, "FUND:JP:FUND:SBI-V-SP500");

            var secondId = repository.SavePosition(second);

            var funds = repository.GetPositions()
                .Where(x => x.Stock.AssetType == AssetTypes.MutualFund &&
                            x.Stock.Broker == "SBI" &&
                            x.Stock.AccountType == AccountTypes.NisaGrowth &&
                            x.Stock.CustodyType == AccountTypes.NisaGrowth &&
                            x.MutualFund.FundCode == "FUND:SBI-V-SP500")
                .ToList();

            Assert.Equal(firstId, secondId);
            var loaded = Assert.Single(funds);
            Assert.Equal(411_318m, loaded.MutualFund.UnitsHeld);
            Assert.Equal(41_000m, loaded.MutualFund.CurrentNav);
            Assert.Equal(1_686_405m, loaded.MutualFund.MarketValue);
            Assert.Equal(473_059m, loaded.MutualFund.UnrealizedGainLoss);
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
    public void SaveBrokerTrades_MatchesExistingUsPosition_WhenCsvHasNoMarketSegment()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_trade_market_fallback_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();

            repository.SavePosition(new StockPosition
            {
                Stock = new Stock
                {
                    Name = "Beyond Meat",
                    Ticker = "BYND",
                    Country = "米国",
                    Currency = "USD",
                    Broker = "野村証券",
                    AccountType = AccountTypes.Specific,
                    CustodyType = "特定",
                    Market = "NASDAQ",
                    DataSource = "Nomura Holdings CSV"
                },
                Purchase = new Purchase
                {
                    PurchaseDate = new DateTime(2021, 11, 5),
                    Shares = 20m,
                    UnitPrice = 85.25m,
                    ExchangeRate = 114.98m
                },
                Split = new StockSplit { SplitRatio = 1m },
                CurrentHolding = new CurrentHolding
                {
                    CurrentShares = 20m,
                    CurrentPrice = 0.73m,
                    CurrentExchangeRate = 162m
                }
            });

            var records = new[]
            {
                new BrokerTradeRecord
                {
                    Broker = "野村証券",
                    Account = "特定",
                    Ticker = "BYND",
                    Name = "ビヨンド ミート インク",
                    TradeType = "現物買付",
                    TradeDate = new DateTime(2021, 11, 5),
                    SettlementDate = new DateTime(2021, 11, 9),
                    Quantity = 10m,
                    SignedQuantity = 10m,
                    UnitPrice = 104.51m,
                    SettlementAmountJpy = 124_055m,
                    FeeJpy = 2_389m,
                    ExchangeRate = 114.23m,
                    Currency = "USD",
                    Source = "野村取引履歴CSV"
                },
                new BrokerTradeRecord
                {
                    Broker = "野村証券",
                    Account = "特定",
                    Ticker = "BYND",
                    Name = "ビヨンド ミート インク",
                    TradeType = "現物買付",
                    TradeDate = new DateTime(2022, 1, 12),
                    SettlementDate = new DateTime(2022, 1, 14),
                    Quantity = 10m,
                    SignedQuantity = 10m,
                    UnitPrice = 66m,
                    SettlementAmountJpy = 81_085m,
                    FeeJpy = 2_389m,
                    ExchangeRate = 115.73m,
                    Currency = "USD",
                    Source = "野村取引履歴CSV"
                }
            };

            repository.SaveBrokerTrades(new[] { records[1] });
            repository.SaveBrokerTrades(records);

            var position = repository.GetPositions().Single(x =>
                x.Stock.Ticker == "BYND" &&
                x.Stock.Broker == "野村証券" &&
                x.Stock.AccountType == AccountTypes.Specific);
            var trades = repository.GetBrokerTrades(position.Stock.Id)
                .OrderBy(x => x.TradeDate)
                .ToList();

            Assert.Equal(2, trades.Count);
            Assert.Equal(new DateTime(2021, 11, 5), trades[0].TradeDate);
            Assert.Equal(104.51m, trades[0].UnitPrice);
            Assert.Equal(10m, trades[0].AfterTradeQuantity);
            Assert.Equal(new DateTime(2022, 1, 12), trades[1].TradeDate);
            Assert.Equal(66m, trades[1].UnitPrice);
            Assert.Equal(20m, trades[1].AfterTradeQuantity);
            Assert.Equal(85.255m, trades[1].AfterTradeAverageCost, precision: 3);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static StockPosition CreateMutualFundPosition(
        decimal unitsHeld,
        decimal averageCostNav,
        decimal currentNav,
        decimal acquisitionAmount,
        decimal marketValue,
        decimal unrealizedGainLoss)
    {
        return new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund,
                Name = "SBI V S&P500 Index Fund",
                Ticker = "FUND:SBI-V-SP500",
                Country = "Japan",
                Currency = "JPY",
                Broker = "SBI",
                AccountType = AccountTypes.NisaGrowth,
                CustodyType = AccountTypes.NisaGrowth,
                Sector = "MutualFund",
                DataSource = "SBI CSV"
            },
            Purchase = new Purchase
            {
                PurchaseDate = new DateTime(2026, 7, 11),
                Shares = unitsHeld,
                UnitPrice = averageCostNav,
                ExchangeRate = 1m,
                ExchangeRateSource = "SBI CSV",
                ExchangeRateInputType = "CSV"
            },
            Split = new StockSplit
            {
                SplitDate = new DateTime(2026, 7, 11),
                SplitRatio = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = unitsHeld,
                CurrentPrice = currentNav,
                CurrentExchangeRate = 1m,
                CurrentPriceSource = "SBI CSV",
                CurrentPriceAcquiredAt = new DateTime(2026, 7, 11),
                UpdatedAt = new DateTime(2026, 7, 11)
            },
            MutualFund = new MutualFundHolding
            {
                FundName = "SBI V S&P500 Index Fund",
                UnitsHeld = unitsHeld,
                UnitBase = 10_000m,
                AverageCostNav = averageCostNav,
                CurrentNav = currentNav,
                AcquisitionAmount = acquisitionAmount,
                MarketValue = marketValue,
                UnrealizedGainLoss = unrealizedGainLoss,
                NavDate = new DateTime(2026, 7, 11),
                NavSource = "SBI CSV",
                DistributionMethod = "Reinvest",
                AccountType = AccountTypes.NisaGrowth,
                TotalPurchaseAmount = acquisitionAmount
            }
        };
    }

    private static void SetStockCanonicalKey(string databasePath, int stockId, string canonicalKey)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Stocks
            SET CanonicalSecurityKey = $canonicalKey
            WHERE Id = $stockId;
            """;
        command.Parameters.AddWithValue("$canonicalKey", canonicalKey);
        command.Parameters.AddWithValue("$stockId", stockId);
        command.ExecuteNonQuery();
    }
}
