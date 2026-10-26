namespace InvestmentStory.Core.Models;

public sealed record DividendReviewDecision(
    int Id, int PlannedDividendId, string Action, int? ActualDividendId,
    string PreviousStatus, string NewStatus, string Reason, DateTime AtUtc);
