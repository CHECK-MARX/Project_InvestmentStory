namespace InvestmentStory.Core.Models;

public sealed class PriceHistoryPoint
{
    public DateTime Date { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}

public sealed class PriceHistoryResult
{
    public bool IsSuccess { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public DateTime FetchedAt { get; init; } = DateTime.Now;
    public IReadOnlyList<PriceHistoryPoint> Points { get; init; } = Array.Empty<PriceHistoryPoint>();
    public IReadOnlyList<ApiFetchLogEntry> Logs { get; init; } = Array.Empty<ApiFetchLogEntry>();

    public static PriceHistoryResult Success(
        string symbol,
        string source,
        IReadOnlyList<PriceHistoryPoint> points,
        params ApiFetchLogEntry[] logs) =>
        new()
        {
            IsSuccess = true,
            Symbol = symbol,
            Source = source,
            Points = points,
            Logs = logs
        };

    public static PriceHistoryResult Failure(
        string symbol,
        string source,
        string errorMessage,
        params ApiFetchLogEntry[] logs) =>
        new()
        {
            IsSuccess = false,
            Symbol = symbol,
            Source = source,
            ErrorMessage = errorMessage,
            Logs = logs
        };
}
