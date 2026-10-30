using System.Text.Json;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class FundDisclosureRepository(string databasePath)
{
    internal static void EnsureSchema(SqliteConnection connection, string databasePath, bool existingDatabase)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FundDisclosures';";
        if (Convert.ToInt32(check.ExecuteScalar()) != 0) return;
        if (existingDatabase)
            DatabaseInitializer.BackupDatabase(connection, databasePath, "before_fund_disclosures");
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE FundDisclosures (
                Id TEXT PRIMARY KEY,
                FundTicker TEXT NOT NULL,
                PublishedDate TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL,
                PayloadJson TEXT NOT NULL
            );
            CREATE INDEX IX_FundDisclosures_FundDate
                ON FundDisclosures(FundTicker, PublishedDate DESC, RecordedAtUtc DESC);
            """;
        create.ExecuteNonQuery();
    }

    public FundDisclosure Save(FundDisclosure disclosure)
    {
        FundLookThroughService.Validate(disclosure);
        var value = disclosure with
        {
            Id = Guid.NewGuid().ToString("N"),
            FundTicker = disclosure.FundTicker.Trim().ToUpperInvariant(),
            Source = disclosure.Source.Trim(),
            UsageRightsNote = disclosure.UsageRightsNote.Trim(),
            RecordedAtUtc = DateTime.UtcNow,
            Constituents = disclosure.Constituents.Select(x => x with
            {
                Ticker = x.Ticker.Trim().ToUpperInvariant(), Name = x.Name.Trim()
            }).ToArray()
        };
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FundDisclosures(Id, FundTicker, PublishedDate, RecordedAtUtc, PayloadJson)
            VALUES ($id, $ticker, $published, $recorded, $payload);
            """;
        command.Parameters.AddWithValue("$id", value.Id);
        command.Parameters.AddWithValue("$ticker", value.FundTicker);
        command.Parameters.AddWithValue("$published", value.PublishedDate.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$recorded", value.RecordedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(value));
        command.ExecuteNonQuery();
        return value;
    }

    public IReadOnlyList<FundDisclosure> GetHistory()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM FundDisclosures ORDER BY PublishedDate DESC, RecordedAtUtc DESC, Id DESC;";
        using var reader = command.ExecuteReader();
        var result = new List<FundDisclosure>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<FundDisclosure>(reader.GetString(0))!);
        return result;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }
}
