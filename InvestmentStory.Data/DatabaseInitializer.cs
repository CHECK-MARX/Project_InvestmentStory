using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class DatabaseInitializer
{
    public void Initialize(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(CreateConnectionString(databasePath));
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON;");
        CreateTables(connection);
        MigrateTables(connection);
        SeedSampleData(connection);
    }

    internal static string CreateConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        };

        return builder.ToString();
    }

    private static void CreateTables(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Stocks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AssetType TEXT NOT NULL DEFAULT 'Stock',
                Name TEXT NOT NULL,
                Ticker TEXT NOT NULL,
                Country TEXT NOT NULL,
                Currency TEXT NOT NULL,
                Broker TEXT NOT NULL,
                Sector TEXT NOT NULL DEFAULT '',
                Industry TEXT NOT NULL DEFAULT '',
                Market TEXT NOT NULL DEFAULT '',
                DataSource TEXT NOT NULL DEFAULT '手入力',
                Memo TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS MutualFundHoldings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL UNIQUE,
                FundName TEXT NOT NULL DEFAULT '',
                FundCode TEXT NOT NULL DEFAULT '',
                AssociationCode TEXT NOT NULL DEFAULT '',
                UnitsHeld REAL NOT NULL DEFAULT 0,
                UnitBase REAL NOT NULL DEFAULT 10000,
                AverageCostNav REAL NOT NULL DEFAULT 0,
                CurrentNav REAL NOT NULL DEFAULT 0,
                AcquisitionAmount REAL NOT NULL DEFAULT 0,
                MarketValue REAL NOT NULL DEFAULT 0,
                UnrealizedGainLoss REAL NOT NULL DEFAULT 0,
                NavDate TEXT NOT NULL DEFAULT '',
                NavSource TEXT NOT NULL DEFAULT '',
                DistributionMethod TEXT NOT NULL DEFAULT '',
                AccountType TEXT NOT NULL DEFAULT '',
                TotalPurchaseAmount REAL NOT NULL DEFAULT 0,
                TotalSaleAmount REAL NOT NULL DEFAULT 0,
                ReinvestedDistributionAmount REAL NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Purchases (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL,
                PurchaseDate TEXT NOT NULL,
                Shares REAL NOT NULL,
                UnitPrice REAL NOT NULL,
                ExchangeRate REAL NOT NULL,
                ExchangeRateAcquiredAt TEXT NOT NULL DEFAULT '',
                ExchangeRateSource TEXT NOT NULL DEFAULT '手入力',
                ExchangeRateInputType TEXT NOT NULL DEFAULT '手入力',
                Fee REAL NOT NULL DEFAULT 0,
                Memo TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS StockSplits (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL,
                SplitDate TEXT NOT NULL,
                SplitRatio REAL NOT NULL,
                Memo TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS CurrentHoldings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL UNIQUE,
                CurrentShares REAL NOT NULL,
                CurrentPrice REAL NOT NULL,
                CurrentExchangeRate REAL NOT NULL,
                ExchangeRateAcquiredAt TEXT NOT NULL DEFAULT '',
                ExchangeRateSource TEXT NOT NULL DEFAULT '手入力',
                ExchangeRateInputType TEXT NOT NULL DEFAULT '手入力',
                AnnualDividendPerShare REAL NOT NULL DEFAULT 0,
                DividendStatus TEXT NOT NULL DEFAULT '配当未入力',
                DividendFrequency TEXT NOT NULL DEFAULT '',
                DividendMonths TEXT NOT NULL DEFAULT '',
                CurrentPriceAcquiredAt TEXT NOT NULL DEFAULT '',
                CurrentPriceSource TEXT NOT NULL DEFAULT '',
                DividendInfoAcquiredAt TEXT NOT NULL DEFAULT '',
                DividendInfoSource TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS DividendPayments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL,
                AccountType TEXT NOT NULL DEFAULT 'Unknown',
                TaxAccountType TEXT NOT NULL DEFAULT 'Unknown',
                PaymentDate TEXT NOT NULL,
                RecordDate TEXT NOT NULL DEFAULT '',
                ExDividendDate TEXT NOT NULL DEFAULT '',
                DeclaredDate TEXT NOT NULL DEFAULT '',
                FiscalYear INTEGER NOT NULL DEFAULT 0,
                FiscalQuarter TEXT NOT NULL DEFAULT '',
                Broker TEXT NOT NULL,
                DividendStatus TEXT NOT NULL DEFAULT 'Actual',
                Source TEXT NOT NULL DEFAULT 'Manual',
                SourceFile TEXT NOT NULL DEFAULT '',
                SourceRowNumber INTEGER NOT NULL DEFAULT 0,
                SourcePriority INTEGER NOT NULL DEFAULT 50,
                Quantity REAL NOT NULL DEFAULT 0,
                DividendPerShare REAL NOT NULL DEFAULT 0,
                GrossAmount REAL NOT NULL,
                ForeignTaxAmount REAL NOT NULL DEFAULT 0,
                DomesticTaxAmount REAL NOT NULL DEFAULT 0,
                TotalTaxAmount REAL NOT NULL DEFAULT 0,
                TaxAmount REAL NOT NULL,
                NetAmount REAL NOT NULL,
                Currency TEXT NOT NULL,
                ExchangeRate REAL NOT NULL DEFAULT 1,
                ExchangeRateAcquiredAt TEXT NOT NULL DEFAULT '',
                ExchangeRateSource TEXT NOT NULL DEFAULT '手入力',
                ExchangeRateInputType TEXT NOT NULL DEFAULT '手入力',
                GrossAmountJpy REAL NOT NULL DEFAULT 0,
                ForeignTaxAmountJpy REAL NOT NULL DEFAULT 0,
                DomesticTaxAmountJpy REAL NOT NULL DEFAULT 0,
                TotalTaxAmountJpy REAL NOT NULL DEFAULT 0,
                NetAmountJpy REAL NOT NULL DEFAULT 0,
                JpyAmount REAL NOT NULL,
                IsTaxEstimated INTEGER NOT NULL DEFAULT 0,
                IsNisa INTEGER NOT NULL DEFAULT 0,
                IsForeignStock INTEGER NOT NULL DEFAULT 0,
                TaxProfileId INTEGER NULL,
                MatchedActualDividendId INTEGER NULL,
                ReplacedByDividendId INTEGER NULL,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                Memo TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS TaxProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Country TEXT NOT NULL,
                Currency TEXT NOT NULL,
                AccountType TEXT NOT NULL,
                AssetType TEXT NOT NULL DEFAULT 'Stock',
                ForeignWithholdingTaxRate REAL NOT NULL DEFAULT 0,
                DomesticIncomeTaxRate REAL NOT NULL DEFAULT 0,
                DomesticLocalTaxRate REAL NOT NULL DEFAULT 0,
                DomesticSpecialTaxRate REAL NOT NULL DEFAULT 0,
                TotalDomesticTaxRate REAL NOT NULL DEFAULT 0,
                IsDomesticTaxExempt INTEGER NOT NULL DEFAULT 0,
                IsForeignTaxExempt INTEGER NOT NULL DEFAULT 0,
                Memo TEXT NOT NULL DEFAULT ''
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS IncomeGoals (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TargetYear INTEGER NOT NULL UNIQUE,
                AnnualPassiveIncomeGoal REAL NOT NULL,
                MonthlyPassiveIncomeGoal REAL NOT NULL,
                TotalAssetGoal REAL NOT NULL
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL DEFAULT ''
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS ApiFetchLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ApiType TEXT NOT NULL,
                Provider TEXT NOT NULL,
                Symbol TEXT NOT NULL,
                HttpStatusCode INTEGER NULL,
                IsSuccess INTEGER NOT NULL,
                ErrorMessage TEXT NOT NULL DEFAULT '',
                FetchedAt TEXT NOT NULL,
                Summary TEXT NOT NULL DEFAULT ''
            );
            """);
    }

    private static void MigrateTables(SqliteConnection connection)
    {
        AddColumnIfMissing(connection, "Stocks", "AssetType", "TEXT NOT NULL DEFAULT 'Stock'");
        AddColumnIfMissing(connection, "Purchases", "ExchangeRateAcquiredAt", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "Purchases", "ExchangeRateSource", "TEXT NOT NULL DEFAULT '手入力'");
        AddColumnIfMissing(connection, "Purchases", "ExchangeRateInputType", "TEXT NOT NULL DEFAULT '手入力'");
        AddColumnIfMissing(connection, "Stocks", "Sector", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "Stocks", "Industry", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "Stocks", "Market", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "Stocks", "DataSource", "TEXT NOT NULL DEFAULT '手入力'");
        AddColumnIfMissing(connection, "CurrentHoldings", "ExchangeRateAcquiredAt", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "CurrentHoldings", "ExchangeRateSource", "TEXT NOT NULL DEFAULT '手入力'");
        AddColumnIfMissing(connection, "CurrentHoldings", "ExchangeRateInputType", "TEXT NOT NULL DEFAULT '手入力'");
        AddColumnIfMissing(connection, "CurrentHoldings", "DividendStatus", "TEXT NOT NULL DEFAULT '配当未入力'");
        AddColumnIfMissing(connection, "CurrentHoldings", "CurrentPriceAcquiredAt", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "CurrentHoldings", "CurrentPriceSource", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "CurrentHoldings", "DividendInfoAcquiredAt", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "CurrentHoldings", "DividendInfoSource", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "ExchangeRate", "REAL NOT NULL DEFAULT 1");
        AddColumnIfMissing(connection, "DividendPayments", "ExchangeRateAcquiredAt", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "ExchangeRateSource", "TEXT NOT NULL DEFAULT '手入力'");
        AddColumnIfMissing(connection, "DividendPayments", "ExchangeRateInputType", "TEXT NOT NULL DEFAULT '手入力'");
        AddColumnIfMissing(connection, "DividendPayments", "AccountType", "TEXT NOT NULL DEFAULT 'Unknown'");
        AddColumnIfMissing(connection, "DividendPayments", "TaxAccountType", "TEXT NOT NULL DEFAULT 'Unknown'");
        AddColumnIfMissing(connection, "DividendPayments", "RecordDate", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "ExDividendDate", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "DeclaredDate", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "FiscalYear", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "FiscalQuarter", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "DividendStatus", "TEXT NOT NULL DEFAULT 'Actual'");
        AddColumnIfMissing(connection, "DividendPayments", "Source", "TEXT NOT NULL DEFAULT 'Manual'");
        AddColumnIfMissing(connection, "DividendPayments", "SourceFile", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "SourceRowNumber", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "SourcePriority", "INTEGER NOT NULL DEFAULT 50");
        AddColumnIfMissing(connection, "DividendPayments", "Quantity", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "DividendPerShare", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "ForeignTaxAmount", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "DomesticTaxAmount", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "TotalTaxAmount", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "GrossAmountJpy", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "ForeignTaxAmountJpy", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "DomesticTaxAmountJpy", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "TotalTaxAmountJpy", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "NetAmountJpy", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "IsTaxEstimated", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "IsNisa", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "IsForeignStock", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "DividendPayments", "TaxProfileId", "INTEGER NULL");
        AddColumnIfMissing(connection, "DividendPayments", "MatchedActualDividendId", "INTEGER NULL");
        AddColumnIfMissing(connection, "DividendPayments", "ReplacedByDividendId", "INTEGER NULL");
        AddColumnIfMissing(connection, "DividendPayments", "CreatedAt", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "DividendPayments", "UpdatedAt", "TEXT NOT NULL DEFAULT ''");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS MutualFundHoldings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL UNIQUE,
                FundName TEXT NOT NULL DEFAULT '',
                FundCode TEXT NOT NULL DEFAULT '',
                AssociationCode TEXT NOT NULL DEFAULT '',
                UnitsHeld REAL NOT NULL DEFAULT 0,
                UnitBase REAL NOT NULL DEFAULT 10000,
                AverageCostNav REAL NOT NULL DEFAULT 0,
                CurrentNav REAL NOT NULL DEFAULT 0,
                AcquisitionAmount REAL NOT NULL DEFAULT 0,
                MarketValue REAL NOT NULL DEFAULT 0,
                UnrealizedGainLoss REAL NOT NULL DEFAULT 0,
                NavDate TEXT NOT NULL DEFAULT '',
                NavSource TEXT NOT NULL DEFAULT '',
                DistributionMethod TEXT NOT NULL DEFAULT '',
                AccountType TEXT NOT NULL DEFAULT '',
                TotalPurchaseAmount REAL NOT NULL DEFAULT 0,
                TotalSaleAmount REAL NOT NULL DEFAULT 0,
                ReinvestedDistributionAmount REAL NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            UPDATE Stocks
            SET AssetType = 'MutualFund'
            WHERE AssetType <> 'MutualFund'
              AND (UPPER(Ticker) LIKE 'FUND:%' OR Sector LIKE '%投資信託%');
            """);

        RemoveTickerUniqueConstraintIfNeeded(connection);
        NormalizeLegacySecurityAliases(connection);
        MigrateDefaultExchangeRateProvider(connection);
        MigrateDefaultMarketDataProvider(connection);
        BackfillDividendPaymentExtensions(connection);
        SeedTaxProfiles(connection);

        Execute(connection, """
            UPDATE CurrentHoldings
            SET DividendStatus = CASE
                WHEN AnnualDividendPerShare > 0 THEN '配当あり'
                WHEN AnnualDividendPerShare = 0 THEN '配当なし'
                ELSE '配当未入力'
            END
            WHERE DividendStatus = '配当未入力';
            """);
    }

    private static void BackfillDividendPaymentExtensions(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE DividendPayments
            SET DividendStatus = CASE WHEN DividendStatus = '' THEN 'Actual' ELSE DividendStatus END,
                Source = CASE
                    WHEN Source <> '' THEN Source
                    WHEN ExchangeRateInputType = 'CSV' THEN 'Csv'
                    ELSE 'Manual'
                END,
                SourcePriority = CASE WHEN SourcePriority <= 0 THEN 100 ELSE SourcePriority END,
                TotalTaxAmount = CASE WHEN TotalTaxAmount = 0 THEN TaxAmount ELSE TotalTaxAmount END,
                DomesticTaxAmount = CASE WHEN DomesticTaxAmount = 0 THEN TaxAmount ELSE DomesticTaxAmount END,
                GrossAmountJpy = CASE WHEN GrossAmountJpy = 0 THEN GrossAmount * ExchangeRate ELSE GrossAmountJpy END,
                TotalTaxAmountJpy = CASE WHEN TotalTaxAmountJpy = 0 THEN TaxAmount * ExchangeRate ELSE TotalTaxAmountJpy END,
                DomesticTaxAmountJpy = CASE WHEN DomesticTaxAmountJpy = 0 THEN TaxAmount * ExchangeRate ELSE DomesticTaxAmountJpy END,
                NetAmountJpy = CASE WHEN NetAmountJpy = 0 THEN JpyAmount ELSE NetAmountJpy END,
                IsForeignStock = CASE WHEN Currency <> 'JPY' THEN 1 ELSE IsForeignStock END,
                IsNisa = CASE WHEN AccountType = 'NISA' OR TaxAccountType = 'NISA' THEN 1 ELSE IsNisa END,
                CreatedAt = CASE WHEN CreatedAt = '' THEN CURRENT_TIMESTAMP ELSE CreatedAt END,
                UpdatedAt = CASE WHEN UpdatedAt = '' THEN CURRENT_TIMESTAMP ELSE UpdatedAt END
            WHERE 1 = 1;
            """);
    }

    private static void SeedTaxProfiles(SqliteConnection connection)
    {
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM TaxProfiles;";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());
        if (count > 0)
        {
            return;
        }

        Execute(connection, """
            INSERT INTO TaxProfiles
                (Name, Country, Currency, AccountType, AssetType, ForeignWithholdingTaxRate, DomesticIncomeTaxRate, DomesticLocalTaxRate, DomesticSpecialTaxRate, TotalDomesticTaxRate, IsDomesticTaxExempt, IsForeignTaxExempt, Memo)
            VALUES
                ('日本株 / 特定口座', 'Japan', 'JPY', 'Specific', 'Stock', 0, 15.315, 5.0, 0, 20.315, 0, 1, '概算。実際の税額は証券会社CSVを正とします。'),
                ('日本株 / 一般口座', 'Japan', 'JPY', 'General', 'Stock', 0, 15.315, 5.0, 0, 20.315, 0, 1, '概算。実際の税額は証券会社CSVを正とします。'),
                ('日本株 / NISA', 'Japan', 'JPY', 'NISA', 'Stock', 0, 0, 0, 0, 0, 1, 1, 'NISA国内株配当を非課税として扱う初期設定です。'),
                ('米国株 / 特定口座', 'United States', 'USD', 'Specific', 'Stock', 10.0, 15.315, 5.0, 0, 20.315, 0, 0, '外国税後の金額に国内税を概算します。'),
                ('米国株 / 一般口座', 'United States', 'USD', 'General', 'Stock', 10.0, 15.315, 5.0, 0, 20.315, 0, 0, '外国税後の金額に国内税を概算します。'),
                ('米国株 / NISA', 'United States', 'USD', 'NISA', 'Stock', 10.0, 0, 0, 0, 0, 1, 0, 'NISAでも外国源泉税が残る前提の概算です。');
            """);
    }

    private static void RemoveTickerUniqueConstraintIfNeeded(SqliteConnection connection)
    {
        if (!StocksTableHasTickerUniqueConstraint(connection))
        {
            return;
        }

        Execute(connection, "PRAGMA foreign_keys = OFF;");
        try
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, """
                CREATE TABLE Stocks_Migration_NoTickerUnique (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AssetType TEXT NOT NULL DEFAULT 'Stock',
                    Name TEXT NOT NULL,
                    Ticker TEXT NOT NULL,
                    Country TEXT NOT NULL,
                    Currency TEXT NOT NULL,
                    Broker TEXT NOT NULL,
                    Sector TEXT NOT NULL DEFAULT '',
                    Industry TEXT NOT NULL DEFAULT '',
                    Market TEXT NOT NULL DEFAULT '',
                    DataSource TEXT NOT NULL DEFAULT '手入力',
                    Memo TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """);

            Execute(connection, transaction, """
                INSERT INTO Stocks_Migration_NoTickerUnique
                    (Id, AssetType, Name, Ticker, Country, Currency, Broker, Sector, Industry, Market, DataSource, Memo, CreatedAt, UpdatedAt)
                SELECT
                    Id, AssetType, Name, Ticker, Country, Currency, Broker, Sector, Industry, Market, DataSource, Memo, CreatedAt, UpdatedAt
                FROM Stocks;
                """);

            Execute(connection, transaction, "DROP TABLE Stocks;");
            Execute(connection, transaction, "ALTER TABLE Stocks_Migration_NoTickerUnique RENAME TO Stocks;");
            transaction.Commit();
        }
        finally
        {
            Execute(connection, "PRAGMA foreign_keys = ON;");
        }
    }

    private static bool StocksTableHasTickerUniqueConstraint(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Stocks';";
        var sql = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
        return sql.Contains("Ticker TEXT NOT NULL UNIQUE", StringComparison.OrdinalIgnoreCase) ||
               sql.Contains("UNIQUE(Ticker", StringComparison.OrdinalIgnoreCase) ||
               sql.Contains("UNIQUE (Ticker", StringComparison.OrdinalIgnoreCase);
    }

    private static void NormalizeLegacySecurityAliases(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE Stocks
            SET Ticker = 'CMBT',
                Name = 'CMB テック'
            WHERE UPPER(Ticker) = 'EURN' OR UPPER(Ticker) = 'CMBT';
            """);

        MergeDuplicateStocksByBrokerAndTicker(connection, "CMBT");
    }

    private static void MigrateDefaultExchangeRateProvider(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE AppSettings
            SET Value = 'Yahoo Finance'
            WHERE Key = 'ExchangeRateProvider'
              AND (Value = '' OR Value = 'Mock');
            """);
    }

    private static void MigrateDefaultMarketDataProvider(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE AppSettings
            SET Value = 'Web/API'
            WHERE Key = 'MarketDataMode'
              AND (Value = '' OR Value = 'Mock');
            """);

        Execute(connection, """
            UPDATE AppSettings
            SET Value = 'Yahoo Finance'
            WHERE Key = 'JapanMarketDataProvider'
              AND (Value = '' OR Value = 'J-Quants')
              AND NOT EXISTS (
                  SELECT 1
                  FROM AppSettings
                  WHERE Key = 'JQuantsApiKey'
                    AND TRIM(Value) <> ''
              );
            """);
    }

    private static void MergeDuplicateStocksByBrokerAndTicker(SqliteConnection connection, string ticker)
    {
        var rows = ReadStockMergeRows(connection, ticker);
        foreach (var group in rows.GroupBy(x => SecuritySymbolNormalizer.NormalizeBroker(x.Broker)))
        {
            var stocks = group.ToList();
            if (stocks.Count <= 1)
            {
                continue;
            }

            var target = stocks
                .OrderByDescending(x => x.CurrentShares > 0m)
                .ThenByDescending(x => x.CurrentShares)
                .ThenByDescending(x => x.CurrentPrice)
                .ThenBy(x => x.Id)
                .First();

            Execute(connection, "UPDATE Stocks SET Ticker = 'CMBT', Name = 'CMB テック' WHERE Id = $targetId;", ("$targetId", target.Id));
            foreach (var duplicate in stocks.Where(x => x.Id != target.Id))
            {
                Execute(connection, "UPDATE DividendPayments SET StockId = $targetId WHERE StockId = $duplicateId;",
                    ("$targetId", target.Id),
                    ("$duplicateId", duplicate.Id));
                Execute(connection, "DELETE FROM Stocks WHERE Id = $duplicateId;", ("$duplicateId", duplicate.Id));
            }
        }
    }

    private static IReadOnlyList<StockMergeRow> ReadStockMergeRows(SqliteConnection connection, string ticker)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.Broker, IFNULL(ch.CurrentShares, 0), IFNULL(ch.CurrentPrice, 0)
            FROM Stocks s
            LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
            WHERE UPPER(s.Ticker) = UPPER($ticker);
            """;
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<StockMergeRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new StockMergeRow(
                reader.GetInt32(0),
                reader.GetString(1),
                Convert.ToDecimal(reader.GetDouble(2)),
                Convert.ToDecimal(reader.GetDouble(3))));
        }

        return rows;
    }

    private sealed record StockMergeRow(int Id, string Broker, decimal CurrentShares, decimal CurrentPrice);

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = pragmaCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }

    private static void SeedSampleData(SqliteConnection connection)
    {
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM Stocks;";
        var stockCount = Convert.ToInt32(countCommand.ExecuteScalar());
        if (stockCount > 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();

        var nvdaId = InsertStock(connection, transaction, "NVIDIA", "NVDA", "米国", "USD", "サンプル証券", "10分割後の値上がりを確認するサンプル");
        InsertPurchase(connection, transaction, nvdaId, new DateTime(2021, 1, 15), 5m, 290m, 160m, 0m, "サンプル購入");
        InsertSplit(connection, transaction, nvdaId, new DateTime(2024, 6, 10), 10m, "10分割");
        InsertCurrentHolding(connection, transaction, nvdaId, 50m, 196m, 160m, 1m, "年4回", "3,6,9,12");

        var tslaId = InsertStock(connection, transaction, "Tesla", "TSLA", "米国", "USD", "サンプル証券", "無配の成長株サンプル");
        InsertPurchase(connection, transaction, tslaId, new DateTime(2021, 6, 1), 10m, 742m, 160m, 0m, "サンプル購入");
        InsertSplit(connection, transaction, tslaId, new DateTime(2022, 8, 25), 3m, "3分割");
        InsertCurrentHolding(connection, transaction, tslaId, 30m, 416m, 160m, 0m, "なし", "");

        var amdId = InsertStock(connection, transaction, "AMD", "AMD", "米国", "USD", "サンプル証券", "分割なしの値上がりサンプル");
        InsertPurchase(connection, transaction, amdId, new DateTime(2023, 1, 10), 5m, 150m, 160m, 0m, "サンプル購入");
        InsertSplit(connection, transaction, amdId, new DateTime(2023, 1, 10), 1m, "分割なし");
        InsertCurrentHolding(connection, transaction, amdId, 5m, 563m, 160m, 0m, "なし", "");

        InsertDividend(connection, transaction, nvdaId, new DateTime(DateTime.Today.Year, 3, 15), "サンプル証券", 12.50m, 3.50m, 9.00m, "USD", 160m, 1440m, "サンプル配当");
        InsertDividend(connection, transaction, nvdaId, new DateTime(DateTime.Today.Year, 6, 15), "サンプル証券", 12.50m, 3.50m, 9.00m, "USD", 160m, 1440m, "サンプル配当");
        InsertGoal(connection, transaction, DateTime.Today.Year, 1_200_000m, 100_000m, 30_000_000m);

        transaction.Commit();
    }

    private static int InsertStock(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        string ticker,
        string country,
        string currency,
        string broker,
        string memo)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Stocks (Name, Ticker, Country, Currency, Broker, Memo)
            VALUES ($name, $ticker, $country, $currency, $broker, $memo);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$country", country);
        command.Parameters.AddWithValue("$currency", currency);
        command.Parameters.AddWithValue("$broker", broker);
        command.Parameters.AddWithValue("$memo", memo);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void InsertPurchase(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int stockId,
        DateTime purchaseDate,
        decimal shares,
        decimal unitPrice,
        decimal exchangeRate,
        decimal fee,
        string memo)
    {
        Execute(connection, transaction, """
            INSERT INTO Purchases
                (StockId, PurchaseDate, Shares, UnitPrice, ExchangeRate, ExchangeRateAcquiredAt, ExchangeRateSource, ExchangeRateInputType, Fee, Memo)
            VALUES
                ($stockId, $purchaseDate, $shares, $unitPrice, $exchangeRate, $exchangeRateAcquiredAt, $exchangeRateSource, $exchangeRateInputType, $fee, $memo);
            """,
            ("$stockId", stockId),
            ("$purchaseDate", ToDateText(purchaseDate)),
            ("$shares", shares),
            ("$unitPrice", unitPrice),
            ("$exchangeRate", exchangeRate),
            ("$exchangeRateAcquiredAt", ToDateTimeText(purchaseDate)),
            ("$exchangeRateSource", "手入力"),
            ("$exchangeRateInputType", "手入力"),
            ("$fee", fee),
            ("$memo", memo));
    }

    private static void InsertSplit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int stockId,
        DateTime splitDate,
        decimal splitRatio,
        string memo)
    {
        Execute(connection, transaction, """
            INSERT INTO StockSplits (StockId, SplitDate, SplitRatio, Memo)
            VALUES ($stockId, $splitDate, $splitRatio, $memo);
            """,
            ("$stockId", stockId),
            ("$splitDate", ToDateText(splitDate)),
            ("$splitRatio", splitRatio),
            ("$memo", memo));
    }

    private static void InsertCurrentHolding(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int stockId,
        decimal currentShares,
        decimal currentPrice,
        decimal currentExchangeRate,
        decimal annualDividendPerShare,
        string dividendFrequency,
        string dividendMonths)
    {
        Execute(connection, transaction, """
            INSERT INTO CurrentHoldings
                (StockId, CurrentShares, CurrentPrice, CurrentExchangeRate, ExchangeRateAcquiredAt, ExchangeRateSource, ExchangeRateInputType, AnnualDividendPerShare, DividendStatus, DividendFrequency, DividendMonths, CurrentPriceAcquiredAt, CurrentPriceSource, DividendInfoAcquiredAt, DividendInfoSource, UpdatedAt)
            VALUES
                ($stockId, $currentShares, $currentPrice, $currentExchangeRate, $exchangeRateAcquiredAt, $exchangeRateSource, $exchangeRateInputType, $annualDividendPerShare, $dividendStatus, $dividendFrequency, $dividendMonths, $currentPriceAcquiredAt, $currentPriceSource, $dividendInfoAcquiredAt, $dividendInfoSource, $updatedAt);
            """,
            ("$stockId", stockId),
            ("$currentShares", currentShares),
            ("$currentPrice", currentPrice),
            ("$currentExchangeRate", currentExchangeRate),
            ("$exchangeRateAcquiredAt", ToDateTimeText(DateTime.Now)),
            ("$exchangeRateSource", "手入力"),
            ("$exchangeRateInputType", "手入力"),
            ("$annualDividendPerShare", annualDividendPerShare),
            ("$dividendStatus", annualDividendPerShare > 0m ? "配当あり" : "配当なし"),
            ("$dividendFrequency", dividendFrequency),
            ("$dividendMonths", dividendMonths),
            ("$currentPriceAcquiredAt", ToDateTimeText(DateTime.Now)),
            ("$currentPriceSource", "サンプルデータ"),
            ("$dividendInfoAcquiredAt", ToDateTimeText(DateTime.Now)),
            ("$dividendInfoSource", "サンプルデータ"),
            ("$updatedAt", ToDateText(DateTime.Today)));
    }

    private static void InsertDividend(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int stockId,
        DateTime paymentDate,
        string broker,
        decimal grossAmount,
        decimal taxAmount,
        decimal netAmount,
        string currency,
        decimal exchangeRate,
        decimal jpyAmount,
        string memo)
    {
        Execute(connection, transaction, """
            INSERT INTO DividendPayments
                (StockId, PaymentDate, Broker, GrossAmount, TaxAmount, NetAmount, Currency, ExchangeRate, ExchangeRateAcquiredAt, ExchangeRateSource, ExchangeRateInputType, JpyAmount, Memo)
            VALUES
                ($stockId, $paymentDate, $broker, $grossAmount, $taxAmount, $netAmount, $currency, $exchangeRate, $exchangeRateAcquiredAt, $exchangeRateSource, $exchangeRateInputType, $jpyAmount, $memo);
            """,
            ("$stockId", stockId),
            ("$paymentDate", ToDateText(paymentDate)),
            ("$broker", broker),
            ("$grossAmount", grossAmount),
            ("$taxAmount", taxAmount),
            ("$netAmount", netAmount),
            ("$currency", currency),
            ("$exchangeRate", exchangeRate),
            ("$exchangeRateAcquiredAt", ToDateTimeText(paymentDate)),
            ("$exchangeRateSource", "手入力"),
            ("$exchangeRateInputType", "手入力"),
            ("$jpyAmount", jpyAmount),
            ("$memo", memo));
    }

    private static void InsertGoal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int targetYear,
        decimal annualGoal,
        decimal monthlyGoal,
        decimal totalAssetGoal)
    {
        Execute(connection, transaction, """
            INSERT INTO IncomeGoals (TargetYear, AnnualPassiveIncomeGoal, MonthlyPassiveIncomeGoal, TotalAssetGoal)
            VALUES ($targetYear, $annualGoal, $monthlyGoal, $totalAssetGoal)
            ON CONFLICT(TargetYear) DO UPDATE SET
                AnnualPassiveIncomeGoal = excluded.AnnualPassiveIncomeGoal,
                MonthlyPassiveIncomeGoal = excluded.MonthlyPassiveIncomeGoal,
                TotalAssetGoal = excluded.TotalAssetGoal;
            """,
            ("$targetYear", targetYear),
            ("$annualGoal", annualGoal),
            ("$monthlyGoal", monthlyGoal),
            ("$totalAssetGoal", totalAssetGoal));
    }

    private static void Execute(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        command.ExecuteNonQuery();
    }

    private static string ToDateText(DateTime date) => date.ToString("yyyy-MM-dd");

    private static string ToDateTimeText(DateTime date) => date.ToString("yyyy-MM-dd HH:mm:ss");
}
