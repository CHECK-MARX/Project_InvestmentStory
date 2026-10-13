namespace InvestmentStory.Core.Models;

public sealed class CsvImportLogEntry
{
    public string BatchId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string FileHash { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string CsvType { get; init; } = string.Empty;
    public string ParserVersion { get; init; } = "2026-10-13";
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
