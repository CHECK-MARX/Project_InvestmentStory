namespace InvestmentStory.Core.Models;

public sealed class TaxProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string AccountType { get; set; } = "Unknown";
    public string AssetType { get; set; } = "Stock";
    public decimal ForeignWithholdingTaxRate { get; set; }
    public decimal DomesticIncomeTaxRate { get; set; }
    public decimal DomesticLocalTaxRate { get; set; }
    public decimal DomesticSpecialTaxRate { get; set; }
    public decimal TotalDomesticTaxRate { get; set; }
    public bool IsDomesticTaxExempt { get; set; }
    public bool IsForeignTaxExempt { get; set; }
    public string Memo { get; set; } = string.Empty;
}

public sealed class DividendTaxInput
{
    public decimal Quantity { get; init; }
    public decimal DividendPerShare { get; init; }
    public string Currency { get; init; } = "JPY";
    public decimal ExchangeRate { get; init; } = 1m;
    public TaxProfile TaxProfile { get; init; } = new();
}

public sealed class DividendTaxCalculation
{
    public decimal GrossAmount { get; init; }
    public decimal ForeignTaxAmount { get; init; }
    public decimal DomesticTaxAmount { get; init; }
    public decimal TotalTaxAmount { get; init; }
    public decimal NetAmount { get; init; }
    public decimal GrossAmountJpy { get; init; }
    public decimal ForeignTaxAmountJpy { get; init; }
    public decimal DomesticTaxAmountJpy { get; init; }
    public decimal TotalTaxAmountJpy { get; init; }
    public decimal NetAmountJpy { get; init; }
}
