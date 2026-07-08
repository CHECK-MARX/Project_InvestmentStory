namespace InvestmentStory.Core.Models;

public sealed class DividendPayment
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public string AccountType { get; set; } = "Unknown";
    public string TaxAccountType { get; set; } = "Unknown";
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public DateTime RecordDate { get; set; } = DateTime.MinValue;
    public DateTime ExDividendDate { get; set; } = DateTime.MinValue;
    public DateTime DeclaredDate { get; set; } = DateTime.MinValue;
    public int FiscalYear { get; set; }
    public string FiscalQuarter { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Broker { get; set; } = string.Empty;
    public string DividendStatus { get; set; } = "Actual";
    public string Source { get; set; } = "Manual";
    public string SourceFile { get; set; } = string.Empty;
    public int SourceRowNumber { get; set; }
    public int SourcePriority { get; set; } = 50;
    public decimal Quantity { get; set; }
    public decimal DividendPerShare { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal ForeignTaxAmount { get; set; }
    public decimal DomesticTaxAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal ExchangeRate { get; set; } = 1m;
    public DateTime ExchangeRateAcquiredAt { get; set; } = DateTime.Today;
    public string ExchangeRateSource { get; set; } = "手入力";
    public string ExchangeRateInputType { get; set; } = "手入力";
    public decimal GrossAmountJpy { get; set; }
    public decimal ForeignTaxAmountJpy { get; set; }
    public decimal DomesticTaxAmountJpy { get; set; }
    public decimal TotalTaxAmountJpy { get; set; }
    public decimal NetAmountJpy { get; set; }
    public decimal JpyAmount { get; set; }
    public bool IsTaxEstimated { get; set; }
    public bool IsNisa { get; set; }
    public bool IsForeignStock { get; set; }
    public int? TaxProfileId { get; set; }
    public int? MatchedActualDividendId { get; set; }
    public int? ReplacedByDividendId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string Memo { get; set; } = string.Empty;

    public decimal DividendAmountUsd => Currency.Equals("USD", StringComparison.OrdinalIgnoreCase) ? NetAmount : 0m;
}
