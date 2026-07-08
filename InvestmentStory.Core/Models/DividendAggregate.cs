namespace InvestmentStory.Core.Models;

public sealed class DividendAggregate
{
    public string Label { get; init; } = string.Empty;
    public decimal AmountJpy { get; init; }
}
