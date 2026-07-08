namespace InvestmentStory.Core.Models;

public sealed class Purchase
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public DateTime PurchaseDate { get; set; } = DateTime.Today;
    public decimal Shares { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DateTime ExchangeRateAcquiredAt { get; set; } = DateTime.Today;
    public string ExchangeRateSource { get; set; } = "手入力";
    public string ExchangeRateInputType { get; set; } = "手入力";
    public decimal Fee { get; set; }
    public string Memo { get; set; } = string.Empty;
}
