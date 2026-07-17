namespace InvestmentStory.Core.Models;

public static class DividendPurchasePlanDisplayUnits
{
    public const string AllAccounts = "AllAccounts";
    public const string Broker = "Broker";
    public const string Account = "Account";
}

public static class DividendPlanDataQuality
{
    public const string Acquired = "Acquired";
    public const string Estimated = "Estimated";
    public const string Missing = "Missing";
}

public static class DividendPlanEligibility
{
    public const string Eligible = "Eligible";
    public const string Ineligible = "Ineligible";
    public const string Estimated = "Estimated";
    public const string Missing = "Missing";
}

public sealed class DividendPurchasePlanInput
{
    public string PlanName { get; init; } = string.Empty;
    public int TargetYear { get; init; } = DateTime.Today.Year;
    public DateTime PlannedPurchaseDate { get; init; } = DateTime.Today;
    public string DisplayUnit { get; init; } = DividendPurchasePlanDisplayUnits.AllAccounts;
    public decimal TargetAnnualNetDividendJpy { get; init; } = 1_200_000m;
    public IReadOnlyList<DividendGrowthPlanItem> PlanItems { get; init; } = Array.Empty<DividendGrowthPlanItem>();
    public IReadOnlyList<DividendPayment> DividendPayments { get; init; } = Array.Empty<DividendPayment>();
}

public sealed class DividendPurchasePlanEvent
{
    public int StockId { get; init; }
    public string PlanKey { get; init; } = string.Empty;
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string AccountType { get; init; } = AccountTypes.Unknown;
    public int Month { get; init; }
    public DateTime PaymentDate { get; init; }
    public DateTime? LastRightsDate { get; init; }
    public DateTime? ExDividendDate { get; init; }
    public decimal CurrentNetDividendJpy { get; init; }
    public decimal AdditionalNetDividendJpy { get; init; }
    public decimal MissedNetDividendJpy { get; init; }
    public bool IsNewStock { get; init; }
    public bool IsEligible { get; init; }
    public string EligibilityStatus { get; init; } = DividendPlanEligibility.Missing;
    public string DataQuality { get; init; } = DividendPlanDataQuality.Missing;
    public string Source { get; init; } = string.Empty;
    public DividendScheduleStatus ScheduleStatus =>
        DividendScheduleStatusResolver.FromPlan(EligibilityStatus, DataQuality);
}

public sealed class DividendPurchasePlanHolding
{
    public string PlanKey { get; init; } = string.Empty;
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string AccountType { get; init; } = AccountTypes.Unknown;
    public string Currency { get; init; } = "JPY";
    public decimal CurrentShares { get; init; }
    public decimal PlannedAdditionalShares { get; init; }
    public decimal PostAddShares { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal PlannedPurchaseAmountJpy { get; init; }
    public decimal AnnualDividendPerShare { get; init; }
    public decimal CurrentAnnualNetDividendJpy { get; init; }
    public decimal TargetYearCurrentNetDividendJpy { get; init; }
    public decimal TargetYearAdditionalNetDividendJpy { get; init; }
    public decimal TargetYearPlannedNetDividendJpy => TargetYearCurrentNetDividendJpy + TargetYearAdditionalNetDividendJpy;
    public decimal NextYearAdditionalNetDividendJpy { get; init; }
    public decimal PostAddNextYearNetDividendJpy { get; init; }
    public decimal MissedNetDividendJpy { get; init; }
    public decimal CurrentYieldRate { get; init; }
    public decimal YieldOnCostRate { get; init; }
    public decimal AdditionalInvestmentYieldRate { get; init; }
    public decimal DividendCompositionRate { get; init; }
    public decimal DividendPaybackYears { get; init; }
    public decimal TargetContributionJpy { get; init; }
    public string DividendMonths { get; init; } = string.Empty;
    public DateTime? NextLastRightsDate { get; init; }
    public DateTime? NextPaymentDate { get; init; }
    public string EligibilityStatus { get; init; } = DividendPlanEligibility.Missing;
    public string DataQuality { get; init; } = DividendPlanDataQuality.Missing;
    public string DataSource { get; init; } = string.Empty;
    public bool IsNewStock { get; init; }
}

public sealed class DividendPurchasePlanMonthlyResult
{
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal CurrentNetDividendJpy { get; init; }
    public decimal ExistingAdditionalNetDividendJpy { get; init; }
    public decimal NewPurchaseNetDividendJpy { get; init; }
    public decimal MissedNetDividendJpy { get; init; }
    public decimal TargetNetDividendJpy { get; init; }
    public decimal CurrentCumulativeNetDividendJpy { get; init; }
    public decimal CumulativeNetDividendJpy { get; init; }
    public IReadOnlyList<DividendPurchasePlanEvent> Events { get; init; } = Array.Empty<DividendPurchasePlanEvent>();
    public decimal PlannedNetDividendJpy => CurrentNetDividendJpy + ExistingAdditionalNetDividendJpy + NewPurchaseNetDividendJpy;
    public decimal AdditionalNetDividendJpy => ExistingAdditionalNetDividendJpy + NewPurchaseNetDividendJpy;
    public decimal TargetDifferenceJpy => PlannedNetDividendJpy - TargetNetDividendJpy;
    public decimal TargetAchievementRate => TargetNetDividendJpy <= 0m ? 0m : PlannedNetDividendJpy / TargetNetDividendJpy * 100m;
}

public sealed class DividendPurchasePlanComposition
{
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal AnnualNetDividendJpy { get; init; }
    public decimal CompositionRate { get; init; }
    public decimal CurrentAnnualNetDividendJpy { get; init; }
    public decimal CurrentCompositionRate { get; init; }
    public decimal CompositionRateChange => CompositionRate - CurrentCompositionRate;
}

public sealed class DividendPurchasePlanSummary
{
    public decimal CurrentTargetYearNetDividendJpy { get; init; }
    public decimal PlannedTargetYearNetDividendJpy { get; init; }
    public decimal TargetYearDividendIncreaseJpy { get; init; }
    public decimal MissedTargetYearNetDividendJpy { get; init; }
    public decimal NextYearAnnualNetDividendJpy { get; init; }
    public decimal TargetAchievementRate { get; init; }
    public decimal PlannedInvestmentJpy { get; init; }
    public decimal AdditionalInvestmentYieldRate { get; init; }
    public decimal AdditionalInvestmentPaybackYears { get; init; }
    public decimal CurrentYieldRate { get; init; }
    public decimal YieldOnCostRate { get; init; }
    public decimal PostAddPortfolioYieldRate { get; init; }
    public decimal CurrentMarketValueJpy { get; init; }
    public decimal CurrentCostJpy { get; init; }
    public decimal ForeignTaxJpy { get; init; }
    public decimal DomesticTaxJpy { get; init; }
    public decimal TotalTaxJpy { get; init; }
}

public sealed class DividendPurchasePlanResult
{
    public required DividendPurchasePlanSummary Summary { get; init; }
    public IReadOnlyList<DividendPurchasePlanHolding> Holdings { get; init; } = Array.Empty<DividendPurchasePlanHolding>();
    public IReadOnlyList<DividendPurchasePlanMonthlyResult> Months { get; init; } = Array.Empty<DividendPurchasePlanMonthlyResult>();
    public IReadOnlyList<DividendPurchasePlanComposition> Composition { get; init; } = Array.Empty<DividendPurchasePlanComposition>();
}
