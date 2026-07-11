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

            Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.backup_*"));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}*"))
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
