using System.Globalization;
using System.Text.Json;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed partial class CashLedgerRepository
{
    private static void EnsureCsvSchema(SqliteConnection connection, string databasePath, bool backupFirst = true)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CashCsvSources';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
        if (backupFirst) new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        command.CommandText = """
            CREATE TABLE CashCsvSources (
                SourceKey TEXT PRIMARY KEY, OperationId TEXT NOT NULL REFERENCES CashOperations(Id), Details TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CashCsvSource> GetCsvSources()
    {
        using var connection = Open();
        return ReadCsvSources(connection);
    }

    private static IReadOnlyList<CashCsvSource> ReadCsvSources(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Details FROM CashCsvSources ORDER BY SourceKey;";
        using var reader = command.ExecuteReader();
        var sources = new List<CashCsvSource>();
        while (reader.Read()) sources.Add(JsonSerializer.Deserialize<CashCsvSource>(reader.GetString(0))!);
        return sources;
    }

    public IReadOnlyList<CashCsvReview> PreviewCsv(CashCsvPlan plan)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var reviews = Review(plan, ReadOperations(connection, transaction), ReadCsvSources(connection, transaction));
        transaction.Commit();
        return reviews;
    }

    private static bool SameMovement(CashOperation left, CashOperation right) =>
        left.Date == right.Date && left.Kind == right.Kind && left.Legs.SequenceEqual(right.Legs);

    private static string MovementKey(CashOperation operation) =>
        JsonSerializer.Serialize(new { operation.Date, operation.Kind,
            Legs = operation.Legs.Select(x => new { x.Account, x.Currency, Amount = x.Amount.ToString("G29", CultureInfo.InvariantCulture) }) });

    private static IReadOnlyList<CashCsvReview> Review(CashCsvPlan plan,
        IReadOnlyList<CashOperation> operations, IReadOnlyList<CashCsvSource> sources)
    {
        var known = sources.Select(x => x.SourceKey).ToHashSet(StringComparer.Ordinal);
        var semantic = sources.ToLookup(x => x.SemanticKey, x => x.OperationId, StringComparer.Ordinal);
        var byId = operations.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var byMovement = operations.ToLookup(MovementKey, StringComparer.Ordinal);
        var counts = plan.Rows.Where(x => x.Operation is not null).GroupBy(x => MovementKey(x.Operation!))
            .ToDictionary(x => x.Key, x => x.Count());
        return plan.Rows.Select(row => new CashCsvReview(row, known.Contains(row.SourceKey),
            row.Operation is null ? [] : byMovement[MovementKey(row.Operation)]
                .Concat(semantic[row.SemanticKey].Where(byId.ContainsKey).Select(id => byId[id])).DistinctBy(x => x.Id).ToArray(),
            row.Operation is not null && counts[MovementKey(row.Operation)] > 1)).ToArray();
    }

    public CashCsvImportResult ImportCsv(CashCsvPlan plan, IReadOnlyList<CashCsvDecision> decisions, bool externalFlowsConfirmed = false)
    {
        // Re-read the actual file; preview objects are never the source of persisted monetary values.
        var current = new CashCsvParser().Parse(plan.FilePath, plan.Account, plan.EmptyCurrency, plan.MappingReason);
        if (current.FileHash != plan.FileHash) throw new InvalidOperationException("CSVが変更されています。プレビューをやり直してください。");
        var selected = decisions.Where(x => x.Resolution != CashCsvResolutions.Hold).ToArray();
        var validKeys = current.Rows.Select(x => x.SourceKey).ToHashSet(StringComparer.Ordinal);
        if (selected.Select(x => x.SourceKey).Distinct().Count() != selected.Length ||
            selected.Any(x => !validKeys.Contains(x.SourceKey)))
            throw new ArgumentException("取込対象行が不正です。プレビューをやり直してください。");
        if (selected.Length == 0) return new(0, 0, current.Rows.Count);
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        var operations = ReadOperations(connection, transaction).ToList();
        var sources = ReadCsvSources(connection, transaction).ToList();
        var reviews = Review(current, operations, sources).ToDictionary(x => x.Candidate.SourceKey);
        var movementKeys = operations.Select(MovementKey).ToHashSet(StringComparer.Ordinal);
        var added = 0; var linked = 0;
        foreach (var decision in selected)
        {
            // Validate against the locked, current ledger, including earlier rows in this batch.
            var review = reviews[decision.SourceKey];
            if (review.AlreadyImported) continue; // Also applies to cancelled entries; never resurrect them.
            var row = review.Candidate;
            var operation = row.Operation ?? throw new InvalidOperationException($"明細{row.RecordNumber}: {row.Problem}");
            CashLedgerService.Validate(operation);
            var external = operation.Kind is CashOperationKind.Deposit or CashOperationKind.Withdrawal;
            if (external && !externalFlowsConfirmed)
                throw new InvalidOperationException("振込明細が管理対象口座の外部との入出金であることを確認してください。口座間振替は手動で両側を登録します。");
            string operationId;
            if (decision.Resolution == CashCsvResolutions.Link)
            {
                var existing = review.Matches.SingleOrDefault(x => x.Id == decision.ExistingOperationId);
                if (existing is null || existing.CancelledAtUtc is not null || !SameMovement(operation, existing))
                    throw new InvalidOperationException($"明細{row.RecordNumber}: 照合先の口座・通貨・日付・種類・金額を確認してください。取消済み取引には照合できません。");
                if (string.IsNullOrWhiteSpace(decision.Reason)) throw new ArgumentException("同一取引と判断した根拠を入力してください。");
                operationId = existing.Id;
                linked++;
            }
            else
            {
                if (decision.Resolution != CashCsvResolutions.Add && decision.Resolution != CashCsvResolutions.Distinct)
                    throw new ArgumentException("取込方法が不正です。");
                if ((review.NeedsReview || movementKeys.Contains(MovementKey(operation))) && decision.Resolution != CashCsvResolutions.Distinct)
                    throw new InvalidOperationException($"明細{row.RecordNumber}: 重複候補があります。再プレビューして、照合または別取引の根拠を指定してください。");
                if (decision.Resolution == CashCsvResolutions.Distinct && string.IsNullOrWhiteSpace(decision.Reason))
                    throw new ArgumentException("別取引と判断した根拠を入力してください。");
                InsertOperation(connection, transaction, operation);
                movementKeys.Add(MovementKey(operation));
                operationId = operation.Id;
                added++;
            }
            var source = new CashCsvSource(row.SourceKey, row.SemanticKey, operationId, current.FileHash,
                Path.GetFileName(current.FilePath), row.RecordNumber, row.Broker, row.Format, row.RawRecord,
                decision.Resolution, decision.Reason.Trim() + (external ? " / 管理対象口座外との入出金を確認済み" : ""), DateTime.UtcNow);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO CashCsvSources (SourceKey,OperationId,Details) VALUES ($key,$id,$details);";
            command.Parameters.AddWithValue("$key", source.SourceKey);
            command.Parameters.AddWithValue("$id", operationId);
            command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(source));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return new(added, linked, current.Rows.Count - added - linked);
    }
}
