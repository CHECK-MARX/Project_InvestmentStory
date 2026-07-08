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
}
