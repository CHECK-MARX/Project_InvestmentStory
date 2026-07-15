using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendMonthlyCompositionChartControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendMonthlyCompositionChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private readonly Brush[] _palette =
    {
        Brushes.DeepSkyBlue, Brushes.MediumPurple, Brushes.Orange,
        Brushes.MediumSeaGreen, Brushes.CornflowerBlue, Brushes.SlateGray
    };
    private IReadOnlyList<DividendPlanStockMonthlyRowViewModel> _rows = Array.Empty<DividendPlanStockMonthlyRowViewModel>();
    private double _left;
    private double _slot;

    public DividendMonthlyCompositionChartControl()
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
        var source = Items?.OfType<DividendPlanStockMonthlyRowViewModel>()
            .Where(x => x.TotalValue > 0m).OrderByDescending(x => x.TotalValue).ToList()
            ?? new List<DividendPlanStockMonthlyRowViewModel>();
        if (source.Count == 0)
        {
            DrawText(dc, "銘柄別の月別配当データがありません。", 13, 16, 18, Brush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        _rows = source.Take(6).ToList();
        var text = Brush("PrimaryTextBrush", Brushes.White);
        var secondary = Brush("SecondaryTextBrush", Brushes.Gray);
        var grid = Brush("BorderBrush", Brushes.DimGray);
        _left = 58d;
        var top = 50d;
        var bottom = 38d;
        var width = Math.Max(120d, ActualWidth - _left - 18d);
        var height = Math.Max(80d, ActualHeight - top - bottom);
        _slot = width / 12d;
        var monthlyTotals = Enumerable.Range(0, 12).Select(index => source.Sum(x => x.PlannedValues[index])).ToArray();
        var max = Math.Max(1m, monthlyTotals.Max() * 1.1m);

        var legendX = 4d;
        for (var index = 0; index < _rows.Count; index++)
        {
            dc.DrawRoundedRectangle(_palette[index], null, new Rect(legendX, 10, 12, 12), 2, 2);
            DrawText(dc, _rows[index].Ticker, 10, legendX + 16, 7, text);
            legendX += Math.Max(58d, _rows[index].Ticker.Length * 10d + 30d);
        }

        for (var tick = 0; tick <= 4; tick++)
        {
            var y = top + height - height * tick / 4d;
            dc.DrawLine(new Pen(grid, 0.6), new Point(_left, y), new Point(_left + width, y));
            DrawText(dc, FormatAxis(max * tick / 4m), 10, 2, y - 7, secondary);
        }

        for (var monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            var x = _left + monthIndex * _slot + _slot * 0.22;
            var barWidth = _slot * 0.56;
            var y = top + height;
            for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
            {
                var value = _rows[rowIndex].PlannedValues[monthIndex];
                if (value <= 0m) continue;
                var segmentHeight = Math.Max(2d, (double)(value / max) * height);
                y -= segmentHeight;
                dc.DrawRoundedRectangle(_palette[rowIndex], null, new Rect(x, y, barWidth, segmentHeight), 2, 2);
            }
            var other = source.Skip(6).Sum(row => row.PlannedValues[monthIndex]);
            if (other > 0m)
            {
                var segmentHeight = Math.Max(2d, (double)(other / max) * height);
                y -= segmentHeight;
                dc.DrawRoundedRectangle(_palette[5], null, new Rect(x, y, barWidth, segmentHeight), 2, 2);
            }
            DrawText(dc, $"{monthIndex + 1}月", 10, x - 2, top + height + 9, secondary);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_rows.Count == 0 || _slot <= 0d) return;
        var month = (int)((e.GetPosition(this).X - _left) / _slot) + 1;
        if (month is < 1 or > 12)
        {
            ToolTip = null;
            return;
        }
        var details = _rows.Where(x => x.PlannedValues[month - 1] > 0m)
            .OrderByDescending(x => x.PlannedValues[month - 1])
            .Select(x => $"{x.Ticker}: {x.PlannedValues[month - 1]:N0}円");
        var total = _rows.Sum(x => x.PlannedValues[month - 1]);
        ToolTip = $"{month}月 合計 {total:N0}円\n{string.Join("\n", details)}";
    }

    private static string FormatAxis(decimal value) => value >= 10_000m ? $"{value / 10_000m:N0}万" : $"{value:N0}";
    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendMonthlyCompositionChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
