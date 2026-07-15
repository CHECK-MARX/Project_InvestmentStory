using InvestmentStory.Core.Models;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class DividendPurchasePlanPersistenceTests
{
    [Fact]
    public void SaveAndReload_PreservesHeaderExistingAccountsAndNewStockFields()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            var plan = CreatePlan();

            var planId = repository.SaveDividendPurchasePlan(plan);
            var loaded = new InvestmentStoryRepository(databasePath).GetLastDividendPurchasePlan();

            Assert.NotNull(loaded);
            Assert.Equal(planId, loaded.Id);
            Assert.Equal("2027年 配当購入計画", loaded.Name);
            Assert.Equal(2027, loaded.TargetYear);
            Assert.Equal(new DateTime(2027, 7, 15), loaded.PlannedPurchaseDate);
            Assert.Equal(DividendPurchasePlanDisplayUnits.Broker, loaded.DisplayUnit);
            Assert.Equal(1_500_000m, loaded.TargetAnnualNetDividendJpy);
            Assert.Equal(3, loaded.Items.Count);

            var nisa = Assert.Single(loaded.Items, item => item.PositionKey == "TRMD|SBI|NisaGrowth");
            Assert.Equal(70m, nisa.CurrentShares);
            Assert.Equal(15m, nisa.PlannedAdditionalShares);
            Assert.Equal("SBI証券", nisa.PlannedBroker);
            Assert.Equal(AccountTypes.NisaGrowth, nisa.PlannedAccountType);

            var specific = Assert.Single(loaded.Items, item => item.PositionKey == "TRMD|SBI|Specific");
            Assert.Equal(42m, specific.CurrentShares);
            Assert.Equal(8m, specific.PlannedAdditionalShares);
            Assert.Equal("野村證券", specific.PlannedBroker);
            Assert.Equal(AccountTypes.Specific, specific.PlannedAccountType);

            var newStock = Assert.Single(loaded.Items, item => item.IsNewStock);
            Assert.Equal("PG", newStock.Ticker);
            Assert.Equal("The Procter & Gamble Company", newStock.Name);
            Assert.Equal("PG|US", newStock.CanonicalSecurityKey);
            Assert.Equal(string.Empty, newStock.PositionKey);
            Assert.Equal("米国", newStock.Country);
            Assert.Equal("USD", newStock.Currency);
            Assert.Equal(12m, newStock.PlannedAdditionalShares);
            Assert.Equal(147.32m, newStock.CurrentPrice);
            Assert.Equal(4.2272m, newStock.AnnualDividendPerShare);
            Assert.Equal("4", newStock.DividendFrequency);
            Assert.Equal("3,6,9,12", newStock.DividendMonths);
            Assert.Equal(new DateTime(2027, 8, 28), newStock.DividendRecordDate);
            Assert.Equal(new DateTime(2027, 8, 27), newStock.ExDividendDate);
            Assert.Equal(new DateTime(2027, 9, 20), newStock.DividendPaymentDate);
            Assert.Equal("Yahoo Finance", newStock.MarketDataSource);
            Assert.Equal("取得済", newStock.MarketDataStatus);
            Assert.Equal("手入力", newStock.DataQuality);
        }
        finally
        {
            DeleteDatabaseAndBackups(databasePath);
        }
    }

    [Fact]
    public void SavingExistingPlan_ReplacesDetailsWithoutDuplicatingAccounts()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            var plan = CreatePlan();
            plan.Id = repository.SaveDividendPurchasePlan(plan);

            plan.Name = "更新後計画";
            plan.Items = plan.Items
                .Select(item =>
                {
                    item.PlannedAdditionalShares += 1m;
                    return item;
                })
                .ToList();
            repository.SaveDividendPurchasePlan(plan);

            var loaded = repository.GetLastDividendPurchasePlan();
            Assert.NotNull(loaded);
            Assert.Equal("更新後計画", loaded.Name);
            Assert.Equal(3, loaded.Items.Count);
            Assert.Equal(2, loaded.Items.Count(item => !item.IsNewStock));
            Assert.Equal(2, loaded.Items.Where(item => !item.IsNewStock).Select(item => item.PositionKey).Distinct().Count());
            Assert.Equal(16m, loaded.Items.Single(item => item.PositionKey == "TRMD|SBI|NisaGrowth").PlannedAdditionalShares);
            Assert.Equal(9m, loaded.Items.Single(item => item.PositionKey == "TRMD|SBI|Specific").PlannedAdditionalShares);
        }
        finally
        {
            DeleteDatabaseAndBackups(databasePath);
        }
    }

    [Fact]
    public void SavingPlan_DoesNotChangeRealPortfolioTradeDividendOrCsvData()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            var tableNames = new[]
            {
                "Stocks", "Purchases", "CurrentHoldings", "DividendPayments", "BrokerTrades", "CsvImportLogs"
            };
            var before = tableNames.ToDictionary(name => name, name => CountRows(databasePath, name));

            repository.SaveDividendPurchasePlan(CreatePlan());

            var after = tableNames.ToDictionary(name => name, name => CountRows(databasePath, name));
            foreach (var tableName in tableNames)
            {
                Assert.Equal(before[tableName], after[tableName]);
            }
            Assert.Equal(1, CountRows(databasePath, "DividendPurchasePlans"));
            Assert.Equal(3, CountRows(databasePath, "DividendPurchasePlanItems"));
        }
        finally
        {
            DeleteDatabaseAndBackups(databasePath);
        }
    }

    [Fact]
    public void Migration_CreatesBackupAndDoesNotChangeExistingBusinessRowCounts()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            var before = new
            {
                Stocks = CountRows(databasePath, "Stocks"),
                Purchases = CountRows(databasePath, "Purchases"),
                Dividends = CountRows(databasePath, "DividendPayments")
            };

            using (var connection = Open(databasePath))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DROP TABLE DividendPurchasePlanItems; DROP TABLE DividendPurchasePlans;";
                command.ExecuteNonQuery();
            }

            repository.Initialize();

            Assert.Equal(before.Stocks, CountRows(databasePath, "Stocks"));
            Assert.Equal(before.Purchases, CountRows(databasePath, "Purchases"));
            Assert.Equal(before.Dividends, CountRows(databasePath, "DividendPayments"));
            Assert.True(TableExists(databasePath, "DividendPurchasePlans"));
            Assert.True(TableExists(databasePath, "DividendPurchasePlanItems"));
            Assert.NotEmpty(FindBackups(databasePath));
        }
        finally
        {
            DeleteDatabaseAndBackups(databasePath);
        }
    }

    private static DividendPurchasePlan CreatePlan() => new()
    {
        Name = "2027年 配当購入計画",
        TargetYear = 2027,
        PlannedPurchaseDate = new DateTime(2027, 7, 15),
        DisplayUnit = DividendPurchasePlanDisplayUnits.Broker,
        TargetAnnualNetDividendJpy = 1_500_000m,
        Items = new List<DividendPurchasePlanItem>
        {
            ExistingItem("TRMD|SBI|NisaGrowth", AccountTypes.NisaGrowth, 70m, 15m, "SBI証券", AccountTypes.NisaGrowth),
            ExistingItem("TRMD|SBI|Specific", AccountTypes.Specific, 42m, 8m, "野村證券", AccountTypes.Specific),
            new()
            {
                ItemOrder = 2,
                IsNewStock = true,
                PlanKey = "New:PG",
                CanonicalSecurityKey = "PG|US",
                Ticker = "PG",
                Name = "The Procter & Gamble Company",
                Broker = "野村證券",
                AccountType = AccountTypes.Specific,
                Country = "米国",
                Currency = "USD",
                CurrentPrice = 147.32m,
                ExchangeRate = 162.15m,
                AnnualDividendPerShare = 4.2272m,
                DividendFrequency = "4",
                DividendMonths = "3,6,9,12",
                DividendRecordDate = new DateTime(2027, 8, 28),
                ExDividendDate = new DateTime(2027, 8, 27),
                DividendPaymentDate = new DateTime(2027, 9, 20),
                AnnualDividendSource = "Yahoo Finance",
                MarketDataSource = "Yahoo Finance",
                MarketDataAcquiredAt = new DateTime(2027, 7, 14, 10, 30, 0),
                MarketDataStatus = "取得済",
                DataQuality = "手入力",
                PlannedAdditionalShares = 12m,
                PlannedBroker = "野村證券",
                PlannedAccountType = AccountTypes.NisaGrowth,
                PurchaseMode = DividendGrowthPurchaseModes.OneTime
            }
        }
    };

    private static DividendPurchasePlanItem ExistingItem(
        string positionKey,
        string accountType,
        decimal currentShares,
        decimal plannedShares,
        string plannedBroker,
        string plannedAccount) => new()
    {
        PlanKey = positionKey,
        CanonicalSecurityKey = "TRMD|US",
        PositionKey = positionKey,
        StockId = accountType == AccountTypes.NisaGrowth ? 10 : 11,
        Ticker = "TRMD",
        Name = "TORM plc",
        Broker = "SBI証券",
        AccountType = accountType,
        Country = "米国",
        Currency = "USD",
        CurrentShares = currentShares,
        CurrentPrice = 29.48m,
        ExchangeRate = 162.15m,
        AnnualDividendPerShare = 3.2m,
        CurrentCostJpy = 300_000m,
        CurrentMarketValueJpy = 400_000m,
        DividendFrequency = "4",
        DividendMonths = "3,6,9,12",
        AnnualDividendSource = "CSV",
        MarketDataSource = "Yahoo Finance",
        MarketDataStatus = "取得済",
        DataQuality = "確定",
        PlannedAdditionalShares = plannedShares,
        PlannedBroker = plannedBroker,
        PlannedAccountType = plannedAccount,
        PurchaseMode = DividendGrowthPurchaseModes.OneTime
    };

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"investment_story_plan_{Guid.NewGuid():N}.db");

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static int CountRows(string databasePath, string tableName)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM [{tableName}];";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool TableExists(string databasePath, string tableName)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static IReadOnlyList<string> FindBackups(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath) ?? Path.GetTempPath();
        var name = Path.GetFileNameWithoutExtension(databasePath);
        return Directory.GetFiles(directory, $"{name}_*_before_dividend_purchase_plan.db");
    }

    private static void DeleteDatabaseAndBackups(string databasePath)
    {
        foreach (var file in FindBackups(databasePath).Append(databasePath))
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
