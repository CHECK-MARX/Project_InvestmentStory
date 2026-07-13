using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class AccountTypeNormalizer
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AccountTypes.Unknown;
        }

        var normalized = value.Trim()
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Equals(AccountTypes.NisaGrowth, StringComparison.OrdinalIgnoreCase))
        {
            return AccountTypes.NisaGrowth;
        }

        if (normalized.Equals(AccountTypes.NisaAccumulation, StringComparison.OrdinalIgnoreCase))
        {
            return AccountTypes.NisaAccumulation;
        }

        if (normalized.Equals(AccountTypes.NisaLegacy, StringComparison.OrdinalIgnoreCase))
        {
            return AccountTypes.NisaLegacy;
        }

        if (normalized.Equals(AccountTypes.Specific, StringComparison.OrdinalIgnoreCase))
        {
            return AccountTypes.Specific;
        }

        if (normalized.Equals(AccountTypes.General, StringComparison.OrdinalIgnoreCase))
        {
            return AccountTypes.General;
        }

        if (IsLegacyNisaLabel(normalized))
        {
            return AccountTypes.NisaLegacy;
        }

        if (normalized.Contains("つみたて", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("積立", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("NISAつみたて", StringComparison.OrdinalIgnoreCase))
        {
            return AccountTypes.NisaAccumulation;
        }

        if (normalized.Contains("成長投資", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("新NISA", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("NISA", StringComparison.OrdinalIgnoreCase))
        {
            return AccountTypes.NisaGrowth;
        }

        if (normalized.Contains("特定", StringComparison.Ordinal))
        {
            return AccountTypes.Specific;
        }

        if (normalized.Contains("一般", StringComparison.Ordinal))
        {
            return AccountTypes.General;
        }

        return AccountTypes.Unknown;
    }

    private static bool IsLegacyNisaLabel(string normalized) =>
        normalized.Contains("旧NISA", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("旧ニーサ", StringComparison.OrdinalIgnoreCase) ||
        (normalized.Contains("旧", StringComparison.Ordinal) &&
            (normalized.Contains("NISA", StringComparison.OrdinalIgnoreCase) ||
             normalized.Contains("ニーサ", StringComparison.OrdinalIgnoreCase)));

    public static string NormalizeForMutualFund(string accountType, string custodyType)
    {
        var custody = Normalize(custodyType);
        if (custody != AccountTypes.Unknown)
        {
            return custody;
        }

        return Normalize(accountType);
    }
}
