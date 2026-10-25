using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class CsvReviewDispositionRepository(string databasePath)
{
    public static void EnsureSchema(SqliteConnection connection, string databasePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CsvReviewDispositions';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stocks';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        command.CommandText = """
            CREATE TABLE CsvReviewDispositions (
              SourceKey TEXT PRIMARY KEY,
              Status TEXT NOT NULL CHECK (Status IN ('要確認','保留','確認済み')),
              Note TEXT NOT NULL,
              UpdatedAtUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Save(string sourceKey, string status, string note)
    {
        if (!sourceKey.StartsWith("holding:", StringComparison.Ordinal) &&
            !sourceKey.StartsWith("dividend:", StringComparison.Ordinal))
            throw new ArgumentException("この記録の状態は取込画面で管理してください。");
        if (!int.TryParse(sourceKey[(sourceKey.IndexOf(':') + 1)..], out var id) || id <= 0)
            throw new ArgumentException("要確認記録のIDが無効です。");
        if (status is not ("要確認" or "保留" or "確認済み"))
            throw new ArgumentException("確認状態が無効です。");
        note = note?.Trim() ?? string.Empty;
        if (status != "要確認" && note.Length == 0)
            throw new ArgumentException("保留または確認済みにする理由を入力してください。");
        if (note.Length > 1000) throw new ArgumentException("理由は1000文字以内にしてください。");

        using var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open();
        var table = sourceKey.StartsWith("holding:", StringComparison.Ordinal)
            ? "CsvImportReviewHoldings" : "CsvImportReviewDividends";
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = $"SELECT COUNT(*) FROM {table} WHERE Id=$id;";
            exists.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(exists.ExecuteScalar()) != 1)
                throw new InvalidOperationException("対象の要確認記録が見つかりません。");
        }
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CsvReviewDispositions (SourceKey,Status,Note,UpdatedAtUtc)
            VALUES ($key,$status,$note,$at)
            ON CONFLICT(SourceKey) DO UPDATE SET Status=excluded.Status,Note=excluded.Note,UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$key", sourceKey);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$note", note);
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
