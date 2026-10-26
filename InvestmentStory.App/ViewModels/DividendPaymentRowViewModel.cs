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
    public string ExDividendDate => Payment.ExDividendDate == DateTime.MinValue
        ? "不明" : Payment.ExDividendDate.ToString("yyyy/MM/dd");
    public string RecordDate => Payment.RecordDate == DateTime.MinValue
        ? "不明" : Payment.RecordDate.ToString("yyyy/MM/dd");
    public string RightsStatus => Payment.RecordDate == DateTime.MinValue &&
        Payment.ExDividendDate == DateTime.MinValue ? "権利日不明" : "権利日あり";
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
        "Excluded" => "対象外",
        _ => Payment.DividendStatus
    };
    public string DataQuality => Payment.IsNetOnly ? "税引後のみ" : _dataQuality ?? (Payment.DividendStatus switch
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
    public string GrossAmount => Payment.IsNetOnly ? "不明" : Formatters.Money(Payment.GrossAmount, Payment.Currency);
    public string ForeignTaxAmount => Payment.IsNetOnly ? "不明" : Formatters.Money(Payment.ForeignTaxAmount, Payment.Currency);
    public string DomesticTaxAmount => Payment.IsNetOnly ? "不明" : Formatters.Money(Payment.DomesticTaxAmount, Payment.Currency);
    public string TaxAmount => Payment.IsNetOnly ? "不明" : Formatters.Money(Payment.TotalTaxAmount > 0m ? Payment.TotalTaxAmount : Payment.TaxAmount, Payment.Currency);
    public string NetAmount => Formatters.Money(Payment.NetAmount, Payment.Currency);
    public string Currency => Payment.Currency;
    public string DividendAmountUsd => Formatters.Money(Payment.DividendAmountUsd, "USD");
    public string ExchangeRate => Payment.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
        ? "1.00" : Payment.ExchangeRate > 0m ? $"{Payment.ExchangeRate:N2} JPY/{Payment.Currency}" : "不明";
    public string ExchangeRateInfo => Payment.ExchangeRate > 0m
        ? $"{Payment.ExchangeRateAcquiredAt:yyyy/MM/dd HH:mm} / {Payment.ExchangeRateSource} / {Payment.ExchangeRateInputType}"
        : "為替不明";
    public string GrossAmountJpy => Payment.IsNetOnly ? "不明" : Formatters.Jpy(Payment.GrossAmountJpy);
    public string TaxAmountJpy => Payment.IsNetOnly ? "不明" : Formatters.Jpy(Payment.TotalTaxAmountJpy);
    public string JpyAmount => Formatters.Jpy(Payment.NetAmountJpy > 0m ? Payment.NetAmountJpy : Payment.JpyAmount);
    public string TaxNote => Payment.IsNetOnly ? "税額不明" : Payment.IsTaxEstimated ? "概算/参考値" : "CSV/実績";
    public string Memo => Payment.Memo;
}
