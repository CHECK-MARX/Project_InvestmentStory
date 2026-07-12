using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class PriceChartControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IEnumerable),
        typeof(PriceChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

    public static readonly DependencyProperty CurrentPriceProperty = DependencyProperty.Register(
        nameof(CurrentPrice),
        typeof(decimal),
        typeof(PriceChartControl),
        new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AverageCostProperty = DependencyProperty.Register(
        nameof(AverageCost),
        typeof(decimal),
        typeof(PriceChartControl),
        new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrencyProperty = DependencyProperty.Register(
        nameof(Currency),
        typeof(string),
        typeof(PriceChartControl),
        new FrameworkPropertyMetadata("JPY", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage),
        typeof(string),
        typeof(PriceChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineOnlyProperty = DependencyProperty.Register(
        nameof(LineOnly),
        typeof(bool),
        typeof(PriceChartControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _observedCollection;

    public IEnumerable? Points
    {
        get => (IEnumerable?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public decimal CurrentPrice
    {
        get => (decimal)GetValue(CurrentPriceProperty);
        set => SetValue(CurrentPriceProperty, value);
    }

    public decimal AverageCost
    {
        get => (decimal)GetValue(AverageCostProperty);
        set => SetValue(AverageCostProperty, value);
    }

    public string Currency
    {
        get => (string)GetValue(CurrencyProperty);
        set => SetValue(CurrencyProperty, value);
    }

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    public bool LineOnly
    {
        get => (bool)GetValue(LineOnlyProperty);
        set => SetValue(LineOnlyProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        return new Size(width, 440);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 10 || bounds.Height <= 10)
        {
            return;
        }

        var surface = Brush("ElevatedSurfaceBrush", Color.FromRgb(30, 41, 59));
        var border = Brush("BorderBrush", Color.FromRgb(38, 52, 73));
        var text = Brush("PrimaryTextBrush", Color.FromRgb(229, 231, 235));
        var secondary = Brush("SecondaryTextBrush", Color.FromRgb(148, 163, 184));
        drawingContext.DrawRoundedRectangle(surface, new Pen(border, 1), bounds, 10, 10);

        var points = Points?.OfType<PriceChartPointViewModel>().OrderBy(x => x.Date).ToList() ?? new List<PriceChartPointViewModel>();
        if (points.Count < 2)
        {
            DrawCenteredText(drawingContext, string.IsNullOrWhiteSpace(StatusMessage) ? "チャート履歴データがありません。" : StatusMessage, bounds, secondary, 14);
            return;
        }

        var padding = 14d;
        var axisWidth = 56d;
        var headerHeight = 42d;
        var volumeHeight = LineOnly ? 28d : Math.Max(72d, bounds.Height * 0.22);
        var footerHeight = 26d;
        var priceRect = new Rect(
            padding,
            headerHeight,
            Math.Max(10, bounds.Width - padding * 2 - axisWidth),
            Math.Max(80, bounds.Height - headerHeight - volumeHeight - footerHeight - padding));
        var volumeRect = new Rect(priceRect.Left, priceRect.Bottom + 10, priceRect.Width, volumeHeight - 14);

        var priceMin = points.Min(x => x.Low);
        var priceMax = points.Max(x => x.High);
        if (CurrentPrice > 0m)
        {
            priceMin = Math.Min(priceMin, CurrentPrice);
            priceMax = Math.Max(priceMax, CurrentPrice);
        }

        if (AverageCost > 0m)
        {
            priceMin = Math.Min(priceMin, AverageCost);
            priceMax = Math.Max(priceMax, AverageCost);
        }

        var priceRange = Math.Max(0.0001, (double)(priceMax - priceMin));
        var pricePad = priceRange * 0.08;
        var min = (double)priceMin - pricePad;
        var max = (double)priceMax + pricePad;
        var maxVolume = Math.Max(1d, (double)points.Max(x => x.Volume));
        var step = priceRect.Width / Math.Max(1, points.Count - 1);
        var candleWidth = Math.Max(2, Math.Min(10, step * 0.62));

        DrawGrid(drawingContext, priceRect, volumeRect, min, max, points, text, secondary, border);
        DrawReferenceLine(drawingContext, priceRect, min, max, AverageCost, "取得単価", Brush("AccentBlueBrush", Color.FromRgb(56, 189, 248)), dashed: false);
        DrawReferenceLine(drawingContext, priceRect, min, max, CurrentPrice, "現在値", secondary, dashed: true);
        if (LineOnly)
        {
            DrawCloseLine(drawingContext, points, priceRect, min, max, step, Brush("AccentBlueBrush", Color.FromRgb(56, 189, 248)), 2.4);
        }
        else
        {
            DrawVolume(drawingContext, points, volumeRect, maxVolume, step, candleWidth);
            DrawCandles(drawingContext, points, priceRect, min, max, step, candleWidth);
        }
        DrawMovingAverage(drawingContext, points, priceRect, min, max, step, x => x.MovingAverage5, Brush("ProfitBrush", Color.FromRgb(34, 197, 94)), 2.0);
        DrawMovingAverage(drawingContext, points, priceRect, min, max, step, x => x.MovingAverage25, Brush("DividendBrush", Color.FromRgb(167, 139, 250)), 2.0);
        DrawHeader(drawingContext, points, bounds, text, secondary);
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PriceChartControl)d;
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

    private void DrawHeader(DrawingContext dc, IReadOnlyList<PriceChartPointViewModel> points, Rect bounds, Brush text, Brush secondary)
    {
        var latest = points[^1];
        var ma5 = latest.MovingAverage5 is null ? "-" : FormatPrice(latest.MovingAverage5.Value);
        var ma25 = latest.MovingAverage25 is null ? "-" : FormatPrice(latest.MovingAverage25.Value);
        var title = LineOnly ? "基準価額 日次ライン" : "日足 ローソク";
        DrawText(dc, $"{title}  {points[0].Date:yyyy/MM/dd} - {latest.Date:yyyy/MM/dd}", new Point(14, 12), text, 13);
        var latestText = LineOnly
            ? $"最新 {FormatPrice(latest.Close)}"
            : $"始 {FormatPrice(latest.Open)}  高 {FormatPrice(latest.High)}  安 {FormatPrice(latest.Low)}  終 {FormatPrice(latest.Close)}";
        DrawText(dc, latestText, new Point(320, 12), secondary, 12);
        DrawText(dc, $"移動平均 5: {ma5}   25: {ma25}", new Point(14, 28), secondary, 11);
    }

    private void DrawGrid(
        DrawingContext dc,
        Rect priceRect,
        Rect volumeRect,
        double min,
        double max,
        IReadOnlyList<PriceChartPointViewModel> points,
        Brush text,
        Brush secondary,
        Brush border)
    {
        var gridPen = new Pen(border, 1);
        var labelBrush = secondary;
        for (var i = 0; i <= 5; i++)
        {
            var y = priceRect.Top + priceRect.Height / 5 * i;
            dc.DrawLine(gridPen, new Point(priceRect.Left, y), new Point(priceRect.Right, y));
            var price = max - (max - min) / 5 * i;
            DrawText(dc, FormatPrice((decimal)price), new Point(priceRect.Right + 8, y - 8), labelBrush, 11);
        }

        for (var i = 0; i <= 6; i++)
        {
            var x = priceRect.Left + priceRect.Width / 6 * i;
            dc.DrawLine(gridPen, new Point(x, priceRect.Top), new Point(x, volumeRect.Bottom));
            var index = Math.Clamp((int)Math.Round((points.Count - 1) / 6d * i), 0, points.Count - 1);
            DrawText(dc, points[index].Date.ToString("MM/dd"), new Point(x - 16, volumeRect.Bottom + 4), labelBrush, 10);
        }

        dc.DrawRectangle(null, new Pen(border, 1.2), priceRect);
        if (!LineOnly)
        {
            dc.DrawLine(new Pen(border, 1), new Point(volumeRect.Left, volumeRect.Top), new Point(volumeRect.Right, volumeRect.Top));
            DrawText(dc, "出来高", new Point(volumeRect.Left, volumeRect.Top + 2), text, 11);
        }
    }

    private void DrawReferenceLine(DrawingContext dc, Rect rect, double min, double max, decimal value, string label, Brush brush, bool dashed)
    {
        if (value <= 0m)
        {
            return;
        }

        var y = MapY((double)value, rect, min, max);
        if (y < rect.Top || y > rect.Bottom)
        {
            return;
        }

        var pen = new Pen(brush, 1.4);
        if (dashed)
        {
            pen.DashStyle = DashStyles.Dash;
        }

        dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        DrawText(dc, $"{label} {FormatPrice(value)}", new Point(rect.Right - 128, y - 16), brush, 10);
    }

    private void DrawVolume(DrawingContext dc, IReadOnlyList<PriceChartPointViewModel> points, Rect rect, double maxVolume, double step, double candleWidth)
    {
        var brush = new SolidColorBrush(Color.FromRgb(202, 138, 4));
        for (var i = 0; i < points.Count; i++)
        {
            var height = maxVolume <= 0 ? 0 : (double)points[i].Volume / maxVolume * rect.Height;
            var x = rect.Left + step * i - candleWidth / 2;
            dc.DrawRectangle(brush, null, new Rect(x, rect.Bottom - height, candleWidth, height));
        }
    }

    private void DrawCandles(DrawingContext dc, IReadOnlyList<PriceChartPointViewModel> points, Rect rect, double min, double max, double step, double candleWidth)
    {
        var profitBrush = Brush("ProfitBrush", Color.FromRgb(34, 197, 94));
        var lossBrush = Brush("LossBrush", Color.FromRgb(239, 68, 68));
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var x = rect.Left + step * i;
            var highY = MapY((double)point.High, rect, min, max);
            var lowY = MapY((double)point.Low, rect, min, max);
            var openY = MapY((double)point.Open, rect, min, max);
            var closeY = MapY((double)point.Close, rect, min, max);
            var isUp = point.Close >= point.Open;
            var candleBrush = isUp ? profitBrush : lossBrush;
            var pen = new Pen(candleBrush, 1.2);
            dc.DrawLine(pen, new Point(x, highY), new Point(x, lowY));
            var top = Math.Min(openY, closeY);
            var height = Math.Max(1.5, Math.Abs(openY - closeY));
            dc.DrawRectangle(candleBrush, pen, new Rect(x - candleWidth / 2, top, candleWidth, height));
        }
    }

    private void DrawCloseLine(
        DrawingContext dc,
        IReadOnlyList<PriceChartPointViewModel> points,
        Rect rect,
        double min,
        double max,
        double step,
        Brush brush,
        double thickness)
    {
        var pen = new Pen(brush, thickness);
        var fill = new SolidColorBrush(Color.FromArgb(26, 56, 189, 248));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < points.Count; i++)
            {
                var point = new Point(rect.Left + step * i, MapY((double)points[i].Close, rect, min, max));
                if (i == 0)
                {
                    context.BeginFigure(point, isFilled: false, isClosed: false);
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: true);
                }
            }
        }
        geometry.Freeze();

        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            var first = new Point(rect.Left, MapY((double)points[0].Close, rect, min, max));
            context.BeginFigure(new Point(first.X, rect.Bottom), isFilled: true, isClosed: true);
            context.LineTo(first, isStroked: true, isSmoothJoin: true);
            for (var i = 1; i < points.Count; i++)
            {
                context.LineTo(new Point(rect.Left + step * i, MapY((double)points[i].Close, rect, min, max)), isStroked: true, isSmoothJoin: true);
            }
            context.LineTo(new Point(rect.Right, rect.Bottom), isStroked: true, isSmoothJoin: true);
        }
        area.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawMovingAverage(
        DrawingContext dc,
        IReadOnlyList<PriceChartPointViewModel> points,
        Rect rect,
        double min,
        double max,
        double step,
        Func<PriceChartPointViewModel, decimal?> selector,
        Brush brush,
        double thickness)
    {
        var pen = new Pen(brush, thickness);
        Point? previous = null;
        for (var i = 0; i < points.Count; i++)
        {
            var value = selector(points[i]);
            if (value is null)
            {
                previous = null;
                continue;
            }

            var current = new Point(rect.Left + step * i, MapY((double)value.Value, rect, min, max));
            if (previous is not null)
            {
                dc.DrawLine(pen, previous.Value, current);
            }

            previous = current;
        }
    }

    private static double MapY(double value, Rect rect, double min, double max)
    {
        if (max <= min)
        {
            return rect.Bottom;
        }

        return rect.Bottom - (value - min) / (max - min) * rect.Height;
    }

    private Brush Brush(string resourceKey, Color fallback)
    {
        if (TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    private void DrawCenteredText(DrawingContext dc, string value, Rect rect, Brush brush, double size)
    {
        var formatted = CreateText(value, brush, size);
        dc.DrawText(formatted, new Point(rect.Left + (rect.Width - formatted.Width) / 2, rect.Top + (rect.Height - formatted.Height) / 2));
    }

    private void DrawText(DrawingContext dc, string value, Point point, Brush brush, double size) =>
        dc.DrawText(CreateText(value, brush, size), point);

    private FormattedText CreateText(string value, Brush brush, double size) =>
        new(
            value,
            CultureInfo.GetCultureInfo("ja-JP"),
            FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private string FormatPrice(decimal value)
    {
        if (Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value:N0}";
        }

        return $"${value:N2}";
    }
}
