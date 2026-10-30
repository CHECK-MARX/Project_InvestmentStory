namespace InvestmentStory.Core.Models;

public sealed record PositionChangeAudit(
    long Id, int StockId, DateTime RecordedAtUtc, string Reason,
    string? BeforeJson, string AfterJson, decimal? MarketValueChangeJpy);
