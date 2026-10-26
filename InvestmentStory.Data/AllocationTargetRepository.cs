using System.Text.Json;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class AllocationTargetRepository(string databasePath)
{
    public static void EnsureSchema(SqliteConnection connection, string databasePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AllocationTargetRevisions';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stocks';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        command.CommandText = """
            CREATE TABLE AllocationTargetRevisions (
              Dimension TEXT NOT NULL,
              Revision INTEGER NOT NULL,
              UpdatedAtUtc TEXT NOT NULL,
              Details TEXT NOT NULL,
              PRIMARY KEY (Dimension, Revision)
            );
            """;
        command.ExecuteNonQuery();
    }

    public AllocationTargetPlan? GetLatest(AllocationDimension dimension)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Details FROM AllocationTargetRevisions WHERE Dimension=$dimension ORDER BY Revision DESC LIMIT 1;";
        command.Parameters.AddWithValue("$dimension", dimension.ToString());
        return command.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<AllocationTargetPlan>(json) : null;
    }

    public IReadOnlyList<AllocationTargetPlan> GetHistory(AllocationDimension dimension)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Details FROM AllocationTargetRevisions WHERE Dimension=$dimension ORDER BY Revision DESC LIMIT 50;";
        command.Parameters.AddWithValue("$dimension", dimension.ToString());
        using var reader = command.ExecuteReader();
        var rows = new List<AllocationTargetPlan>();
        while (reader.Read()) rows.Add(JsonSerializer.Deserialize<AllocationTargetPlan>(reader.GetString(0))!);
        return rows;
    }

    public AllocationTargetPlan Save(AllocationDimension dimension, IReadOnlyList<AllocationTarget> targets,
        int expectedRevision, IReadOnlyList<AllocationClassificationEvidence>? classificationEvidence = null)
    {
        AllocationTargetService.ValidateTargets(targets);
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(Revision),0) FROM AllocationTargetRevisions WHERE Dimension=$dimension;";
        command.Parameters.AddWithValue("$dimension", dimension.ToString());
        var actualRevision = Convert.ToInt32(command.ExecuteScalar());
        if (actualRevision != expectedRevision)
            throw new InvalidOperationException("配分目標が別の操作で更新されています。再読込して確認してください。");
        var plan = new AllocationTargetPlan(dimension, actualRevision + 1, DateTime.UtcNow,
            targets.Select(x => x with { Key = x.Key.Trim(), Label = x.Label.Trim() }).ToArray(),
            classificationEvidence?.ToArray() ?? []);
        command.CommandText = "INSERT INTO AllocationTargetRevisions (Dimension,Revision,UpdatedAtUtc,Details) VALUES ($dimension,$revision,$at,$details);";
        command.Parameters.AddWithValue("$revision", plan.Revision);
        command.Parameters.AddWithValue("$at", plan.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(plan));
        command.ExecuteNonQuery();
        transaction.Commit();
        return plan;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open();
        return connection;
    }
}
