namespace InvestmentStory.Core.Models;

public sealed class DashboardSummary
{
    public decimal TotalCurrentMarketValueJpy { get; init; }
    public decimal TotalPurchaseAmountJpy { get; init; }
    public decimal TotalUnrealizedGainJpy { get; init; }
    public decimal TotalUnrealizedGainRate { get; init; }
    public decimal ForeignAssetTotalUsd { get; init; }
    public decimal ForeignAssetTotalJpy { get; init; }
    public decimal FxIncludedUnrealizedGainJpy { get; init; }
    public decimal CurrentUsdJpyRate { get; init; }
    public DateTime ExchangeRateAcquiredAt { get; init; }
    public string ExchangeRateSource { get; init; } = string.Empty;
    public string ExchangeRateInputType { get; init; } = string.Empty;
    public decimal ThisMonthPassiveIncomeJpy { get; init; }
    public decimal ThisYearPassiveIncomeJpy { get; init; }
    public decimal ThisMonthPlannedIncomeJpy { get; init; }
    public decimal ThisYearPlannedIncomeJpy { get; init; }
    public decimal ThisYearForecastIncludingPlannedJpy { get; init; }
    public decimal AnnualPassiveIncomeForecastJpy { get; init; }
    public decimal AnnualGrossDividendForecastJpy { get; init; }
    public decimal AnnualNetDividendForecastJpy { get; init; }
    public decimal MonthlyAveragePassiveIncomeForecastJpy { get; init; }
    public decimal ForeignTaxActualJpy { get; init; }
    public decimal DomesticTaxActualJpy { get; init; }
    public decimal TotalTaxActualJpy { get; init; }
    public decimal NisaDividendActualJpy { get; init; }
    public decimal TaxableDividendActualJpy { get; init; }
    public decimal DomesticStockDividendActualJpy { get; init; }
    public decimal ForeignStockDividendActualJpy { get; init; }
    public decimal AnnualGoalAchievementRate { get; init; }
    public decimal MonthlyGoalAchievementRate { get; init; }
    public decimal AnnualGoalGapJpy { get; init; }
    public decimal MonthlyGoalGapJpy { get; init; }
}
