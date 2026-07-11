namespace InvestmentStory.Core.Models;

public sealed class MonthlyDividendBreakdown
{
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal ActualJpy { get; init; }
    public decimal PlannedJpy { get; init; }
    public decimal PreviousYearActualJpy { get; init; }
    public decimal MonthlyGoalJpy { get; init; }
    public decimal ForecastJpy => ActualJpy + PlannedJpy;
    public string Label => $"{Month}月";
}

public sealed class DividendRankingItem
{
    public int Rank { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal AmountJpy { get; init; }
    public decimal Rate { get; init; }
    public decimal ShareOfTotal { get; init; }
    public decimal PreviousYearDifferenceJpy { get; init; }
}

public sealed class PortfolioReturnSummary
{
    public decimal UnrealizedGainLossJpy { get; init; }
    public decimal RealizedGainLossJpy { get; init; }
    public decimal CumulativeDividendJpy { get; init; }
    public decimal TotalReturnJpy { get; init; }
    public decimal TotalReturnRate { get; init; }
    public decimal CapitalRecoveryRate { get; init; }
    public decimal Top1ConcentrationRate { get; init; }
    public decimal Top3ConcentrationRate { get; init; }
    public decimal Top5ConcentrationRate { get; init; }
    public decimal Top10ConcentrationRate { get; init; }
    public decimal Hhi { get; init; }
}

public sealed class SnapshotComparison
{
    public decimal? TotalAssetDayChangeJpy { get; init; }
    public decimal? TotalAssetMonthChangeJpy { get; init; }
    public decimal? UnrealizedDayChangeJpy { get; init; }
    public decimal? UnrealizedMonthChangeJpy { get; init; }
}

public sealed class FxSensitivityPoint
{
    public decimal RateDelta { get; init; }
    public decimal UsdJpyRate { get; init; }
    public decimal TotalMarketValueJpy { get; init; }
    public decimal ChangeFromCurrentJpy { get; init; }
}
