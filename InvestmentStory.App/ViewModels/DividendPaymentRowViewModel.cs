using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DividendPaymentRowViewModel
{
    public DividendPaymentRowViewModel(DividendPayment payment)
    {
        Payment = payment;
    }

    public DividendPayment Payment { get; }
    public int Id => Payment.Id;
    public string PaymentDate => Payment.PaymentDate.ToString("yyyy/MM/dd");
    public string Ticker => Payment.Ticker;
    public string StockName => Payment.StockName;
    public string Broker => Payment.Broker;
    public string AccountType => Payment.AccountType;
    public string Status => Payment.DividendStatus switch
    {
        "Actual" => "実績",
        "Estimated" => "見込み",
        "Planned" => "予定",
        "Confirmed" => "確認済み予定",
        "PaymentDue" => "入金予定日経過",
        "Replaced" => "置換済み",
        _ => Payment.DividendStatus
    };
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
