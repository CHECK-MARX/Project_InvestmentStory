namespace InvestmentStory.Core.Models;

public enum CashOperationKind
{
    Deposit, Withdrawal, BuySettlement, SellSettlement, Dividend, Tax, Fee, Transfer, Exchange
}

public sealed record CashLeg(string Account, string Currency, decimal Amount);

public sealed class CashOperation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; init; } = DateTime.Today;
    public CashOperationKind Kind { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public IReadOnlyList<CashLeg> Legs { get; init; } = Array.Empty<CashLeg>();
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CancelledAtUtc { get; init; }
    public string CancellationReason { get; init; } = string.Empty;
}

public sealed class CashReconciliation
{
    public string Account { get; init; } = string.Empty;
    public string Currency { get; init; } = "JPY";
    // Opening balance is immediately before StartDate; closing is at EndDate's end.
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public decimal? OpeningBalance { get; init; }
    public decimal? ClosingBalance { get; init; }
    public bool AllEntriesConfirmed { get; init; }
    public bool ExternalFlowsConfirmed { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string LedgerFingerprint { get; init; } = string.Empty;
    public DateTime ReviewedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed record CashReconciliationResult(
    decimal Movement, decimal ExternalNetFlow, decimal? ExpectedClosingBalance,
    decimal? Difference, bool IsComplete, string Status);
