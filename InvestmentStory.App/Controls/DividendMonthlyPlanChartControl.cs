using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendMonthlyPlanChartControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendMonthlyPlanChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanMonthlyRowViewModel> _rows = Array.Empty<DividendPlanMonthlyRowViewModel>();
    private double _left;
    private double _top;
    private double _chartWidth;
    private double _slotWidth;

    public DividendMonthlyPlanChartControl()
    {
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => ToolTip = null;
    }

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _rows = Items?.OfType<DividendPlanMonthlyRowViewModel>().OrderBy(x => x.Month).ToList()
                ?? new List<DividendPlanMonthlyRowViewModel>();
        if (_rows.Count == 0 || ActualWidth < 320 || ActualHeight < 180)
        {
            DrawText(dc, "月別配当データがありません。", 14, 18, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var current = Brush("AccentBlueBrush", Brushes.DeepSkyBlue);
        var add = Brush("DividendBrush", Brushes.MediumPurple);
        var newly = Brush("WarningBrush", Brushes.Orange);
        var cumulative = Brush("AccentCyanBrush", Brushes.Cyan);
        var target = Brush("PrimaryTextBrush", Brushes.White);
        var grid = Brush("BorderBrush", Brushes.DimGray);

        _left = 64d;
        _top = 52d;
        var right = 78d;
        var bottom = 42d;
        _chartWidth = Math.Max(120d, ActualWidth - _left - right);
        var chartHeight = Math.Max(90d, ActualHeight - _top - bottom);
        _slotWidth = _chartWidth / _rows.Count;
        var monthlyMax = Math.Max(1m, _rows.Max(x => Math.Max(x.PlannedValue, x.TargetValue)) * 1.15m);
        var cumulativeMax = Math.Max(1m, _rows.Max(x => x.PlannedCumulativeValue) * 1.08m);

        DrawLegend(dc, current, add, newly, cumulative, target, text);
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = _top + chartHeight - chartHeight * tick / 4d;
            dc.DrawLine(new Pen(grid, 0.7), new Point(_left, y), new Point(_left + _chartWidth, y));
            DrawText(dc, FormatAxis(monthlyMax * tick / 4m), 10, 2, y - 7, secondary);
            DrawText(dc, FormatAxis(cumulativeMax * tick / 4m), 10, _left + _chartWidth + 8, y - 7, secondary);
        }

        var targetY = ValueY(_rows.Max(x => x.TargetValue), monthlyMax, _top, chartHeight);
        dc.DrawLine(new Pen(target, 1.1) { DashStyle = DashStyles.Dash },
            new Point(_left, targetY), new Point(_left + _chartWidth, targetY));

        var cumulativePoints = new List<Point>();
        foreach (var (row, index) in _rows.Select((row, index) => (row, index)))
        {
            var centerX = _left + _slotWidth * index + _slotWidth / 2d;
            var barWidth = Math.Clamp(_slotWidth * 0.56d, 9d, 38d);
            var x = centerX - barWidth / 2d;
            var y = _top + chartHeight;
            y = DrawStack(dc, current, x, y, barWidth, chartHeight, monthlyMax, row.CurrentValue);
            y = DrawStack(dc, add, x, y, barWidth, chartHeight, monthlyMax, row.ExistingAdditionalValue);
            DrawStack(dc, newly, x, y, barWidth, chartHeight, monthlyMax, row.NewPurchaseValue);
            DrawText(dc, $"{row.Month}月", 11, centerX - 13, _top + chartHeight + 10, secondary);
            cumulativePoints.Add(new Point(centerX,
                ValueY(row.PlannedCumulativeValue, cumulativeMax, _top, chartHeight)));
        }

        if (cumulativePoints.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(cumulativePoints[0], false, false);
                context.PolyLineTo(cumulativePoints.Skip(1).ToList(), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(cumulative, 2.6), geometry);
            foreach (var point in cumulativePoints)
            {
                dc.DrawEllipse(cumulative, new Pen(Brush("SurfaceBackgroundBrush", Brushes.Navy), 1.2), point, 3.5, 3.5);
            }
        }
    }

    private static double DrawStack(
        DrawingContext dc, Brush brush, double x, double y, double width,
        double chartHeight, decimal max, decimal value)
    {
        if (value <= 0m) return y;
        var height = Math.Max(2d, (double)(value / max) * chartHeight);
        var nextY = y - height;
        dc.DrawRoundedRectangle(brush, null, new Rect(x, nextY, width, height), 2, 2);
        return nextY;
    }

    private void DrawLegend(
        DrawingContext dc, Brush current, Brush add, Brush newly, Brush cumulative, Brush target, Brush text)
    {
        var values = new[]
        {
            ("現在保有", current, false), ("買い増し", add, false), ("新規購入", newly, false),
            ("年間累計", cumulative, true), ("月目標", target, true)
        };
        var x = 4d;
        foreach (var (label, brush, line) in values)
        {
            if (line)
                dc.DrawLine(new Pen(brush, 2) { DashStyle = label == "月目標" ? DashStyles.Dash : DashStyles.Solid },
                    new Point(x, 17), new Point(x + 16, 17));
            else
                dc.DrawRoundedRectangle(brush, null, new Rect(x, 10, 14, 14), 3, 3);
            DrawText(dc, label, 11, x + 20, 8, text);
            x += label.Length * 12 + 42;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_rows.Count == 0 || _slotWidth <= 0d) return;
        var point = e.GetPosition(this);
        var index = (int)((point.X - _left) / _slotWidth);
        ToolTip = index >= 0 && index < _rows.Count ? _rows[index].ToolTipText : null;
    }

    private static double ValueY(decimal value, decimal max, double top, double height) =>
        top + height - (double)(Math.Max(0m, value) / max) * height;

    private static string FormatAxis(decimal value) => value switch
    {
        >= 100_000_000m => $"{value / 100_000_000m:N1}億",
        >= 10_000m => $"{value / 10_000m:N0}万",
        _ => $"{value:N0}"
    };

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendMonthlyPlanChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
