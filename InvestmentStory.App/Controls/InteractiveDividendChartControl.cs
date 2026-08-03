using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.Controls;

public abstract class InteractiveDividendChartControl : FrameworkElement
{
    public static readonly DependencyProperty InteractionStateProperty = DependencyProperty.Register(
        nameof(InteractionState), typeof(DividendChartInteractionState), typeof(InteractiveDividendChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnInteractionStateChanged));

    private readonly List<ChartHitTarget> _hitTargets = new();
    private readonly ToolTip _interactiveToolTip;
    private ChartHitTarget? _hoveredTarget;
    protected Point? HoverPosition { get; private set; }

    protected InteractiveDividendChartControl()
    {
        _interactiveToolTip = new ToolTip
        {
            Placement = PlacementMode.MousePoint,
            PlacementTarget = this,
            StaysOpen = true,
            IsOpen = false
        };
        ToolTip = _interactiveToolTip;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 60_000);
        Focusable = true;
        MouseMove += HandleMouseMove;
        MouseLeave += (_, _) =>
        {
            _interactiveToolTip.IsOpen = false;
            _hoveredTarget = null;
            HoverPosition = null;
            InvalidateVisual();
        };
        Unloaded += (_, _) => _interactiveToolTip.IsOpen = false;
        MouseLeftButtonDown += HandleMouseClick;
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            InteractionState?.Clear();
            args.Handled = true;
        };
    }

    public DividendChartInteractionState? InteractionState
    {
        get => (DividendChartInteractionState?)GetValue(InteractionStateProperty);
        set => SetValue(InteractionStateProperty, value);
    }

    protected void BeginInteractiveRender() => _hitTargets.Clear();

    protected void AddHitTarget(
        Rect bounds,
        string tooltip,
        string ticker = "",
        int? month = null,
        DividendScheduleStatus? status = null,
        string seriesKey = "",
        bool isLegend = false) =>
        _hitTargets.Add(new ChartHitTarget(bounds, null, tooltip, ticker, month, status, seriesKey, isLegend));

    protected void AddGeometryHitTarget(
        Geometry geometry,
        string tooltip,
        string ticker = "",
        int? month = null,
        DividendScheduleStatus? status = null,
        string seriesKey = "") =>
        _hitTargets.Add(new ChartHitTarget(geometry.Bounds, geometry, tooltip, ticker, month, status, seriesKey, false));

    protected double InteractionOpacity(string? ticker, int? month, DividendScheduleStatus? status) =>
        InteractionState?.OpacityFor(ticker, month, status) ?? 1d;
    protected bool IsInteractionSelected(string? ticker, int? month, DividendScheduleStatus? status) =>
        InteractionState?.IsSelected(ticker, month, status) ?? false;
    protected bool IsInteractionHovered(string? ticker, int? month, DividendScheduleStatus? status) =>
        Matches(_hoveredTarget, ticker, month, status);
    protected bool IsSeriesVisible(string seriesKey) => InteractionState?.IsSeriesVisible(seriesKey) ?? true;

    protected static Brush WithOpacity(Brush source, double opacity)
    {
        var brush = source.CloneCurrentValue();
        brush.Opacity = Math.Clamp(opacity, 0d, 1d);
        return brush;
    }

    private void HandleMouseMove(object sender, MouseEventArgs args)
    {
        HoverPosition = args.GetPosition(this);
        var hit = FindHit(HoverPosition.Value);
        _hoveredTarget = hit;
        if (hit is null)
        {
            _interactiveToolTip.IsOpen = false;
        }
        else
        {
            _interactiveToolTip.Content = hit.Tooltip;
            _interactiveToolTip.IsOpen = true;
        }
        Cursor = hit is null ? Cursors.Arrow : Cursors.Hand;
        InvalidateVisual();
    }

    private void HandleMouseClick(object sender, MouseButtonEventArgs args)
    {
        Focus();
        var hit = FindHit(args.GetPosition(this));
        if (hit is null)
        {
            InteractionState?.Clear();
            return;
        }
        if (hit.IsLegend)
            InteractionState?.ToggleSeries(hit.SeriesKey);
        else
            InteractionState?.Select(hit.Ticker, hit.Month, hit.Status, hit.Tooltip);
        args.Handled = true;
    }

    private ChartHitTarget? FindHit(Point point) => _hitTargets.LastOrDefault(x =>
        x.Bounds.Contains(point) && (x.Geometry is null || x.Geometry.FillContains(point)));

    private static bool Matches(
        ChartHitTarget? target,
        string? ticker,
        int? month,
        DividendScheduleStatus? status) =>
        target is not null &&
        (string.IsNullOrWhiteSpace(ticker) || string.Equals(target.Ticker, ticker, StringComparison.OrdinalIgnoreCase)) &&
        (month is null || target.Month == month) &&
        (status is null || target.Status == status);

    private static void OnInteractionStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (InteractiveDividendChartControl)dependencyObject;
        if (args.OldValue is INotifyPropertyChanged oldState) oldState.PropertyChanged -= control.StatePropertyChanged;
        if (args.NewValue is INotifyPropertyChanged newState) newState.PropertyChanged += control.StatePropertyChanged;
        control.InvalidateVisual();
    }
    private void StatePropertyChanged(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();

    private sealed record ChartHitTarget(
        Rect Bounds,
        Geometry? Geometry,
        string Tooltip,
        string Ticker,
        int? Month,
        DividendScheduleStatus? Status,
        string SeriesKey,
        bool IsLegend);
}
