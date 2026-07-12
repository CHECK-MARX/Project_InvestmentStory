namespace InvestmentStory.Core.Models;

public sealed class PassiveIncomeSimulationInput
{
    public decimal CurrentAnnualPassiveIncome { get; set; }
    public decimal MonthlyAdditionalInvestment { get; set; }
    public decimal AssumedDividendYieldRate { get; set; }
    public decimal AnnualDividendGrowthRate { get; set; }
    public decimal TargetAnnualPassiveIncome { get; set; }
    public int StartYear { get; set; } = DateTime.Today.Year;
    public int StartMonth { get; set; } = DateTime.Today.Month;
}

public sealed class PassiveIncomeProjection
{
    public int Year { get; init; }
    public int YearsFromNow { get; init; }
    public decimal AnnualPassiveIncome { get; init; }
    public decimal YearOverYearIncrease { get; init; }
    public decimal TargetAchievementRate { get; init; }
}

public sealed class PassiveIncomeSimulationResult
{
    public IReadOnlyList<PassiveIncomeProjection> Projections { get; init; } = Array.Empty<PassiveIncomeProjection>();
    public IReadOnlyList<PassiveIncomeMonthlyProjection> MonthlyProjections { get; init; } = Array.Empty<PassiveIncomeMonthlyProjection>();
    public int? TargetAchievementYear { get; init; }
    public int? YearsToTarget { get; init; }
}

public sealed class PassiveIncomeMonthlyProjection
{
    public DateTime YearMonth { get; init; }
    public int MonthsFromNow { get; init; }
    public decimal AnnualPassiveIncome { get; init; }
    public decimal MonthlyPassiveIncome { get; init; }
    public decimal TargetAchievementRate { get; init; }
}

public sealed class TsumitateNisaSimulationInput
{
    public decimal CurrentMarketValueJpy { get; set; }
    public decimal CurrentCostJpy { get; set; }
    public decimal MonthlyContributionJpy { get; set; } = 100_000m;
    public decimal ExpectedAnnualReturnRate { get; set; } = 5m;
    public decimal TargetMarketValueJpy { get; set; } = 20_000_000m;
    public int StartYear { get; set; } = DateTime.Today.Year;
    public int StartMonth { get; set; } = DateTime.Today.Month;
}

public sealed class TsumitateNisaProjection
{
    public DateTime YearMonth { get; init; }
    public int MonthsFromNow { get; init; }
    public decimal MarketValueJpy { get; init; }
    public decimal CostJpy { get; init; }
    public decimal GainLossJpy { get; init; }
    public decimal GainLossRate { get; init; }
    public decimal CumulativeContributionJpy { get; init; }
    public decimal TargetAchievementRate { get; init; }
}

public sealed class TsumitateNisaSimulationResult
{
    public IReadOnlyList<TsumitateNisaProjection> Projections { get; init; } = Array.Empty<TsumitateNisaProjection>();
    public DateTime? TargetAchievementMonth { get; init; }
    public int? MonthsToTarget { get; init; }
}
