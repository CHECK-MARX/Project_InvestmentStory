using System.Globalization;
using InvestmentStory.Core.Models;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class ReminderRepository(string databasePath)
{
    public static void EnsureSchema(SqliteConnection connection, string databasePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ReminderStates';";
        var hasStates = Convert.ToInt32(command.ExecuteScalar()) != 0;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ReminderPreferences';";
        var hasPreferences = Convert.ToInt32(command.ExecuteScalar()) != 0;
        if (hasStates && hasPreferences) return;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stocks';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        if (!hasStates)
        {
            command.CommandText = """
            CREATE TABLE ReminderStates (
              EventKey TEXT PRIMARY KEY,
              ReadAtUtc TEXT NULL,
              SnoozedUntilUtc TEXT NULL,
              LastNotifiedAtUtc TEXT NULL
            );
            """;
            command.ExecuteNonQuery();
        }
        if (!hasPreferences)
        {
            command.CommandText = """
                CREATE TABLE ReminderPreferences (
                  Id INTEGER PRIMARY KEY CHECK (Id = 1),
                  Mode TEXT NOT NULL CHECK (Mode IN ('InApp', 'Windows'))
                );
                INSERT INTO ReminderPreferences (Id, Mode) VALUES (1, 'InApp');
                """;
            command.ExecuteNonQuery();
        }
    }

    public string GetMode()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Mode FROM ReminderPreferences WHERE Id=1;";
        return command.ExecuteScalar() as string ?? "InApp";
    }

    public void SaveMode(string mode)
    {
        if (mode is not ("InApp" or "Windows")) throw new ArgumentException("通知方法が無効です。");
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ReminderPreferences SET Mode=$mode WHERE Id=1;";
        command.Parameters.AddWithValue("$mode", mode);
        command.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, ReminderState> GetStates()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT EventKey,ReadAtUtc,SnoozedUntilUtc,LastNotifiedAtUtc FROM ReminderStates;";
        using var reader = command.ExecuteReader();
        var states = new Dictionary<string, ReminderState>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var key = reader.GetString(0);
            states[key] = new ReminderState(key, ReadUtc(reader, 1), ReadUtc(reader, 2), ReadUtc(reader, 3));
        }
        return states;
    }

    public void MarkRead(string key, DateTime nowUtc) => Update(key,
        "ReadAtUtc=$value", nowUtc.ToUniversalTime().ToString("O"));

    public void MarkUnread(string key) => Update(key, "ReadAtUtc=$value", DBNull.Value);

    public void SnoozeUntil(string key, DateTime untilUtc, DateTime nowUtc)
    {
        if (untilUtc.ToUniversalTime() <= nowUtc.ToUniversalTime())
            throw new ArgumentException("延期先は現在より後にしてください。");
        Update(key, "SnoozedUntilUtc=$value", untilUtc.ToUniversalTime().ToString("O"));
    }

    public void ClearSnooze(string key) => Update(key, "SnoozedUntilUtc=$value", DBNull.Value);

    public void MarkNotified(IReadOnlyList<string> keys, DateTime nowUtc)
    {
        if (keys.Count == 0) return;
        foreach (var key in keys) ValidateKey(key);
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ReminderStates (EventKey,LastNotifiedAtUtc) VALUES ($key,$at)
                ON CONFLICT(EventKey) DO UPDATE SET LastNotifiedAtUtc=excluded.LastNotifiedAtUtc;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$at", nowUtc.ToUniversalTime().ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private void Update(string key, string assignment, object value)
    {
        ValidateKey(key);
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO ReminderStates (EventKey) VALUES ($key) ON CONFLICT(EventKey) DO NOTHING;
            UPDATE ReminderStates SET {assignment} WHERE EventKey=$key;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery(); transaction.Commit();
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Contains('\n'))
            throw new ArgumentException("通知IDが無効です。");
    }

    private static DateTime? ReadUtc(SqliteDataReader reader, int index) => reader.IsDBNull(index)
        ? null : DateTime.Parse(reader.GetString(index), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open(); return connection;
    }
}
