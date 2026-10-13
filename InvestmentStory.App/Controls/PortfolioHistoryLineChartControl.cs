using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class PortfolioHistoryLineChartControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(PortfolioHistoryLineChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<PortfolioSnapshotRowViewModel> _rows = Array.Empty<PortfolioSnapshotRowViewModel>();
    private Rect _plot;
    private decimal _axisMin;
    private decimal _axisMax;
    private DateTime _firstDate;
    private double _daySpan;
    private int _hoverIndex = -1;

    public PortfolioHistoryLineChartControl()
    {
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

        _rows = Items?.OfType<PortfolioSnapshotRowViewModel>()
            .OrderBy(x => x.SnapshotDate).ToArray() ?? Array.Empty<PortfolioSnapshotRowViewModel>();
        if (_rows.Count == 0 || ActualWidth < 220 || ActualHeight < 170)
        {
            ClearHover();
            return;
        }

        _plot = new Rect(72, 18, ActualWidth - 94, ActualHeight - 62);
        _firstDate = _rows[0].SnapshotDate;
        _daySpan = Math.Max(1, (_rows[^1].SnapshotDate - _firstDate).TotalDays);
        var smallest = _rows.Min(x => Math.Min(x.TotalMarketValueJpy,
            Math.Min(x.TotalCostBasisJpy, x.UnrealizedGainLossJpy)));
        var largest = _rows.Max(x => Math.Max(x.TotalMarketValueJpy,
            Math.Max(x.TotalCostBasisJpy, x.UnrealizedGainLossJpy)));
        _axisMin = Math.Min(0m, smallest);
        _axisMax = Math.Max(1m, largest);
        var padding = (_axisMax - _axisMin) * .06m;
        _axisMax += padding;
        if (_axisMin < 0m) _axisMin -= padding;

        var textBrush = ResourceBrush("SecondaryTextBrush", Brushes.Gray);
        var gridBrush = ResourceBrush("BorderBrush", Brushes.DimGray);
        var totalBrush = ResourceBrush("AccentBlueBrush", Brushes.DeepSkyBlue);
        var costBrush = ResourceBrush("DividendBrush", Brushes.MediumPurple);
        var gainBrush = ResourceBrush("ProfitBrush", Brushes.MediumSeaGreen);

        for (var tick = 0; tick <= 4; tick++)
        {
            var y = _plot.Bottom - _plot.Height * tick / 4d;
            var value = _axisMin + (_axisMax - _axisMin) * tick / 4m;
            dc.DrawLine(new Pen(gridBrush, .75), new Point(_plot.Left, y), new Point(_plot.Right, y));
            DrawText(dc, FormatAxis(value), 10, 3, y - 8, textBrush);
        }

        DrawSeries(dc, row => row.TotalCostBasisJpy, costBrush);
        DrawSeries(dc, row => row.UnrealizedGainLossJpy, gainBrush);
        DrawSeries(dc, row => row.TotalMarketValueJpy, totalBrush);

        DrawDate(dc, _rows[0].SnapshotDate, _plot.Left, textBrush, 0);
        if (_rows.Count > 2 && _daySpan >= 90 && _plot.Width > 460)
        {
            var middle = _firstDate.AddDays(_daySpan / 2d);
            DrawDate(dc, middle, _plot.Left + _plot.Width / 2d, textBrush, 1);
        }
        if (_rows.Count > 1)
            DrawDate(dc, _rows[^1].SnapshotDate, _plot.Right, textBrush, 2);

        if (_hoverIndex >= _rows.Count) _hoverIndex = -1;
        if (_hoverIndex >= 0)
        {
            var hovered = _rows[_hoverIndex];
            var x = X(hovered);
            var guide = new Pen(textBrush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(guide, new Point(x, _plot.Top), new Point(x, _plot.Bottom));
            DrawHoverPoint(dc, x, hovered.TotalMarketValueJpy, totalBrush);
            DrawHoverPoint(dc, x, hovered.TotalCostBasisJpy, costBrush);
            DrawHoverPoint(dc, x, hovered.UnrealizedGainLossJpy, gainBrush);
            DrawHoverCard(dc, hovered, x, totalBrush, costBrush, gainBrush);
        }
    }

    private void DrawHoverCard(DrawingContext dc, PortfolioSnapshotRowViewModel row, double x,
        Brush totalBrush, Brush costBrush, Brush gainBrush)
    {
        const double cardWidth = 218;
        const double cardHeight = 106;
        if (_plot.Width < cardWidth + 8) return;
        var left = x + 14 + cardWidth <= _plot.Right ? x + 14 : x - cardWidth - 14;
        left = Math.Clamp(left, _plot.Left + 4, _plot.Right - cardWidth - 4);
        var top = _plot.Top + 6;
        var card = new Rect(left, top, cardWidth, cardHeight);
        dc.DrawRoundedRectangle(ResourceBrush("ElevatedSurfaceBrush", Brushes.Black),
            new Pen(ResourceBrush("BorderBrush", Brushes.DimGray), 1), card, 7, 7);
        var textBrush = ResourceBrush("PrimaryTextBrush", Brushes.White);
        DrawText(dc, row.Date, 12, left + 12, top + 8, textBrush);
        DrawCardValue(dc, "総資産", row.TotalMarketValue, left, top + 34, totalBrush, textBrush);
        DrawCardValue(dc, "投資元本", row.TotalCostBasis, left, top + 57, costBrush, textBrush);
        DrawCardValue(dc, "含み損益", row.UnrealizedGainLoss, left, top + 80, gainBrush, textBrush);
    }

    private void DrawCardValue(DrawingContext dc, string label, string value, double left, double top,
        Brush marker, Brush text)
    {
        dc.DrawEllipse(marker, null, new Point(left + 16, top + 7), 3, 3);
        DrawText(dc, label, 10, left + 27, top, text);
        var formatted = MakeText(value, 11, text);
        dc.DrawText(formatted, new Point(left + 207 - formatted.Width, top - 1));
    }

    private void DrawSeries(DrawingContext dc, Func<PortfolioSnapshotRowViewModel, decimal> value, Brush brush)
    {
        var points = _rows.Select(row => new Point(X(row), Y(value(row)))).ToArray();
        if (points.Length > 1)
        {
            var line = new StreamGeometry();
            using (var context = line.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToArray(), true, false);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(brush, 2.4), line);
        }

        if (points.Length <= 60)
        {
            foreach (var point in points)
                dc.DrawEllipse(brush, null, point, 2.7, 2.7);
        }
    }

    private void DrawHoverPoint(DrawingContext dc, double x, decimal value, Brush brush)
    {
        var point = new Point(x, Y(value));
        dc.DrawEllipse(brush, new Pen(ResourceBrush("ElevatedSurfaceBrush", Brushes.Black), 2),
            point, 5, 5);
    }

    private void DrawDate(DrawingContext dc, DateTime date, double x, Brush brush, int alignment)
    {
        var formatted = MakeText(date.ToString("yyyy/MM/dd"), 10, brush);
        var left = alignment switch
        {
            0 => x,
            2 => x - formatted.Width,
            _ => x - formatted.Width / 2d
        };
        dc.DrawText(formatted, new Point(left, _plot.Bottom + 10));
    }

    private void HandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_rows.Count == 0 || !_plot.Contains(e.GetPosition(this)))
        {
            ClearHover();
            return;
        }

        var x = e.GetPosition(this).X;
        var closest = Enumerable.Range(0, _rows.Count)
            .MinBy(index => Math.Abs(X(_rows[index]) - x));
        if (closest == _hoverIndex) return;
        _hoverIndex = closest;
        InvalidateVisual();
    }

    private void ClearHover()
    {
        if (_hoverIndex < 0) return;
        _hoverIndex = -1;
        InvalidateVisual();
    }

    private double X(PortfolioSnapshotRowViewModel row) =>
        _rows.Count == 1 ? _plot.Left + _plot.Width / 2d
            : _plot.Left + (row.SnapshotDate - _firstDate).TotalDays / _daySpan * _plot.Width;

    private double Y(decimal value) =>
        _plot.Bottom - (double)((value - _axisMin) / (_axisMax - _axisMin)) * _plot.Height;

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static string FormatAxis(decimal value) => Math.Abs(value) switch
    {
        >= 100_000_000m => $"{value / 100_000_000m:N1}億",
        >= 10_000m => $"{value / 10_000m:N0}万",
        _ => $"{value:N0}"
    };

    private FormattedText MakeText(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(MakeText(value, size, brush), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PortfolioHistoryLineChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.CollectionChanged;
        control.ClearHover();
        control.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ClearHover();
        InvalidateVisual();
    }
}
