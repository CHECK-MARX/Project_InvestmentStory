namespace InvestmentStory.Core.Models;

public sealed record CsvReviewSummary(int UnresolvedTrades, int HoldingHistory, int DividendHistory);

public sealed record CsvReviewRecord(
    string SourceKey, string Category, string EventDate, string Security,
    string Broker, string Account, string SourceFile, int SourceRowNumber,
    string Reason, string Status, string ReviewNote = "");

public enum TodayReviewKind { Csv, Cost, Dividend, Update }

public sealed record TodayReviewItem(TodayReviewKind Kind, string SourceKey, string Label, string Context);

public sealed record TodayReviewCard(
    TodayReviewKind Kind,
    string Title,
    int Count,
    string Detail,
    string Impact,
    string NextAction)
{
    public IReadOnlyList<TodayReviewItem> Items { get; init; } = [];
    public bool HasItems => Items.Count > 0;
}

public sealed record TodayReviewOverview(
    DateTime AsOfDate,
    IReadOnlyList<TodayReviewCard> Cards,
    string ScopeNote);
