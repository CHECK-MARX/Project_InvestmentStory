using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DividendPaymentRowViewModel
{
    private readonly string? _displayStatus;
    private readonly string? _dataQuality;

    public DividendPaymentRowViewModel(
        DividendPayment payment,
        string? displayStatus = null,
        string? dataQuality = null)
    {
        Payment = payment;
        _displayStatus = displayStatus;
        _dataQuality = dataQuality;
    }

    public DividendPayment Payment { get; }
    public int Id => Payment.Id;
    public string PaymentDate => Payment.PaymentDate.ToString("yyyy/MM/dd");
    public string Ticker => Payment.Ticker;
    public string StockName => Payment.StockName;
    public string Broker => Payment.Broker;
    public string AccountType => Payment.AccountType;
    public string Status => _displayStatus ?? Payment.DividendStatus switch
    {
        "Actual" => "入金済み",
        "Estimated" => "推定",
        "Planned" => "入金予定",
        "Confirmed" => "入金予定",
        "PaymentDue" => "予定日経過（CSV未照合）",
        "Replaced" => "置換済み",
        _ => Payment.DividendStatus
    };
    public string DataQuality => _dataQuality ?? (Payment.DividendStatus switch
    {
        "Actual" => "実績",
        "Estimated" => "推定",
        "PaymentDue" => "未照合",
        "Planned" or "Confirmed" => "予定",
        _ => "-"
    });
    public string Source => Payment.Source;
    public string Quantity => Payment.Quantity == 0m ? "-" : Payment.Quantity.ToString("N2");
    public string DividendPerShare => Payment.DividendPerShare == 0m ? "-" : Formatters.Money(Payment.DividendPerShare, Payment.Currency);
    public string GrossAmount => Formatters.Money(Payment.GrossAmount, Payment.Currency);
    public string ForeignTaxAmount => Formatters.Money(Payment.ForeignTaxAmount, Payment.Currency);
    public string DomesticTaxAmount => Formatters.Money(Payment.DomesticTaxAmount, Payment.Currency);
    public string TaxAmount => Formatters.Money(Payment.TotalTaxAmount > 0m ? Payment.TotalTaxAmount : Payment.TaxAmount, Payment.Currency);
    public string NetAmount => Formatters.Money(Payment.NetAmount, Payment.Currency);
    public string Currency => Payment.Currency;
    public string DividendAmountUsd => Formatters.Money(Payment.DividendAmountUsd, "USD");
    public string ExchangeRate => Payment.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? "1.00" : $"{Payment.ExchangeRate:N2} JPY/USD";
    public string ExchangeRateInfo => $"{Payment.ExchangeRateAcquiredAt:yyyy/MM/dd HH:mm} / {Payment.ExchangeRateSource} / {Payment.ExchangeRateInputType}";
    public string GrossAmountJpy => Formatters.Jpy(Payment.GrossAmountJpy);
    public string TaxAmountJpy => Formatters.Jpy(Payment.TotalTaxAmountJpy);
    public string JpyAmount => Formatters.Jpy(Payment.NetAmountJpy > 0m ? Payment.NetAmountJpy : Payment.JpyAmount);
    public string TaxNote => Payment.IsTaxEstimated ? "概算/参考値" : "CSV/実績";
    public string Memo => Payment.Memo;
}
