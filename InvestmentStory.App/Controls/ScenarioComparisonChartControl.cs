using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Controls;

public sealed class ScenarioComparisonChartControl : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IEnumerable),
        typeof(ScenarioComparisonChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty TargetAmountProperty = DependencyProperty.Register(
        nameof(TargetAmount),
        typeof(decimal),
        typeof(ScenarioComparisonChartControl),
        new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage),
        typeof(string),
        typeof(ScenarioComparisonChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedScenarioKeyProperty = DependencyProperty.Register(
        nameof(SelectedScenarioKey),
        typeof(string),
        typeof(ScenarioComparisonChartControl),
        new FrameworkPropertyMetadata("Standard", FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly IReadOnlyDictionary<string, Color> SeriesColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
        ["Conservative"] = Color.FromRgb(148, 163, 184),
        ["Standard"] = Color.FromRgb(56, 189, 248),
        ["Aggressive"] = Color.FromRgb(167, 139, 250),
        ["Actual"] = Color.FromRgb(245, 158, 11)
    };

    private INotifyCollectionChanged? _observedCollection;
    private IReadOnlyList<MutualFundScenarioChartSeriesViewModel> _renderedSeries = Array.Empty<MutualFundScenarioChartSeriesViewModel>();
    private readonly List<(Rect Bounds, MutualFundScenarioChartSeriesViewModel Series)> _legendHits = new();
    private readonly List<AchievementHitTarget> _achievementHits = new();
    private Rect _plotRect;
    private int _viewStart;
    private int _viewEnd = -1;
    private int? _hoverSourceIndex;
    private bool _isPanning;
    private Point _panOrigin;
    private int _panStartIndex;
    private int _panEndIndex;
    private readonly Stopwatch _animationClock = new();
    private double _animationProgress = 1d;

    public ScenarioComparisonChartControl()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        Unloaded += (_, _) => StopAnimation();
    }

    public IEnumerable? Series
    {
        get => (IEnumerable?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public decimal TargetAmount
    {
        get => (decimal)GetValue(TargetAmountProperty);
        set => SetValue(TargetAmountProperty, value);
    }

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    public string SelectedScenarioKey
    {
        get => (string)GetValue(SelectedScenarioKeyProperty);
        set => SetValue(SelectedScenarioKeyProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        return new Size(width, 540);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 20 || bounds.Height <= 20)
        {
            return;
        }

        var surface = ResourceBrush("ElevatedSurfaceBrush", Color.FromRgb(30, 41, 59));
        var border = ResourceBrush("BorderBrush", Color.FromRgb(38, 52, 73));
        var text = ResourceBrush("PrimaryTextBrush", Color.FromRgb(229, 231, 235));
        var secondary = ResourceBrush("SecondaryTextBrush", Color.FromRgb(148, 163, 184));
        dc.DrawRoundedRectangle(surface, new Pen(border, 1), bounds, 10, 10);
        _achievementHits.Clear();

        _renderedSeries = Series is null
            ? Array.Empty<MutualFundScenarioChartSeriesViewModel>()
            : Series.OfType<MutualFundScenarioChartSeriesViewModel>().ToList();
        var visible = _renderedSeries.Where(x => x.IsVisible && x.Points.Count > 0).ToList();
        if (visible.Count == 0)
        {
            DrawText(dc, string.IsNullOrWhiteSpace(StatusMessage) ? "表示対象のシナリオがありません。" : StatusMessage,
                bounds.TopLeft + new Vector(22, 28), secondary, 13);
            DrawLegend(dc, bounds, text);
            return;
        }

        var totalCount = visible.Max(x => x.Points.Count);
        NormalizeViewport(totalCount);
        _plotRect = new Rect(72, 62, Math.Max(160, bounds.Width - 160), Math.Max(220, bounds.Height - 132));
        var (start, end) = GetViewport(totalCount);
        var values = visible
            .SelectMany(series => series.Points.Skip(start).Take(end - start + 1))
            .Select(point => point.MarketValueJpy)
            .ToList();
        if (TargetAmount > 0m)
        {
            values.Add(TargetAmount);
        }

        var maxValue = Math.Max(1m, values.Max());
        var max = (double)maxValue * 1.1d;
        var min = 0d;
        var step = _plotRect.Width / Math.Max(1, end - start);

        DrawGrid(dc, _plotRect, min, max, visible[0], start, end, text, secondary, border);
        var targetY = DrawTargetLine(dc, _plotRect, min, max, TargetAmount);

        dc.PushClip(new RectangleGeometry(new Rect(
            _plotRect.Left,
            _plotRect.Top,
            _plotRect.Width * _animationProgress,
            _plotRect.Height)));
        foreach (var series in visible)
        {
            DrawSeries(dc, series, _plotRect, min, max, start, end, step);
        }
        dc.Pop();

        var annotations = BuildAchievementAnnotations(visible, _plotRect, min, max, start, end, step, targetY);
        DrawAchievementAnnotations(dc, annotations, _plotRect);
        DrawTargetLabel(dc, _plotRect, targetY, TargetAmount, annotations.Where(x => x.ShowLabel).Select(x => x.LabelBounds));

        DrawHover(dc, visible, _plotRect, min, max, start, end, step);
        DrawLegend(dc, bounds, text);
        DrawText(dc, StatusMessage, new Point(18, 18), secondary, 12);
        if (start > 0 || end < totalCount - 1)
        {
            DrawText(dc, "拡大表示中  ホイール: 拡大/縮小  ドラッグ: 移動  ダブルクリック: 全期間",
                new Point(bounds.Right - 460, 18), secondary, 11);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var totalCount = GetTotalPointCount();
        var (start, end) = GetViewport(totalCount);
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed && _plotRect.Width > 0)
        {
            var range = Math.Max(1, _panEndIndex - _panStartIndex);
            var shift = (int)Math.Round((_panOrigin.X - point.X) / _plotRect.Width * range);
            SetViewport(_panStartIndex + shift, _panEndIndex + shift, totalCount);
            _hoverSourceIndex = null;
            ToolTip = null;
            InvalidateVisual();
            return;
        }

        if (_plotRect.Contains(point) && totalCount > 0)
        {
            var achievementHit = _achievementHits
                .Where(x => x.Bounds.Contains(point))
                .OrderBy(x => x.Priority)
                .FirstOrDefault();
            if (achievementHit is not null)
            {
                _hoverSourceIndex = achievementHit.PointIndex;
                ToolTip = BuildAchievementToolTip(achievementHit.Series, achievementHit.PointIndex);
                InvalidateVisual();
                return;
            }

            var relative = Math.Clamp((point.X - _plotRect.Left) / Math.Max(1d, _plotRect.Width), 0d, 1d);
            _hoverSourceIndex = start + (int)Math.Round(relative * Math.Max(0, end - start));
            ToolTip = BuildToolTip(_hoverSourceIndex.Value);
        }
        else
        {
            _hoverSourceIndex = null;
            ToolTip = null;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_isPanning)
        {
            _hoverSourceIndex = null;
            ToolTip = null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var point = e.GetPosition(this);
        if (e.ClickCount == 2 && _plotRect.Contains(point))
        {
            ResetViewport();
            e.Handled = true;
            return;
        }

        var hit = _legendHits.FirstOrDefault(x => x.Bounds.Contains(point));
        if (hit.Series is not null)
        {
            hit.Series.IsVisible = !hit.Series.IsVisible;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _plotRect.Contains(point))
        {
            var totalCount = GetTotalPointCount();
            (_panStartIndex, _panEndIndex) = GetViewport(totalCount);
            _panOrigin = point;
            _isPanning = true;
            Cursor = Cursors.SizeWE;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = Cursors.Cross;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_plotRect.Contains(e.GetPosition(this)))
        {
            return;
        }

        var totalCount = GetTotalPointCount();
        var (start, end) = GetViewport(totalCount);
        var currentRange = Math.Max(2, end - start + 1);
        var nextRange = e.Delta > 0
            ? Math.Max(12, (int)Math.Round(currentRange * 0.8d))
            : Math.Min(totalCount, (int)Math.Round(currentRange * 1.25d));
        var relative = Math.Clamp((e.GetPosition(this).X - _plotRect.Left) / Math.Max(1d, _plotRect.Width), 0d, 1d);
        var anchor = start + (int)Math.Round(relative * (currentRange - 1));
        var nextStart = anchor - (int)Math.Round(relative * (nextRange - 1));
        SetViewport(nextStart, nextStart + nextRange - 1, totalCount);
        e.Handled = true;
        InvalidateVisual();
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScenarioComparisonChartControl)d;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged -= control.OnCollectionChanged;
        }

        control._observedCollection = e.NewValue as INotifyCollectionChanged;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged += control.OnCollectionChanged;
        }

        control.ResetViewport(startAnimation: true);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ResetViewport(startAnimation: true);

    private void ResetViewport(bool startAnimation = false)
    {
        _viewStart = 0;
        _viewEnd = -1;
        if (startAnimation)
        {
            StartAnimation();
        }
        InvalidateVisual();
    }

    private void StartAnimation()
    {
        StopAnimation();
        _animationProgress = 0d;
        _animationClock.Restart();
        CompositionTarget.Rendering += OnAnimationFrame;
    }

    private void StopAnimation()
    {
        CompositionTarget.Rendering -= OnAnimationFrame;
        _animationClock.Stop();
        _animationProgress = 1d;
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        var linear = Math.Clamp(_animationClock.Elapsed.TotalMilliseconds / 650d, 0d, 1d);
        _animationProgress = 1d - Math.Pow(1d - linear, 3d);
        InvalidateVisual();
        if (linear >= 1d)
        {
            StopAnimation();
        }
    }

    private int GetTotalPointCount() => _renderedSeries.Count == 0 ? 0 : _renderedSeries.Max(x => x.Points.Count);

    private void NormalizeViewport(int totalCount)
    {
        if (totalCount <= 0)
        {
            _viewStart = 0;
            _viewEnd = -1;
            return;
        }

        if (_viewEnd < 0)
        {
            _viewStart = 0;
            _viewEnd = totalCount - 1;
        }
        SetViewport(_viewStart, _viewEnd, totalCount);
    }

    private (int Start, int End) GetViewport(int totalCount)
    {
        NormalizeViewport(totalCount);
        return (_viewStart, _viewEnd);
    }

    private void SetViewport(int start, int end, int totalCount)
    {
        if (totalCount <= 0)
        {
            _viewStart = 0;
            _viewEnd = -1;
            return;
        }

        var range = Math.Clamp(end - start + 1, 1, totalCount);
        var clampedStart = Math.Clamp(start, 0, Math.Max(0, totalCount - range));
        _viewStart = clampedStart;
        _viewEnd = clampedStart + range - 1;
    }

    private static void DrawGrid(
        DrawingContext dc,
        Rect rect,
        double min,
        double max,
        MutualFundScenarioChartSeriesViewModel axisSeries,
        int start,
        int end,
        Brush text,
        Brush secondary,
        Brush border)
    {
        var pen = new Pen(border, 1);
        dc.DrawRectangle(null, pen, rect);
        for (var i = 0; i <= 5; i++)
        {
            var y = rect.Top + rect.Height / 5 * i;
            dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
            DrawText(dc, FormatJpy((decimal)(max - (max - min) / 5 * i)), new Point(rect.Right + 8, y - 8), secondary, 10);
        }

        var divisions = Math.Min(6, Math.Max(1, end - start));
        for (var i = 0; i <= divisions; i++)
        {
            var index = start + (int)Math.Round((end - start) * (i / (double)divisions));
            if (index >= axisSeries.Points.Count)
            {
                continue;
            }
            var x = rect.Left + rect.Width * (index - start) / Math.Max(1, end - start);
            dc.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            DrawText(dc, axisSeries.Points[index].YearMonth.ToString("yyyy/MM"), new Point(x - 26, rect.Bottom + 8), text, 10);
        }
    }

    private static double? DrawTargetLine(DrawingContext dc, Rect rect, double min, double max, decimal target)
    {
        if (target <= 0m)
        {
            return null;
        }

        var brush = ResourceBrush("WarningBrush", Color.FromRgb(245, 158, 11));
        var y = MapY(rect, min, max, target);
        var pen = new Pen(brush, 2.4) { DashStyle = DashStyles.Dash };
        dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        return y;
    }

    private static void DrawSeries(
        DrawingContext dc,
        MutualFundScenarioChartSeriesViewModel series,
        Rect rect,
        double min,
        double max,
        int start,
        int end,
        double step)
    {
        var color = GetSeriesColor(series.Key);
        var brush = new SolidColorBrush(color);
        var lastIndex = Math.Min(end, series.Points.Count - 1);
        if (lastIndex < start)
        {
            return;
        }

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(rect.Left, rect.Bottom), true, true);
            for (var index = start; index <= lastIndex; index++)
            {
                ctx.LineTo(new Point(rect.Left + step * (index - start), MapY(rect, min, max, series.Points[index].MarketValueJpy)), true, false);
            }
            ctx.LineTo(new Point(rect.Left + step * (lastIndex - start), rect.Bottom), true, false);
        }
        area.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(24, color.R, color.G, color.B)), null, area);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            for (var index = start; index <= lastIndex; index++)
            {
                var point = new Point(rect.Left + step * (index - start), MapY(rect, min, max, series.Points[index].MarketValueJpy));
                if (index == start)
                {
                    ctx.BeginFigure(point, false, false);
                }
                else
                {
                    ctx.LineTo(point, true, false);
                }
            }
        }
        line.Freeze();
        dc.DrawGeometry(null, GetSeriesPen(series.Key, brush), line);

        var markerInterval = Math.Max(1, (lastIndex - start + 1) / 14);
        for (var index = start; index <= lastIndex; index++)
        {
            if (index != lastIndex && (index - start) % markerInterval != 0)
            {
                continue;
            }

            var point = new Point(
                rect.Left + step * (index - start),
                MapY(rect, min, max, series.Points[index].MarketValueJpy));
            DrawScenarioMarker(dc, series.Key, point, brush, 3.2, false);
        }
    }

    private IReadOnlyList<AchievementAnnotation> BuildAchievementAnnotations(
        IReadOnlyList<MutualFundScenarioChartSeriesViewModel> visible,
        Rect rect,
        double min,
        double max,
        int start,
        int end,
        double step,
        double? targetY)
    {
        var candidates = visible
            .Where(series => series.TargetAchievementMonth is not null)
            .Select(series =>
            {
                var pointIndex = FindPointIndex(series.Points, series.TargetAchievementMonth!.Value);
                var marker = pointIndex >= start && pointIndex <= end && pointIndex < series.Points.Count
                    ? new Point(
                        rect.Left + step * (pointIndex - start),
                        MapY(rect, min, max, series.Points[pointIndex].MarketValueJpy))
                    : new Point(double.NaN, double.NaN);
                return new AchievementCandidate(series, pointIndex, marker, GetShortScenarioName(series.Key));
            })
            .Where(candidate => candidate.PointIndex >= start && candidate.PointIndex <= end && !double.IsNaN(candidate.Marker.X))
            .OrderBy(candidate => candidate.PointIndex)
            .ToList();

        if (candidates.Count == 0)
        {
            return Array.Empty<AchievementAnnotation>();
        }

        var labeledCandidates = new HashSet<AchievementCandidate>(candidates);
        if (rect.Width < 1050 && candidates.Count > 3)
        {
            labeledCandidates.Clear();
            labeledCandidates.Add(candidates[0]);
            labeledCandidates.Add(candidates[^1]);
            var selected = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Series.Key, SelectedScenarioKey, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                labeledCandidates.Add(selected);
            }
        }

        var clusters = new List<List<AchievementCandidate>>();
        foreach (var candidate in candidates)
        {
            if (clusters.Count == 0 || candidate.PointIndex - clusters[^1][^1].PointIndex > 6)
            {
                clusters.Add(new List<AchievementCandidate>());
            }
            clusters[^1].Add(candidate);
        }

        var annotations = new List<AchievementAnnotation>();
        var occupied = new List<Rect>();
        foreach (var cluster in clusters)
        {
            var labeled = cluster.Where(labeledCandidates.Contains).ToList();
            var verticalOffsets = GetVerticalOffsets(labeled.Count);
            for (var index = 0; index < cluster.Count; index++)
            {
                var candidate = cluster[index];
                var labelIndex = labeled.IndexOf(candidate);
                if (labelIndex < 0)
                {
                    annotations.Add(new AchievementAnnotation(
                        candidate.Series,
                        candidate.PointIndex,
                        candidate.Marker,
                        false,
                        Rect.Empty,
                        string.Empty));
                    continue;
                }

                var shortLabel = $"{candidate.ShortName} {candidate.Series.TargetAchievementMonth!.Value:yyyy/MM}";
                var labelWidth = Math.Clamp(MeasureTextWidth(shortLabel, 10.5, FontWeights.SemiBold) + 16, 88, 132);
                var horizontalOffset = (labelIndex - (labeled.Count - 1) / 2d) * 24d;
                var labelBounds = FindAvailableLabelBounds(
                    candidate.Marker,
                    targetY ?? candidate.Marker.Y,
                    labelWidth,
                    26,
                    verticalOffsets[labelIndex],
                    horizontalOffset,
                    rect,
                    occupied);
                occupied.Add(labelBounds);
                annotations.Add(new AchievementAnnotation(
                    candidate.Series,
                    candidate.PointIndex,
                    candidate.Marker,
                    true,
                    labelBounds,
                    shortLabel));
            }
        }

        return annotations.OrderBy(annotation => annotation.PointIndex).ToList();
    }

    private void DrawAchievementAnnotations(
        DrawingContext dc,
        IReadOnlyList<AchievementAnnotation> annotations,
        Rect rect)
    {
        _achievementHits.Clear();
        var surface = ResourceBrush("SurfaceBackgroundBrush", Color.FromRgb(23, 32, 51));
        var primaryText = ResourceBrush("PrimaryTextBrush", Color.FromRgb(229, 231, 235));
        foreach (var annotation in annotations)
        {
            var color = GetSeriesColor(annotation.Series.Key);
            var brush = new SolidColorBrush(color);
            var guideBrush = new SolidColorBrush(Color.FromArgb(62, color.R, color.G, color.B));
            var guidePen = new Pen(guideBrush, 0.8) { DashStyle = DashStyles.Dash };
            dc.DrawLine(guidePen, new Point(annotation.Marker.X, rect.Top), new Point(annotation.Marker.X, rect.Bottom));
            DrawScenarioMarker(dc, annotation.Series.Key, annotation.Marker, brush, 6.2, true);

            _achievementHits.Add(new AchievementHitTarget(
                new Rect(annotation.Marker.X - 8, annotation.Marker.Y - 8, 16, 16),
                annotation.Series,
                annotation.PointIndex,
                0));
            _achievementHits.Add(new AchievementHitTarget(
                new Rect(annotation.Marker.X - 4, rect.Top, 8, rect.Height),
                annotation.Series,
                annotation.PointIndex,
                2));

            if (!annotation.ShowLabel)
            {
                continue;
            }

            var anchor = GetNearestLabelAnchor(annotation.Marker, annotation.LabelBounds);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)), 1), annotation.Marker, anchor);
            dc.DrawRoundedRectangle(surface, new Pen(brush, 1.1), annotation.LabelBounds, 6, 6);
            DrawText(
                dc,
                annotation.ShortLabel,
                new Point(annotation.LabelBounds.Left + 8, annotation.LabelBounds.Top + 4),
                primaryText,
                10.5,
                FontWeights.SemiBold);
            _achievementHits.Add(new AchievementHitTarget(
                annotation.LabelBounds,
                annotation.Series,
                annotation.PointIndex,
                0));
        }
    }

    private static void DrawTargetLabel(
        DrawingContext dc,
        Rect rect,
        double? targetY,
        decimal target,
        IEnumerable<Rect> achievementLabelBounds)
    {
        if (targetY is null || target <= 0m)
        {
            return;
        }

        var brush = ResourceBrush("WarningBrush", Color.FromRgb(245, 158, 11));
        var surface = ResourceBrush("SurfaceBackgroundBrush", Color.FromRgb(23, 32, 51));
        var occupied = achievementLabelBounds.ToList();
        var candidates = new[]
        {
            new Rect(rect.Left + 8, targetY.Value - 31, 190, 25),
            new Rect(rect.Left + 8, targetY.Value + 7, 190, 25),
            new Rect(rect.Left + 8, rect.Top + 8, 190, 25),
            new Rect(rect.Left + 8, rect.Bottom - 33, 190, 25)
        };
        var available = candidates
            .Select(candidate => ClampToPlot(candidate, rect))
            .Where(candidate => occupied.All(existing => !InflateRect(existing, 5).IntersectsWith(candidate)))
            .Cast<Rect?>()
            .FirstOrDefault();
        var labelBounds = available ?? ClampToPlot(candidates[0], rect);

        dc.DrawRoundedRectangle(surface, new Pen(brush, 1.1), labelBounds, 6, 6);
        DrawText(dc, $"目標資産 {FormatJpy(target)}", new Point(labelBounds.Left + 8, labelBounds.Top + 4), brush, 10.5, FontWeights.SemiBold);
    }

    private string BuildAchievementToolTip(MutualFundScenarioChartSeriesViewModel series, int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= series.Points.Count)
        {
            return string.Empty;
        }

        var point = series.Points[pointIndex];
        var start = series.Points[0].YearMonth;
        var elapsedMonths = Math.Max(0, (point.YearMonth.Year - start.Year) * 12 + point.YearMonth.Month - start.Month);
        var duration = $"{elapsedMonths / 12}年{elapsedMonths % 12}か月";
        return string.Join(Environment.NewLine, new[]
        {
            $"シナリオ: {series.Name}",
            $"到達年月: {point.YearMonth:yyyy年MM月}",
            $"資産額: {FormatJpy(point.MarketValueJpy)}",
            $"累計積立額: {FormatJpy(point.CumulativeContributionJpy)}",
            $"運用益: {FormatSignedJpy(point.UnrealizedGainJpy)}",
            $"目標達成まで: {duration}"
        });
    }

    private static Rect FindAvailableLabelBounds(
        Point marker,
        double baseY,
        double width,
        double height,
        double preferredVerticalOffset,
        double preferredHorizontalOffset,
        Rect plot,
        IReadOnlyList<Rect> occupied)
    {
        var sideX = marker.X + 10;
        if (sideX + width > plot.Right - 4)
        {
            sideX = marker.X - width - 10;
        }

        var verticalOffsets = new[] { preferredVerticalOffset, -60d, -30d, 30d, 60d, -90d, 90d }
            .Distinct()
            .ToArray();
        var horizontalOffsets = new[] { preferredHorizontalOffset, preferredHorizontalOffset - 28, preferredHorizontalOffset + 28, 0d }
            .Distinct()
            .ToArray();
        Rect fallback = Rect.Empty;
        foreach (var verticalOffset in verticalOffsets)
        {
            foreach (var horizontalOffset in horizontalOffsets)
            {
                var candidate = ClampToPlot(
                    new Rect(sideX + horizontalOffset, baseY + verticalOffset - height / 2d, width, height),
                    plot);
                fallback = candidate;
                if (occupied.All(existing => !InflateRect(existing, 5).IntersectsWith(candidate)))
                {
                    return candidate;
                }
            }
        }

        return fallback;
    }

    private static double[] GetVerticalOffsets(int count) => count switch
    {
        <= 0 => Array.Empty<double>(),
        1 => new[] { -34d },
        2 => new[] { -46d, 42d },
        3 => new[] { -60d, -30d, 38d },
        4 => new[] { -62d, -32d, 32d, 62d },
        _ => Enumerable.Range(0, count).Select(index => -72d + index * 144d / Math.Max(1, count - 1)).ToArray()
    };

    private static Point GetNearestLabelAnchor(Point marker, Rect labelBounds)
    {
        var x = Math.Clamp(marker.X, labelBounds.Left, labelBounds.Right);
        if (marker.Y < labelBounds.Top)
        {
            return new Point(x, labelBounds.Top);
        }
        if (marker.Y > labelBounds.Bottom)
        {
            return new Point(x, labelBounds.Bottom);
        }
        return marker.X < labelBounds.Left
            ? new Point(labelBounds.Left, marker.Y)
            : new Point(labelBounds.Right, marker.Y);
    }

    private static Rect ClampToPlot(Rect bounds, Rect plot)
    {
        var x = Math.Clamp(bounds.X, plot.Left + 4, Math.Max(plot.Left + 4, plot.Right - bounds.Width - 4));
        var y = Math.Clamp(bounds.Y, plot.Top + 4, Math.Max(plot.Top + 4, plot.Bottom - bounds.Height - 4));
        return new Rect(x, y, bounds.Width, bounds.Height);
    }

    private static Rect InflateRect(Rect bounds, double amount)
    {
        bounds.Inflate(amount, amount);
        return bounds;
    }

    private static string GetShortScenarioName(string key) => key switch
    {
        "Conservative" => "保守",
        "Standard" => "標準",
        "Aggressive" => "積極",
        "Actual" => "実績",
        _ => key
    };

    private static Pen GetSeriesPen(string key, Brush brush)
    {
        var pen = new Pen(brush, string.Equals(key, "Actual", StringComparison.OrdinalIgnoreCase) ? 3.4 : 2.3);
        if (string.Equals(key, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            pen.DashStyle = DashStyles.Dash;
        }
        else if (string.Equals(key, "Aggressive", StringComparison.OrdinalIgnoreCase))
        {
            pen.DashStyle = new DashStyle(new[] { 7d, 3d, 1.5d, 3d }, 0d);
        }
        return pen;
    }

    private static void DrawScenarioMarker(
        DrawingContext dc,
        string key,
        Point center,
        Brush brush,
        double size,
        bool filled)
    {
        var fill = filled ? brush : ResourceBrush("ElevatedSurfaceBrush", Color.FromRgb(30, 41, 59));
        var pen = new Pen(filled ? Brushes.White : brush, filled ? 1.4 : 1.1);
        if (string.Equals(key, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            dc.DrawRectangle(fill, pen, new Rect(center.X - size, center.Y - size, size * 2, size * 2));
            return;
        }
        if (string.Equals(key, "Aggressive", StringComparison.OrdinalIgnoreCase))
        {
            var triangle = new StreamGeometry();
            using (var context = triangle.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - size - 1), true, true);
                context.LineTo(new Point(center.X + size + 1, center.Y + size), true, false);
                context.LineTo(new Point(center.X - size - 1, center.Y + size), true, false);
            }
            triangle.Freeze();
            dc.DrawGeometry(fill, pen, triangle);
            return;
        }
        if (string.Equals(key, "Actual", StringComparison.OrdinalIgnoreCase))
        {
            var diamond = new StreamGeometry();
            using (var context = diamond.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - size - 1), true, true);
                context.LineTo(new Point(center.X + size + 1, center.Y), true, false);
                context.LineTo(new Point(center.X, center.Y + size + 1), true, false);
                context.LineTo(new Point(center.X - size - 1, center.Y), true, false);
            }
            diamond.Freeze();
            dc.DrawGeometry(fill, pen, diamond);
            return;
        }
        dc.DrawEllipse(fill, pen, center, size, size);
    }

    private void DrawHover(
        DrawingContext dc,
        IReadOnlyList<MutualFundScenarioChartSeriesViewModel> visible,
        Rect rect,
        double min,
        double max,
        int start,
        int end,
        double step)
    {
        if (_hoverSourceIndex is not { } index || index < start || index > end)
        {
            return;
        }

        var x = rect.Left + step * (index - start);
        var guide = ResourceBrush("AccentBlueBrush", Color.FromRgb(56, 189, 248));
        dc.DrawLine(new Pen(guide, 1) { DashStyle = DashStyles.Dot }, new Point(x, rect.Top), new Point(x, rect.Bottom));
        foreach (var series in visible.Where(series => index < series.Points.Count))
        {
            var point = series.Points[index];
            var marker = new Point(x, MapY(rect, min, max, point.MarketValueJpy));
            var brush = new SolidColorBrush(GetSeriesColor(series.Key));
            DrawScenarioMarker(dc, series.Key, marker, brush, 4.5, false);
        }
    }

    private void DrawLegend(DrawingContext dc, Rect bounds, Brush text)
    {
        _legendHits.Clear();
        var x = 18d;
        var y = bounds.Bottom - 32;
        foreach (var series in _renderedSeries)
        {
            var brush = new SolidColorBrush(GetSeriesColor(series.Key));
            var itemRect = new Rect(x, y, 170, 22);
            _legendHits.Add((itemRect, series));
            dc.DrawRoundedRectangle(series.IsVisible ? brush : Brushes.Transparent, new Pen(brush, 1), new Rect(x, y + 5, 16, 9), 3, 3);
            DrawText(dc, $"{series.Name}  {series.AnnualReturnRate}", new Point(x + 23, y), text, 11,
                series.IsVisible ? FontWeights.SemiBold : FontWeights.Normal);
            x += 180;
        }
    }

    private string BuildToolTip(int index)
    {
        var visible = _renderedSeries.Where(x => x.IsVisible && x.Points.Count > index).ToList();
        if (visible.Count == 0)
        {
            return string.Empty;
        }

        var month = visible[0].Points[index].YearMonth;
        var lines = new List<string> { $"{month:yyyy年MM月}" };
        foreach (var series in visible)
        {
            var point = series.Points[index];
            lines.Add($"{series.Name}  {FormatJpy(point.MarketValueJpy)}");
            lines.Add($"  累計積立 {FormatJpy(point.CumulativeContributionJpy)} / 運用益 {FormatSignedJpy(point.UnrealizedGainJpy)} / 達成率 {point.TargetAchievementRate:N1}%");
        }
        lines.Add($"目標資産  {FormatJpy(TargetAmount)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static int FindPointIndex(IReadOnlyList<MutualFundScenarioChartPointViewModel> points, DateTime month)
    {
        for (var index = 0; index < points.Count; index++)
        {
            if (points[index].YearMonth == month)
            {
                return index;
            }
        }
        return -1;
    }

    private static Color GetSeriesColor(string key) =>
        SeriesColors.TryGetValue(key, out var color) ? color : Color.FromRgb(56, 189, 248);

    private static double MapY(Rect rect, double min, double max, decimal value) =>
        rect.Bottom - ((double)value - min) / Math.Max(0.0001, max - min) * rect.Height;

    private static string FormatJpy(decimal value) => $"{value:N0}円";
    private static string FormatSignedJpy(decimal value) => $"{(value > 0m ? "+" : string.Empty)}{value:N0}円";

    private static void DrawText(DrawingContext dc, string text, Point origin, Brush brush, double size, FontWeight? weight = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var pixelsPerDip = Application.Current?.MainWindow is { } window
            ? VisualTreeHelper.GetDpi(window).PixelsPerDip
            : 1d;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Yu Gothic UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip);
        dc.DrawText(formatted, origin);
    }

    private static double MeasureTextWidth(string text, double size, FontWeight weight)
    {
        var pixelsPerDip = Application.Current?.MainWindow is { } window
            ? VisualTreeHelper.GetDpi(window).PixelsPerDip
            : 1d;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Yu Gothic UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            Brushes.White,
            pixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private sealed record AchievementCandidate(
        MutualFundScenarioChartSeriesViewModel Series,
        int PointIndex,
        Point Marker,
        string ShortName);

    private sealed record AchievementAnnotation(
        MutualFundScenarioChartSeriesViewModel Series,
        int PointIndex,
        Point Marker,
        bool ShowLabel,
        Rect LabelBounds,
        string ShortLabel);

    private sealed record AchievementHitTarget(
        Rect Bounds,
        MutualFundScenarioChartSeriesViewModel Series,
        int PointIndex,
        int Priority);

    private static Brush ResourceBrush(string resourceKey, Color fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback);
    }
}
