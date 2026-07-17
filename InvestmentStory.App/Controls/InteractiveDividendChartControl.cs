using System.ComponentModel;
using System.Windows;
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
    protected Point? HoverPosition { get; private set; }

    protected InteractiveDividendChartControl()
    {
        Focusable = true;
        MouseMove += HandleMouseMove;
        MouseLeave += (_, _) =>
        {
            ToolTip = null;
            HoverPosition = null;
            InvalidateVisual();
        };
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
        ToolTip = hit?.Tooltip;
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
