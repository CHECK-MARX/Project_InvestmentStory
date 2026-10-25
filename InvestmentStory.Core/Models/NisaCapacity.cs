namespace InvestmentStory.Core.Models;

public static class NisaTransactionKinds
{
    public const string Buy = "Buy";
    public const string Sell = "Sell";
}

public sealed record NisaTransaction(
    long Id, DateTime EffectiveDate, string Frame, string Kind,
    decimal BookCostJpy, string Source, string Evidence, string RuleVersion);

public sealed record NisaHistoryState(DateTime? CompleteThroughDate, DateTime? ConfirmedAtUtc, string Evidence);

public sealed record NisaCapacityAnalysis(
    int RuleYear, DateTime AsOf, string RuleVersion, string RuleSource,
    decimal KnownValuationJpy, decimal? TotalValuationJpy, int UnknownValuationCount,
    decimal? AnnualGrowthUsedJpy, decimal? AnnualAccumulationUsedJpy,
    decimal? AnnualGrowthRemainingJpy, decimal? AnnualAccumulationRemainingJpy,
    decimal? LifetimeUsedJpy, decimal? LifetimeRemainingJpy,
    decimal? GrowthLifetimeRemainingJpy,
    string Status);
