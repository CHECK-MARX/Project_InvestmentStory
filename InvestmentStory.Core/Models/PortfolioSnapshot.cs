namespace InvestmentStory.Core.Models;

public sealed class PortfolioSnapshot
{
    public int Id { get; set; }
    public DateTime SnapshotDate { get; set; } = DateTime.Today;
    public decimal TotalMarketValueJpy { get; set; }
    public decimal TotalCostBasisJpy { get; set; }
    public decimal UnrealizedGainLossJpy { get; set; }
    public decimal CumulativeDividendJpy { get; set; }
    public decimal CumulativeNetDividendJpy
    {
        get => CumulativeDividendJpy;
        set => CumulativeDividendJpy = value;
    }
    public decimal RealizedGainLossJpy { get; set; }
    public decimal TotalReturnJpy { get; set; }
    public decimal UsdJpyRate { get; set; }
    public decimal StockValueJpy { get; set; }
    public decimal MutualFundValueJpy { get; set; }
    public decimal CashValueJpy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
