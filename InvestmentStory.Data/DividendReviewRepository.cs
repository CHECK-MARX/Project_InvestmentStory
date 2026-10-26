using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class DividendReviewRepository(string databasePath)
{
    public static void EnsureSchema(SqliteConnection connection, string databasePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DividendReviewHistory';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stocks';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        command.CommandText = """
            CREATE TABLE DividendReviewHistory (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              PlannedDividendId INTEGER NOT NULL,
              Action TEXT NOT NULL CHECK (Action IN ('Linked','Held','Excluded')),
              ActualDividendId INTEGER NULL,
              PreviousStatus TEXT NOT NULL,
              NewStatus TEXT NOT NULL,
              Reason TEXT NOT NULL,
              AtUtc TEXT NOT NULL
            );
            CREATE INDEX IX_DividendReviewHistory_Planned
              ON DividendReviewHistory(PlannedDividendId, Id);
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DividendReviewDecision> GetHistory(int plannedDividendId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,PlannedDividendId,Action,ActualDividendId,PreviousStatus,NewStatus,Reason,AtUtc
            FROM DividendReviewHistory WHERE PlannedDividendId=$id ORDER BY Id DESC;
            """;
        command.Parameters.AddWithValue("$id", plannedDividendId);
        using var reader = command.ExecuteReader();
        var result = new List<DividendReviewDecision>();
        while (reader.Read()) result.Add(Read(reader));
        return result;
    }

    public IReadOnlyDictionary<int, DividendReviewDecision> GetLatestDecisions()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT h.Id,h.PlannedDividendId,h.Action,h.ActualDividendId,h.PreviousStatus,h.NewStatus,h.Reason,h.AtUtc
            FROM DividendReviewHistory h
            WHERE h.Id=(SELECT MAX(n.Id) FROM DividendReviewHistory n
                        WHERE n.PlannedDividendId=h.PlannedDividendId);
            """;
        using var reader = command.ExecuteReader();
        var result = new Dictionary<int, DividendReviewDecision>();
        while (reader.Read())
        {
            var item = Read(reader);
            result[item.PlannedDividendId] = item;
        }
        return result;
    }

    public void Apply(int plannedId, string action, int? actualId, string reason)
    {
        if (plannedId <= 0 || action is not ("Linked" or "Held" or "Excluded"))
            throw new ArgumentException("配当照合の対象または操作が無効です。");
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 1000)
            throw new ArgumentException("判断理由を1〜1000文字で入力してください。");
        var payments = new InvestmentStoryRepository(databasePath).GetDividendPayments();
        var planned = payments.SingleOrDefault(x => x.Id == plannedId)
            ?? throw new InvalidOperationException("予定が見つかりません。");
        if (!DividendConstants.IsUnconfirmed(planned.DividendStatus) ||
            planned.MatchedActualDividendId is not null || planned.ReplacedByDividendId is not null)
            throw new InvalidOperationException("この予定は既に照合済みか、確認対象外です。");
        if (action == "Linked")
        {
            if (actualId is not > 0 || !new DividendReconciliationService()
                    .FindActualCandidates(planned, payments).Any(x => x.Id == actualId))
                throw new ArgumentException("同じ銘柄・証券会社・口座に対応する実績候補を選択してください。");
            if (payments.Any(x => x.Id != plannedId &&
                                  x.MatchedActualDividendId == actualId &&
                                  string.Equals(x.DividendStatus, DividendConstants.Replaced,
                                      StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("この実績は別の予定と紐付け済みです。");
        }
        else if (actualId is not null)
            throw new ArgumentException("紐付け以外では実績を指定しないでください。");

        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        var newStatus = action switch
        {
            "Linked" => DividendConstants.Replaced,
            "Excluded" => DividendConstants.Excluded,
            _ => planned.DividendStatus
        };
        if (action != "Held")
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE DividendPayments
                SET DividendStatus=$newStatus, MatchedActualDividendId=$actualId,
                    ReplacedByDividendId=$actualId, UpdatedAt=$at
                WHERE Id=$id AND DividendStatus=$oldStatus
                  AND MatchedActualDividendId IS NULL AND ReplacedByDividendId IS NULL;
                """;
            update.Parameters.AddWithValue("$newStatus", newStatus);
            update.Parameters.AddWithValue("$actualId", actualId is null ? DBNull.Value : actualId.Value);
            update.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$id", plannedId);
            update.Parameters.AddWithValue("$oldStatus", planned.DividendStatus);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("予定が変更されています。再読み込みしてください。");
        }
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO DividendReviewHistory
              (PlannedDividendId,Action,ActualDividendId,PreviousStatus,NewStatus,Reason,AtUtc)
            VALUES ($id,$action,$actualId,$oldStatus,$newStatus,$reason,$at);
            """;
        insert.Parameters.AddWithValue("$id", plannedId);
        insert.Parameters.AddWithValue("$action", action);
        insert.Parameters.AddWithValue("$actualId", actualId is null ? DBNull.Value : actualId.Value);
        insert.Parameters.AddWithValue("$oldStatus", planned.DividendStatus);
        insert.Parameters.AddWithValue("$newStatus", newStatus);
        insert.Parameters.AddWithValue("$reason", reason);
        insert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        insert.ExecuteNonQuery();
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open();
        return connection;
    }

    private static DividendReviewDecision Read(SqliteDataReader reader) => new(
        reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4),
        reader.GetString(5), reader.GetString(6), DateTime.Parse(reader.GetString(7),
            null, System.Globalization.DateTimeStyles.RoundtripKind));
}
