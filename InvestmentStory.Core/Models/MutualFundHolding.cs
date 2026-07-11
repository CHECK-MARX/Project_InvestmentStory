namespace InvestmentStory.Core.Models;

public sealed class MutualFundHolding
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public string FundName { get; set; } = string.Empty;
    public string FundCode { get; set; } = string.Empty;
    public string AssociationCode { get; set; } = string.Empty;
    public decimal UnitsHeld { get; set; }
    public decimal UnitBase { get; set; } = 10000m;
    public decimal AverageCostNav { get; set; }
    public decimal CurrentNav { get; set; }
    public decimal AcquisitionAmount { get; set; }
    public decimal MarketValue { get; set; }
    public decimal UnrealizedGainLoss { get; set; }
    public DateTime NavDate { get; set; } = DateTime.MinValue;
    public string NavSource { get; set; } = string.Empty;
    public string DistributionMethod { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public decimal TotalPurchaseAmount { get; set; }
    public decimal TotalSaleAmount { get; set; }
    public decimal ReinvestedDistributionAmount { get; set; }
}
