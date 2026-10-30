namespace InvestmentStory.Core.Models;

public sealed class MarketAnomalyCandidate
{
    public int Id { get; set; }
    public int StockId { get; init; }
    public string Fingerprint { get; set; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RuleVersion { get; init; } = "1";
    public string PreviousValue { get; init; } = string.Empty;
    public string IncomingValue { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string SuggestedCheck { get; init; } = string.Empty;
    public DateTime ObservedAt { get; init; }
    public string Status { get; set; } = "Pending";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MarketAnomalyDecision
{
    public int Id { get; set; }
    public int CandidateId { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;
}
