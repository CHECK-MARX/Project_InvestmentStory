using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class ChartBarRowViewModel
{
    private const double MaxBarWidth = 320d;

    public ChartBarRowViewModel(DividendAggregate aggregate, decimal maxAmountJpy)
    {
        Label = aggregate.Label;
        AmountJpy = Formatters.Jpy(aggregate.AmountJpy);
        ToolTip = $"X: {Label}\nY: {AmountJpy}";
        BarWidth = CalculateBarWidth(aggregate.AmountJpy, maxAmountJpy);
    }

    public ChartBarRowViewModel(string label, decimal amountJpy, decimal maxAmountJpy, bool signed = false)
    {
        Label = label;
        AmountJpy = signed ? Formatters.SignedJpy(amountJpy) : Formatters.Jpy(amountJpy);
        ToolTip = $"X: {Label}\nY: {AmountJpy}";
        BarWidth = CalculateBarWidth(Math.Abs(amountJpy), maxAmountJpy);
    }

    public string Label { get; }
    public string AmountJpy { get; }
    public string ToolTip { get; }
    public double BarWidth { get; }

    private static double CalculateBarWidth(decimal amountJpy, decimal maxAmountJpy)
    {
        if (amountJpy <= 0m || maxAmountJpy <= 0m)
        {
            return 0d;
        }

        var width = (double)(Math.Abs(amountJpy) / maxAmountJpy) * MaxBarWidth;
        return Math.Max(8d, width);
    }
}
