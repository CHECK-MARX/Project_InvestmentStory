using System.Windows.Media;
using InvestmentStory.App.Infrastructure;

namespace InvestmentStory.App.ViewModels;

public sealed class CompositionCategoryViewModel
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(56, 189, 248), Color.FromRgb(167, 139, 250),
        Color.FromRgb(34, 197, 94), Color.FromRgb(245, 158, 11),
        Color.FromRgb(251, 113, 133), Color.FromRgb(45, 212, 191),
        Color.FromRgb(96, 165, 250), Color.FromRgb(217, 70, 239),
        Color.FromRgb(148, 163, 184)
    ];

    public CompositionCategoryViewModel(string label, decimal amount, decimal total,
        IReadOnlyList<CompositionHoldingViewModel> holdings, int colorIndex)
    {
        Label = label;
        Amount = amount;
        AmountText = Formatters.Jpy(amount);
        SharePercent = total <= 0m ? 0d : (double)(amount / total * 100m);
        ShareText = $"{SharePercent:N1}%";
        HoldingCountText = $"{holdings.Count}銘柄";
        Holdings = holdings;
        var brush = new SolidColorBrush(Palette[colorIndex % Palette.Length]);
        brush.Freeze();
        AccentBrush = brush;
    }

    public string Label { get; }
    public decimal Amount { get; }
    public string AmountText { get; }
    public double SharePercent { get; }
    public string ShareText { get; }
    public string HoldingCountText { get; }
    public IReadOnlyList<CompositionHoldingViewModel> Holdings { get; }
    public Brush AccentBrush { get; }
}

public sealed class CompositionHoldingViewModel
{
    public CompositionHoldingViewModel(string ticker, string name, decimal amount)
    {
        Amount = amount;
        Label = ticker.StartsWith("FUND:", StringComparison.OrdinalIgnoreCase)
            ? name : $"{ticker}  {name}";
        AmountText = Formatters.Jpy(amount);
    }

    public decimal Amount { get; }
    public string Label { get; }
    public string AmountText { get; }
}
