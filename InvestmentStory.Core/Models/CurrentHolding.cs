namespace InvestmentStory.Core.Models;

public sealed class CurrentHolding
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public decimal CurrentShares { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CurrentExchangeRate { get; set; } = 1m;
    public DateTime ExchangeRateAcquiredAt { get; set; } = DateTime.Today;
    public string ExchangeRateSource { get; set; } = "手入力";
    public string ExchangeRateInputType { get; set; } = "手入力";
    public decimal AnnualDividendPerShare { get; set; }
    public string DividendStatus { get; set; } = "配当未入力";
    public string DividendFrequency { get; set; } = "年4回";
    public string DividendMonths { get; set; } = string.Empty;
    public DateTime CurrentPriceAcquiredAt { get; set; } = DateTime.MinValue;
    public string CurrentPriceSource { get; set; } = string.Empty;
    public DateTime DividendInfoAcquiredAt { get; set; } = DateTime.MinValue;
    public string DividendInfoSource { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Today;
}
