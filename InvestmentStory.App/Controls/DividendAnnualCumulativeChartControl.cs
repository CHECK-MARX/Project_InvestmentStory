using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class DividendAnnualCumulativeChartControl : InteractiveDividendChartControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DividendAnnualCumulativeChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    public static readonly DependencyProperty SelectedYearProperty = DependencyProperty.Register(
        nameof(SelectedYear), typeof(int), typeof(DividendAnnualCumulativeChartControl),
        new FrameworkPropertyMetadata(DateTime.Today.Year, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AsOfDateProperty = DependencyProperty.Register(
        nameof(AsOfDate), typeof(DateTime), typeof(DividendAnnualCumulativeChartControl),
        new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.AffectsRender));

    private IReadOnlyList<DividendMonthlySummaryRowViewModel> _rows = Array.Empty<DividendMonthlySummaryRowViewModel>();
    private double _left;
    private double _top;
    private double _width;
    private double _height;

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int SelectedYear
    {
        get => (int)GetValue(SelectedYearProperty);
        set => SetValue(SelectedYearProperty, value);
    }

    public DateTime AsOfDate
    {
        get => (DateTime)GetValue(AsOfDateProperty);
        set => SetValue(AsOfDateProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        BeginInteractiveRender();
        _rows = Items?.OfType<DividendMonthlySummaryRowViewModel>().OrderBy(x => x.Month).ToList()
                ?? new List<DividendMonthlySummaryRowViewModel>();
        var text = ResourceBrush("PrimaryTextBrush", Brushes.White);
        var secondary = ResourceBrush("SecondaryTextBrush", Brushes.Gray);
        var grid = ResourceBrush("BorderBrush", Brushes.DimGray);
        if (_rows.Count == 0 || ActualWidth < 360 || ActualHeight < 220)
        {
            DrawText(dc, "累計配当データがありません。", 13, 18, 18, secondary);
            return;
        }

        _left = 82d;
        _top = 46d;
        _width = ActualWidth - _left - 26d;
        _height = ActualHeight - _top - 42d;
        var lastActualMonth = SelectedYear < AsOfDate.Year ? 12
            : SelectedYear > AsOfDate.Year ? 0 : AsOfDate.Month;
        var max = Math.Max(1m, _rows.Max(x => Math.Max(x.CumulativeForecastValue, x.CumulativeGoalValue)) * 1.08m);
        var actual = ResourceBrush("AccentBlueBrush", Brushes.DeepSkyBlue);
        var forecast = ResourceBrush("DividendBrush", Brushes.MediumPurple);
        var target = ResourceBrush("WarningBrush", Brushes.Orange);

        DrawLegend(dc, actual, forecast, target, text);
        DrawText(dc, "累計税引後配当（円）", 11, 4, 28, secondary);
        if (lastActualMonth is > 0 and < 12 && ActualWidth >= 850d)
            DrawText(dc, $"{lastActualMonth}月まで実績 ／ 紫の破線は予定・推定を含む見込み", 10,
                ActualWidth - 390d, 28, secondary);
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = _top + _height - _height * tick / 4d;
            dc.DrawLine(new Pen(grid, 0.7), new Point(_left, y), new Point(_left + _width, y));
            DrawText(dc, FormatAxis(max * tick / 4m), 10, 4, y - 7, secondary);
        }

        for (var index = 0; index < _rows.Count; index++)
        {
            var slotLeft = index == 0 ? _left : (X(index - 1) + X(index)) / 2d;
            var slotRight = index == _rows.Count - 1 ? _left + _width : (X(index) + X(index + 1)) / 2d;
            AddHitTarget(new Rect(slotLeft, _top, slotRight - slotLeft, _height + 30d),
                BuildPointToolTip(index, "月次集計", _rows[index].CumulativeForecastValue),
                month: _rows[index].Month, seriesKey: "annual:month");
        }

        if (IsSeriesVisible("annual:actual"))
            DrawSeries(dc, _rows.Take(lastActualMonth).Select((row, index) => (index, row.CumulativeActualValue)).ToList(), max, actual,
                new Pen(actual, 2.5), true, "annual:actual");
        if (lastActualMonth < 12 && IsSeriesVisible("annual:forecast"))
            DrawSeries(dc, _rows.Skip(Math.Max(0, lastActualMonth - 1))
                    .Select((row, index) => (index + Math.Max(0, lastActualMonth - 1), row.CumulativeForecastValue))
                    .ToList(), max, forecast,
                new Pen(forecast, 2.5) { DashStyle = DashStyles.Dash }, true, "annual:forecast");
        if (IsSeriesVisible("annual:goal"))
            DrawSeries(dc, _rows.Select((row, index) => (index, row.CumulativeGoalValue)).ToList(), max, target,
                new Pen(target, 1.7) { DashStyle = DashStyles.Dot }, false, "annual:goal");

        for (var index = 0; index < _rows.Count; index++)
        {
            var x = X(index);
            DrawText(dc, $"{index + 1}月", 11, x - 14, _top + _height + 10, secondary);
        }


        DrawCrosshair(dc, max, secondary, grid);
    }

    private void DrawSeries(
        DrawingContext dc,
        IReadOnlyList<(int Index, decimal Value)> values,
        decimal max,
        Brush markerBrush,
        Pen pen,
        bool drawMarkers,
        string seriesKey)
    {
        var points = values.Select(value => new Point(X(value.Index), Y(value.Value, max))).ToList();
        if (points.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToList(), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        foreach (var (point, value) in points.Zip(values))
        {
            var opacity = InteractionOpacity(null, _rows[value.Index].Month, null);
            if (drawMarkers)
            {
                dc.DrawEllipse(WithOpacity(markerBrush, opacity), null, point, 3.2, 3.2);
            }
            AddHitTarget(new Rect(point.X - 10, point.Y - 10, 20, 20),
                BuildPointToolTip(value.Index, SeriesDisplayName(seriesKey), value.Value),
                month: _rows[value.Index].Month, seriesKey: seriesKey);
        }
    }

    private string BuildPointToolTip(int index, string seriesName, decimal value)
    {
        var row = _rows[index];
        var previous = index == 0 ? 0m : _rows[index - 1].CumulativeForecastValue;
        var previousDifference = row.CumulativeForecastValue - previous;
        var targetDifference = row.CumulativeForecastValue - row.CumulativeGoalValue;
        var estimated = row.Entries.Where(x => x.ScheduleStatus == InvestmentStory.Core.Models.DividendScheduleStatus.Estimated)
            .Sum(x => x.NetJpyValue);
        var achievement = row.CumulativeGoalValue <= 0m
            ? (decimal?)null
            : row.CumulativeForecastValue / row.CumulativeGoalValue * 100m;
        return $"{row.Month}月  {seriesName}: {value:N0}円{Environment.NewLine}" +
               $"当月配当: {row.ForecastValue:N0}円  前月比: {previousDifference:+#,##0;-#,##0;0}円{Environment.NewLine}" +
               $"当月内訳: 入金済み {row.ActualValue:N0}円  入金予定 {row.UpcomingValue:N0}円（うち推定 {estimated:N0}円）{Environment.NewLine}" +
               $"実績累計: {row.CumulativeActualValue:N0}円{Environment.NewLine}" +
               $"実績＋予定累計: {row.CumulativeForecastValue:N0}円{Environment.NewLine}" +
               $"累計目標: {row.CumulativeGoalValue:N0}円  差額: {targetDifference:+#,##0;-#,##0;0}円" +
               (achievement is null ? string.Empty : $"{Environment.NewLine}累計目標達成率: {achievement:N2}%");
    }

    private void DrawCrosshair(DrawingContext dc, decimal max, Brush text, Brush grid)
    {
        if (HoverPosition is not { } hover
            || hover.X < _left || hover.X > _left + _width
            || hover.Y < _top || hover.Y > _top + _height)
        {
            return;
        }

        var index = Math.Clamp((int)Math.Round((hover.X - _left) / Math.Max(1d, _width) * 11d), 0, 11);
        var x = X(index);
        var y = Math.Clamp(hover.Y, _top, _top + _height);
        var pen = new Pen(WithOpacity(text, .55d), .8) { DashStyle = DashStyles.Dash };
        dc.DrawLine(pen, new Point(x, _top), new Point(x, _top + _height));
        dc.DrawLine(pen, new Point(_left, y), new Point(_left + _width, y));
        var value = max * (decimal)((_top + _height - y) / _height);
        dc.DrawRoundedRectangle(WithOpacity(grid, .94d), null,
            new Rect(Math.Max(_left, x - 24), _top + _height + 4, 50, 20), 3, 3);
        DrawText(dc, $"{index + 1}月", 10, Math.Max(_left + 3, x - 15), _top + _height + 7, text);
        DrawText(dc, $"{value:N0}円", 10, _left + 4, Math.Max(_top, y - 17), text);
    }

    private static string SeriesDisplayName(string seriesKey) => seriesKey switch
    {
        "annual:actual" => "実績累計",
        "annual:forecast" => "実績＋予定累計",
        "annual:goal" => "月別目標累計",
        _ => "月次集計"
    };

    private void DrawLegend(DrawingContext dc, Brush actual, Brush forecast, Brush target, Brush text)
    {
        DrawLegendItem(dc, "実績累計", actual, 4, false, text, "annual:actual");
        DrawLegendItem(dc, "実績＋予定累計", forecast, 112, true, text, "annual:forecast");
        DrawLegendItem(dc, "月別目標累計", target, 272, true, text, "annual:goal");
    }

    private void DrawLegendItem(
        DrawingContext dc, string label, Brush brush, double x, bool dashed, Brush text, string seriesKey)
    {
        var legendBrush = WithOpacity(brush, IsSeriesVisible(seriesKey) ? 1d : .22d);
        dc.DrawLine(new Pen(legendBrush, 2.4) { DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid },
            new Point(x, 12), new Point(x + 22, 12));
        DrawText(dc, label, 11, x + 28, 3, text);
        AddHitTarget(new Rect(x, 0, label.Length * 12 + 50, 26), $"{label}の表示を切り替えます。",
            seriesKey: seriesKey, isLegend: true);
    }

    private double X(int index) => _left + _width * index / 11d;
    private double Y(decimal value, decimal max) => _top + _height - (double)(Math.Max(0m, value) / max) * _height;
    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static string FormatAxis(decimal value) => value switch
    {
        >= 100_000_000m => $"{value / 100_000_000m:N1}億",
        >= 10_000m => $"{value / 10_000m:N0}万",
        _ => $"{value:N0}"
    };

    private static void DrawText(DrawingContext dc, string value, double size, double x, double y, Brush brush) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), size, brush, 1.0), new Point(x, y));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DividendAnnualCumulativeChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
