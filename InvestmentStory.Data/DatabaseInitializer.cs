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
        BackupBeforeDividendPurchasePlanMigrationIfNeeded(connection, databasePath);
        CreateTables(connection);
        BackupBeforeSecurityMigrationIfNeeded(connection, databasePath);
        DropCsvIdempotencyIndexesBeforeMigration(connection);
        MigrateTables(connection);
        RepairCsvReimportDuplicates(connection, databasePath);
        BackfillInitialPositionTrades(connection, databasePath);
        CreateCsvIdempotencyIndexes(connection);
        SeedSampleData(connection);
        BackfillCanonicalSecurityKeys(connection);
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
                CanonicalSecurityKey TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL,
                Ticker TEXT NOT NULL,
                Country TEXT NOT NULL,
                Currency TEXT NOT NULL,
                Broker TEXT NOT NULL,
                AccountType TEXT NOT NULL DEFAULT 'Unknown',
                CustodyType TEXT NOT NULL DEFAULT '',
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
            CREATE TABLE IF NOT EXISTS DividendPurchasePlans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL DEFAULT 'Default',
                TargetYear INTEGER NOT NULL,
                PlannedPurchaseDate TEXT NOT NULL,
                DisplayUnit TEXT NOT NULL DEFAULT 'AllAccounts',
                TargetAnnualNetDividendJpy REAL NOT NULL DEFAULT 0,
                IsLastUsed INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS DividendPurchasePlanItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlanId INTEGER NOT NULL,
                ItemOrder INTEGER NOT NULL DEFAULT 0,
                IsNewStock INTEGER NOT NULL DEFAULT 0,
                StockId INTEGER NOT NULL DEFAULT 0,
                PlanKey TEXT NOT NULL DEFAULT '',
                CanonicalSecurityKey TEXT NOT NULL DEFAULT '',
                PositionKey TEXT NOT NULL DEFAULT '',
                Ticker TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',
                Broker TEXT NOT NULL DEFAULT '',
                AccountType TEXT NOT NULL DEFAULT 'Unknown',
                Country TEXT NOT NULL DEFAULT '',
                Currency TEXT NOT NULL DEFAULT 'JPY',
                CurrentShares REAL NOT NULL DEFAULT 0,
                CurrentPrice REAL NOT NULL DEFAULT 0,
                ExchangeRate REAL NOT NULL DEFAULT 1,
                AnnualDividendPerShare REAL NOT NULL DEFAULT 0,
                CurrentCostJpy REAL NOT NULL DEFAULT 0,
                CurrentMarketValueJpy REAL NOT NULL DEFAULT 0,
                DividendFrequency TEXT NOT NULL DEFAULT '',
                DividendMonths TEXT NOT NULL DEFAULT '',
                DividendRecordDate TEXT NULL,
                ExDividendDate TEXT NULL,
                DividendPaymentDate TEXT NULL,
                AnnualDividendSource TEXT NOT NULL DEFAULT '',
                MarketDataSource TEXT NOT NULL DEFAULT '',
                MarketDataAcquiredAt TEXT NULL,
                MarketDataStatus TEXT NOT NULL DEFAULT '',
                DataQuality TEXT NOT NULL DEFAULT '',
                PlannedAdditionalShares REAL NOT NULL DEFAULT 0,
                PlannedBroker TEXT NOT NULL DEFAULT '',
                PlannedAccountType TEXT NOT NULL DEFAULT 'Unknown',
                AnnualDividendGrowthRate REAL NOT NULL DEFAULT 0,
                PurchaseMode TEXT NOT NULL DEFAULT 'OneTime',
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (PlanId) REFERENCES DividendPurchasePlans(Id) ON DELETE CASCADE,
                UNIQUE(PlanId, PlanKey)
            );
            """);

        Execute(connection, """
            CREATE INDEX IF NOT EXISTS IX_DividendPurchasePlans_LastUsed
            ON DividendPurchasePlans(IsLastUsed, UpdatedAt DESC);
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

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS PortfolioSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SnapshotDate TEXT NOT NULL UNIQUE,
                TotalMarketValueJpy REAL NOT NULL DEFAULT 0,
                TotalCostBasisJpy REAL NOT NULL DEFAULT 0,
                UnrealizedGainLossJpy REAL NOT NULL DEFAULT 0,
                CumulativeDividendJpy REAL NOT NULL DEFAULT 0,
                RealizedGainLossJpy REAL NOT NULL DEFAULT 0,
                TotalReturnJpy REAL NOT NULL DEFAULT 0,
                UsdJpyRate REAL NOT NULL DEFAULT 0,
                StockValueJpy REAL NOT NULL DEFAULT 0,
                MutualFundValueJpy REAL NOT NULL DEFAULT 0,
                CashValueJpy REAL NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS BrokerTrades (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL,
                TradeDate TEXT NOT NULL,
                SettlementDate TEXT NOT NULL,
                Broker TEXT NOT NULL DEFAULT '',
                AccountType TEXT NOT NULL DEFAULT 'Unknown',
                CustodyType TEXT NOT NULL DEFAULT '',
                TradeType TEXT NOT NULL DEFAULT '',
                Quantity REAL NOT NULL DEFAULT 0,
                SignedQuantity REAL NOT NULL DEFAULT 0,
                UnitPrice REAL NOT NULL DEFAULT 0,
                Currency TEXT NOT NULL DEFAULT 'JPY',
                ExchangeRate REAL NOT NULL DEFAULT 1,
                SettlementAmountJpy REAL NOT NULL DEFAULT 0,
                FeeJpy REAL NOT NULL DEFAULT 0,
                TaxJpy REAL NOT NULL DEFAULT 0,
                RealizedGainLoss REAL NOT NULL DEFAULT 0,
                RealizedGainLossJpy REAL NOT NULL DEFAULT 0,
                AfterTradeQuantity REAL NOT NULL DEFAULT 0,
                AfterTradeAverageCost REAL NOT NULL DEFAULT 0,
                Source TEXT NOT NULL DEFAULT '',
                SourceFile TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(StockId, TradeDate, SettlementDate, Broker, AccountType, TradeType, Quantity, UnitPrice, SettlementAmountJpy, Source),
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS DataQualityInfos (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL,
                FieldName TEXT NOT NULL,
                Value TEXT NOT NULL DEFAULT '',
                SourceType TEXT NOT NULL DEFAULT 'Unknown',
                SourceName TEXT NOT NULL DEFAULT '',
                RetrievedAt TEXT NOT NULL DEFAULT '',
                ConfidenceLevel TEXT NOT NULL DEFAULT 'Missing',
                IsEstimated INTEGER NOT NULL DEFAULT 0,
                IsStale INTEGER NOT NULL DEFAULT 0,
                HasConflict INTEGER NOT NULL DEFAULT 0,
                ConflictDescription TEXT NOT NULL DEFAULT '',
                ManualOverride INTEGER NOT NULL DEFAULT 0,
                Memo TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(StockId, FieldName),
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);
    }

    private static void BackupBeforeSecurityMigrationIfNeeded(SqliteConnection connection, string databasePath)
    {
        if (!ColumnExists(connection, "Stocks", "CanonicalSecurityKey"))
        {
            BackupDatabase(databasePath, "before_security_fix");
        }
    }

    private static void BackupBeforeDividendPurchasePlanMigrationIfNeeded(
        SqliteConnection connection,
        string databasePath)
    {
        if (TableExists(connection, "Stocks") &&
            (!TableExists(connection, "DividendPurchasePlans") ||
             !TableExists(connection, "DividendPurchasePlanItems")))
        {
            BackupDatabase(databasePath, "before_dividend_purchase_plan");
        }
    }

    private static void MigrateTables(SqliteConnection connection)
    {
        AddColumnIfMissing(connection, "Stocks", "AssetType", "TEXT NOT NULL DEFAULT 'Stock'");
        AddColumnIfMissing(connection, "Stocks", "CanonicalSecurityKey", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "Stocks", "AccountType", "TEXT NOT NULL DEFAULT 'Unknown'");
        AddColumnIfMissing(connection, "Stocks", "CustodyType", "TEXT NOT NULL DEFAULT ''");
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
            CREATE TABLE IF NOT EXISTS PortfolioSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SnapshotDate TEXT NOT NULL UNIQUE,
                TotalMarketValueJpy REAL NOT NULL DEFAULT 0,
                TotalCostBasisJpy REAL NOT NULL DEFAULT 0,
                UnrealizedGainLossJpy REAL NOT NULL DEFAULT 0,
                CumulativeDividendJpy REAL NOT NULL DEFAULT 0,
                RealizedGainLossJpy REAL NOT NULL DEFAULT 0,
                TotalReturnJpy REAL NOT NULL DEFAULT 0,
                UsdJpyRate REAL NOT NULL DEFAULT 0,
                StockValueJpy REAL NOT NULL DEFAULT 0,
                MutualFundValueJpy REAL NOT NULL DEFAULT 0,
                CashValueJpy REAL NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        AddColumnIfMissing(connection, "PortfolioSnapshots", "StockValueJpy", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "PortfolioSnapshots", "MutualFundValueJpy", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "PortfolioSnapshots", "CashValueJpy", "REAL NOT NULL DEFAULT 0");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS BrokerTrades (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL,
                TradeDate TEXT NOT NULL,
                SettlementDate TEXT NOT NULL,
                Broker TEXT NOT NULL DEFAULT '',
                AccountType TEXT NOT NULL DEFAULT 'Unknown',
                CustodyType TEXT NOT NULL DEFAULT '',
                TradeType TEXT NOT NULL DEFAULT '',
                Quantity REAL NOT NULL DEFAULT 0,
                SignedQuantity REAL NOT NULL DEFAULT 0,
                UnitPrice REAL NOT NULL DEFAULT 0,
                Currency TEXT NOT NULL DEFAULT 'JPY',
                ExchangeRate REAL NOT NULL DEFAULT 1,
                SettlementAmountJpy REAL NOT NULL DEFAULT 0,
                FeeJpy REAL NOT NULL DEFAULT 0,
                TaxJpy REAL NOT NULL DEFAULT 0,
                RealizedGainLoss REAL NOT NULL DEFAULT 0,
                RealizedGainLossJpy REAL NOT NULL DEFAULT 0,
                AfterTradeQuantity REAL NOT NULL DEFAULT 0,
                AfterTradeAverageCost REAL NOT NULL DEFAULT 0,
                Source TEXT NOT NULL DEFAULT '',
                SourceFile TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(StockId, TradeDate, SettlementDate, Broker, AccountType, TradeType, Quantity, UnitPrice, SettlementAmountJpy, Source),
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS DataQualityInfos (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockId INTEGER NOT NULL,
                FieldName TEXT NOT NULL,
                Value TEXT NOT NULL DEFAULT '',
                SourceType TEXT NOT NULL DEFAULT 'Unknown',
                SourceName TEXT NOT NULL DEFAULT '',
                RetrievedAt TEXT NOT NULL DEFAULT '',
                ConfidenceLevel TEXT NOT NULL DEFAULT 'Missing',
                IsEstimated INTEGER NOT NULL DEFAULT 0,
                IsStale INTEGER NOT NULL DEFAULT 0,
                HasConflict INTEGER NOT NULL DEFAULT 0,
                ConflictDescription TEXT NOT NULL DEFAULT '',
                ManualOverride INTEGER NOT NULL DEFAULT 0,
                Memo TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(StockId, FieldName),
                FOREIGN KEY (StockId) REFERENCES Stocks(Id) ON DELETE CASCADE
            );
            """);

        AddColumnIfMissing(connection, "DataQualityInfos", "Value", "TEXT NOT NULL DEFAULT ''");

        Execute(connection, """
            UPDATE Stocks
            SET AssetType = 'MutualFund'
            WHERE AssetType <> 'MutualFund'
              AND (UPPER(Ticker) LIKE 'FUND:%' OR Sector LIKE '%投資信託%');
            """);

        Execute(connection, """
            UPDATE Stocks
            SET AccountType = CASE
                WHEN AccountType LIKE '%旧%NISA%' OR AccountType LIKE '%旧ニーサ%' THEN 'NisaLegacy'
                WHEN AccountType LIKE '%つみたて%' OR AccountType LIKE '%積立%' THEN 'NisaAccumulation'
                WHEN AccountType LIKE '%NISA%' OR AccountType LIKE '%成長投資%' THEN 'NisaGrowth'
                WHEN AccountType LIKE '%特定%' THEN 'Specific'
                WHEN AccountType LIKE '%一般%' THEN 'General'
                WHEN AccountType IN ('NisaGrowth', 'NisaAccumulation', 'NisaLegacy', 'Specific', 'General') THEN AccountType
                ELSE 'Unknown'
            END;
            """);

        Execute(connection, """
            UPDATE DividendPayments
            SET AccountType = CASE
                    WHEN AccountType LIKE '%旧%NISA%' OR AccountType LIKE '%旧ニーサ%' THEN 'NisaLegacy'
                    WHEN AccountType LIKE '%つみたて%' OR AccountType LIKE '%積立%' THEN 'NisaAccumulation'
                    WHEN AccountType LIKE '%NISA%' OR AccountType LIKE '%成長投資%' THEN 'NisaGrowth'
                    WHEN AccountType LIKE '%特定%' THEN 'Specific'
                    WHEN AccountType LIKE '%一般%' THEN 'General'
                    WHEN AccountType IN ('NisaGrowth', 'NisaAccumulation', 'NisaLegacy', 'Specific', 'General') THEN AccountType
                    ELSE 'Unknown'
                END,
                TaxAccountType = CASE
                    WHEN TaxAccountType LIKE '%旧%NISA%' OR TaxAccountType LIKE '%旧ニーサ%' THEN 'NisaLegacy'
                    WHEN TaxAccountType LIKE '%つみたて%' OR TaxAccountType LIKE '%積立%' THEN 'NisaAccumulation'
                    WHEN TaxAccountType LIKE '%NISA%' OR TaxAccountType LIKE '%成長投資%' THEN 'NisaGrowth'
                    WHEN TaxAccountType LIKE '%特定%' THEN 'Specific'
                    WHEN TaxAccountType LIKE '%一般%' THEN 'General'
                    WHEN TaxAccountType IN ('NisaGrowth', 'NisaAccumulation', 'NisaLegacy', 'Specific', 'General') THEN TaxAccountType
                    ELSE 'Unknown'
                END,
                IsNisa = CASE
                    WHEN AccountType IN ('NisaGrowth', 'NisaAccumulation', 'NisaLegacy', 'NISA')
                      OR TaxAccountType IN ('NisaGrowth', 'NisaAccumulation', 'NisaLegacy', 'NISA') THEN 1
                    ELSE 0
                END;
            """);

        Execute(connection, """
            UPDATE DividendPayments
            SET IsNisa = CASE
                WHEN AccountType IN ('NisaGrowth', 'NisaAccumulation', 'NisaLegacy')
                  OR TaxAccountType IN ('NisaGrowth', 'NisaAccumulation', 'NisaLegacy') THEN 1
                ELSE 0
            END;
            """);

        RemoveTickerUniqueConstraintIfNeeded(connection);
        NormalizeLegacySecurityAliases(connection);
        BackfillCanonicalSecurityKeys(connection);
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

    private static void RepairCsvReimportDuplicates(SqliteConnection connection, string databasePath)
    {
        NormalizeStockIdentityColumns(connection);
        NormalizeMutualFundAccountTypes(connection);
        BackfillCanonicalSecurityKeys(connection);

        var duplicatePositionGroups = ReadDuplicatePositionGroups(connection);
        var duplicateDividendCount = CountDuplicateDividendRows(connection);
        var zeroPriceInboundCount = CountZeroPriceInboundEvents(connection);
        if (duplicatePositionGroups.Count > 0 || duplicateDividendCount > 0 || zeroPriceInboundCount > 0)
        {
            BackupDatabase(databasePath);
        }

        DeduplicateDividendPayments(connection);
        NormalizeZeroPriceInboundEvents(connection);

        foreach (var group in duplicatePositionGroups)
        {
            var rows = ReadDuplicatePositionRows(connection, group);
            if (rows.Count <= 1)
            {
                continue;
            }

            var target = rows
                .OrderByDescending(x => x.PositionValue)
                .ThenByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .First();

            foreach (var duplicate in rows.Where(x => x.Id != target.Id))
            {
                MergeDuplicatePosition(connection, target.Id, duplicate.Id);
            }
        }
    }

    private static void NormalizeStockIdentityColumns(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE Stocks
            SET AssetType = CASE
                    WHEN AssetType = 'MutualFund' OR UPPER(Ticker) LIKE 'FUND:%' THEN 'MutualFund'
                    ELSE 'Stock'
                END,
                Broker = REPLACE(REPLACE(TRIM(Broker), ' ', ''), '　', ''),
                Ticker = CASE
                    WHEN AssetType = 'MutualFund' OR UPPER(Ticker) LIKE 'FUND:%'
                        THEN UPPER(REPLACE(REPLACE(TRIM(Ticker), ' ', ''), '　', ''))
                    ELSE UPPER(TRIM(Ticker))
                END,
                Currency = CASE
                    WHEN UPPER(TRIM(Currency)) = '' OR UPPER(TRIM(Currency)) = 'YEN' THEN 'JPY'
                    ELSE UPPER(TRIM(Currency))
                END,
                CustodyType = CASE
                    WHEN TRIM(CustodyType) = '' THEN AccountType
                    ELSE REPLACE(REPLACE(TRIM(CustodyType), ' ', ''), '　', '')
                END;
            """);
    }

    private static void NormalizeMutualFundAccountTypes(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE Stocks
            SET AccountType = CASE
                    WHEN CustodyType LIKE '%旧%NISA%'
                      OR CustodyType LIKE '%旧ニーサ%'
                      OR AccountType LIKE '%旧%NISA%'
                      OR AccountType LIKE '%旧ニーサ%'
                      OR AccountType = 'NisaLegacy' THEN 'NisaLegacy'
                    WHEN CustodyType LIKE '%つみたて%'
                      OR CustodyType LIKE '%積立%'
                      OR AccountType LIKE '%つみたて%'
                      OR AccountType LIKE '%積立%'
                      OR AccountType = 'NisaAccumulation' THEN 'NisaAccumulation'
                    WHEN AccountType = 'NisaGrowth'
                      OR CustodyType LIKE '%成長投資%'
                      OR AccountType LIKE '%成長投資%'
                      OR AccountType = 'NISA' THEN 'NisaGrowth'
                    WHEN AccountType = 'Specific'
                      OR CustodyType LIKE '%特定%' THEN 'Specific'
                    WHEN AccountType = 'General'
                      OR CustodyType LIKE '%一般%' THEN 'General'
                    ELSE AccountType
                END
            WHERE AssetType = 'MutualFund';
            """);

        Execute(connection, """
            UPDATE MutualFundHoldings
            SET AccountType = (
                SELECT s.AccountType
                FROM Stocks s
                WHERE s.Id = MutualFundHoldings.StockId
            )
            WHERE EXISTS (
                SELECT 1
                FROM Stocks s
                WHERE s.Id = MutualFundHoldings.StockId
                  AND s.AssetType = 'MutualFund'
            );
            """);
    }

    private static void CreateCsvIdempotencyIndexes(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Stocks_PositionCanonicalIdentity
            ON Stocks(Broker, CanonicalSecurityKey, AssetType, AccountType, CustodyType, Currency)
            WHERE CanonicalSecurityKey <> '';
            """);

        Execute(connection, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Stocks_PositionIdentity
            ON Stocks(Broker, Ticker, AssetType, AccountType, CustodyType, Currency);
            """);

        Execute(connection, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_DividendPayments_ImportIdentity
            ON DividendPayments(
                StockId, Broker, AccountType, DividendStatus, PaymentDate, Currency,
                Quantity, DividendPerShare, GrossAmount, TotalTaxAmount, NetAmount, NetAmountJpy
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS CsvImportLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName TEXT NOT NULL DEFAULT '',
                FileSize INTEGER NOT NULL DEFAULT 0,
                FileHash TEXT NOT NULL DEFAULT '',
                Broker TEXT NOT NULL DEFAULT '',
                CsvType TEXT NOT NULL DEFAULT '',
                ImportedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                RowCount INTEGER NOT NULL DEFAULT 0,
                InsertedCount INTEGER NOT NULL DEFAULT 0,
                UpdatedCount INTEGER NOT NULL DEFAULT 0,
                SkippedCount INTEGER NOT NULL DEFAULT 0,
                ConflictCount INTEGER NOT NULL DEFAULT 0,
                Result TEXT NOT NULL DEFAULT '',
                ErrorSummary TEXT NOT NULL DEFAULT ''
            );
            """);

        Execute(connection, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_CsvImportLogs_FileHash
            ON CsvImportLogs(FileHash, Broker, CsvType)
            WHERE FileHash <> '';
            """);
    }

    private static void DropCsvIdempotencyIndexesBeforeMigration(SqliteConnection connection)
    {
        Execute(connection, "DROP INDEX IF EXISTS UX_Stocks_PositionCanonicalIdentity;");
        Execute(connection, "DROP INDEX IF EXISTS UX_Stocks_PositionIdentity;");
    }

    private static void DeduplicateDividendPayments(SqliteConnection connection)
    {
        Execute(connection, """
            DELETE FROM DividendPayments
            WHERE Id NOT IN (
                SELECT MAX(Id)
                FROM DividendPayments
                GROUP BY
                    StockId, Broker, AccountType, DividendStatus, PaymentDate, Currency,
                    Quantity, DividendPerShare, GrossAmount, TotalTaxAmount, NetAmount, NetAmountJpy
            );
            """);
    }

    private static int CountDuplicateDividendRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(RowCount - 1), 0)
            FROM (
                SELECT COUNT(*) AS RowCount
                FROM DividendPayments
                GROUP BY
                    StockId, Broker, AccountType, DividendStatus, PaymentDate, Currency,
                    Quantity, DividendPerShare, GrossAmount, TotalTaxAmount, NetAmount, NetAmountJpy
                HAVING COUNT(*) > 1
            );
            """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int CountZeroPriceInboundEvents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM BrokerTrades
            WHERE SignedQuantity > 0
              AND UnitPrice = 0
              AND SettlementAmountJpy = 0
              AND TradeType NOT IN ('OpeningBalance', 'StockSplit', 'ReverseSplit', 'UnknownAdjustment');
            """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void NormalizeZeroPriceInboundEvents(SqliteConnection connection)
    {
        Execute(connection, """
            UPDATE BrokerTrades
            SET TradeType = CASE
                    WHEN (AfterTradeQuantity > SignedQuantity
                       AND ROUND(AfterTradeQuantity / NULLIF(AfterTradeQuantity - SignedQuantity, 0), 6) IN (2, 3, 4, 5, 10))
                      OR EXISTS (
                          SELECT 1
                          FROM CurrentHoldings ch
                          WHERE ch.StockId = BrokerTrades.StockId
                            AND ch.CurrentShares > BrokerTrades.SignedQuantity
                            AND ROUND(ch.CurrentShares / NULLIF(ch.CurrentShares - BrokerTrades.SignedQuantity, 0), 6) IN (2, 3, 4, 5, 10)
                      )
                        THEN 'StockSplit'
                    ELSE 'TransferIn'
                END,
                AfterTradeQuantity = CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM CurrentHoldings ch
                        WHERE ch.StockId = BrokerTrades.StockId
                          AND ch.CurrentShares > BrokerTrades.SignedQuantity
                          AND ROUND(ch.CurrentShares / NULLIF(ch.CurrentShares - BrokerTrades.SignedQuantity, 0), 6) IN (2, 3, 4, 5, 10)
                    )
                        THEN (
                            SELECT ch.CurrentShares
                            FROM CurrentHoldings ch
                            WHERE ch.StockId = BrokerTrades.StockId
                            LIMIT 1
                        )
                    ELSE AfterTradeQuantity
                END,
                AfterTradeAverageCost = CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM CurrentHoldings ch
                        WHERE ch.StockId = BrokerTrades.StockId
                          AND ch.CurrentShares > BrokerTrades.SignedQuantity
                          AND ROUND(ch.CurrentShares / NULLIF(ch.CurrentShares - BrokerTrades.SignedQuantity, 0), 6) IN (2, 3, 4, 5, 10)
                    )
                        THEN COALESCE((
                            SELECT CASE
                                WHEN ch.CurrentShares <= 0 THEN BrokerTrades.AfterTradeAverageCost
                                WHEN s.AssetType = 'MutualFund' THEN NULLIF(mf.AverageCostNav, 0)
                                ELSE NULLIF(p.Shares * p.UnitPrice / ch.CurrentShares, 0)
                            END
                            FROM CurrentHoldings ch
                            INNER JOIN Stocks s ON s.Id = ch.StockId
                            LEFT JOIN Purchases p ON p.StockId = ch.StockId
                            LEFT JOIN MutualFundHoldings mf ON mf.StockId = ch.StockId
                            WHERE ch.StockId = BrokerTrades.StockId
                            LIMIT 1
                        ), AfterTradeAverageCost)
                    ELSE AfterTradeAverageCost
                END
            WHERE SignedQuantity > 0
              AND UnitPrice = 0
              AND SettlementAmountJpy = 0
              AND TradeType NOT IN ('OpeningBalance', 'StockSplit', 'ReverseSplit', 'UnknownAdjustment');
            """);
    }

    private static void BackfillInitialPositionTrades(SqliteConnection connection, string databasePath)
    {
        var missingCount = CountMissingInitialPositionTrades(connection);
        if (missingCount == 0)
        {
            return;
        }

        BackupDatabase(databasePath, "before_initial_position_backfill");
        Execute(connection, """
            INSERT OR IGNORE INTO BrokerTrades
                (StockId, TradeDate, SettlementDate, Broker, AccountType, CustodyType, TradeType,
                 Quantity, SignedQuantity, UnitPrice, Currency, ExchangeRate, SettlementAmountJpy,
                 FeeJpy, TaxJpy, RealizedGainLoss, RealizedGainLossJpy, AfterTradeQuantity,
                 AfterTradeAverageCost, Source, SourceFile, CreatedAt)
            SELECT
                s.Id,
                SUBSTR(COALESCE(NULLIF(p.PurchaseDate, ''), NULLIF(mf.NavDate, ''), NULLIF(ch.UpdatedAt, ''), DATE('now')), 1, 10),
                SUBSTR(COALESCE(NULLIF(p.PurchaseDate, ''), NULLIF(mf.NavDate, ''), NULLIF(ch.UpdatedAt, ''), DATE('now')), 1, 10),
                s.Broker,
                s.AccountType,
                s.CustodyType,
                'InitialPosition',
                CASE WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.UnitsHeld, 0) ELSE IFNULL(ch.CurrentShares, 0) END,
                CASE WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.UnitsHeld, 0) ELSE IFNULL(ch.CurrentShares, 0) END,
                CASE
                    WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.AverageCostNav, 0)
                    ELSE
                        CASE
                            WHEN IFNULL(ch.CurrentShares, 0) = 0 THEN 0
                            ELSE (
                                CASE
                                    WHEN IFNULL(p.Shares, 0) > 0 THEN IFNULL(p.Shares, 0) * IFNULL(p.UnitPrice, 0)
                                    ELSE IFNULL(ch.CurrentShares, 0) * IFNULL(p.UnitPrice, 0)
                                END
                            ) / NULLIF(IFNULL(ch.CurrentShares, 0), 0)
                        END
                END,
                s.Currency,
                CASE
                    WHEN UPPER(IFNULL(s.Currency, 'JPY')) = 'JPY' THEN 1
                    WHEN IFNULL(p.ExchangeRate, 0) > 0 THEN p.ExchangeRate
                    WHEN IFNULL(ch.CurrentExchangeRate, 0) > 0 THEN ch.CurrentExchangeRate
                    ELSE 1
                END,
                CASE
                    WHEN s.AssetType = 'MutualFund' THEN
                        CASE
                            WHEN IFNULL(mf.AcquisitionAmount, 0) > 0 THEN mf.AcquisitionAmount
                            WHEN IFNULL(mf.UnitBase, 0) > 0 THEN IFNULL(mf.UnitsHeld, 0) / mf.UnitBase * IFNULL(mf.AverageCostNav, 0)
                            ELSE 0
                        END
                    ELSE (
                        CASE
                            WHEN IFNULL(p.Shares, 0) > 0 THEN IFNULL(p.Shares, 0) * IFNULL(p.UnitPrice, 0)
                            ELSE IFNULL(ch.CurrentShares, 0) * IFNULL(p.UnitPrice, 0)
                        END + IFNULL(p.Fee, 0)
                    ) * CASE
                            WHEN UPPER(IFNULL(s.Currency, 'JPY')) = 'JPY' THEN 1
                            WHEN IFNULL(p.ExchangeRate, 0) > 0 THEN p.ExchangeRate
                            WHEN IFNULL(ch.CurrentExchangeRate, 0) > 0 THEN ch.CurrentExchangeRate
                            ELSE 1
                        END
                END,
                0,
                0,
                0,
                0,
                CASE WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.UnitsHeld, 0) ELSE IFNULL(ch.CurrentShares, 0) END,
                CASE
                    WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.AverageCostNav, 0)
                    ELSE
                        CASE
                            WHEN IFNULL(ch.CurrentShares, 0) = 0 THEN 0
                            ELSE (
                                CASE
                                    WHEN IFNULL(p.Shares, 0) > 0 THEN IFNULL(p.Shares, 0) * IFNULL(p.UnitPrice, 0)
                                    ELSE IFNULL(ch.CurrentShares, 0) * IFNULL(p.UnitPrice, 0)
                                END
                            ) / NULLIF(IFNULL(ch.CurrentShares, 0), 0)
                        END
                END,
                'HoldingsSnapshot',
                '',
                CURRENT_TIMESTAMP
            FROM Stocks s
            LEFT JOIN Purchases p ON p.StockId = s.Id
            LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
            LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id
            WHERE NOT EXISTS (SELECT 1 FROM BrokerTrades bt WHERE bt.StockId = s.Id)
              AND (
                    (s.AssetType = 'MutualFund' AND IFNULL(mf.UnitsHeld, 0) > 0)
                 OR (s.AssetType <> 'MutualFund' AND IFNULL(ch.CurrentShares, 0) > 0)
              );
            """);
    }

    private static int CountMissingInitialPositionTrades(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM Stocks s
            LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
            LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id
            WHERE NOT EXISTS (SELECT 1 FROM BrokerTrades bt WHERE bt.StockId = s.Id)
              AND (
                    (s.AssetType = 'MutualFund' AND IFNULL(mf.UnitsHeld, 0) > 0)
                 OR (s.AssetType <> 'MutualFund' AND IFNULL(ch.CurrentShares, 0) > 0)
              );
            """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static IReadOnlyList<DuplicatePositionGroup> ReadDuplicatePositionGroups(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Broker, COALESCE(NULLIF(CanonicalSecurityKey, ''), Ticker), AssetType, AccountType, CustodyType, Currency
            FROM Stocks
            GROUP BY Broker, COALESCE(NULLIF(CanonicalSecurityKey, ''), Ticker), AssetType, AccountType, CustodyType, Currency
            HAVING COUNT(*) > 1;
            """;

        var groups = new List<DuplicatePositionGroup>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(new DuplicatePositionGroup(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return groups;
    }

    private static IReadOnlyList<DuplicatePositionRow> ReadDuplicatePositionRows(
        SqliteConnection connection,
        DuplicatePositionGroup group)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.Id,
                CASE
                    WHEN IFNULL(mf.MarketValue, 0) > 0 THEN IFNULL(mf.MarketValue, 0)
                    ELSE IFNULL(ch.CurrentShares, 0) * IFNULL(ch.CurrentPrice, 0) * IFNULL(ch.CurrentExchangeRate, 1)
                END AS PositionValue,
                COALESCE(NULLIF(mf.UpdatedAt, ''), NULLIF(ch.UpdatedAt, ''), NULLIF(s.UpdatedAt, ''), '') AS UpdatedAt
            FROM Stocks s
            LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
            LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id
            WHERE s.Broker = $broker
              AND COALESCE(NULLIF(s.CanonicalSecurityKey, ''), s.Ticker) = $ticker
              AND s.AssetType = $assetType
              AND s.AccountType = $accountType
              AND s.CustodyType = $custodyType
              AND s.Currency = $currency;
            """;
        command.Parameters.AddWithValue("$broker", group.Broker);
        command.Parameters.AddWithValue("$ticker", group.Ticker);
        command.Parameters.AddWithValue("$assetType", group.AssetType);
        command.Parameters.AddWithValue("$accountType", group.AccountType);
        command.Parameters.AddWithValue("$custodyType", group.CustodyType);
        command.Parameters.AddWithValue("$currency", group.Currency);

        var rows = new List<DuplicatePositionRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DuplicatePositionRow(
                reader.GetInt32(0),
                Convert.ToDecimal(reader.GetValue(1)),
                ParseDateTime(reader.GetString(2))));
        }

        return rows;
    }

    private static void MergeDuplicatePosition(SqliteConnection connection, int targetId, int duplicateId)
    {
        Execute(connection, """
            UPDATE Stocks
            SET Memo = (SELECT Memo FROM Stocks WHERE Id = $duplicateId)
            WHERE Id = $targetId
              AND TRIM(Memo) = ''
              AND EXISTS (SELECT 1 FROM Stocks WHERE Id = $duplicateId AND TRIM(Memo) <> '');
            """,
            ("$targetId", targetId),
            ("$duplicateId", duplicateId));

        Execute(connection, """
            DELETE FROM DataQualityInfos
            WHERE Id IN (
                SELECT d.Id
                FROM DataQualityInfos d
                INNER JOIN DataQualityInfos t ON t.StockId = $targetId AND t.FieldName = d.FieldName
                WHERE d.StockId = $duplicateId
            );
            """,
            ("$targetId", targetId),
            ("$duplicateId", duplicateId));

        Execute(connection, "UPDATE DataQualityInfos SET StockId = $targetId WHERE StockId = $duplicateId;",
            ("$targetId", targetId),
            ("$duplicateId", duplicateId));

        Execute(connection, """
            DELETE FROM BrokerTrades
            WHERE Id IN (
                SELECT d.Id
                FROM BrokerTrades d
                INNER JOIN BrokerTrades t
                    ON t.StockId = $targetId
                   AND t.TradeDate = d.TradeDate
                   AND t.SettlementDate = d.SettlementDate
                   AND t.Broker = d.Broker
                   AND t.AccountType = d.AccountType
                   AND t.TradeType = d.TradeType
                   AND t.Quantity = d.Quantity
                   AND t.UnitPrice = d.UnitPrice
                   AND t.SettlementAmountJpy = d.SettlementAmountJpy
                   AND t.Source = d.Source
                WHERE d.StockId = $duplicateId
            );
            """,
            ("$targetId", targetId),
            ("$duplicateId", duplicateId));

        Execute(connection, "UPDATE BrokerTrades SET StockId = $targetId WHERE StockId = $duplicateId;",
            ("$targetId", targetId),
            ("$duplicateId", duplicateId));

        Execute(connection, """
            DELETE FROM DividendPayments
            WHERE Id IN (
                SELECT d.Id
                FROM DividendPayments d
                INNER JOIN DividendPayments t
                    ON t.StockId = $targetId
                   AND t.Broker = d.Broker
                   AND t.AccountType = d.AccountType
                   AND t.DividendStatus = d.DividendStatus
                   AND t.PaymentDate = d.PaymentDate
                   AND t.Currency = d.Currency
                   AND t.Quantity = d.Quantity
                   AND t.DividendPerShare = d.DividendPerShare
                   AND t.GrossAmount = d.GrossAmount
                   AND t.TotalTaxAmount = d.TotalTaxAmount
                   AND t.NetAmount = d.NetAmount
                   AND t.NetAmountJpy = d.NetAmountJpy
                WHERE d.StockId = $duplicateId
            );
            """,
            ("$targetId", targetId),
            ("$duplicateId", duplicateId));

        Execute(connection, "UPDATE DividendPayments SET StockId = $targetId WHERE StockId = $duplicateId;",
            ("$targetId", targetId),
            ("$duplicateId", duplicateId));

        Execute(connection, "DELETE FROM Stocks WHERE Id = $duplicateId;",
            ("$duplicateId", duplicateId));
    }

    private static void BackupDatabase(string databasePath, string reason = "backup")
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(databasePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(databasePath);
        var extension = Path.GetExtension(databasePath);
        var suffix = string.IsNullOrWhiteSpace(reason) ? "backup" : reason;
        var backupPath = Path.Combine(directory, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}_{suffix}{extension}");
        if (!File.Exists(backupPath))
        {
            File.Copy(databasePath, backupPath);
        }
    }

    private static DateTime ParseDateTime(string value) =>
        DateTime.TryParse(value, out var date) ? date : DateTime.MinValue;

    private sealed record DuplicatePositionGroup(
        string Broker,
        string Ticker,
        string AssetType,
        string AccountType,
        string CustodyType,
        string Currency);

    private sealed record DuplicatePositionRow(int Id, decimal PositionValue, DateTime UpdatedAt);

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
                    CanonicalSecurityKey TEXT NOT NULL DEFAULT '',
                    Name TEXT NOT NULL,
                    Ticker TEXT NOT NULL,
                    Country TEXT NOT NULL,
                Currency TEXT NOT NULL,
                Broker TEXT NOT NULL,
                AccountType TEXT NOT NULL DEFAULT 'Unknown',
                CustodyType TEXT NOT NULL DEFAULT '',
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
                    (Id, AssetType, CanonicalSecurityKey, Name, Ticker, Country, Currency, Broker, AccountType, CustodyType, Sector, Industry, Market, DataSource, Memo, CreatedAt, UpdatedAt)
                SELECT
                    Id, AssetType, CanonicalSecurityKey, Name, Ticker, Country, Currency, Broker, AccountType, CustodyType, Sector, Industry, Market, DataSource, Memo, CreatedAt, UpdatedAt
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

    private static void BackfillCanonicalSecurityKeys(SqliteConnection connection)
    {
        var rows = new List<(int Id, string CanonicalSecurityKey)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    s.Id, s.Name, s.Ticker, s.Country, s.Currency, s.Broker, s.AccountType, s.CustodyType,
                    s.Sector, s.Industry, s.Market, s.DataSource, s.Memo, s.AssetType,
                    mf.FundName, mf.FundCode, mf.AssociationCode
                FROM Stocks s
                LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id;
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var stock = new InvestmentStory.Core.Models.Stock
                {
                    Id = reader.GetInt32(0),
                    Name = GetString(reader, 1),
                    Ticker = GetString(reader, 2),
                    Country = GetString(reader, 3),
                    Currency = GetString(reader, 4),
                    Broker = GetString(reader, 5),
                    AccountType = GetString(reader, 6),
                    CustodyType = GetString(reader, 7),
                    Sector = GetString(reader, 8),
                    Industry = GetString(reader, 9),
                    Market = GetString(reader, 10),
                    DataSource = GetString(reader, 11),
                    Memo = GetString(reader, 12),
                    AssetType = GetString(reader, 13)
                };
                var fund = new InvestmentStory.Core.Models.MutualFundHolding
                {
                    FundName = GetString(reader, 14),
                    FundCode = GetString(reader, 15),
                    AssociationCode = GetString(reader, 16)
                };
                rows.Add((stock.Id, SecurityIdentityService.BuildCanonicalKey(stock, fund)));
            }
        }

        foreach (var row in rows)
        {
            Execute(connection, """
                UPDATE Stocks
                SET CanonicalSecurityKey = $canonicalSecurityKey
                WHERE Id = $id
                  AND IFNULL(CanonicalSecurityKey, '') <> $canonicalSecurityKey;
                """,
                ("$id", row.Id),
                ("$canonicalSecurityKey", row.CanonicalSecurityKey));
        }
    }

    private static string GetString(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? string.Empty : reader.GetString(index);

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

            Execute(
                connection,
                "UPDATE Stocks SET Ticker = 'CMBT', Name = 'CMB テック' WHERE Id = $targetId;",
                ("$targetId", target.Id));

            foreach (var duplicate in stocks.Where(x => x.Id != target.Id))
            {
                MergeDuplicatePosition(connection, target.Id, duplicate.Id);
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

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
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
