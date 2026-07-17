using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.Controls;

public sealed class DividendMonthMapControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendMonthMapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanStockMonthlyRowViewModel> _rows = Array.Empty<DividendPlanStockMonthlyRowViewModel>();
    private double _left;
    private double _top;
    private double _cellWidth;
    private double _cellHeight;

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        BeginInteractiveRender();
        _rows = Items?.OfType<DividendPlanStockMonthlyRowViewModel>()
                    .Where(x => x.TotalValue > 0m).OrderByDescending(x => x.TotalValue).Take(12).ToList()
                ?? new List<DividendPlanStockMonthlyRowViewModel>();
        if (_rows.Count == 0)
        {
            DrawText(dc, "配当予定データがありません。", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        _left = Math.Clamp(ActualWidth * 0.18d, 78d, 150d);
        _top = 56d;
        _cellWidth = Math.Max(22d, (ActualWidth - _left - 10d) / 12d);
        _cellHeight = Math.Max(22d, (ActualHeight - _top - 8d) / _rows.Count);
        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var line = Brush("BorderBrush", Brushes.DimGray);

        DrawStatusLegend(dc, text);
        for (var month = 1; month <= 12; month++)
            DrawText(dc, $"{month}月", 10, _left + (month - 1) * _cellWidth + 4, 34, secondary);

        foreach (var (row, rowIndex) in _rows.Select((row, index) => (row, index)))
        {
            var y = _top + rowIndex * _cellHeight;
            DrawText(dc, row.Ticker, 11, 4, y + 4, text);
            dc.DrawLine(new Pen(line, 0.5), new Point(_left, y + _cellHeight / 2d),
                new Point(_left + _cellWidth * 12, y + _cellHeight / 2d));
            for (var monthIndex = 0; monthIndex < 12; monthIndex++)
            {
                var status = row.ScheduleStatuses[monthIndex];
                if (status is null) continue;
                var seriesKey = $"status:{status}";
                if (!IsSeriesVisible(seriesKey)) continue;
                var opacity = InteractionOpacity(row.Ticker, monthIndex + 1, status);
                var brush = DividendChartColorRegistry.StatusBrush(status.Value, opacity);
                var visual = DividendChartColorRegistry.ForStatus(status.Value);
                var center = new Point(_left + monthIndex * _cellWidth + _cellWidth / 2d, y + _cellHeight / 2d);
                var radius = row.PlannedValues[monthIndex] > 0m ? 6d : 4d;
                var pen = new Pen(new SolidColorBrush(visual.Color), 1.2);
                if (visual.DashPattern is not null) pen.DashStyle = new DashStyle(visual.DashPattern, 0);
                dc.DrawEllipse(brush, pen, center, radius, radius);
                AddHitTarget(new Rect(center.X - 10, center.Y - 10, 20, 20),
                    row.ToolTipForMonth(monthIndex + 1), row.Ticker, monthIndex + 1, status, seriesKey);
            }
        }
    }

    private void DrawStatusLegend(DrawingContext dc, Brush text)
    {
        var x = 4d;
        foreach (var visual in DividendChartColorRegistry.Legend)
        {
            var seriesKey = $"status:{visual.Status}";
            var center = new Point(x + 5, 9);
            dc.DrawEllipse(DividendChartColorRegistry.StatusBrush(visual.Status,
                IsSeriesVisible(seriesKey) ? 1d : .2d), null, center, 5, 5);
            DrawText(dc, visual.DisplayName, 9, x + 14, 2, text);
            var width = 18 + Math.Max(45, visual.DisplayName.Length * 10);
            AddHitTarget(new Rect(x, 0, width, 20), $"{visual.DisplayName}の表示を切り替えます。",
                status: visual.Status, seriesKey: seriesKey, isLegend: true);
            x += width + 5;
            if (x > ActualWidth - 100) break;
        }
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendMonthMapControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
