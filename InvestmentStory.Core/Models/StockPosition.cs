namespace InvestmentStory.Core.Models;

public sealed class StockPosition
{
    public Stock Stock { get; set; } = new();
    public Purchase Purchase { get; set; } = new();
    public StockSplit Split { get; set; } = new();
    public CurrentHolding CurrentHolding { get; set; } = new();
    public MutualFundHolding MutualFund { get; set; } = new();
    public string ManualChangeReason { get; set; } = string.Empty;
    public string ExpectedAuditBeforeJson { get; set; } = string.Empty;
    public string ExpectedAuditAfterJson { get; set; } = string.Empty;
    public bool IsMutualFund => AssetTypes.IsMutualFund(Stock.AssetType);
}
