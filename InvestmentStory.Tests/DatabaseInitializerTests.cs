using InvestmentStory.Core.Models;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public void Initialize_MigratesTickerUniqueConstraintAndLegacyEurnAlias()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_{Guid.NewGuid():N}.db");
        try
        {
            CreateLegacyDatabase(path);

            new DatabaseInitializer().Initialize(path);

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString());
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Ticker, Name FROM Stocks WHERE Id = 1;";
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("CMBT", reader.GetString(0));
                Assert.Equal("CMB テック", reader.GetString(1));
            }

            Execute(connection, """
                INSERT INTO Stocks (Name, Ticker, Country, Currency, Broker)
                VALUES ('Cisco SBI', 'CSCO', '米国', 'USD', 'SBI証券');
                """);
            Execute(connection, """
                INSERT INTO Stocks (Name, Ticker, Country, Currency, Broker)
                VALUES ('Cisco Nomura', 'CSCO', '米国', 'USD', '野村証券');
                """);

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM Stocks WHERE Ticker = 'CSCO';";
            Assert.Equal(2L, (long)countCommand.ExecuteScalar()!);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Initialize_RepairsDuplicatePositionsCreatedByCsvReimport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_duplicate_repair_{Guid.NewGuid():N}.db");
        try
        {
            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                Execute(connection, "DROP INDEX IF EXISTS UX_Stocks_PositionIdentity;");
                Execute(connection, """
                    INSERT INTO Stocks
                        (AssetType, Name, Ticker, Country, Currency, Broker, AccountType, CustodyType, Sector, DataSource)
                    VALUES
                        ('MutualFund', 'SBI V S&P500', 'FUND:SBI-V-SP500', 'Japan', 'JPY', 'SBI', 'NisaGrowth', 'NisaGrowth', 'MutualFund', 'SBI CSV');
                    """);
                var firstId = LastInsertId(connection);
                Execute(connection, """
                    INSERT INTO MutualFundHoldings
                        (StockId, FundName, UnitsHeld, UnitBase, AverageCostNav, CurrentNav, AcquisitionAmount, MarketValue, UnrealizedGainLoss, NavDate, NavSource, AccountType)
                    VALUES
                        ($stockId, 'SBI V S&P500', 411318, 10000, 29499, 40579, 1213346, 1669087, 455741, '2026-07-11', 'SBI CSV', 'NisaGrowth');
                    """,
                    ("$stockId", firstId));
                Execute(connection, """
                    INSERT INTO CurrentHoldings (StockId, CurrentShares, CurrentPrice, CurrentExchangeRate, UpdatedAt)
                    VALUES ($stockId, 411318, 40579, 1, '2026-07-11');
                    """,
                    ("$stockId", firstId));

                Execute(connection, """
                    INSERT INTO Stocks
                        (AssetType, Name, Ticker, Country, Currency, Broker, AccountType, CustodyType, Sector, DataSource)
                    VALUES
                        ('MutualFund', 'SBI V S&P500', 'FUND:SBI-V-SP500', 'Japan', 'JPY', 'SBI', 'NisaGrowth', 'NisaGrowth', 'MutualFund', 'SBI CSV');
                    """);
                var secondId = LastInsertId(connection);
                Execute(connection, """
                    INSERT INTO MutualFundHoldings
                        (StockId, FundName, UnitsHeld, UnitBase, AverageCostNav, CurrentNav, AcquisitionAmount, MarketValue, UnrealizedGainLoss, NavDate, NavSource, AccountType)
                    VALUES
                        ($stockId, 'SBI V S&P500', 411318, 10000, 29499, 41000, 1213346, 1686405, 473059, '2026-07-11', 'SBI CSV', 'NisaGrowth');
                    """,
                    ("$stockId", secondId));
                Execute(connection, """
                    INSERT INTO CurrentHoldings (StockId, CurrentShares, CurrentPrice, CurrentExchangeRate, UpdatedAt)
                    VALUES ($stockId, 411318, 41000, 1, '2026-07-11');
                    """,
                    ("$stockId", secondId));
            }

            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = """
                    SELECT COUNT(*)
                    FROM Stocks
                    WHERE Ticker = 'FUND:SBI-V-SP500'
                      AND Broker = 'SBI'
                      AND AssetType = 'MutualFund'
                      AND AccountType = 'NisaGrowth'
                      AND CustodyType = 'NisaGrowth'
                      AND Currency = 'JPY';
                    """;
                Assert.Equal(1L, (long)countCommand.ExecuteScalar()!);

                using var valueCommand = connection.CreateCommand();
                valueCommand.CommandText = """
                    SELECT mf.MarketValue
                    FROM MutualFundHoldings mf
                    INNER JOIN Stocks s ON s.Id = mf.StockId
                    WHERE s.Ticker = 'FUND:SBI-V-SP500';
                    """;
                Assert.Equal(1_686_405d, (double)valueCommand.ExecuteScalar()!, precision: 0);
            }

            Assert.NotEmpty(Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}_*_backup{Path.GetExtension(path)}"));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}*"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Initialize_RepairsMutualFundDuplicatesWithLegacyCanonicalKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_legacy_fund_key_repair_{Guid.NewGuid():N}.db");
        try
        {
            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                Execute(connection, "DROP INDEX IF EXISTS UX_Stocks_PositionCanonicalIdentity;");
                Execute(connection, "DROP INDEX IF EXISTS UX_Stocks_PositionIdentity;");
                InsertMutualFund(
                    connection,
                    canonicalKey: "FUND:JP:FUND:SBI-V-SP500",
                    currentNav: 40_579,
                    marketValue: 1_669_087,
                    unrealizedGainLoss: 455_741);
                InsertMutualFund(
                    connection,
                    canonicalKey: "FUND:JP:FUND:SBIVSP500",
                    currentNav: 41_000,
                    marketValue: 1_686_405,
                    unrealizedGainLoss: 473_059);
            }

            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = """
                    SELECT COUNT(*)
                    FROM Stocks
                    WHERE AssetType = 'MutualFund'
                      AND Broker = 'SBI'
                      AND AccountType = 'NisaGrowth'
                      AND CustodyType = 'NisaGrowth'
                      AND Currency = 'JPY'
                      AND CanonicalSecurityKey = 'FUND:JP:FUND:SBIVSP500';
                    """;
                Assert.Equal(1L, (long)countCommand.ExecuteScalar()!);

                using var valueCommand = connection.CreateCommand();
                valueCommand.CommandText = """
                    SELECT mf.MarketValue
                    FROM MutualFundHoldings mf
                    INNER JOIN Stocks s ON s.Id = mf.StockId
                    WHERE s.CanonicalSecurityKey = 'FUND:JP:FUND:SBIVSP500';
                    """;
                Assert.Equal(1_686_405d, (double)valueCommand.ExecuteScalar()!, precision: 0);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}*{Path.GetExtension(path)}"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Initialize_RepairsMutualFundDuplicatesWithMismatchedAccumulationAccountType()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_fund_account_repair_{Guid.NewGuid():N}.db");
        try
        {
            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                Execute(connection, "DROP INDEX IF EXISTS UX_Stocks_PositionCanonicalIdentity;");
                Execute(connection, "DROP INDEX IF EXISTS UX_Stocks_PositionIdentity;");
                InsertMutualFund(
                    connection,
                    canonicalKey: "FUND:JP:FUND:SBIVSP500",
                    currentNav: 40_579,
                    marketValue: 1_669_087,
                    unrealizedGainLoss: 455_741,
                    accountType: AccountTypes.NisaGrowth,
                    custodyType: "投資信託（金額/NISA預り（つみたて投資枠））");
                InsertMutualFund(
                    connection,
                    canonicalKey: "FUND:JP:FUND:SBIVSP500",
                    currentNav: 41_000,
                    marketValue: 1_686_405,
                    unrealizedGainLoss: 473_059,
                    accountType: AccountTypes.NisaAccumulation,
                    custodyType: "投資信託（金額/NISA預り（つみたて投資枠））");
            }

            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = """
                    SELECT COUNT(*)
                    FROM Stocks
                    WHERE AssetType = 'MutualFund'
                      AND Broker = 'SBI'
                      AND AccountType = 'NisaAccumulation'
                      AND CustodyType = '投資信託（金額/NISA預り（つみたて投資枠））'
                      AND Currency = 'JPY'
                      AND CanonicalSecurityKey = 'FUND:JP:FUND:SBIVSP500';
                    """;
                Assert.Equal(1L, (long)countCommand.ExecuteScalar()!);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}*{Path.GetExtension(path)}"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Initialize_ReclassifiesOldAccumulationNisaMutualFundAsLegacyNisa()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_legacy_nisa_repair_{Guid.NewGuid():N}.db");
        try
        {
            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                InsertMutualFund(
                    connection,
                    canonicalKey: "FUND:JP:FUND:SBIVSP500:LEGACY",
                    currentNav: 40_579,
                    marketValue: 1_001_546,
                    unrealizedGainLoss: 534_871,
                    accountType: AccountTypes.NisaAccumulation,
                    custodyType: "投資信託（金額/旧つみたてNISA預り）");
            }

            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT s.AccountType, mf.AccountType
                    FROM Stocks s
                    JOIN MutualFundHoldings mf ON mf.StockId = s.Id
                    WHERE s.CustodyType = '投資信託（金額/旧つみたてNISA預り）'
                    LIMIT 1;
                    """;

                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(AccountTypes.NisaLegacy, reader.GetString(0));
                Assert.Equal(AccountTypes.NisaLegacy, reader.GetString(1));
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}*{Path.GetExtension(path)}"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Initialize_NormalizesExistingZeroPriceInboundSplitCandidate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_split_repair_{Guid.NewGuid():N}.db");
        try
        {
            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                var stockId = InsertStock(connection, "NVDA", "NVIDIA", "米国", "USD", "SBI証券");
                Execute(connection, """
                    INSERT INTO BrokerTrades
                        (StockId, TradeDate, SettlementDate, Broker, AccountType, CustodyType, TradeType,
                         Quantity, SignedQuantity, UnitPrice, Currency, ExchangeRate, SettlementAmountJpy,
                         AfterTradeQuantity, AfterTradeAverageCost, Source)
                    VALUES
                        ($stockId, '2024-06-10', '2024-06-10', 'SBI証券', 'Specific', 'Specific', '入庫',
                         45, 45, 0, 'USD', 160, 0, 50, 29, 'SBI取引履歴CSV');
                    """,
                    ("$stockId", stockId));
            }

            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT TradeType
                    FROM BrokerTrades
                    WHERE Source = 'SBI取引履歴CSV'
                      AND Quantity = 45
                    LIMIT 1;
                    """;
                Assert.Equal("StockSplit", (string)command.ExecuteScalar()!);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}*{Path.GetExtension(path)}"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Initialize_NormalizesTransferInSplitCandidateUsingCurrentHoldings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_current_holding_split_repair_{Guid.NewGuid():N}.db");
        try
        {
            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                var stockId = InsertStock(connection, "TSLA", "Tesla", "US", "USD", "Nomura");
                Execute(connection, """
                    INSERT INTO Purchases (StockId, PurchaseDate, Shares, UnitPrice, ExchangeRate, Fee)
                    VALUES ($stockId, '2022-08-31', 30, 170.723398984269, 161.46, 0);
                    """,
                    ("$stockId", stockId));
                Execute(connection, """
                    INSERT INTO CurrentHoldings (StockId, CurrentShares, CurrentPrice, CurrentExchangeRate, UpdatedAt)
                    VALUES ($stockId, 30, 419.77, 161.672, '2026-07-12');
                    """,
                    ("$stockId", stockId));
                Execute(connection, """
                    INSERT INTO BrokerTrades
                        (StockId, TradeDate, SettlementDate, Broker, AccountType, CustodyType, TradeType,
                         Quantity, SignedQuantity, UnitPrice, Currency, ExchangeRate, SettlementAmountJpy,
                         AfterTradeQuantity, AfterTradeAverageCost, Source)
                    VALUES
                        ($stockId, '2022-08-31', '2022-08-31', 'Nomura', 'Specific', 'Specific', 'TransferIn',
                         20, 20, 0, 'USD', 138.77, 0, 20, 0, 'Nomura CSV');
                    """,
                    ("$stockId", stockId));
            }

            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT TradeType, AfterTradeQuantity, AfterTradeAverageCost
                    FROM BrokerTrades
                    WHERE Source = 'Nomura CSV'
                    LIMIT 1;
                    """;
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("StockSplit", reader.GetString(0));
                Assert.Equal(30d, reader.GetDouble(1), precision: 0);
                Assert.Equal(170.723398984269d, reader.GetDouble(2), precision: 6);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}*{Path.GetExtension(path)}"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Initialize_BackfillsInitialPositionTradesFromCurrentHoldings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_initial_position_{Guid.NewGuid():N}.db");
        try
        {
            new DatabaseInitializer().Initialize(path);

            long stockId;
            long fundId;
            using (var connection = Open(path))
            {
                stockId = InsertStock(connection, "TEST", "Test Stock", "Japan", "JPY", "SBI");
                Execute(connection, """
                    INSERT INTO Purchases (StockId, PurchaseDate, Shares, UnitPrice, ExchangeRate, Fee)
                    VALUES ($stockId, '2026-07-01', 70, 1200, 1, 0);
                    """,
                    ("$stockId", stockId));
                Execute(connection, """
                    INSERT INTO CurrentHoldings (StockId, CurrentShares, CurrentPrice, CurrentExchangeRate, UpdatedAt)
                    VALUES ($stockId, 70, 1400, 1, '2026-07-12');
                    """,
                    ("$stockId", stockId));

                InsertMutualFund(
                    connection,
                    canonicalKey: "FUND:JP:FUND:TEST",
                    currentNav: 40579,
                    marketValue: 1669087,
                    unrealizedGainLoss: 455741,
                    accountType: AccountTypes.NisaAccumulation,
                    custodyType: AccountTypes.NisaAccumulation);
                using var fundIdCommand = connection.CreateCommand();
                fundIdCommand.CommandText = "SELECT Id FROM Stocks WHERE CanonicalSecurityKey = 'FUND:JP:FUND:TEST' LIMIT 1;";
                fundId = (long)fundIdCommand.ExecuteScalar()!;
            }

            new DatabaseInitializer().Initialize(path);

            using (var connection = Open(path))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT TradeType, Quantity, UnitPrice, SettlementAmountJpy, RealizedGainLossJpy, AfterTradeQuantity
                    FROM BrokerTrades
                    WHERE StockId = $stockId
                      AND Source = 'HoldingsSnapshot';
                    """;
                command.Parameters.AddWithValue("$stockId", stockId);
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("InitialPosition", reader.GetString(0));
                Assert.Equal(70d, reader.GetDouble(1), precision: 0);
                Assert.Equal(1200d, reader.GetDouble(2), precision: 0);
                Assert.Equal(84000d, reader.GetDouble(3), precision: 0);
                Assert.Equal(0d, reader.GetDouble(4), precision: 0);
                Assert.Equal(70d, reader.GetDouble(5), precision: 0);
            }

            using (var connection = Open(path))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT TradeType, Quantity, UnitPrice, SettlementAmountJpy, AfterTradeQuantity
                    FROM BrokerTrades
                    WHERE StockId = $stockId
                      AND Source = 'HoldingsSnapshot';
                    """;
                command.Parameters.AddWithValue("$stockId", fundId);
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("InitialPosition", reader.GetString(0));
                Assert.Equal(411318d, reader.GetDouble(1), precision: 0);
                Assert.Equal(29499d, reader.GetDouble(2), precision: 0);
                Assert.Equal(1213346d, reader.GetDouble(3), precision: 0);
                Assert.Equal(411318d, reader.GetDouble(4), precision: 0);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}*{Path.GetExtension(path)}"))
            {
                File.Delete(file);
            }
        }
    }

    private static void CreateLegacyDatabase(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();

        Execute(connection, """
            CREATE TABLE Stocks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Ticker TEXT NOT NULL UNIQUE,
                Country TEXT NOT NULL,
                Currency TEXT NOT NULL,
                Broker TEXT NOT NULL,
                Memo TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            INSERT INTO Stocks (Id, Name, Ticker, Country, Currency, Broker, Memo)
            VALUES (1, 'ユーロナブ', 'EURN', '米国', 'USD', 'SBI証券', 'legacy');
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long LastInsertId(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_insert_rowid();";
        return (long)command.ExecuteScalar()!;
    }

    private static long InsertStock(
        SqliteConnection connection,
        string ticker,
        string name,
        string country,
        string currency,
        string broker)
    {
        Execute(connection, """
            INSERT INTO Stocks
                (Name, Ticker, Country, Currency, Broker, AccountType, CustodyType)
            VALUES
                ($name, $ticker, $country, $currency, $broker, 'Specific', 'Specific');
            """,
            ("$name", name),
            ("$ticker", ticker),
            ("$country", country),
            ("$currency", currency),
            ("$broker", broker));
        return LastInsertId(connection);
    }

    private static void InsertMutualFund(
        SqliteConnection connection,
        string canonicalKey,
        double currentNav,
        double marketValue,
        double unrealizedGainLoss,
        string accountType = AccountTypes.NisaGrowth,
        string custodyType = AccountTypes.NisaGrowth)
    {
        Execute(connection, """
            INSERT INTO Stocks
                (AssetType, CanonicalSecurityKey, Name, Ticker, Country, Currency, Broker, AccountType, CustodyType, Sector, DataSource)
            VALUES
                ('MutualFund', $canonicalKey, 'SBI V S&P500', 'FUND:SBI-V-SP500', 'Japan', 'JPY', 'SBI', $accountType, $custodyType, 'MutualFund', 'SBI CSV');
            """,
            ("$canonicalKey", canonicalKey),
            ("$accountType", accountType),
            ("$custodyType", custodyType));
        var stockId = LastInsertId(connection);
        Execute(connection, """
            INSERT INTO MutualFundHoldings
                (StockId, FundName, FundCode, UnitsHeld, UnitBase, AverageCostNav, CurrentNav, AcquisitionAmount, MarketValue, UnrealizedGainLoss, NavDate, NavSource, AccountType)
            VALUES
                ($stockId, 'SBI V S&P500', 'FUND:SBI-V-SP500', 411318, 10000, 29499, $currentNav, 1213346, $marketValue, $unrealizedGainLoss, '2026-07-11', 'SBI CSV', $accountType);
            """,
            ("$stockId", stockId),
            ("$currentNav", currentNav),
            ("$marketValue", marketValue),
            ("$unrealizedGainLoss", unrealizedGainLoss),
            ("$accountType", accountType));
        Execute(connection, """
            INSERT INTO CurrentHoldings (StockId, CurrentShares, CurrentPrice, CurrentExchangeRate, UpdatedAt)
            VALUES ($stockId, 411318, $currentNav, 1, '2026-07-11');
            """,
            ("$stockId", stockId),
            ("$currentNav", currentNav));
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        command.ExecuteNonQuery();
    }
}
