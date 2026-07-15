using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InvestmentStory.App.Controls;

public sealed partial class AnimatedMetricTextBlock : TextBlock
{
    public static readonly DependencyProperty TargetTextProperty = DependencyProperty.Register(
        nameof(TargetText),
        typeof(string),
        typeof(AnimatedMetricTextBlock),
        new FrameworkPropertyMetadata(string.Empty, OnTargetTextChanged));

    public static readonly DependencyProperty DurationMillisecondsProperty = DependencyProperty.Register(
        nameof(DurationMilliseconds),
        typeof(double),
        typeof(AnimatedMetricTextBlock),
        new FrameworkPropertyMetadata(650d));

    private readonly Stopwatch _stopwatch = new();
    private decimal _from;
    private decimal _to;
    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private int _decimalPlaces;
    private bool _useGrouping;
    private bool _explicitPlus;

    public AnimatedMetricTextBlock()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string TargetText
    {
        get => (string)GetValue(TargetTextProperty);
        set => SetValue(TargetTextProperty, value);
    }

    public double DurationMilliseconds
    {
        get => (double)GetValue(DurationMillisecondsProperty);
        set => SetValue(DurationMillisecondsProperty, value);
    }

    private static void OnTargetTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (AnimatedMetricTextBlock)dependencyObject;
        control.BeginAnimation(args.NewValue as string ?? string.Empty);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => BeginAnimation(TargetText);

    private void OnUnloaded(object sender, RoutedEventArgs e) => StopAnimation();

    private void BeginAnimation(string targetText)
    {
        if (!IsLoaded || !TryParseTarget(targetText, out var target))
        {
            StopAnimation();
            Text = targetText;
            return;
        }

        _from = TryParseTarget(Text, out var current) ? current : 0m;
        _to = target;
        if (_from == _to || DurationMilliseconds <= 0d)
        {
            StopAnimation();
            Text = targetText;
            return;
        }

        StopAnimation();
        _stopwatch.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    private bool TryParseTarget(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = MetricNumberRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var numberText = match.Groups["number"].Value;
        if (!decimal.TryParse(
                numberText.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value))
        {
            return false;
        }

        _prefix = match.Groups["prefix"].Value;
        _suffix = match.Groups["suffix"].Value;
        _explicitPlus = numberText.StartsWith('+');
        _useGrouping = numberText.Contains(',');
        var decimalIndex = numberText.IndexOf('.');
        _decimalPlaces = decimalIndex < 0 ? 0 : numberText.Length - decimalIndex - 1;
        return true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var progress = Math.Clamp(_stopwatch.Elapsed.TotalMilliseconds / DurationMilliseconds, 0d, 1d);
        var eased = 1d - Math.Pow(1d - progress, 3d);
        var value = _from + (_to - _from) * (decimal)eased;
        var format = (_useGrouping ? "N" : "F") + _decimalPlaces;
        var formatted = value.ToString(format, CultureInfo.GetCultureInfo("ja-JP"));
        if (_explicitPlus && value > 0m)
        {
            formatted = "+" + formatted;
        }

        Text = _prefix + formatted + _suffix;
        if (progress < 1d)
        {
            return;
        }

        Text = TargetText;
        StopAnimation();
    }

    private void StopAnimation()
    {
        CompositionTarget.Rendering -= OnRendering;
        _stopwatch.Stop();
    }

    [GeneratedRegex(@"^(?<prefix>.*?)(?<number>[+-]?\d[\d,]*(?:\.\d+)?)(?<suffix>.*)$")]
    private static partial Regex MetricNumberRegex();
}
