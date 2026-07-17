using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendCompositionDonutControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendCompositionDonutControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private Point _center;
    private double _outerRadius;
    private double _innerRadius;

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        BeginInteractiveRender();
        var source = Items?.OfType<DividendPlanCompositionRowViewModel>().OrderByDescending(x => x.Value).ToList()
                     ?? new List<DividendPlanCompositionRowViewModel>();
        if (source.Count == 0 || source.Sum(x => x.Value) <= 0m)
        {
            DrawText(dc, "配当構成データがありません。", 14, 16, 20, Brush("SecondaryTextBrush", Brushes.Gray));
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
        for (var index = 0; index < top.Count; index++)
        {
            var row = top[index];
            var sweep = (double)(row.Value / total) * 360d;
            var seriesKey = $"ticker:{row.Ticker}";
            var geometry = RingSlice(_center, _outerRadius, _innerRadius, start, sweep);
            if (IsSeriesVisible(seriesKey))
            {
                var brush = DividendChartColorRegistry.SecurityBrush(row.Ticker,
                    opacityMultiplier: InteractionOpacity(row.Ticker, null, null));
                dc.DrawGeometry(brush, null, geometry);
            }
            AddGeometryHitTarget(geometry, ToolTipFor(row), row.Ticker, seriesKey: seriesKey);
            start += sweep;
        }

        DrawText(dc, "年間配当", 12, _center.X - 30, _center.Y - 16, Brush("SecondaryTextBrush", Brushes.Gray));
        DrawText(dc, $"{total:N0}円", 16, _center.X - 36, _center.Y + 3, Brush("PrimaryTextBrush", Brushes.White));
        var legendX = _outerRadius * 2 + 34d;
        for (var index = 0; index < top.Count; index++)
        {
            var row = top[index];
            var y = 15d + index * 34d;
            var seriesKey = $"ticker:{row.Ticker}";
            var legendBrush = DividendChartColorRegistry.SecurityBrush(row.Ticker,
                opacityMultiplier: IsSeriesVisible(seriesKey) ? 1d : .22d);
            dc.DrawRoundedRectangle(legendBrush, null, new Rect(legendX, y + 2, 13, 13), 3, 3);
            DrawText(dc, $"{row.Ticker}  {row.Amount}  {row.Rate}", 12, legendX + 20, y,
                Brush("PrimaryTextBrush", Brushes.White));
            AddHitTarget(new Rect(legendX, y - 2, Math.Max(120d, ActualWidth - legendX), 26),
                $"{row.Ticker}の表示を切り替えます。", row.Ticker, seriesKey: seriesKey, isLegend: true);
        }
    }

    private static string ToolTipFor(DividendPlanCompositionRowViewModel row) =>
        $"銘柄 {row.Ticker} {row.Name}\n購入後年間配当 {row.Amount}\n購入後構成比 {row.Rate}\n" +
        $"現在構成比 {row.CurrentRate}\n構成比変化 {row.RateChange}";

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
}
