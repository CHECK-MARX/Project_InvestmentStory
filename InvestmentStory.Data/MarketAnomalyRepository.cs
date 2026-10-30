using System.Security.Cryptography;
using System.Text;
using InvestmentStory.Core.Models;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class MarketAnomalyRepository(string databasePath)
{
    public void RecordCandidates(IEnumerable<MarketAnomalyCandidate> candidates)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var candidate in candidates)
        {
            if (candidate.StockId <= 0 || string.IsNullOrWhiteSpace(candidate.Category))
                throw new ArgumentException("銘柄と異常値の種類が必要です。");
            var fingerprint = ComputeFingerprint(candidate);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO MarketAnomalyCandidates
                    (StockId, Fingerprint, Category, RuleVersion, PreviousValue,
                     IncomingValue, Evidence, SuggestedCheck, ObservedAt, Status, RecordedAtUtc)
                VALUES ($stockId, $fingerprint, $category, $version, $previous,
                        $incoming, $evidence, $check, $observedAt, 'Pending', $recordedAt);
                """;
            command.Parameters.AddWithValue("$stockId", candidate.StockId);
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.Parameters.AddWithValue("$category", candidate.Category);
            command.Parameters.AddWithValue("$version", candidate.RuleVersion);
            command.Parameters.AddWithValue("$previous", candidate.PreviousValue);
            command.Parameters.AddWithValue("$incoming", candidate.IncomingValue);
            command.Parameters.AddWithValue("$evidence", candidate.Evidence);
            command.Parameters.AddWithValue("$check", candidate.SuggestedCheck);
            command.Parameters.AddWithValue("$observedAt", candidate.ObservedAt.ToString("O"));
            command.Parameters.AddWithValue("$recordedAt", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<MarketAnomalyCandidate> GetCandidates(int stockId, bool includeResolved = true)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StockId, Fingerprint, Category, RuleVersion, PreviousValue,
                   IncomingValue, Evidence, SuggestedCheck, ObservedAt, Status, RecordedAtUtc
            FROM MarketAnomalyCandidates
            WHERE StockId = $stockId AND ($includeResolved = 1 OR Status = 'Pending')
            ORDER BY Id DESC;
            """;
        command.Parameters.AddWithValue("$stockId", stockId);
        command.Parameters.AddWithValue("$includeResolved", includeResolved ? 1 : 0);
        using var reader = command.ExecuteReader();
        var result = new List<MarketAnomalyCandidate>();
        while (reader.Read())
        {
            result.Add(new MarketAnomalyCandidate
            {
                Id = reader.GetInt32(0),
                StockId = reader.GetInt32(1),
                Fingerprint = reader.GetString(2),
                Category = reader.GetString(3),
                RuleVersion = reader.GetString(4),
                PreviousValue = reader.GetString(5),
                IncomingValue = reader.GetString(6),
                Evidence = reader.GetString(7),
                SuggestedCheck = reader.GetString(8),
                ObservedAt = DateTime.Parse(reader.GetString(9)),
                Status = reader.GetString(10),
                RecordedAtUtc = DateTime.Parse(reader.GetString(11))
            });
        }
        return result;
    }

    public void Review(int candidateId, string decision, string reason)
    {
        if (decision is not ("Confirmed" or "Dismissed" or "Pending"))
            throw new ArgumentException("確認・対象外・再確認待ちのいずれかを選んでください。");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("判断理由を入力してください。");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE MarketAnomalyCandidates SET Status = $status WHERE Id = $id;";
        update.Parameters.AddWithValue("$status", decision);
        update.Parameters.AddWithValue("$id", candidateId);
        if (update.ExecuteNonQuery() != 1)
            throw new ArgumentException("確認対象の異常値候補が見つかりません。");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO MarketAnomalyDecisions(CandidateId, Decision, Reason, RecordedAtUtc)
            VALUES ($id, $decision, $reason, $at);
            """;
        insert.Parameters.AddWithValue("$id", candidateId);
        insert.Parameters.AddWithValue("$decision", decision);
        insert.Parameters.AddWithValue("$reason", reason.Trim());
        insert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        insert.ExecuteNonQuery();
        transaction.Commit();
    }

    public bool IsAccepted(MarketAnomalyCandidate candidate)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM MarketAnomalyCandidates WHERE Fingerprint = $fingerprint;";
        command.Parameters.AddWithValue("$fingerprint", ComputeFingerprint(candidate));
        return string.Equals(command.ExecuteScalar() as string, "Dismissed", StringComparison.Ordinal);
    }

    public IReadOnlyList<MarketAnomalyDecision> GetDecisions(int candidateId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CandidateId, Decision, Reason, RecordedAtUtc
            FROM MarketAnomalyDecisions WHERE CandidateId = $id ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$id", candidateId);
        using var reader = command.ExecuteReader();
        var result = new List<MarketAnomalyDecision>();
        while (reader.Read())
            result.Add(new MarketAnomalyDecision
            {
                Id = reader.GetInt32(0),
                CandidateId = reader.GetInt32(1),
                Decision = reader.GetString(2),
                Reason = reader.GetString(3),
                RecordedAtUtc = DateTime.Parse(reader.GetString(4))
            });
        return result;
    }

    public static string ComputeFingerprint(MarketAnomalyCandidate candidate)
    {
        var source = $"{candidate.StockId}|{candidate.Category}|{candidate.RuleVersion}|" +
                     $"{candidate.PreviousValue}|{candidate.IncomingValue}";
        if (candidate.Category is "数量・分割候補" or "損益符号不一致")
            source += $"|{candidate.ObservedAt:yyyy-MM-dd}|{candidate.Evidence}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open();
        return connection;
    }
}
