using System.Text.Json;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed partial class CashLedgerRepository
{
    internal static void EnsurePerformanceSchema(SqliteConnection connection, string databasePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PerformanceReports';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stocks';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        command.CommandText = "CREATE TABLE PerformanceReports (Id TEXT PRIMARY KEY, CreatedAtUtc TEXT NOT NULL, Details TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PerformanceReport> GetPerformanceReports()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Details FROM PerformanceReports ORDER BY CreatedAtUtc DESC, Id DESC LIMIT 200;";
        using var reader = command.ExecuteReader(); var result = new List<PerformanceReport>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<PerformanceReport>(reader.GetString(0))!);
        return result;
    }

    public PerformanceReport SavePerformanceReport(PerformancePeriodInput input, string expectedLedgerFingerprint)
    {
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        var ledger = new CashLedgerState(ReadOperations(connection, transaction), ReadReconciliations(connection, transaction));
        var fingerprint = PerformancePeriodService.Fingerprint(ledger, input.EndDate);
        if (fingerprint != expectedLedgerFingerprint)
            throw new InvalidOperationException("計算後に現金台帳・残高照合が変更されています。再計算してください。");
        var result = new PerformancePeriodService().Calculate(input, ledger);
        if (result.Problems.Count > 0) throw new InvalidOperationException("不足条件が残っているため成績を保存できません。");
        var report = new PerformanceReport(Guid.NewGuid().ToString("N"), DateTime.UtcNow, fingerprint, input, result,
            PerformancePeriodService.RelevantLedger(ledger, input.EndDate));
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO PerformanceReports (Id,CreatedAtUtc,Details) VALUES ($id,$at,$details);";
        command.Parameters.AddWithValue("$id", report.Id);
        command.Parameters.AddWithValue("$at", report.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(report));
        command.ExecuteNonQuery(); transaction.Commit(); return report;
    }
}
