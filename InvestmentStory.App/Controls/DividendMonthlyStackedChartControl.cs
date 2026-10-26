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
        var legendBottom = DrawLegend(dc, text);
        var top = legendBottom + 53d;
        var right = 24d;
        var bottom = 42d;
        var width = ActualWidth - left - right;
        var height = ActualHeight - top - bottom;
        var slot = width / 12d;
        var max = Math.Max(1m, Enumerable.Range(1, 12)
            .Select(month => entries.Where(x => x.Month == month).Sum(x => x.NetJpyValue))
            .Max() * 1.15m);

        DrawText(dc, "棒の色＝入金状態  ／  棒の区切り＝銘柄別", 11, 4, legendBottom + 7, secondary);
        DrawText(dc, "税引後配当額（円）  ・  凡例クリックで表示切替／棒にマウスを重ねると銘柄・金額を表示", 10, 4, legendBottom + 25, secondary);
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
            var visibleEntries = monthEntries.Where(x => x.NetJpyValue > 0m
                && IsSeriesVisible($"status:{x.ScheduleStatus}")).ToList();
            var center = left + slot * (month - 1) + slot / 2d;
            var barWidth = Math.Clamp(slot * 0.58d, 12d, 44d);
            var y = top + height;
            var monthTooltip = summaries.TryGetValue(month, out var summary)
                ? summary.ToolTipText
                : BuildMonthTooltip(month, monthEntries);
            AddHitTarget(new Rect(left + slot * (month - 1), top, slot, height + 30),
                monthTooltip, month: month, seriesKey: "month:summary");
            foreach (var entry in visibleEntries)
            {
                var seriesKey = $"status:{entry.ScheduleStatus}";
                var segmentHeight = Math.Max(1d, (double)(entry.NetJpyValue / max) * height);
                var rectangle = new Rect(center - barWidth / 2d, y - segmentHeight, barWidth, segmentHeight);
                var opacity = InteractionOpacity(entry.Ticker, month, entry.ScheduleStatus);
                var brush = DividendChartColorRegistry.StatusBrush(entry.ScheduleStatus, opacity);
                var visual = DividendChartColorRegistry.ForStatus(entry.ScheduleStatus);
                var outline = new Pen(visual.DashPattern is null
                        ? new SolidColorBrush(Color.FromRgb(15, 23, 42))
                        : text,
                    visual.DashPattern is null ? .8 : 1.4);
                if (visual.DashPattern is not null) outline.DashStyle = new DashStyle(visual.DashPattern, 0);
                dc.DrawRoundedRectangle(brush, outline, rectangle, 2, 2);
                if (segmentHeight >= 20d && barWidth >= 30d)
                {
                    var tickerText = FormatText(entry.Ticker, 9, Brushes.Black);
                    if (tickerText.Width <= barWidth - 5d)
                        dc.DrawText(tickerText, new Point(center - tickerText.Width / 2d,
                            rectangle.Y + (segmentHeight - tickerText.Height) / 2d));
                }
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

            if (ActualWidth >= 850d && visibleEntries.Count > 0)
            {
                var totalText = FormatText(FormatBarTotal(visibleEntries.Sum(x => x.NetJpyValue)), 10, text);
                dc.DrawText(totalText, new Point(center - totalText.Width / 2d, Math.Max(top, y - totalText.Height - 3d)));
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

    private double DrawLegend(DrawingContext dc, Brush text)
    {
        var x = 4d;
        var y = 4d;
        foreach (var visual in DividendChartColorRegistry.Legend)
        {
            var seriesKey = $"status:{visual.Status}";
            var hitWidth = 18 + Math.Max(48, visual.DisplayName.Length * 11);
            if (x > 4d && x + hitWidth > ActualWidth - 8d)
            {
                x = 4d;
                y += 22d;
            }
            var rect = new Rect(x, y, 13, 13);
            var pen = new Pen(new SolidColorBrush(visual.Color), 1.2);
            if (visual.DashPattern is not null) pen.DashStyle = new DashStyle(visual.DashPattern, 0);
            dc.DrawRoundedRectangle(DividendChartColorRegistry.StatusBrush(visual.Status,
                IsSeriesVisible(seriesKey) ? 1d : .22d), pen, rect, 2, 2);
            DrawText(dc, visual.DisplayName, 10, x + 18, y - 2, text);
            AddHitTarget(new Rect(x, y - 2, hitWidth, 18), $"{visual.DisplayName}の表示を切り替えます。",
                status: visual.Status, seriesKey: seriesKey, isLegend: true);
            x += hitWidth + 8;
        }
        return y + 13d;
    }

    private static string FormatBarTotal(decimal value) => value >= 10_000m
        ? $"{value / 10_000m:N1}万"
        : $"{value:N0}円";

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static string FormatAxis(decimal value) => value switch
    {
        >= 100_000_000m => $"{value / 100_000_000m:N1}億",
        >= 10_000m => $"{value / 10_000m:N0}万",
        _ => $"{value:N0}"
    };

    private static FormattedText FormatText(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0);

    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(FormatText(value, size, brush), new Point(x, y));

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
