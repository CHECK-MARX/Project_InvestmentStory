using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendMonthlyStackedChartControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendMonthlyStackedChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    public static readonly DependencyProperty SummaryItemsProperty = DependencyProperty.Register(
        nameof(SummaryItems), typeof(IEnumerable), typeof(DividendMonthlyStackedChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IEnumerable? SummaryItems
    {
        get => (IEnumerable?)GetValue(SummaryItemsProperty);
        set => SetValue(SummaryItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        BeginInteractiveRender();
        var entries = Items?.OfType<DividendDashboardEntryViewModel>().ToList()
                      ?? new List<DividendDashboardEntryViewModel>();
        var summaries = SummaryItems?.OfType<DividendMonthlySummaryRowViewModel>()
                            .ToDictionary(x => x.Month)
                        ?? new Dictionary<int, DividendMonthlySummaryRowViewModel>();
        var text = ResourceBrush("PrimaryTextBrush", Brushes.White);
        var secondary = ResourceBrush("SecondaryTextBrush", Brushes.Gray);
        var grid = ResourceBrush("BorderBrush", Brushes.DimGray);
        if (ActualWidth < 360 || ActualHeight < 220)
        {
            DrawText(dc, "グラフの表示領域が不足しています。", 13, 18, 18, secondary);
            return;
        }

        var left = 76d;
        var top = 68d;
        var right = 24d;
        var bottom = 42d;
        var width = ActualWidth - left - right;
        var height = ActualHeight - top - bottom;
        var slot = width / 12d;
        var max = Math.Max(1m, Enumerable.Range(1, 12)
            .Select(month => entries.Where(x => x.Month == month).Sum(x => x.NetJpyValue))
            .Max() * 1.15m);

        DrawLegend(dc, text, secondary);
        DrawText(dc, "税引後配当額（円）", 11, 4, 28, secondary);
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = top + height - height * tick / 4d;
            dc.DrawLine(new Pen(grid, 0.7), new Point(left, y), new Point(left + width, y));
            DrawText(dc, FormatAxis(max * tick / 4m), 10, 4, y - 7, secondary);
        }

        for (var month = 1; month <= 12; month++)
        {
            var monthEntries = entries.Where(x => x.Month == month)
                .OrderByDescending(x => x.IsActual)
                .ThenBy(x => x.Ticker)
                .ToList();
            var center = left + slot * (month - 1) + slot / 2d;
            var barWidth = Math.Clamp(slot * 0.58d, 12d, 44d);
            var y = top + height;
            var monthTooltip = summaries.TryGetValue(month, out var summary)
                ? summary.ToolTipText
                : BuildMonthTooltip(month, monthEntries);
            AddHitTarget(new Rect(left + slot * (month - 1), top, slot, height + 30),
                monthTooltip, month: month, seriesKey: "month:summary");
            foreach (var entry in monthEntries)
            {
                if (entry.NetJpyValue <= 0m) continue;
                var seriesKey = $"status:{entry.ScheduleStatus}";
                if (!IsSeriesVisible(seriesKey)) continue;
                var segmentHeight = Math.Max(3d, (double)(entry.NetJpyValue / max) * height);
                var rectangle = new Rect(center - barWidth / 2d, y - segmentHeight, barWidth, segmentHeight);
                var opacity = InteractionOpacity(entry.Ticker, month, entry.ScheduleStatus);
                var brush = DividendChartColorRegistry.SecurityBrush(entry.Ticker, entry.ScheduleStatus, opacity);
                var visual = DividendChartColorRegistry.ForStatus(entry.ScheduleStatus);
                var outline = new Pen(new SolidColorBrush(DividendChartColorRegistry.ColorForSecurity(entry.Ticker)),
                    entry.ScheduleStatus == InvestmentStory.Core.Models.DividendScheduleStatus.Paid ? .7 : 1.4);
                if (visual.DashPattern is not null) outline.DashStyle = new DashStyle(visual.DashPattern, 0);
                dc.DrawRoundedRectangle(brush, outline, rectangle, 2, 2);
                var hitRectangle = rectangle;
                if (hitRectangle.Height < 10d)
                {
                    hitRectangle.Y -= (10d - hitRectangle.Height) / 2d;
                    hitRectangle.Height = 10d;
                }
                AddHitTarget(hitRectangle, BuildEntryTooltip(entry, monthEntries, entries), entry.Ticker,
                    month, entry.ScheduleStatus, seriesKey);
                y -= segmentHeight;
            }

            DrawText(dc, $"{month}月", 11, center - 14, top + height + 10, secondary);
        }
    }

    private static string BuildEntryTooltip(
        DividendDashboardEntryViewModel entry,
        IReadOnlyCollection<DividendDashboardEntryViewModel> monthEntries,
        IReadOnlyCollection<DividendDashboardEntryViewModel> allEntries)
    {
        var monthTotal = monthEntries.Sum(x => x.NetJpyValue);
        var annualTotal = allEntries.Sum(x => x.NetJpyValue);
        var monthRate = monthTotal <= 0m ? 0m : entry.NetJpyValue / monthTotal * 100m;
        var annualRate = annualTotal <= 0m ? 0m : entry.NetJpyValue / annualTotal * 100m;
        return $"{entry.ToolTipText}{Environment.NewLine}" +
               $"月内構成比: {monthRate:N2}%  年間構成比: {annualRate:N2}%";
    }

    private static string BuildMonthTooltip(
        int month,
        IReadOnlyCollection<DividendDashboardEntryViewModel> entries)
    {
        var actual = entries.Where(x => x.ScheduleStatus == InvestmentStory.Core.Models.DividendScheduleStatus.Paid)
            .Sum(x => x.NetJpyValue);
        var expected = entries.Where(x => x.ScheduleStatus == InvestmentStory.Core.Models.DividendScheduleStatus.Expected)
            .Sum(x => x.NetJpyValue);
        var estimated = entries.Where(x => x.ScheduleStatus == InvestmentStory.Core.Models.DividendScheduleStatus.Estimated)
            .Sum(x => x.NetJpyValue);
        return $"{month}月{Environment.NewLine}" +
               $"月合計: {entries.Sum(x => x.NetJpyValue):N0}円{Environment.NewLine}" +
               $"実績: {actual:N0}円  予定: {expected:N0}円  推定: {estimated:N0}円{Environment.NewLine}" +
               $"銘柄数: {entries.Select(x => x.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).Count()}件";
    }

    private void DrawLegend(DrawingContext dc, Brush text, Brush secondary)
    {
        var x = 4d;
        var y = 4d;
        foreach (var visual in DividendChartColorRegistry.Legend)
        {
            var seriesKey = $"status:{visual.Status}";
            var rect = new Rect(x, y, 13, 13);
            var pen = new Pen(new SolidColorBrush(visual.Color), 1.2);
            if (visual.DashPattern is not null) pen.DashStyle = new DashStyle(visual.DashPattern, 0);
            dc.DrawRoundedRectangle(DividendChartColorRegistry.StatusBrush(visual.Status,
                IsSeriesVisible(seriesKey) ? 1d : .22d), pen, rect, 2, 2);
            DrawText(dc, visual.DisplayName, 10, x + 18, y - 2, text);
            var hitWidth = 18 + Math.Max(48, visual.DisplayName.Length * 11);
            AddHitTarget(new Rect(x, y - 2, hitWidth, 18), $"{visual.DisplayName}の表示を切り替えます。",
                status: visual.Status, seriesKey: seriesKey, isLegend: true);
            x += hitWidth + 8;
            if (x > Math.Max(420, ActualWidth - 180))
            {
                x = 4;
                y += 22;
            }
        }
        DrawText(dc, "凡例クリック: 表示切替 / 棒クリック: 関連グラフを選択", 10, 4, 46, secondary);
    }

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static string FormatAxis(decimal value) => value switch
    {
        >= 100_000_000m => $"{value / 100_000_000m:N1}億",
        >= 10_000m => $"{value / 10_000m:N0}万",
        _ => $"{value:N0}"
    };

    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendMonthlyStackedChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
