using System.Windows;

namespace InvestmentStory.App.Infrastructure;

public static class UiDensityManager
{
    public static string Normalize(string? value) => value is "Compact" or "LargeText" ? value : "Standard";

    public static double FontSize(string? value) => Normalize(value) switch
    {
        "Compact" => 12,
        "LargeText" => 17,
        _ => 13
    };

    public static void Apply(string? value)
    {
        if (Application.Current is null) return;
        var density = Normalize(value);
        Application.Current.Resources["UiGridRowHeight"] = density switch
        {
            "Compact" => 34d,
            "LargeText" => 54d,
            _ => 42d
        };
        Application.Current.Resources["UiGridHeaderHeight"] = density switch
        {
            "Compact" => 36d,
            "LargeText" => 54d,
            _ => 42d
        };
        Application.Current.Resources["UiInputHeight"] = density == "LargeText" ? 44d : 34d;
        Application.Current.Resources["UiFontSize"] = FontSize(density);
        Application.Current.Resources["UiSectionTitleFontSize"] = density switch
        {
            "Compact" => 18d,
            "LargeText" => 24d,
            _ => 20d
        };
        Application.Current.Resources["UiLabelFontSize"] = density switch
        {
            "Compact" => 12d,
            "LargeText" => 16d,
            _ => 13d
        };
        Application.Current.Resources["UiMetricValueFontSize"] = density switch
        {
            "Compact" => 25d,
            "LargeText" => 34d,
            _ => 28d
        };
        Application.Current.Resources["UiSubtleFontSize"] = density switch
        {
            "Compact" => 11d,
            "LargeText" => 15d,
            _ => 12d
        };
    }
}
