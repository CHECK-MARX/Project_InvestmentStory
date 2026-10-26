using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

/// <summary>Monthly actual, forecast, prior-year actual and target on one yen axis.</summary>
public sealed class MonthlyDividendLineChartControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(MonthlyDividendLineChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<MonthlyDividendBreakdownRowViewModel> _rows = Array.Empty<MonthlyDividendBreakdownRowViewModel>();
    private Rect _plot;
    private decimal _axisMax;
    private int _hoverIndex = -1;

    public MonthlyDividendLineChartControl()
    {
        Cursor = Cursors.Cross;
        Focusable = true;
        MouseDown += (_, _) => Focus();
        KeyDown += HandleKeyDown;
        MouseMove += HandleMouseMove;
        MouseLeave += (_, _) => ClearHover();
    }

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        _rows = Items?.OfType<MonthlyDividendBreakdownRowViewModel>()
            .OrderBy(x => x.Month).ToArray() ?? Array.Empty<MonthlyDividendBreakdownRowViewModel>();
        if (_rows.Count == 0 || ActualWidth < 280 || ActualHeight < 170)
        {
            DrawText(dc, "月別配当データがありません。", 12, 12, 12,
                ResourceBrush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        _plot = new Rect(66, 12, ActualWidth - 84, ActualHeight - 48);
        var maxValue = _rows.Max(x => Math.Max(Math.Max(x.ActualJpy, x.ForecastJpy),
            Math.Max(x.PreviousYearActualJpy, x.MonthlyGoalJpy)));
        var step = NiceStep(Math.Max(1000m, maxValue) / 5m);
        _axisMax = step * Math.Max(1m, decimal.Ceiling(maxValue / step));

        var secondary = ResourceBrush("SecondaryTextBrush", Brushes.Gray);
        var grid = ResourceBrush("BorderBrush", Brushes.DimGray);
        var actual = ResourceBrush("AccentBlueBrush", Brushes.DeepSkyBlue);
        var forecast = ResourceBrush("DividendBrush", Brushes.MediumPurple);
        var previous = ResourceBrush("NeutralBrush", Brushes.SlateGray);
        var goal = ResourceBrush("WarningBrush", Brushes.Orange);

        for (var tick = 0; tick <= 5; tick++)
        {
            var value = _axisMax * tick / 5m;
            var y = Y(value);
            dc.DrawLine(new Pen(grid, .7), new Point(_plot.Left, y), new Point(_plot.Right, y));
            DrawText(dc, FormatAxis(value), 10, 2, y - 7, secondary);
        }

        DrawSeries(dc, x => x.PreviousYearActualJpy, previous, 1.8, null);
        DrawSeries(dc, x => x.MonthlyGoalJpy, goal, 1.8, DashStyles.Dash);
        DrawSeries(dc, x => x.ForecastJpy, forecast, 2.2, DashStyles.Dash);
        DrawSeries(dc, x => x.ActualJpy, actual, 2.6, null, IsActualPeriod);

        for (var i = 0; i < _rows.Count; i++)
        {
            var label = MakeText(_rows[i].Label, 10, secondary);
            dc.DrawText(label, new Point(X(i) - label.Width / 2d, _plot.Bottom + 10));
        }

        if (_hoverIndex >= _rows.Count) _hoverIndex = -1;
        if (_hoverIndex < 0) return;

        var row = _rows[_hoverIndex];
        var hoverX = X(_hoverIndex);
        dc.DrawLine(new Pen(secondary, 1) { DashStyle = DashStyles.Dot },
            new Point(hoverX, _plot.Top), new Point(hoverX, _plot.Bottom));
        DrawHoverPoint(dc, hoverX, row.ForecastJpy, forecast);
        DrawHoverPoint(dc, hoverX, row.PreviousYearActualJpy, previous);
        DrawHoverPoint(dc, hoverX, row.MonthlyGoalJpy, goal);
        if (IsActualPeriod(row)) DrawHoverPoint(dc, hoverX, row.ActualJpy, actual);
        DrawHoverCard(dc, row, hoverX, actual, forecast, previous, goal);
    }

    private void DrawSeries(DrawingContext dc, Func<MonthlyDividendBreakdownRowViewModel, decimal> getValue,
        Brush brush, double thickness, DashStyle? dash, Func<MonthlyDividendBreakdownRowViewModel, bool>? include = null)
    {
        var points = _rows.Select((row, index) => (row, index))
            .Where(x => include?.Invoke(x.row) ?? true)
            .Select(x => new Point(X(x.index), Y(getValue(x.row))))
            .ToArray();
        if (points.Length > 1)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToArray(), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness) { DashStyle = dash ?? DashStyles.Solid }, geometry);
        }
        foreach (var point in points.Where(x => x.Y < _plot.Bottom - .5))
            dc.DrawEllipse(brush, null, point, 3, 3);
    }

    private void DrawHoverPoint(DrawingContext dc, double x, decimal value, Brush brush) =>
        dc.DrawEllipse(brush, new Pen(ResourceBrush("ElevatedSurfaceBrush", Brushes.Black), 1.5),
            new Point(x, Y(value)), 5, 5);

    private void DrawHoverCard(DrawingContext dc, MonthlyDividendBreakdownRowViewModel row, double x,
        Brush actual, Brush forecast, Brush previous, Brush goal)
    {
        const double width = 224;
        const double height = 148;
        if (_plot.Width < width + 16) return;
        var left = x + 12 + width < _plot.Right ? x + 12 : x - width - 12;
        left = Math.Clamp(left, _plot.Left + 4, _plot.Right - width - 4);
        var top = _plot.Top + 6;
        dc.DrawRoundedRectangle(ResourceBrush("ElevatedSurfaceBrush", Brushes.Black),
            new Pen(ResourceBrush("BorderBrush", Brushes.DimGray), 1),
            new Rect(left, top, width, height), 7, 7);

        var text = ResourceBrush("PrimaryTextBrush", Brushes.White);
        DrawText(dc, $"{row.Year}年 {row.Label}", 12, left + 12, top + 8, text);
        DrawCardValue(dc, "今年実績", IsActualPeriod(row) ? row.Actual : "—", actual, left, top + 32, text);
        DrawCardValue(dc, "入金予定", row.Planned, forecast, left, top + 53, text);
        DrawCardValue(dc, "着地見込み", row.Forecast, forecast, left, top + 74, text);
        DrawCardValue(dc, "前年実績", row.PreviousYear, previous, left, top + 95, text);
        DrawCardValue(dc, "月間目標", row.Goal, goal, left, top + 116, text);
    }

    private static void DrawCardValue(DrawingContext dc, string label, string value,
        Brush marker, double left, double top, Brush text)
    {
        dc.DrawEllipse(marker, null, new Point(left + 16, top + 7), 3, 3);
        DrawText(dc, label, 11, left + 27, top, text);
        var amount = MakeText(value, 11, text);
        dc.DrawText(amount, new Point(left + 212 - amount.Width, top));
    }

    private void HandleMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_rows.Count == 0 || !_plot.Contains(position))
        {
            ClearHover();
            return;
        }
        var closest = _rows.Count == 1 ? 0 : Math.Clamp(
            (int)Math.Round((position.X - _plot.Left) / _plot.Width * (_rows.Count - 1)),
            0, _rows.Count - 1);
        if (closest == _hoverIndex) return;
        _hoverIndex = closest;
        InvalidateVisual();
    }

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (_rows.Count == 0) return;
        _hoverIndex = e.Key switch
        {
            Key.Left => Math.Max(0, _hoverIndex < 0 ? 0 : _hoverIndex - 1),
            Key.Right => Math.Min(_rows.Count - 1, _hoverIndex + 1),
            Key.Home => 0,
            Key.End => _rows.Count - 1,
            _ => _hoverIndex
        };
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    private void ClearHover()
    {
        if (_hoverIndex < 0) return;
        _hoverIndex = -1;
        InvalidateVisual();
    }

    private double X(int index) => _rows.Count == 1 ? _plot.Left + _plot.Width / 2d
        : _plot.Left + _plot.Width * index / (_rows.Count - 1d);

    private double Y(decimal value) => _plot.Bottom - (double)(Math.Max(0m, value) / _axisMax) * _plot.Height;

    private static bool IsActualPeriod(MonthlyDividendBreakdownRowViewModel row) =>
        row.Year < DateTime.Today.Year || row.Year == DateTime.Today.Year && row.Month <= DateTime.Today.Month;

    private static decimal NiceStep(decimal value)
    {
        var power = (decimal)Math.Pow(10d, Math.Floor(Math.Log10((double)value)));
        var factor = value / power;
        return (factor <= 1m ? 1m : factor <= 2m ? 2m : factor <= 5m ? 5m : 10m) * power;
    }

    private static string FormatAxis(decimal value) => value switch
    {
        >= 100_000_000m => $"{value / 100_000_000m:N1}億",
        >= 10_000m => $"{value / 10_000m:N0}万",
        _ => $"{value:N0}"
    };

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static FormattedText MakeText(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0);

    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(MakeText(value, size, brush), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MonthlyDividendLineChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.CollectionChanged;
        control.ClearHover();
        control.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
