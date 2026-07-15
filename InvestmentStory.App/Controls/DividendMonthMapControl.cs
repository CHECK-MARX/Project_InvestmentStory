using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendMonthMapControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendMonthMapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private IReadOnlyList<DividendPlanStockMonthlyRowViewModel> _rows = Array.Empty<DividendPlanStockMonthlyRowViewModel>();
    private double _left;
    private double _top;
    private double _cellWidth;
    private double _cellHeight;

    public DividendMonthMapControl()
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
            DrawText(dc, "配当予定データがありません。", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        _left = Math.Clamp(ActualWidth * 0.18d, 78d, 150d);
        _top = 32d;
        _cellWidth = Math.Max(22d, (ActualWidth - _left - 10d) / 12d);
        _cellHeight = Math.Max(22d, (ActualHeight - _top - 8d) / _rows.Count);
        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var line = Brush("BorderBrush", Brushes.DimGray);

        for (var month = 1; month <= 12; month++)
            DrawText(dc, $"{month}月", 10, _left + (month - 1) * _cellWidth + 4, 8, secondary);

        foreach (var (row, rowIndex) in _rows.Select((row, index) => (row, index)))
        {
            var y = _top + rowIndex * _cellHeight;
            DrawText(dc, row.Ticker, 11, 4, y + 4, text);
            dc.DrawLine(new Pen(line, 0.5), new Point(_left, y + _cellHeight / 2d),
                new Point(_left + _cellWidth * 12, y + _cellHeight / 2d));
            for (var monthIndex = 0; monthIndex < 12; monthIndex++)
            {
                if (row.PlannedValues[monthIndex] <= 0m && row.MissedValues[monthIndex] <= 0m) continue;
                var brush = StatusBrush(row.Statuses[monthIndex]);
                var center = new Point(_left + monthIndex * _cellWidth + _cellWidth / 2d, y + _cellHeight / 2d);
                var radius = row.PlannedValues[monthIndex] > 0m ? 6d : 4d;
                dc.DrawEllipse(brush, new Pen(Brush("SurfaceBackgroundBrush", Brushes.Navy), 1), center, radius, radius);
            }
        }
    }

    private Brush StatusBrush(string status) => status switch
    {
        "受取見込み" => Brush("AccentBlueBrush", Brushes.DeepSkyBlue),
        "推定" => Brush("WarningBrush", Brushes.Orange),
        "購入が間に合わない" => Brush("MutedTextBrush", Brushes.Gray),
        _ => Brush("LossBrush", Brushes.IndianRed)
    };

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
        var control = (DividendMonthMapControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
