namespace InvestmentStory.Core.Models;

public static class AccountTypes
{
    public const string NisaGrowth = "NisaGrowth";
    public const string NisaAccumulation = "NisaAccumulation";
    public const string NisaLegacy = "NisaLegacy";
    public const string Specific = "Specific";
    public const string General = "General";
    public const string Unknown = "Unknown";

    public static bool IsNisa(string accountType) =>
        string.Equals(accountType, NisaGrowth, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(accountType, NisaAccumulation, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(accountType, NisaLegacy, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(accountType, "NISA", StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(string accountType) =>
        accountType switch
        {
            NisaGrowth => "NISA成長投資枠",
            NisaAccumulation => "NISAつみたて投資枠",
            NisaLegacy => "旧NISA",
            Specific => "特定口座",
            General => "一般口座",
            _ => "その他"
        };
}
