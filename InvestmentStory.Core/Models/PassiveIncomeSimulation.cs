namespace InvestmentStory.Core.Models;

public sealed class PassiveIncomeSimulationInput
{
    public decimal CurrentAnnualPassiveIncome { get; set; }
    public decimal MonthlyAdditionalInvestment { get; set; }
    public decimal AssumedDividendYieldRate { get; set; }
    public decimal AnnualDividendGrowthRate { get; set; }
    public decimal TargetAnnualPassiveIncome { get; set; }
    public int StartYear { get; set; } = DateTime.Today.Year;
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
    public int? TargetAchievementYear { get; init; }
    public int? YearsToTarget { get; init; }
}
