using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendHeatmapControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendHeatmapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanStockMonthlyRowViewModel> _rows = Array.Empty<DividendPlanStockMonthlyRowViewModel>();
    private double _left;
    private double _top;
    private double _cellWidth;
    private double _cellHeight;

    public DividendHeatmapControl()
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
        _rows = Items?.OfType<DividendPlanStockMonthlyRowViewModel>()
                    .Where(x => x.TotalValue > 0m).OrderByDescending(x => x.TotalValue).Take(12).ToList()
                ?? new List<DividendPlanStockMonthlyRowViewModel>();
        if (_rows.Count == 0)
        {
            DrawText(dc, "配当月データがありません。", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        _left = Math.Clamp(ActualWidth * 0.18d, 78d, 150d);
        _top = 32d;
        _cellWidth = Math.Max(22d, (ActualWidth - _left - 10d) / 12d);
        _cellHeight = Math.Max(22d, (ActualHeight - _top - 8d) / _rows.Count);
        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var border = Brush("BorderBrush", Brushes.DimGray);
        var accent = (Brush("AccentBlueBrush", Brushes.DeepSkyBlue) as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        var max = Math.Max(1m, _rows.SelectMany(x => x.PlannedValues).Max());

        for (var month = 1; month <= 12; month++)
            DrawText(dc, $"{month}月", 10, _left + (month - 1) * _cellWidth + 4, 8, secondary);

        foreach (var (row, rowIndex) in _rows.Select((row, index) => (row, index)))
        {
            var y = _top + rowIndex * _cellHeight;
            DrawText(dc, row.Ticker, 11, 4, y + 4, text);
            for (var monthIndex = 0; monthIndex < 12; monthIndex++)
            {
                var value = row.PlannedValues[monthIndex];
                var ratio = value <= 0m ? 0d : Math.Clamp((double)(value / max), 0.12d, 1d);
                var fill = value <= 0m
                    ? new SolidColorBrush(Color.FromArgb(40, 100, 116, 139))
                    : new SolidColorBrush(Color.FromArgb((byte)(55 + ratio * 200), accent.R, accent.G, accent.B));
                var rect = new Rect(_left + monthIndex * _cellWidth + 2, y + 2, _cellWidth - 4, _cellHeight - 4);
                dc.DrawRoundedRectangle(fill, new Pen(border, 0.5), rect, 3, 3);
            }
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_rows.Count == 0 || _cellWidth <= 0d || _cellHeight <= 0d) return;
        var point = e.GetPosition(this);
        var month = (int)((point.X - _left) / _cellWidth) + 1;
        var row = (int)((point.Y - _top) / _cellHeight);
        ToolTip = month is >= 1 and <= 12 && row >= 0 && row < _rows.Count
            ? _rows[row].ToolTipForMonth(month)
            : null;
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendHeatmapControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
