namespace InvestmentStory.Core.Models;

public static class DataDisplayModes
{
    public const string Normal = "Normal";
    public const string Sample = "Sample";

    public static bool IsSample(string value) =>
        string.Equals(value, Sample, StringComparison.OrdinalIgnoreCase);
}
