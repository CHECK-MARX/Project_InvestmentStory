using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class ScenarioComparisonChartControl : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IEnumerable),
        typeof(ScenarioComparisonChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty TargetAmountProperty = DependencyProperty.Register(
        nameof(TargetAmount),
        typeof(decimal),
        typeof(ScenarioComparisonChartControl),
        new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage),
        typeof(string),
        typeof(ScenarioComparisonChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Color[] SeriesColors =
    [
        Color.FromRgb(34, 197, 94),
        Color.FromRgb(56, 189, 248),
        Color.FromRgb(167, 139, 250),
        Color.FromRgb(245, 158, 11)
    ];

    private INotifyCollectionChanged? _observedCollection;
    private IReadOnlyList<MutualFundScenarioChartSeriesViewModel> _renderedSeries = Array.Empty<MutualFundScenarioChartSeriesViewModel>();
    private Rect _plotRect;
    private double _step;
    private int? _hoverIndex;
    private readonly List<(Rect Bounds, MutualFundScenarioChartSeriesViewModel Series)> _legendHits = new();

    public IEnumerable? Series
    {
        get => (IEnumerable?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public decimal TargetAmount
    {
        get => (decimal)GetValue(TargetAmountProperty);
        set => SetValue(TargetAmountProperty, value);
    }

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        return new Size(width, 420);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 10 || bounds.Height <= 10)
        {
            return;
        }

        var surface = Brush("ElevatedSurfaceBrush", Color.FromRgb(30, 41, 59));
        var border = Brush("BorderBrush", Color.FromRgb(38, 52, 73));
        var text = Brush("PrimaryTextBrush", Color.FromRgb(229, 231, 235));
        var secondary = Brush("SecondaryTextBrush", Color.FromRgb(148, 163, 184));
        dc.DrawRoundedRectangle(surface, new Pen(border, 1), bounds, 8, 8);

        _renderedSeries = Series is null
            ? Array.Empty<MutualFundScenarioChartSeriesViewModel>()
            : Series.OfType<MutualFundScenarioChartSeriesViewModel>().ToList();
        var visible = _renderedSeries.Where(x => x.IsVisible && x.Points.Count > 0).ToList();
        if (visible.Count == 0)
        {
            DrawText(dc, string.IsNullOrWhiteSpace(StatusMessage) ? "表示対象のシナリオがありません。" : StatusMessage, bounds.TopLeft + new Vector(18, 24), secondary, 13);
            DrawLegend(dc, bounds, text);
            return;
        }

        _plotRect = new Rect(56, 50, Math.Max(80, bounds.Width - 82), Math.Max(120, bounds.Height - 92));
        var allValues = visible.SelectMany(x => x.Points.Select(p => p.MarketValueJpy)).ToList();
        if (TargetAmount > 0m)
        {
            allValues.Add(TargetAmount);
        }

        var minValue = 0m;
        var maxValue = Math.Max(1m, allValues.Max());
        var pad = (double)(maxValue - minValue) * 0.08;
        var min = (double)minValue;
        var max = (double)maxValue + pad;
        var count = visible.Max(x => x.Points.Count);
        _step = _plotRect.Width / Math.Max(1, count - 1);

        DrawGrid(dc, _plotRect, min, max, count, text, secondary, border);
        DrawTarget(dc, _plotRect, min, max, TargetAmount, secondary);
        for (var index = 0; index < visible.Count; index++)
        {
            DrawSeries(dc, visible[index], _plotRect, min, max, _step, new SolidColorBrush(SeriesColors[index % SeriesColors.Length]));
        }

        DrawHover(dc, visible, _plotRect, min, max, secondary);
        DrawLegend(dc, bounds, text);
        DrawText(dc, StatusMessage, new Point(16, 18), secondary, 12);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_plotRect.Contains(point) && _step > 0)
        {
            _hoverIndex = Math.Max(0, (int)Math.Round((point.X - _plotRect.Left) / _step));
            ToolTip = BuildToolTip(_hoverIndex.Value);
        }
        else
        {
            _hoverIndex = null;
            ToolTip = null;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = null;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var point = e.GetPosition(this);
        var hit = _legendHits.FirstOrDefault(x => x.Bounds.Contains(point));
        if (hit.Series is not null)
        {
            hit.Series.IsVisible = !hit.Series.IsVisible;
            InvalidateVisual();
        }
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScenarioComparisonChartControl)d;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged -= control.OnCollectionChanged;
        }

        control._observedCollection = e.NewValue as INotifyCollectionChanged;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged += control.OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private static void DrawGrid(DrawingContext dc, Rect rect, double min, double max, int count, Brush text, Brush secondary, Brush border)
    {
        var pen = new Pen(border, 1);
        dc.DrawRectangle(null, pen, rect);
        for (var i = 0; i <= 4; i++)
        {
            var y = rect.Top + rect.Height / 4 * i;
            dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
            var value = (decimal)(max - (max - min) / 4 * i);
            DrawText(dc, FormatJpy(value), new Point(rect.Right + 6, y - 8), secondary, 10);
        }

        if (count > 0)
        {
            DrawText(dc, "年月", new Point(rect.Left, rect.Bottom + 10), text, 10);
        }
    }

    private static void DrawTarget(DrawingContext dc, Rect rect, double min, double max, decimal target, Brush brush)
    {
        if (target <= 0m)
        {
            return;
        }

        var y = MapY(rect, min, max, target);
        var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        DrawText(dc, $"目標 {FormatJpy(target)}", new Point(rect.Left + 8, y - 18), brush, 11);
    }

    private static void DrawSeries(
        DrawingContext dc,
        MutualFundScenarioChartSeriesViewModel series,
        Rect rect,
        double min,
        double max,
        double step,
        Brush brush)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < series.Points.Count; i++)
            {
                var point = new Point(rect.Left + step * i, MapY(rect, min, max, series.Points[i].MarketValueJpy));
                if (i == 0)
                {
                    ctx.BeginFigure(point, false, false);
                }
                else
                {
                    ctx.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(brush, 2.2), geometry);
        if (series.TargetAchievementMonth is { } targetMonth)
        {
            var targetIndex = series.Points.ToList().FindIndex(x => x.YearMonth == targetMonth);
            if (targetIndex >= 0)
            {
                var marker = new Point(rect.Left + step * targetIndex, MapY(rect, min, max, series.Points[targetIndex].MarketValueJpy));
                dc.DrawEllipse(brush, null, marker, 4, 4);
            }
        }
    }

    private void DrawHover(DrawingContext dc, IReadOnlyList<MutualFundScenarioChartSeriesViewModel> visible, Rect rect, double min, double max, Brush brush)
    {
        if (_hoverIndex is not { } index || visible.Count == 0)
        {
            return;
        }

        var safeIndex = Math.Clamp(index, 0, visible.Max(x => x.Points.Count) - 1);
        var x = rect.Left + _step * safeIndex;
        var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dot };
        dc.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
        foreach (var series in visible)
        {
            if (series.Points.Count <= safeIndex)
            {
                continue;
            }

            var y = MapY(rect, min, max, series.Points[safeIndex].MarketValueJpy);
            dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }

    private void DrawLegend(DrawingContext dc, Rect bounds, Brush text)
    {
        _legendHits.Clear();
        var x = 16d;
        var y = bounds.Bottom - 28;
        for (var i = 0; i < _renderedSeries.Count; i++)
        {
            var series = _renderedSeries[i];
            var color = new SolidColorBrush(SeriesColors[i % SeriesColors.Length]);
            var itemRect = new Rect(x, y, 150, 20);
            _legendHits.Add((itemRect, series));
            dc.DrawRectangle(series.IsVisible ? color : Brushes.Transparent, new Pen(color, 1), new Rect(x, y + 5, 12, 8));
            DrawText(dc, $"{series.Name} {series.AnnualReturnRate}", new Point(x + 18, y), text, 11);
            x += 158;
        }
    }

    private string BuildToolTip(int index)
    {
        var visible = _renderedSeries.Where(x => x.IsVisible && x.Points.Count > index).ToList();
        if (visible.Count == 0)
        {
            return string.Empty;
        }

        var month = visible[0].Points[index].YearMonth;
        var lines = new List<string> { $"年月: {month:yyyy/MM}" };
        foreach (var series in visible)
        {
            var point = series.Points[index];
            lines.Add($"{series.Name}: {FormatJpy(point.MarketValueJpy)} / 達成率 {point.TargetAchievementRate:N1}%");
        }

        lines.Add($"目標金額: {FormatJpy(TargetAmount)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static double MapY(Rect rect, double min, double max, decimal value) =>
        rect.Bottom - ((double)value - min) / Math.Max(0.0001, max - min) * rect.Height;

    private static string FormatJpy(decimal value) => $"{value:N0}円";

    private static void DrawText(DrawingContext dc, string text, Point origin, Brush brush, double size, FontWeight? weight = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var pixelsPerDip = Application.Current?.MainWindow is { } window
            ? VisualTreeHelper.GetDpi(window).PixelsPerDip
            : 1d;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Yu Gothic UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip);
        dc.DrawText(formatted, origin);
    }

    private static Brush Brush(string resourceKey, Color fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
