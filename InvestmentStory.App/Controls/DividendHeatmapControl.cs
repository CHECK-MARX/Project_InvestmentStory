using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.Controls;

public sealed class DividendHeatmapControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendHeatmapControl),
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
            DrawText(dc, "配当月データがありません。", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        _left = Math.Clamp(ActualWidth * 0.18d, 78d, 150d);
        _top = 56d;
        _cellWidth = Math.Max(22d, (ActualWidth - _left - 10d) / 12d);
        _cellHeight = Math.Max(22d, (ActualHeight - _top - 8d) / _rows.Count);
        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var border = Brush("BorderBrush", Brushes.DimGray);
        var max = Math.Max(1m, _rows.SelectMany(x => x.PlannedValues).Max());

        DrawScaleLegend(dc, max, secondary);
        for (var month = 1; month <= 12; month++)
            DrawText(dc, $"{month}月", 10, _left + (month - 1) * _cellWidth + 4, 34, secondary);

        foreach (var (row, rowIndex) in _rows.Select((row, index) => (row, index)))
        {
            var y = _top + rowIndex * _cellHeight;
            DrawText(dc, row.Ticker, 11, 4, y + 4, text);
            for (var monthIndex = 0; monthIndex < 12; monthIndex++)
            {
                var value = row.PlannedValues[monthIndex];
                var status = row.ScheduleStatuses[monthIndex];
                var rect = new Rect(_left + monthIndex * _cellWidth + 2, y + 2, _cellWidth - 4, _cellHeight - 4);
                if (status is null && value <= 0m)
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(18, 100, 116, 139)),
                        new Pen(border, .35), rect, 3, 3);
                    continue;
                }
                var seriesKey = status is null ? "status:none" : $"status:{status}";
                if (!IsSeriesVisible(seriesKey)) continue;
                var opacity = InteractionOpacity(row.Ticker, monthIndex + 1, status);
                var fill = DividendChartColorRegistry.HeatmapBrush(value, max, status);
                fill.Opacity *= opacity;
                var pen = status is null
                    ? new Pen(border, .5)
                    : new Pen(DividendChartColorRegistry.StatusBrush(status.Value, opacity), 1.2);
                if (status is not null && DividendChartColorRegistry.ForStatus(status.Value).DashPattern is { } dash)
                    pen.DashStyle = new DashStyle(dash, 0);
                dc.DrawRoundedRectangle(fill, pen, rect, 3, 3);
                AddHitTarget(rect, row.ToolTipForMonth(monthIndex + 1), row.Ticker,
                    monthIndex + 1, status, seriesKey);
            }
        }
    }

    private void DrawScaleLegend(DrawingContext dc, decimal max, Brush secondary)
    {
        DrawText(dc, "金額", 9, 4, 2, secondary);
        var color = DividendChartColorRegistry.ForStatus(DividendScheduleStatus.Expected).Color;
        for (var index = 0; index < 5; index++)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(35 + index * 48), color.R, color.G, color.B)),
                null, new Rect(34 + index * 20, 2, 20, 12));
        }
        DrawText(dc, "0", 9, 34, 17, secondary);
        DrawText(dc, $"{max:N0}円", 9, 104, 17, secondary);
        DrawText(dc, "塗り=金額 / 枠=状態 / 空欄=配当なし", 9, 190, 5, secondary);
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendHeatmapControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
