namespace InvestmentStory.Core.Models;

public sealed class DividendPurchasePlan
{
    public int Id { get; set; }
    public string Name { get; set; } = "Default";
    public int TargetYear { get; set; } = DateTime.Today.Year;
    public DateTime PlannedPurchaseDate { get; set; } = DateTime.Today;
    public string DisplayUnit { get; set; } = DividendPurchasePlanDisplayUnits.AllAccounts;
    public decimal TargetAnnualNetDividendJpy { get; set; } = 1_200_000m;
    public bool IsLastUsed { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public IReadOnlyList<DividendPurchasePlanItem> Items { get; set; } = Array.Empty<DividendPurchasePlanItem>();
}

public sealed class DividendPurchasePlanItem
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int ItemOrder { get; set; }
    public bool IsNewStock { get; set; }
    public int StockId { get; set; }
    public string PlanKey { get; set; } = string.Empty;
    public string CanonicalSecurityKey { get; set; } = string.Empty;
    public string PositionKey { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Broker { get; set; } = string.Empty;
    public string AccountType { get; set; } = AccountTypes.Unknown;
    public string Country { get; set; } = string.Empty;
    public string Currency { get; set; } = "JPY";
    public decimal CurrentShares { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal AnnualDividendPerShare { get; set; }
    public decimal CurrentCostJpy { get; set; }
    public decimal CurrentMarketValueJpy { get; set; }
    public string DividendFrequency { get; set; } = string.Empty;
    public string DividendMonths { get; set; } = string.Empty;
    public DateTime? DividendRecordDate { get; set; }
    public DateTime? ExDividendDate { get; set; }
    public DateTime? DividendPaymentDate { get; set; }
    public string AnnualDividendSource { get; set; } = string.Empty;
    public string MarketDataSource { get; set; } = string.Empty;
    public DateTime? MarketDataAcquiredAt { get; set; }
    public string MarketDataStatus { get; set; } = string.Empty;
    public string DataQuality { get; set; } = string.Empty;
    public decimal PlannedAdditionalShares { get; set; }
    public string PlannedBroker { get; set; } = string.Empty;
    public string PlannedAccountType { get; set; } = AccountTypes.Unknown;
    public decimal AnnualDividendGrowthRate { get; set; }
    public string PurchaseMode { get; set; } = DividendGrowthPurchaseModes.OneTime;
}
