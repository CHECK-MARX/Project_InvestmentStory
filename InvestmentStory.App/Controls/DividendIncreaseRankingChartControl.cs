using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendIncreaseRankingChartControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendIncreaseRankingChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanRankingRowViewModel> _rows = Array.Empty<DividendPlanRankingRowViewModel>();

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        BeginInteractiveRender();
        _rows = Items?.OfType<DividendPlanRankingRowViewModel>()
                    .Where(item => item.Value > 0m)
                    .OrderByDescending(item => item.Value)
                    .Take(10)
                    .ToList()
                ?? new List<DividendPlanRankingRowViewModel>();
        if (_rows.Count == 0 || ActualWidth < 240d || ActualHeight < 100d)
        {
            DrawText(dc, "購入予定を入力すると表示されます", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var track = Brush("ElevatedSurfaceBrush", Brushes.DarkSlateGray);
        var max = Math.Max(1m, _rows.Max(item => item.Value));
        var left = Math.Min(150d, Math.Max(95d, ActualWidth * 0.23d));
        var right = 100d;
        var chartWidth = Math.Max(60d, ActualWidth - left - right);
        var top = 10d;
        var rowHeight = Math.Max(30d, (ActualHeight - 18d) / _rows.Count);

        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            var y = top + index * rowHeight;
            var height = Math.Min(18d, Math.Max(11d, rowHeight - 10d));
            var barY = y + (rowHeight - height) / 2d;
            var width = Math.Max(3d, (double)(row.Value / max) * chartWidth);
            var seriesKey = $"ticker:{row.Ticker}";
            if (!IsSeriesVisible(seriesKey)) continue;
            var opacity = InteractionOpacity(row.Ticker, null, null);
            var bar = DividendChartColorRegistry.SecurityBrush(row.Ticker, opacityMultiplier: opacity);
            DrawText(dc, $"{index + 1}. {row.Ticker}", 12, 4, y + 5, text);
            dc.DrawRoundedRectangle(WithOpacity(track, opacity), null, new Rect(left, barY, chartWidth, height), 4, 4);
            dc.DrawRoundedRectangle(bar, null, new Rect(left, barY, width, height), 4, 4);
            DrawText(dc, row.Amount, 12, left + chartWidth + 10, y + 5, secondary);
            AddHitTarget(new Rect(0, y, ActualWidth, rowHeight), row.ToolTipText, row.Ticker,
                seriesKey: seriesKey);
        }
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendIncreaseRankingChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
