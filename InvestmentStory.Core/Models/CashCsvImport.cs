namespace InvestmentStory.Core.Models;

public sealed record CashCsvCandidate(string SourceKey, string SemanticKey, int RecordNumber,
    string Broker, string Format, string RawRecord, CashOperation? Operation, string Problem);
public sealed record CashCsvPlan(string FilePath, string FileHash, IReadOnlyList<CashCsvCandidate> Rows,
    string Account, string EmptyCurrency, string MappingReason);
public sealed record CashCsvSource(string SourceKey, string SemanticKey, string OperationId,
    string FileHash, string FileName, int RecordNumber, string Broker, string Format,
    string RawRecord, string Resolution, string Reason, DateTime ImportedAtUtc);
public sealed record CashCsvReview(CashCsvCandidate Candidate, bool AlreadyImported,
    IReadOnlyList<CashOperation> Matches, bool SameAmountInFile)
{
    public bool NeedsReview => Matches.Count > 0 || SameAmountInFile;
}
public sealed record CashCsvDecision(string SourceKey, string Resolution, string Reason, string? ExistingOperationId = null);
public sealed record CashCsvImportResult(int AddedCount, int LinkedCount, int SkippedCount);

public static class CashCsvResolutions
{
    public const string Hold = "保留";
    public const string Add = "新規追加";
    public const string Link = "既存と照合";
    public const string Distinct = "別取引として追加";
}
