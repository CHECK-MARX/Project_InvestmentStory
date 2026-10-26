namespace InvestmentStory.Core.Models;

public sealed record CashLedgerState(
    IReadOnlyList<CashOperation> Operations, IReadOnlyList<CashReconciliation> Reconciliations);

public sealed record CashAssetBalance(
    string Account, string Currency, DateTime? BalanceDate, decimal? StatementBalance,
    decimal? IncludedJpy, string Status, string Evidence);

public sealed class CashAssetSummary
{
    public DateTime AsOfDate { get; init; } = DateTime.Today;
    public IReadOnlyList<CashAssetBalance> Balances { get; init; } = Array.Empty<CashAssetBalance>();
    public string Error { get; init; } = string.Empty;
    public int IncludedCount => Balances.Count(x => x.IncludedJpy.HasValue);
    // No registered balance is unknown, not a confirmed zero. This is never a claim
    // that every cash account held by the user is registered in the application.
    public decimal? TotalRegisteredCashJpy => Error.Length == 0 && Balances.Count > 0 && IncludedCount == Balances.Count
        ? Balances.Sum(x => x.IncludedJpy!.Value) : null;
    public decimal? KnownSubtotalJpy => Error.Length == 0 && IncludedCount > 0
        ? Balances.Where(x => x.IncludedJpy.HasValue).Sum(x => x.IncludedJpy!.Value) : null;
}
