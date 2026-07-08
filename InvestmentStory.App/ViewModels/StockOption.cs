namespace InvestmentStory.App.ViewModels;

public sealed class StockOption
{
    public int StockId { get; init; }
    public string Display { get; init; } = string.Empty;
    public string Currency { get; init; } = "USD";
    public string Broker { get; init; } = string.Empty;
    public decimal ExchangeRate { get; init; } = 1m;
}
