namespace InvestmentStory.Core.Models;

// A confirmed event is an accounting instruction. Imported, unverified rows remain pending.
public sealed record CorporateAction
{
    public long Id { get; init; }
    public long? SupersedesId { get; init; }
    public int StockId { get; init; }
    public DateTime EventDate { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string SourceBroker { get; init; } = string.Empty;
    public string SourceAccount { get; init; } = AccountTypes.Unknown;
    public string DestinationBroker { get; init; } = string.Empty;
    public string DestinationAccount { get; init; } = AccountTypes.Unknown;
    public decimal Quantity { get; init; }
    public decimal SplitRatio { get; init; } = 1m;
    public decimal? CarriedCostJpy { get; init; }
    public string OldTicker { get; init; } = string.Empty;
    public string NewTicker { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int SourceRowNumber { get; init; }
    public string Status { get; init; } = "Pending";
    public string Reason { get; init; } = string.Empty;
    public DateTime RecordedAtUtc { get; init; }
}

public sealed record AccountBalance(string Broker, string Account, decimal Quantity, decimal? CostJpy);

public sealed record CorporateActionBalance(
    DateTime AsOf, bool IsComplete, string Reason, IReadOnlyList<AccountBalance> Accounts)
{
    public decimal? TotalQuantity => IsComplete ? Accounts.Sum(x => x.Quantity) : null;
    public decimal? TotalCostJpy => IsComplete && Accounts.All(x => x.CostJpy.HasValue)
        ? Accounts.Sum(x => x.CostJpy!.Value) : null;
}
