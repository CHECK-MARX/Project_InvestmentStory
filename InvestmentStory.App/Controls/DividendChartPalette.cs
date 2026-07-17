using System.Windows.Media;

namespace InvestmentStory.App.Controls;

internal static class DividendChartPalette
{
    private static readonly Color[] Colors =
    {
        Color.FromRgb(56, 189, 248),
        Color.FromRgb(167, 139, 250),
        Color.FromRgb(34, 197, 94),
        Color.FromRgb(245, 158, 11),
        Color.FromRgb(244, 114, 182),
        Color.FromRgb(45, 212, 191),
        Color.FromRgb(96, 165, 250),
        Color.FromRgb(251, 113, 133),
        Color.FromRgb(163, 230, 53),
        Color.FromRgb(192, 132, 252),
        Color.FromRgb(251, 191, 36),
        Color.FromRgb(34, 211, 238)
    };

    public static Color ColorForTicker(string ticker)
    {
        return DividendChartColorRegistry.ColorForSecurity(ticker);
    }

    public static SolidColorBrush BrushForTicker(string ticker, bool actual)
    {
        var color = ColorForTicker(ticker);
        var brush = new SolidColorBrush(actual
            ? color
            : Color.FromArgb(145, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
