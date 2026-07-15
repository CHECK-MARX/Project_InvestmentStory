using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendWaterfallChartControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendWaterfallChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));
    public static readonly DependencyProperty CurrentValueProperty = DependencyProperty.Register(
        nameof(CurrentValue), typeof(decimal), typeof(DividendWaterfallChartControl),
        new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));

    private IReadOnlyList<Segment> _segments = Array.Empty<Segment>();
    private double _left;
    private double _slot;

    public DividendWaterfallChartControl()
    {
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => ToolTip = null;
    }

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public decimal CurrentValue
    {
        get => (decimal)GetValue(CurrentValueProperty);
        set => SetValue(CurrentValueProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var additions = Items?.OfType<DividendPlanRankingRowViewModel>()
            .Where(x => x.Value > 0m).OrderByDescending(x => x.Value).Take(7).ToList()
            ?? new List<DividendPlanRankingRowViewModel>();
        if (CurrentValue <= 0m && additions.Count == 0)
        {
            DrawText(dc, "購入計画を入力すると配当ウォーターフォールを表示します。", 13, 16, 18,
                Brush("SecondaryTextBrush", Brushes.Gray));
            _segments = Array.Empty<Segment>();
            return;
        }

        var segments = new List<Segment> { new("現在", CurrentValue, 0m, CurrentValue, null, true) };
        var running = CurrentValue;
        foreach (var row in additions)
        {
            var before = running;
            running += row.Value;
            segments.Add(new Segment(row.Ticker, row.Value, before, running, row, false));
        }
        segments.Add(new Segment("購入後", running, 0m, running, null, true));
        _segments = segments;

        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var baseBrush = Brush("AccentBlueBrush", Brushes.DeepSkyBlue);
        var addBrush = Brush("ProfitBrush", Brushes.MediumSeaGreen);
        var finalBrush = Brush("DividendBrush", Brushes.MediumPurple);
        var grid = Brush("BorderBrush", Brushes.DimGray);
        _left = 58d;
        var top = 28d;
        var bottom = 48d;
        var width = Math.Max(140d, ActualWidth - _left - 20d);
        var height = Math.Max(90d, ActualHeight - top - bottom);
        _slot = width / segments.Count;
        var max = Math.Max(1m, running * 1.12m);

        for (var tick = 0; tick <= 4; tick++)
        {
            var y = top + height - height * tick / 4d;
            dc.DrawLine(new Pen(grid, 0.6), new Point(_left, y), new Point(_left + width, y));
            DrawText(dc, FormatAxis(max * tick / 4m), 10, 2, y - 7, secondary);
        }

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var x = _left + index * _slot + _slot * 0.2;
            var barWidth = _slot * 0.6;
            var yTop = Y(segment.To, max, top, height);
            var yBottom = Y(segment.From, max, top, height);
            var rect = new Rect(x, Math.Min(yTop, yBottom), barWidth, Math.Max(2, Math.Abs(yBottom - yTop)));
            var brush = index == segments.Count - 1 ? finalBrush : segment.IsTotal ? baseBrush : addBrush;
            dc.DrawRoundedRectangle(brush, null, rect, 3, 3);
            DrawText(dc, segment.Label, 10, x, top + height + 9, text);
            if (!segment.IsTotal && index < segments.Count - 1)
            {
                var connectorY = Y(segment.To, max, top, height);
                dc.DrawLine(new Pen(secondary, 0.8) { DashStyle = DashStyles.Dash },
                    new Point(x + barWidth, connectorY), new Point(x + _slot, connectorY));
            }
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_segments.Count == 0 || _slot <= 0d) return;
        var index = (int)((e.GetPosition(this).X - _left) / _slot);
        if (index < 0 || index >= _segments.Count)
        {
            ToolTip = null;
            return;
        }
        var segment = _segments[index];
        ToolTip = segment.Row?.ToolTipText
                  ?? $"{segment.Label}\n税引後年間配当 {segment.To:N0}円";
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
        var control = (DividendWaterfallChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    private sealed record Segment(string Label, decimal Value, decimal From, decimal To, DividendPlanRankingRowViewModel? Row, bool IsTotal);
}
