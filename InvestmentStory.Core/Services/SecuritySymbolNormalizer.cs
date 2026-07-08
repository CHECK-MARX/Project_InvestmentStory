namespace InvestmentStory.Core.Services;

public static class SecuritySymbolNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> TickerAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EURN"] = "CMBT"
        };

    private static readonly IReadOnlyDictionary<string, string> NameOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CMBT"] = "CMB テック",
            ["EURN"] = "CMB テック"
        };

    public static string NormalizeTicker(string ticker)
    {
        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            normalized = normalized[..^2];
        }

        return TickerAliases.TryGetValue(normalized, out var currentTicker)
            ? currentTicker
            : normalized;
    }

    public static string NormalizeName(string ticker, string name)
    {
        var normalizedTicker = NormalizeTicker(ticker);
        return NameOverrides.TryGetValue(normalizedTicker, out var currentName)
            ? currentName
            : name.Trim();
    }

    public static string NormalizeBroker(string broker)
    {
        var normalized = broker.Trim().Replace("證", "証");
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }
}
