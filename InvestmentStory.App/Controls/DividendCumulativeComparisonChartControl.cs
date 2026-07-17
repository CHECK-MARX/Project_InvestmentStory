using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendCumulativeComparisonChartControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendCumulativeComparisonChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanMonthlyRowViewModel> _rows = Array.Empty<DividendPlanMonthlyRowViewModel>();
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
        _rows = Items?.OfType<DividendPlanMonthlyRowViewModel>().OrderBy(x => x.Month).ToList()
                ?? new List<DividendPlanMonthlyRowViewModel>();
        if (_rows.Count == 0 || ActualWidth < 260 || ActualHeight < 160)
        {
            DrawText(dc, "累計配当データがありません。", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var current = Brush("AccentBlueBrush", Brushes.DeepSkyBlue);
        var planned = Brush("DividendBrush", Brushes.MediumPurple);
        var grid = Brush("BorderBrush", Brushes.DimGray);
        var isUnchanged = _rows.All(row => row.CurrentCumulativeValue == row.PlannedCumulativeValue);
        _left = 62d;
        var top = 42d;
        var right = 24d;
        var bottom = 36d;
        var width = Math.Max(100d, ActualWidth - _left - right);
        var height = Math.Max(80d, ActualHeight - top - bottom);
        _slot = width / Math.Max(1, _rows.Count - 1);
        var max = Math.Max(1m, _rows.Max(x => Math.Max(x.CurrentCumulativeValue, x.PlannedCumulativeValue)) * 1.08m);

        var currentLegend = WithOpacity(current, IsSeriesVisible("comparison:current") ? 1d : .22d);
        dc.DrawLine(new Pen(currentLegend, 2.2), new Point(8, 16), new Point(28, 16));
        DrawText(dc, isUnchanged ? "現在＝購入後" : "現在", 11, 34, 8, text);
        AddHitTarget(new Rect(4, 4, 84, 24), "現在累計の表示を切り替えます。",
            seriesKey: "comparison:current", isLegend: true);
        if (!isUnchanged)
        {
            var plannedLegend = WithOpacity(planned, IsSeriesVisible("comparison:planned") ? 1d : .22d);
            dc.DrawLine(new Pen(plannedLegend, 2.6), new Point(90, 16), new Point(110, 16));
            DrawText(dc, "購入後", 11, 116, 8, text);
            AddHitTarget(new Rect(88, 4, 94, 24), "購入後累計の表示を切り替えます。",
                seriesKey: "comparison:planned", isLegend: true);
        }
        else
        {
            DrawText(dc, "購入計画なしのため同額", 11, Math.Max(150d, ActualWidth - 178d), 8, secondary);
        }

        for (var tick = 0; tick <= 4; tick++)
        {
            var y = top + height - height * tick / 4d;
            dc.DrawLine(new Pen(grid, 0.7), new Point(_left, y), new Point(_left + width, y));
            DrawText(dc, FormatAxis(max * tick / 4m), 10, 2, y - 7, secondary);
        }

        var currentPoints = new List<Point>();
        var plannedPoints = new List<Point>();
        foreach (var (row, index) in _rows.Select((row, index) => (row, index)))
        {
            var x = _left + _slot * index;
            currentPoints.Add(new Point(x, Y(row.CurrentCumulativeValue, max, top, height)));
            plannedPoints.Add(new Point(x, Y(row.PlannedCumulativeValue, max, top, height)));
            DrawText(dc, $"{row.Month}月", 10, x - 12, top + height + 8, secondary);
        }

        if (isUnchanged && IsSeriesVisible("comparison:current"))
        {
            DrawArea(dc, currentPoints, top + height, Color.FromArgb(32, 56, 189, 248));
            DrawLine(dc, currentPoints, new Pen(current, 2.6));
            foreach (var point in currentPoints)
                dc.DrawEllipse(current, null, point, 3.2, 3.2);
        }
        else if (!isUnchanged)
        {
            if (IsSeriesVisible("comparison:planned"))
            {
                DrawArea(dc, plannedPoints, top + height, Color.FromArgb(40, 167, 139, 250));
                DrawLine(dc, plannedPoints, new Pen(planned, 2.8));
                foreach (var point in plannedPoints) dc.DrawEllipse(planned, null, point, 3.2, 3.2);
            }
            if (IsSeriesVisible("comparison:current")) DrawLine(dc, currentPoints, new Pen(current, 2.1));
        }
        foreach (var (row, index) in _rows.Select((row, index) => (row, index)))
        {
            var x = _left + _slot * index;
            AddHitTarget(new Rect(x - Math.Max(8d, _slot / 2d), top, Math.Max(16d, _slot), height),
                row.ToolTipText, month: row.Month, seriesKey: "comparison:month");
        }
    }

    private static void DrawLine(DrawingContext dc, IReadOnlyList<Point> points, Pen pen)
    {
        if (points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawArea(DrawingContext dc, IReadOnlyList<Point> points, double baseline, Color color)
    {
        if (points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, baseline), true, true);
            context.LineTo(points[0], true, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, false);
            context.LineTo(new Point(points[^1].X, baseline), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }

    private static double Y(decimal value, decimal max, double top, double height) =>
        top + height - (double)(Math.Max(0m, value) / max) * height;

    private static string FormatAxis(decimal value) => value >= 10_000m ? $"{value / 10_000m:N0}万" : $"{value:N0}";
    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendCumulativeComparisonChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
