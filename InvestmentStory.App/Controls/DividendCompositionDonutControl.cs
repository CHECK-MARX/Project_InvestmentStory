using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendCompositionDonutControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendCompositionDonutControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private readonly Brush[] _palette =
    {
        Brushes.DeepSkyBlue, Brushes.MediumPurple, Brushes.MediumSeaGreen,
        Brushes.Orange, Brushes.CornflowerBlue, Brushes.SlateGray
    };
    private IReadOnlyList<Segment> _segments = Array.Empty<Segment>();
    private Point _center;
    private double _outerRadius;
    private double _innerRadius;

    public DividendCompositionDonutControl() => MouseMove += OnMouseMove;

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var source = Items?.OfType<DividendPlanCompositionRowViewModel>().OrderByDescending(x => x.Value).ToList()
                     ?? new List<DividendPlanCompositionRowViewModel>();
        if (source.Count == 0 || source.Sum(x => x.Value) <= 0m)
        {
            DrawText(dc, "配当構成データがありません。", 14, 16, 20, Brush("SecondaryTextBrush", Brushes.Gray));
            _segments = Array.Empty<Segment>();
            return;
        }

        var top = source.Take(5).ToList();
        if (source.Count > 5)
        {
            var other = source.Skip(5).Sum(x => x.Value);
            top.Add(new DividendPlanCompositionRowViewModel("その他", "その他", other, other / source.Sum(x => x.Value) * 100m));
        }
        var total = top.Sum(x => x.Value);
        _center = new Point(Math.Min(ActualHeight, ActualWidth * 0.48) / 2d, ActualHeight / 2d);
        _outerRadius = Math.Max(36d, Math.Min(ActualHeight * 0.39, ActualWidth * 0.2));
        _innerRadius = _outerRadius * 0.58;
        var start = -90d;
        var segments = new List<Segment>();
        for (var index = 0; index < top.Count; index++)
        {
            var sweep = (double)(top[index].Value / total) * 360d;
            var brush = _palette[index % _palette.Length];
            dc.DrawGeometry(brush, null, RingSlice(_center, _outerRadius, _innerRadius, start, sweep));
            segments.Add(new Segment(start, sweep, top[index]));
            start += sweep;
        }
        _segments = segments;

        DrawText(dc, "年間配当", 12, _center.X - 30, _center.Y - 16, Brush("SecondaryTextBrush", Brushes.Gray));
        DrawText(dc, $"{total:N0}円", 16, _center.X - 36, _center.Y + 3, Brush("PrimaryTextBrush", Brushes.White));
        var legendX = _outerRadius * 2 + 34d;
        for (var index = 0; index < top.Count; index++)
        {
            var y = 15d + index * 34d;
            dc.DrawRoundedRectangle(_palette[index % _palette.Length], null, new Rect(legendX, y + 2, 13, 13), 3, 3);
            DrawText(dc, $"{top[index].Ticker}  {top[index].Amount}  {top[index].Rate}", 12, legendX + 20, y, Brush("PrimaryTextBrush", Brushes.White));
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        var dx = point.X - _center.X;
        var dy = point.Y - _center.Y;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius < _innerRadius || radius > _outerRadius)
        {
            ToolTip = null;
            return;
        }
        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        if (angle < -90d) angle += 360d;
        var segment = _segments.FirstOrDefault(x => angle >= x.Start && angle <= x.Start + x.Sweep);
        ToolTip = segment?.Row is null ? null : $"{segment.Row.Ticker} {segment.Row.Name}\n年間配当 {segment.Row.Amount}\n構成比 {segment.Row.Rate}";
    }

    private static Geometry RingSlice(Point center, double outer, double inner, double startDegrees, double sweepDegrees)
    {
        var start = startDegrees * Math.PI / 180d;
        var end = (startDegrees + sweepDegrees) * Math.PI / 180d;
        Point P(double radius, double angle) => new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(P(outer, start), true, true);
        context.ArcTo(P(outer, end), new Size(outer, outer), 0, sweepDegrees > 180d, SweepDirection.Clockwise, true, false);
        context.LineTo(P(inner, end), true, false);
        context.ArcTo(P(inner, start), new Size(inner, inner), 0, sweepDegrees > 180d, SweepDirection.Counterclockwise, true, false);
        geometry.Freeze();
        return geometry;
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendCompositionDonutControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    private sealed record Segment(double Start, double Sweep, DividendPlanCompositionRowViewModel Row);
}
