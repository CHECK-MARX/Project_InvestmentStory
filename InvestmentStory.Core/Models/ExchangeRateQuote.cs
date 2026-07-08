namespace InvestmentStory.Core.Models;

public sealed class ExchangeRateQuote
{
    public string BaseCurrency { get; init; } = "USD";
    public string QuoteCurrency { get; init; } = "JPY";
    public decimal Rate { get; init; }
    public DateTime AcquiredAt { get; init; } = DateTime.Now;
    public string Source { get; init; } = "手入力";
    public string InputType { get; init; } = "手入力";
    public string Pair => $"{BaseCurrency}/{QuoteCurrency}";
}
