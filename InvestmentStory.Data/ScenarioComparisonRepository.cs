using System.Text.Json;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class ScenarioComparisonRepository(string databasePath)
{
    internal static void EnsureSchema(SqliteConnection connection, string databasePath, bool existingDatabase)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SavedScenarios';";
        if (Convert.ToInt32(check.ExecuteScalar()) != 0) return;
        if (existingDatabase)
            DatabaseInitializer.BackupDatabase(connection, databasePath, "before_saved_scenarios");
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE SavedScenarios (
                Id TEXT PRIMARY KEY,
                RecordedAtUtc TEXT NOT NULL,
                PayloadJson TEXT NOT NULL
            );
            CREATE INDEX IX_SavedScenarios_RecordedAtUtc ON SavedScenarios(RecordedAtUtc DESC);
            """;
        create.ExecuteNonQuery();
    }

    public SavedScenario Save(ScenarioInputs inputs, ScenarioBaseline baseline, ScenarioOutcome outcome)
    {
        ScenarioComparisonService.Validate(inputs);
        var saved = new SavedScenario(Guid.NewGuid().ToString("N"), DateTime.UtcNow,
            inputs with { Name = inputs.Name.Trim() }, baseline, outcome);
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SavedScenarios(Id, RecordedAtUtc, PayloadJson) VALUES ($id, $at, $payload);";
        command.Parameters.AddWithValue("$id", saved.Id);
        command.Parameters.AddWithValue("$at", saved.RecordedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(saved));
        command.ExecuteNonQuery();
        return saved;
    }

    public IReadOnlyList<SavedScenario> GetHistory()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM SavedScenarios ORDER BY RecordedAtUtc DESC, Id DESC;";
        using var reader = command.ExecuteReader();
        var result = new List<SavedScenario>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<SavedScenario>(reader.GetString(0))!);
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
