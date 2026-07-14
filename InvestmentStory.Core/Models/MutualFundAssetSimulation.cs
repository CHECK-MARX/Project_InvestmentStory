namespace InvestmentStory.Core.Models;

public static class MutualFundSimulationScopeKeys
{
    public const string AllAccounts = "All";
    public const string AccountPrefix = "Account:";
    public const string FundPrefix = "Fund:";

    public static string Account(string accountType) => AccountPrefix + accountType;

    public static string Fund(string fundKey) => FundPrefix + fundKey;
}

public sealed class MutualFundSimulationScopeOption
{
    public string Key { get; init; } = MutualFundSimulationScopeKeys.AllAccounts;
    public string LegacyKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = "すべての投資信託";
    public int FundCount { get; init; }
    public int PositionCount { get; init; }
}

public sealed class MutualFundSimulationInput
{
    public decimal MonthlyContributionJpy { get; init; }
    public decimal ExpectedAnnualReturnRate { get; init; }
    public decimal TargetAmountJpy { get; init; }
    public int ProjectionYears { get; init; } = 20;
    public int TargetYears { get; init; } = 20;
    public int StartYear { get; init; } = DateTime.Today.Year;
    public int StartMonth { get; init; } = DateTime.Today.Month;
}

public sealed class MutualFundScenarioInput
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal? AnnualReturnRate { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string Basis { get; init; } = string.Empty;
    public string UnavailableReason { get; init; } = string.Empty;
}

public sealed class MutualFundPortfolioSummary
{
    public decimal CurrentMarketValueJpy { get; init; }
    public decimal CurrentCostJpy { get; init; }
    public decimal UnrealizedGainJpy { get; init; }
    public decimal UnrealizedGainRate { get; init; }
    public decimal? ActualAnnualizedReturnRate { get; init; }
    public MutualFundActualAnnualizedReturnEstimate? ActualAnnualizedReturnEstimate { get; init; }
    public int FundCount { get; init; }
    public int PositionCount { get; init; }
    public bool AllowsMonthlyContribution { get; init; }
    public decimal EffectiveMonthlyContributionJpy { get; init; }
}

public sealed class MutualFundActualAnnualizedReturnEstimate
{
    public decimal AnnualizedReturnRate { get; init; }
    public string DisplayName { get; init; } = "実績参考年利";
    public string Method { get; init; } = string.Empty;
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
    public string Precision { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public string UnavailableReason { get; init; } = string.Empty;

    public string PeriodDisplay => PeriodStart is null || PeriodEnd is null
        ? string.Empty
        : $"{PeriodStart.Value:yyyy/MM/dd}～{PeriodEnd.Value:yyyy/MM/dd}";
}

public sealed class MutualFundSimulationAccountBreakdown
{
    public string AccountType { get; init; } = AccountTypes.Unknown;
    public string AccountDisplayName { get; init; } = "未分類";
    public decimal CurrentMarketValueJpy { get; init; }
    public decimal CurrentCostJpy { get; init; }
    public decimal UnrealizedGainJpy { get; init; }
    public int FundCount { get; init; }
    public int PositionCount { get; init; }
    public bool AllowsContribution { get; init; }
    public decimal MonthlyContributionJpy { get; init; }
}

public sealed class MutualFundMonthlyProjection
{
    public DateTime YearMonth { get; init; }
    public int MonthsFromNow { get; init; }
    public decimal MarketValueJpy { get; init; }
    public decimal NoContributionMarketValueJpy { get; init; }
    public decimal CostJpy { get; init; }
    public decimal CumulativeContributionJpy { get; init; }
    public decimal UnrealizedGainJpy { get; init; }
    public decimal TargetAchievementRate { get; init; }
}

public sealed class MutualFundContributionComparison
{
    public string Label { get; init; } = string.Empty;
    public decimal MonthlyContributionJpy { get; init; }
    public decimal FinalMarketValueJpy { get; init; }
}

public sealed class MutualFundAssetSimulationResult
{
    public required MutualFundPortfolioSummary Summary { get; init; }
    public IReadOnlyList<MutualFundSimulationAccountBreakdown> AccountBreakdowns { get; init; } = Array.Empty<MutualFundSimulationAccountBreakdown>();
    public IReadOnlyList<MutualFundMonthlyProjection> Projections { get; init; } = Array.Empty<MutualFundMonthlyProjection>();
    public IReadOnlyList<MutualFundContributionComparison> ContributionComparisons { get; init; } = Array.Empty<MutualFundContributionComparison>();
    public DateTime? TargetAchievementMonth { get; init; }
    public int? MonthsToTarget { get; init; }
    public decimal RequiredMonthlyContributionJpy { get; init; }
    public decimal AdditionalMonthlyContributionNeededJpy { get; init; }
    public decimal MonthlyContributionMarginJpy { get; init; }
    public bool IsRequiredMonthlyContributionApplicable { get; init; } = true;
}

public sealed class MutualFundScenarioComparisonResult
{
    public required MutualFundPortfolioSummary Summary { get; init; }
    public IReadOnlyList<MutualFundSimulationAccountBreakdown> AccountBreakdowns { get; init; } = Array.Empty<MutualFundSimulationAccountBreakdown>();
    public IReadOnlyList<MutualFundScenarioResult> Scenarios { get; init; } = Array.Empty<MutualFundScenarioResult>();
    public IReadOnlyList<MutualFundScenarioMonthlyComparison> MonthlyComparisons { get; init; } = Array.Empty<MutualFundScenarioMonthlyComparison>();
}

public sealed class MutualFundScenarioResult
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal? AnnualReturnRate { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsAvailable { get; init; }
    public string Basis { get; init; } = string.Empty;
    public string UnavailableReason { get; init; } = string.Empty;
    public decimal FinalMarketValueJpy { get; init; }
    public decimal FiveYearMarketValueJpy { get; init; }
    public decimal TenYearMarketValueJpy { get; init; }
    public DateTime? TargetAchievementMonth { get; init; }
    public int? MonthsToTarget { get; init; }
    public bool ReachesTargetWithinProjection { get; init; }
    public decimal CumulativeContributionAtTargetJpy { get; init; }
    public decimal InvestmentGainAtTargetJpy { get; init; }
    public decimal NoContributionFinalMarketValueJpy { get; init; }
    public IReadOnlyList<MutualFundScenarioMonthlyProjection> Projections { get; init; } = Array.Empty<MutualFundScenarioMonthlyProjection>();
}

public sealed class MutualFundScenarioMonthlyProjection
{
    public DateTime YearMonth { get; init; }
    public int MonthsFromNow { get; init; }
    public decimal MarketValueJpy { get; init; }
    public decimal NoContributionMarketValueJpy { get; init; }
    public decimal CumulativeContributionJpy { get; init; }
    public decimal TotalCostJpy { get; init; }
    public decimal UnrealizedGainJpy { get; init; }
    public decimal TargetAchievementRate { get; init; }
}

public sealed class MutualFundScenarioMonthlyComparison
{
    public DateTime YearMonth { get; init; }
    public int MonthsFromNow { get; init; }
    public decimal CumulativeContributionJpy { get; init; }
    public IReadOnlyDictionary<string, MutualFundScenarioMonthlyProjection> ScenarioValues { get; init; } =
        new Dictionary<string, MutualFundScenarioMonthlyProjection>(StringComparer.OrdinalIgnoreCase);
}
