namespace InvestmentStory.App.ViewModels;

public sealed class PriceChartPointViewModel
{
    public DateTime Date { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal? SecondaryClose { get; init; }
    public decimal Volume { get; init; }
    public decimal? MovingAverage5 { get; init; }
    public decimal? MovingAverage25 { get; init; }
    public string ToolTipText { get; init; } = string.Empty;
}
