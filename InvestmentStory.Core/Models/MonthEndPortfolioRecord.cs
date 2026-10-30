namespace InvestmentStory.Core.Models;

public sealed class MonthEndPortfolioRecord
{
    public DateTime SnapshotDate { get; set; }
    public string FormulaVersion { get; set; } = "MonthEndPortfolio/1";
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsFrozen { get; set; }
    public List<MonthEndPositionEvidence> Positions { get; set; } = [];
    public List<MonthEndCashEvidence> CashBalances { get; set; } = [];
    public decimal? StoredSecuritiesJpy { get; set; }
    public decimal? StoredCostJpy { get; set; }
    public decimal? StoredCashJpy { get; set; }
    public decimal? StoredTotalJpy { get; set; }
    public decimal? StoredUnrealizedJpy { get; set; }
    public decimal? StoredTotalReturnJpy { get; set; }
    public decimal CumulativeDividendJpy { get; set; }
    public decimal? RealizedGainLossJpy { get; set; }
    public decimal? UsdJpyRate { get; set; }
    public string UsdJpySource { get; set; } = string.Empty;
    public DateTime? UsdJpyAcquiredAt { get; set; }
    public string QualitySummary { get; set; } = string.Empty;
}

public sealed class MonthEndPositionEvidence
{
    public int StockId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Broker { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? MarketValueJpy { get; set; }
    public decimal? CostJpy { get; set; }
    public string CostQuality { get; set; } = string.Empty;
    public string CostSource { get; set; } = string.Empty;
    public DateTime? CostConfirmedAt { get; set; }
    public string PriceSource { get; set; } = string.Empty;
    public DateTime? PriceAcquiredAt { get; set; }
    public string PriceFreshness { get; set; } = string.Empty;
    public decimal? FxRate { get; set; }
    public string FxSource { get; set; } = string.Empty;
    public DateTime? FxAcquiredAt { get; set; }
    public string FxFreshness { get; set; } = string.Empty;
}

public sealed class MonthEndCashEvidence
{
    public string Account { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public DateTime? BalanceDate { get; set; }
    public decimal? IncludedJpy { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
}

public sealed record MonthEndRecalculation(
    decimal? SecuritiesJpy, decimal? CostJpy, decimal? CashJpy, decimal? TotalJpy,
    decimal? UnrealizedJpy, decimal? TotalReturnJpy, decimal? TotalDifferenceJpy,
    string DifferenceReason);
