using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class MonthEndPortfolioRepository(string databasePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static void EnsureSchema(SqliteConnection connection, string databasePath, bool existingDatabase)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MonthEndPortfolioArchives';";
        if (Convert.ToInt32(check.ExecuteScalar()) == 1) return;
        if (existingDatabase)
            DatabaseInitializer.BackupDatabase(connection, databasePath, "before_month_end_portfolio_archive");
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS MonthEndPortfolioDrafts (
                SnapshotDate TEXT PRIMARY KEY,
                PayloadJson TEXT NOT NULL,
                PayloadHash TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS MonthEndPortfolioArchives (
                SnapshotDate TEXT PRIMARY KEY,
                PayloadJson TEXT NOT NULL,
                PayloadHash TEXT NOT NULL,
                FrozenAtUtc TEXT NOT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    public bool SaveDraft(MonthEndPortfolioRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!MonthEndPortfolioService.IsMonthEnd(record.SnapshotDate))
            throw new ArgumentException("月末日以外は保存できません。", nameof(record));
        if (record.IsFrozen) throw new ArgumentException("固定済みの記録は変更できません。", nameof(record));
        var payload = JsonSerializer.Serialize(record, JsonOptions);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MonthEndPortfolioDrafts(SnapshotDate, PayloadJson, PayloadHash, UpdatedAtUtc)
            SELECT $date, $payload, $hash, $now
            WHERE NOT EXISTS(SELECT 1 FROM MonthEndPortfolioArchives WHERE SnapshotDate=$date)
            ON CONFLICT(SnapshotDate) DO UPDATE SET
                PayloadJson=excluded.PayloadJson, PayloadHash=excluded.PayloadHash,
                UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$date", record.SnapshotDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$hash", Hash(payload));
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        return command.ExecuteNonQuery() == 1;
    }

    public int FreezePastMonthEnds(DateTime today)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var freeze = connection.CreateCommand();
        freeze.Transaction = transaction;
        freeze.CommandText = """
            INSERT OR IGNORE INTO MonthEndPortfolioArchives
                (SnapshotDate, PayloadJson, PayloadHash, FrozenAtUtc)
            SELECT SnapshotDate, PayloadJson, PayloadHash, $now
            FROM MonthEndPortfolioDrafts WHERE SnapshotDate < $today;
            """;
        freeze.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        freeze.Parameters.AddWithValue("$today", today.Date.ToString("yyyy-MM-dd"));
        var count = freeze.ExecuteNonQuery();
        using var prune = connection.CreateCommand();
        prune.Transaction = transaction;
        prune.CommandText = """
            DELETE FROM MonthEndPortfolioDrafts
            WHERE SnapshotDate < $today AND SnapshotDate IN
                (SELECT SnapshotDate FROM MonthEndPortfolioArchives);
            """;
        prune.Parameters.AddWithValue("$today", today.Date.ToString("yyyy-MM-dd"));
        prune.ExecuteNonQuery();
        transaction.Commit();
        return count;
    }

    public IReadOnlyList<MonthEndPortfolioRecord> GetRecords()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PayloadJson, PayloadHash, 1 FROM MonthEndPortfolioArchives
            UNION ALL
            SELECT PayloadJson, PayloadHash, 0 FROM MonthEndPortfolioDrafts
            ORDER BY 1;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<MonthEndPortfolioRecord>();
        while (reader.Read())
        {
            var payload = reader.GetString(0);
            if (!string.Equals(Hash(payload), reader.GetString(1), StringComparison.Ordinal))
                throw new InvalidDataException("月末記録の整合性が確認できません。復元・監査を行ってください。");
            var record = JsonSerializer.Deserialize<MonthEndPortfolioRecord>(payload, JsonOptions)
                ?? throw new InvalidDataException("月末記録を読み込めません。");
            record.IsFrozen = reader.GetInt32(2) == 1;
            result.Add(record);
        }
        return result.OrderByDescending(x => x.SnapshotDate).ToList();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open();
        return connection;
    }

    private static string Hash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
