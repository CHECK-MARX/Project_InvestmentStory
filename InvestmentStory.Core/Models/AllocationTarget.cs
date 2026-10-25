namespace InvestmentStory.Core.Models;

public enum AllocationDimension
{
    Country,
    Sector,
    Security,
    Currency,
    Broker,
    AssetType
}

public sealed record AllocationTarget(string Key, string Label, decimal Percent);

public sealed record AllocationClassificationEvidence(int StockId, string Ticker, string CategoryKey, string Evidence);

public sealed record AllocationTargetPlan(
    AllocationDimension Dimension,
    int Revision,
    DateTime UpdatedAtUtc,
    IReadOnlyList<AllocationTarget> Targets,
    IReadOnlyList<AllocationClassificationEvidence>? ClassificationEvidence = null);

public sealed record AllocationHoldingCategory(
    int StockId,
    string Ticker,
    string Key,
    string Label,
    string ClassificationEvidence,
    decimal? MarketValueJpy);

public sealed record AllocationCategoryResult(
    string Key,
    string Label,
    string ClassificationEvidence,
    decimal? CurrentJpy,
    decimal? CurrentPercent,
    decimal TargetPercent,
    decimal? GapJpy,
    decimal? AdditionalFundingPurchaseJpy,
    decimal? BuySellJpy)
{
    public decimal? GapPercentPoints => CurrentPercent is decimal current ? TargetPercent - current : null;
}

public sealed record AllocationAnalysis(
    AllocationDimension Dimension,
    IReadOnlyList<AllocationHoldingCategory> Holdings,
    IReadOnlyList<AllocationCategoryResult> Categories,
    decimal KnownValueJpy,
    decimal? TotalValueJpy,
    decimal? MinimumAdditionalFundingJpy,
    decimal? AdditionalFundingJpy,
    bool CanReachTargetsWithAdditionalFunding,
    IReadOnlyList<string> Problems);
