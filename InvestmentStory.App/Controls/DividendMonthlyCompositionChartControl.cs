using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.Controls;

public sealed class DividendMonthlyCompositionChartControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendMonthlyCompositionChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanStockMonthlyRowViewModel> _rows = Array.Empty<DividendPlanStockMonthlyRowViewModel>();
    private double _left;
    private double _slot;

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        BeginInteractiveRender();
        var source = Items?.OfType<DividendPlanStockMonthlyRowViewModel>()
            .Where(x => x.TotalValue > 0m).OrderByDescending(x => x.TotalValue).ToList()
            ?? new List<DividendPlanStockMonthlyRowViewModel>();
        if (source.Count == 0)
        {
            DrawText(dc, "銘柄別の月別配当データがありません。", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        _rows = source.Take(5).ToList();
        var otherRows = source.Skip(5).ToList();
        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var grid = Brush("BorderBrush", Brushes.DimGray);
        _left = 58d;
        var top = 50d;
        var bottom = 38d;
        var width = Math.Max(120d, ActualWidth - _left - 18d);
        var height = Math.Max(80d, ActualHeight - top - bottom);
        _slot = width / 12d;
        var monthlyTotals = Enumerable.Range(0, 12).Select(index => source.Sum(x => x.PlannedValues[index])).ToArray();
        var max = Math.Max(1m, monthlyTotals.Max() * 1.1m);

        var legendX = 4d;
        foreach (var row in _rows)
        {
            var seriesKey = $"ticker:{row.Ticker}";
            var legendBrush = DividendChartColorRegistry.SecurityBrush(row.Ticker,
                opacityMultiplier: IsSeriesVisible(seriesKey) ? 1d : .22d);
            dc.DrawRoundedRectangle(legendBrush, null, new Rect(legendX, 10, 12, 12), 2, 2);
            DrawText(dc, row.Ticker, 10, legendX + 16, 7, text);
            var legendWidth = Math.Max(58d, row.Ticker.Length * 10d + 30d);
            AddHitTarget(new Rect(legendX, 4, legendWidth, 26), $"{row.Ticker}の表示を切り替えます。",
                row.Ticker, seriesKey: seriesKey, isLegend: true);
            legendX += legendWidth;
        }
        if (otherRows.Count > 0)
        {
            var seriesKey = "ticker:OTHER";
            var legendBrush = DividendChartColorRegistry.SecurityBrush("OTHER",
                opacityMultiplier: IsSeriesVisible(seriesKey) ? 1d : .22d);
            dc.DrawRoundedRectangle(legendBrush, null, new Rect(legendX, 10, 12, 12), 2, 2);
            DrawText(dc, "その他", 10, legendX + 16, 7, text);
            AddHitTarget(new Rect(legendX, 4, 72, 26), "その他の銘柄の表示を切り替えます。",
                seriesKey: seriesKey, isLegend: true);
        }

        for (var tick = 0; tick <= 4; tick++)
        {
            var y = top + height - height * tick / 4d;
            dc.DrawLine(new Pen(grid, 0.6), new Point(_left, y), new Point(_left + width, y));
            DrawText(dc, FormatAxis(max * tick / 4m), 10, 2, y - 7, secondary);
        }

        for (var monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            var x = _left + monthIndex * _slot + _slot * 0.22;
            var barWidth = _slot * 0.56;
            var y = top + height;
            for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
            {
                var row = _rows[rowIndex];
                var seriesKey = $"ticker:{row.Ticker}";
                if (!IsSeriesVisible(seriesKey)) continue;
                var value = row.PlannedValues[monthIndex];
                if (value <= 0m) continue;
                var segmentHeight = Math.Max(2d, (double)(value / max) * height);
                y -= segmentHeight;
                var rect = new Rect(x, y, barWidth, segmentHeight);
                var status = row.ScheduleStatuses[monthIndex];
                var opacity = InteractionOpacity(row.Ticker, monthIndex + 1, status);
                var brush = DividendChartColorRegistry.SecurityBrush(row.Ticker,
                    status ?? DividendScheduleStatus.Expected, opacity);
                dc.DrawRoundedRectangle(brush, null, rect, 2, 2);
                AddHitTarget(rect, row.ToolTipForMonth(monthIndex + 1), row.Ticker, monthIndex + 1,
                    status, seriesKey);
            }
            var other = otherRows.Sum(row => row.PlannedValues[monthIndex]);
            if (other > 0m && IsSeriesVisible("ticker:OTHER"))
            {
                var segmentHeight = Math.Max(2d, (double)(other / max) * height);
                y -= segmentHeight;
                var rect = new Rect(x, y, barWidth, segmentHeight);
                dc.DrawRoundedRectangle(DividendChartColorRegistry.SecurityBrush("OTHER",
                    opacityMultiplier: InteractionOpacity(null, monthIndex + 1, null)), null, rect, 2, 2);
                var details = string.Join("\n", otherRows.Where(row => row.PlannedValues[monthIndex] > 0m)
                    .OrderByDescending(row => row.PlannedValues[monthIndex])
                    .Select(row => $"{row.Ticker} {row.PlannedValues[monthIndex]:N0}円"));
                AddHitTarget(rect, $"{monthIndex + 1}月 その他 {other:N0}円\n{details}",
                    month: monthIndex + 1, seriesKey: "ticker:OTHER");
            }
            DrawText(dc, $"{monthIndex + 1}月", 10, x - 2, top + height + 9, secondary);
        }
    }

    private static string FormatAxis(decimal value) => value >= 10_000m ? $"{value / 10_000m:N0}万" : $"{value:N0}";
    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendMonthlyCompositionChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
