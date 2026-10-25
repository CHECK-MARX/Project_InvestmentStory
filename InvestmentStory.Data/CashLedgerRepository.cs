using System.Globalization;
using System.Text.Json;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed partial class CashLedgerRepository(string databasePath)
{
    internal static void EnsureSchema(SqliteConnection connection, string databasePath)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CashOperations';";
        if (Convert.ToInt32(check.ExecuteScalar()) != 0)
        {
            EnsureCsvSchema(connection, databasePath);
            return;
        }
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stocks';";
        if (Convert.ToInt32(check.ExecuteScalar()) != 0)
        {
            DatabaseInitializer.BackupDatabase(connection, databasePath, "before_cash_ledger");
        }
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE CashOperations (
                Id TEXT PRIMARY KEY, Date TEXT NOT NULL, Kind TEXT NOT NULL, Evidence TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL, CancelledAtUtc TEXT NULL, CancellationReason TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE CashLegs (
                OperationId TEXT NOT NULL REFERENCES CashOperations(Id), LegIndex INTEGER NOT NULL,
                Account TEXT NOT NULL, Currency TEXT NOT NULL, Amount TEXT NOT NULL,
                PRIMARY KEY (OperationId, LegIndex)
            );
            CREATE TABLE CashReconciliations (PeriodKey TEXT PRIMARY KEY, Details TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        EnsureCsvSchema(connection, databasePath, backupFirst: false);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    public IReadOnlyList<CashOperation> GetOperations()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var operations = ReadOperations(connection, transaction);
        transaction.Commit();
        return operations;
    }

    public CashLedgerState GetState()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = new CashLedgerState(ReadOperations(connection, transaction), ReadReconciliations(connection, transaction));
        transaction.Commit();
        return state;
    }

    private static IReadOnlyList<CashOperation> ReadOperations(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT OperationId, Account, Currency, Amount FROM CashLegs ORDER BY OperationId, LegIndex;";
        var legs = new Dictionary<string, List<CashLeg>>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var id = reader.GetString(0);
                if (!legs.TryGetValue(id, out var values)) legs[id] = values = new();
                values.Add(new(reader.GetString(1), reader.GetString(2), decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
            }
        command.CommandText = "SELECT Id, Date, Kind, Evidence, CreatedAtUtc, CancelledAtUtc, CancellationReason FROM CashOperations ORDER BY Date, CreatedAtUtc, Id;";
        var operations = new List<CashOperation>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
            operations.Add(new()
            {
                Id = rows.GetString(0), Date = DateTime.ParseExact(rows.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Kind = Enum.Parse<CashOperationKind>(rows.GetString(2)), Evidence = rows.GetString(3),
                CreatedAtUtc = DateTime.Parse(rows.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CancelledAtUtc = rows.IsDBNull(5) ? null : DateTime.Parse(rows.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CancellationReason = rows.GetString(6), Legs = legs[rows.GetString(0)]
            });
        return operations;
    }

    public void Add(CashOperation operation)
    {
        CashLedgerService.Validate(operation);
        if (operation.CancelledAtUtc is not null) throw new ArgumentException("取消済みの取引は新規登録できません。");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        InsertOperation(connection, transaction, operation);
        transaction.Commit();
    }

    private static void InsertOperation(SqliteConnection connection, SqliteTransaction transaction, CashOperation operation)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO CashOperations (Id,Date,Kind,Evidence,CreatedAtUtc) VALUES ($id,$date,$kind,$evidence,$created);";
        command.Parameters.AddWithValue("$id", operation.Id);
        command.Parameters.AddWithValue("$date", operation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", operation.Kind.ToString());
        command.Parameters.AddWithValue("$evidence", operation.Evidence.Trim());
        command.Parameters.AddWithValue("$created", operation.CreatedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
        for (var i = 0; i < operation.Legs.Count; i++)
        {
            var leg = operation.Legs[i];
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO CashLegs (OperationId,LegIndex,Account,Currency,Amount) VALUES ($id,$index,$account,$currency,$amount);";
            command.Parameters.AddWithValue("$id", operation.Id);
            command.Parameters.AddWithValue("$index", i);
            command.Parameters.AddWithValue("$account", leg.Account);
            command.Parameters.AddWithValue("$currency", leg.Currency);
            // Preserve decimal precision instead of SQLite REAL's binary floating point.
            command.Parameters.AddWithValue("$amount", leg.Amount.ToString(CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    public void Cancel(string id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("取消理由を入力してください。");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CashOperations SET CancelledAtUtc=$at, CancellationReason=$reason WHERE Id=$id AND CancelledAtUtc IS NULL;";
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$reason", reason.Trim());
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("対象が見つからないか、既に取消済みです。再読込してください。");
    }

    public IReadOnlyList<CashReconciliation> GetReconciliations()
    {
        using var connection = Open();
        return ReadReconciliations(connection);
    }

    private static IReadOnlyList<CashReconciliation> ReadReconciliations(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Details FROM CashReconciliations ORDER BY PeriodKey;";
        using var reader = command.ExecuteReader();
        var rows = new List<CashReconciliation>();
        while (reader.Read()) rows.Add(JsonSerializer.Deserialize<CashReconciliation>(reader.GetString(0))!);
        return rows;
    }

    public void SaveReconciliation(CashReconciliation period)
    {
        CashLedgerService.ValidatePeriod(period);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (period.LedgerFingerprint != CashLedgerService.Fingerprint(ReadOperations(connection, transaction), period))
            throw new InvalidOperationException("台帳が変更されています。再読込してから照合してください。");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO CashReconciliations (PeriodKey,Details) VALUES ($key,$details) ON CONFLICT(PeriodKey) DO UPDATE SET Details=excluded.Details;";
        command.Parameters.AddWithValue("$key", JsonSerializer.Serialize(new[] { period.Account, period.Currency,
            period.StartDate.ToString("yyyy-MM-dd"), period.EndDate.ToString("yyyy-MM-dd") }));
        command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(period));
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
