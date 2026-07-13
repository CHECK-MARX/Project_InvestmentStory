namespace InvestmentStory.Core.Models;

public static class DividendGrowthDisplayModes
{
    public const string AggregateBySecurity = "AggregateBySecurity";
    public const string Position = "Position";
}

public static class DividendGrowthPurchaseModes
{
    public const string OneTime = "OneTime";
    public const string ContinueMonthly = "ContinueMonthly";
    public const string None = "None";
}

public sealed class DividendGrowthSimulationInput
{
    public string PlanName { get; init; } = "Default";
    public string DisplayMode { get; init; } = DividendGrowthDisplayModes.AggregateBySecurity;
    public decimal TargetAnnualDividendJpy { get; init; } = 1_200_000m;
    public int ProjectionYears { get; init; } = 10;
    public int StartYear { get; init; } = DateTime.Today.Year;
    public IReadOnlyList<DividendGrowthPlanItem> PlanItems { get; init; } = Array.Empty<DividendGrowthPlanItem>();
}

public sealed class DividendGrowthPlanItem
{
    public string PlanKey { get; init; } = string.Empty;
    public string CanonicalKey { get; init; } = string.Empty;
    public string PositionKey { get; init; } = string.Empty;
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string AccountType { get; init; } = AccountTypes.Unknown;
    public string Country { get; init; } = string.Empty;
    public string Currency { get; init; } = "JPY";
    public decimal CurrentShares { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal ExchangeRate { get; init; } = 1m;
    public decimal AnnualDividendPerShare { get; init; }
    public string AnnualDividendSource { get; init; } = string.Empty;
    public string MarketDataSource { get; init; } = string.Empty;
    public DateTime? MarketDataAcquiredAt { get; init; }
    public string MarketDataStatus { get; init; } = string.Empty;
    public decimal PlannedAdditionalShares { get; init; }
    public string PlannedBroker { get; init; } = string.Empty;
    public string PlannedAccountType { get; init; } = AccountTypes.Unknown;
    public decimal AnnualDividendGrowthRate { get; init; }
    public string PurchaseMode { get; init; } = DividendGrowthPurchaseModes.OneTime;
    public bool IsNewStock { get; init; }
    public IReadOnlyList<DividendGrowthPlanItem> Components { get; init; } = Array.Empty<DividendGrowthPlanItem>();
}

public sealed class DividendGrowthSimulationHolding
{
    public string PlanKey { get; init; } = string.Empty;
    public string CanonicalKey { get; init; } = string.Empty;
    public string PositionKey { get; init; } = string.Empty;
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string AccountType { get; init; } = AccountTypes.Unknown;
    public string Country { get; init; } = string.Empty;
    public string Currency { get; init; } = "JPY";
    public decimal CurrentShares { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal ExchangeRate { get; init; } = 1m;
    public decimal AnnualDividendPerShare { get; init; }
    public string AnnualDividendSource { get; init; } = string.Empty;
    public decimal CurrentAnnualDividend { get; init; }
    public decimal CurrentAnnualDividendJpy { get; init; }
    public decimal CurrentNetAnnualDividendJpy { get; init; }
    public decimal PlannedAdditionalShares { get; init; }
    public decimal PlannedPurchaseAmount { get; init; }
    public decimal PlannedPurchaseAmountJpy { get; init; }
    public string PlannedBroker { get; init; } = string.Empty;
    public string PlannedAccountType { get; init; } = AccountTypes.Unknown;
    public decimal AnnualDividendGrowthRate { get; init; }
    public string PurchaseMode { get; init; } = DividendGrowthPurchaseModes.OneTime;
    public decimal PostAddShares { get; init; }
    public decimal PostAddAnnualDividend { get; init; }
    public decimal PostAddAnnualDividendJpy { get; init; }
    public decimal PostAddNetAnnualDividendJpy { get; init; }
    public decimal DividendIncreaseJpy { get; init; }
    public decimal NetDividendIncreaseJpy { get; init; }
    public decimal CurrentYieldRate { get; init; }
    public decimal InvestmentDividendYieldRate { get; init; }
    public decimal ForeignTaxJpy { get; init; }
    public decimal DomesticTaxJpy { get; init; }
    public decimal TotalTaxJpy { get; init; }
    public bool IsNewStock { get; init; }
    public string Warning { get; init; } = string.Empty;
}

public sealed class DividendGrowthSimulationSummary
{
    public decimal CurrentGrossAnnualDividendJpy { get; init; }
    public decimal CurrentNetAnnualDividendJpy { get; init; }
    public decimal ExistingPlannedInvestmentJpy { get; init; }
    public decimal NewPlannedInvestmentJpy { get; init; }
    public decimal TotalPlannedInvestmentJpy { get; init; }
    public decimal PostAddGrossAnnualDividendJpy { get; init; }
    public decimal PostAddNetAnnualDividendJpy { get; init; }
    public decimal AnnualDividendIncreaseJpy { get; init; }
    public decimal NetAnnualDividendIncreaseJpy { get; init; }
    public decimal ForeignTaxJpy { get; init; }
    public decimal DomesticTaxJpy { get; init; }
    public decimal TotalTaxJpy { get; init; }
    public decimal MonthlyNetDividendJpy { get; init; }
    public decimal InvestmentDividendYieldRate { get; init; }
    public decimal TargetAnnualDividendJpy { get; init; }
    public decimal TargetAchievementRate { get; init; }
    public decimal TargetGapJpy { get; init; }
    public int? TargetAchievementYear { get; init; }
}

public sealed class DividendGrowthProjection
{
    public int Year { get; init; }
    public int YearsFromNow { get; init; }
    public decimal CurrentOnlyGrossDividendJpy { get; init; }
    public decimal CurrentOnlyNetDividendJpy { get; init; }
    public decimal PlannedGrossDividendJpy { get; init; }
    public decimal PlannedNetDividendJpy { get; init; }
    public decimal MonthlyAverageNetDividendJpy { get; init; }
    public decimal TargetAnnualDividendJpy { get; init; }
    public decimal TargetAchievementRate { get; init; }
}

public sealed class DividendGrowthSimulationResult
{
    public required DividendGrowthSimulationSummary Summary { get; init; }
    public IReadOnlyList<DividendGrowthSimulationHolding> Holdings { get; init; } = Array.Empty<DividendGrowthSimulationHolding>();
    public IReadOnlyList<DividendGrowthSimulationHolding> NewStocks { get; init; } = Array.Empty<DividendGrowthSimulationHolding>();
    public IReadOnlyList<DividendGrowthProjection> Projections { get; init; } = Array.Empty<DividendGrowthProjection>();
}
