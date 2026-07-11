namespace InvestmentStory.Core.Models;

public sealed class BrokerTrade
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public DateTime TradeDate { get; set; } = DateTime.Today;
    public DateTime SettlementDate { get; set; } = DateTime.Today;
    public string Broker { get; set; } = string.Empty;
    public string AccountType { get; set; } = AccountTypes.Unknown;
    public string CustodyType { get; set; } = string.Empty;
    public string TradeType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SignedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "JPY";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SettlementAmountJpy { get; set; }
    public decimal FeeJpy { get; set; }
    public decimal TaxJpy { get; set; }
    public decimal RealizedGainLoss { get; set; }
    public decimal RealizedGainLossJpy { get; set; }
    public decimal AfterTradeQuantity { get; set; }
    public decimal AfterTradeAverageCost { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
