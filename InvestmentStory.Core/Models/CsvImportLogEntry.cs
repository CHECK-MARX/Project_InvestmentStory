namespace InvestmentStory.Core.Models;

public sealed class CsvImportLogEntry
{
    public long Id { get; init; }
    public DateTime ImportedAtUtc { get; init; }
    public string ImportedAtText => ImportedAtUtc == default ? string.Empty :
        DateTime.SpecifyKind(ImportedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    public string ResultLabel => Result switch
    {
        "Succeeded" or "SucceededBatch" => "取込済み",
        "Partial" or "PartialBatch" => "一部反映",
        "Skipped" => "対象なし",
        "Canceled" => "中止",
        "Failed" => "失敗",
        _ => Result
    };
    public string BatchId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string FileHash { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string CsvType { get; init; } = string.Empty;
    public string ParserVersion { get; init; } = "2026-10-25";
    public string FormatVersion { get; init; } = string.Empty;
    public string FormatVersionLabel => string.IsNullOrWhiteSpace(FormatVersion) ? "未記録" : FormatVersion;
    public int RowCount { get; init; }
    public int ParsedRowCount { get; init; }
    public int PendingRowCount { get; init; }
    public int ExcludedRowCount { get; init; }
    public int InsertedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int SkippedCount { get; init; }
    public int ConflictCount { get; init; }
    public string Result { get; init; } = string.Empty;
    public string ErrorSummary { get; init; } = string.Empty;
}
