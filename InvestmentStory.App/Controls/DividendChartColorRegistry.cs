using System.Windows.Media;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.Controls;

public sealed record DividendStatusVisual(
    DividendScheduleStatus Status,
    string DisplayName,
    Color Color,
    double Opacity,
    DoubleCollection? DashPattern = null);

public static class DividendChartColorRegistry
{
    private static readonly Color[] SecurityColors =
    {
        Color.FromRgb(14, 165, 233), Color.FromRgb(167, 139, 250),
        Color.FromRgb(245, 158, 11), Color.FromRgb(34, 197, 94),
        Color.FromRgb(96, 165, 250), Color.FromRgb(244, 114, 182),
        Color.FromRgb(45, 212, 191), Color.FromRgb(251, 113, 133),
        Color.FromRgb(250, 204, 21), Color.FromRgb(129, 140, 248),
        Color.FromRgb(74, 222, 128), Color.FromRgb(56, 189, 248),
        Color.FromRgb(192, 132, 252), Color.FromRgb(251, 146, 60),
        Color.FromRgb(94, 234, 212), Color.FromRgb(248, 113, 113)
    };

    private static readonly IReadOnlyDictionary<DividendScheduleStatus, DividendStatusVisual> Statuses =
        new Dictionary<DividendScheduleStatus, DividendStatusVisual>
        {
            [DividendScheduleStatus.Paid] = new(DividendScheduleStatus.Paid, "入金済み", Color.FromRgb(56, 189, 248), 1d),
            [DividendScheduleStatus.Expected] = new(DividendScheduleStatus.Expected, "入金予定", Color.FromRgb(34, 197, 94), .88d),
            [DividendScheduleStatus.Estimated] = new(DividendScheduleStatus.Estimated, "推定", Color.FromRgb(245, 158, 11), .78d, new DoubleCollection { 2, 2 }),
            [DividendScheduleStatus.MissedEligibility] = new(DividendScheduleStatus.MissedEligibility, "権利取得不可", Color.FromRgb(100, 116, 139), .62d, new DoubleCollection { 4, 2 }),
            [DividendScheduleStatus.NotAvailable] = new(DividendScheduleStatus.NotAvailable, "未取得", Color.FromRgb(239, 68, 68), .88d),
            [DividendScheduleStatus.OverdueUnmatched] = new(DividendScheduleStatus.OverdueUnmatched, "期限超過・未照合", Color.FromRgb(248, 113, 113), .9d, new DoubleCollection { 1, 2 })
        };

    public static IReadOnlyList<DividendStatusVisual> Legend => Statuses.Values.ToList();
    public static DividendStatusVisual ForStatus(DividendScheduleStatus status) => Statuses[status];

    public static Color ColorForSecurity(string? canonicalSecurityKey)
    {
        var key = string.IsNullOrWhiteSpace(canonicalSecurityKey)
            ? "UNKNOWN"
            : canonicalSecurityKey.Trim().ToUpperInvariant();
        var hash = 2166136261u;
        foreach (var character in key)
        {
            hash ^= character;
            hash *= 16777619u;
        }
        return SecurityColors[hash % SecurityColors.Length];
    }

    public static SolidColorBrush SecurityBrush(
        string? canonicalSecurityKey,
        DividendScheduleStatus status = DividendScheduleStatus.Paid,
        double opacityMultiplier = 1d)
    {
        var color = ColorForSecurity(canonicalSecurityKey);
        var statusOpacity = ForStatus(status).Opacity;
        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(255d * statusOpacity * opacityMultiplier, 20d, 255d),
            color.R, color.G, color.B));
    }

    public static SolidColorBrush StatusBrush(DividendScheduleStatus status, double opacityMultiplier = 1d)
    {
        var visual = ForStatus(status);
        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(255d * visual.Opacity * opacityMultiplier, 20d, 255d),
            visual.Color.R, visual.Color.G, visual.Color.B));
    }

    public static SolidColorBrush HeatmapBrush(decimal amount, decimal maximum, DividendScheduleStatus? status)
    {
        if (status == DividendScheduleStatus.NotAvailable)
            return StatusBrush(DividendScheduleStatus.NotAvailable, .55d);
        if (amount <= 0m)
            return new SolidColorBrush(Color.FromArgb(28, 100, 116, 139));

        var ratio = maximum <= 0m ? 0d : Math.Clamp((double)(amount / maximum), 0d, 1d);
        var baseColor = status is null
            ? Color.FromRgb(56, 189, 248)
            : ForStatus(status.Value).Color;
        return new SolidColorBrush(Color.FromArgb(
            (byte)(60d + 190d * Math.Sqrt(ratio)), baseColor.R, baseColor.G, baseColor.B));
    }
}
