using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendInvestmentRankingChartControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendInvestmentRankingChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanRankingRowViewModel> _rows = Array.Empty<DividendPlanRankingRowViewModel>();
    private double _top;
    private double _rowHeight;

    public DividendInvestmentRankingChartControl()
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
        _rows = Items?.OfType<DividendPlanRankingRowViewModel>()
                    .Where(x => x.InvestmentValue > 0m).OrderByDescending(x => x.InvestmentValue).Take(10).ToList()
                ?? new List<DividendPlanRankingRowViewModel>();
        if (_rows.Count == 0)
        {
            DrawText(dc, "購入予定を入力すると表示されます", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var bar = Brush("WarningBrush", Brushes.Orange);
        var track = Brush("ElevatedSurfaceBrush", Brushes.DarkSlateGray);
        var max = Math.Max(1m, _rows.Max(x => x.InvestmentValue));
        var left = Math.Clamp(ActualWidth * 0.2d, 82d, 140d);
        var right = 102d;
        var width = Math.Max(60d, ActualWidth - left - right);
        _top = 8d;
        _rowHeight = Math.Max(28d, (ActualHeight - 16d) / _rows.Count);

        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            var y = _top + index * _rowHeight;
            var height = Math.Min(17d, _rowHeight - 8d);
            var barY = y + (_rowHeight - height) / 2d;
            var barWidth = Math.Max(3d, (double)(row.InvestmentValue / max) * width);
            DrawText(dc, $"{index + 1}. {row.Ticker}", 11, 4, y + 5, text);
            dc.DrawRoundedRectangle(track, null, new Rect(left, barY, width, height), 4, 4);
            dc.DrawRoundedRectangle(bar, null, new Rect(left, barY, barWidth, height), 4, 4);
            DrawText(dc, row.InvestmentAmount, 11, left + width + 8, y + 5, secondary);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_rows.Count == 0 || _rowHeight <= 0d) return;
        var index = (int)((e.GetPosition(this).Y - _top) / _rowHeight);
        ToolTip = index >= 0 && index < _rows.Count ? _rows[index].ToolTipText : null;
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendInvestmentRankingChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
