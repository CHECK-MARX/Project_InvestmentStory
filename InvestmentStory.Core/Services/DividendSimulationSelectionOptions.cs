using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class DividendSimulationSelectionOptions
{
    private static readonly SelectionOption<string>[] DefaultBrokerOptions =
    [
        new() { Value = "SBI証券", DisplayName = "SBI証券" },
        new() { Value = "野村證券", DisplayName = "野村證券" },
        new() { Value = "その他", DisplayName = "その他" }
    ];

    public static IReadOnlyList<SelectionOption<string>> CountryOptions { get; } =
    [
        new() { Value = "Japan", DisplayName = "日本" },
        new() { Value = "United States", DisplayName = "米国" },
        new() { Value = "Other", DisplayName = "その他" }
    ];

    public static IReadOnlyList<string> CurrencyOptions { get; } =
    [
        "JPY",
        "USD"
    ];

    public static IReadOnlyList<SelectionOption<string>> AccountOptions { get; } =
    [
        new() { Value = AccountTypes.Specific, DisplayName = "特定口座" },
        new() { Value = AccountTypes.General, DisplayName = "一般口座" },
        new() { Value = AccountTypes.NisaGrowth, DisplayName = "NISA成長投資枠" },
        new() { Value = AccountTypes.NisaAccumulation, DisplayName = "NISAつみたて投資枠" },
        new() { Value = AccountTypes.NisaLegacy, DisplayName = "旧NISA" },
        new() { Value = AccountTypes.Unknown, DisplayName = "その他" }
    ];

    public static IReadOnlyList<SelectionOption<string>> PurchaseModeOptions { get; } =
    [
        new() { Value = DividendGrowthPurchaseModes.OneTime, DisplayName = "今回だけ購入" },
        new() { Value = DividendGrowthPurchaseModes.ContinueMonthly, DisplayName = "毎月継続" },
        new() { Value = DividendGrowthPurchaseModes.None, DisplayName = "購入なし" }
    ];

    public static IReadOnlyList<SelectionOption<string>> BrokerOptions { get; } =
        BuildBrokerOptions();

    public static IReadOnlyList<SelectionOption<string>> BuildBrokerOptions(params string?[] preferredBrokers)
    {
        var options = new List<SelectionOption<string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var broker in preferredBrokers.Concat(DefaultBrokerOptions.Select(x => x.Value)))
        {
            var normalized = NormalizeBroker(broker);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var key = NormalizeBrokerKey(normalized);
            if (!seen.Add(key))
            {
                continue;
            }

            options.Add(new SelectionOption<string>
            {
                Value = normalized,
                DisplayName = ToBrokerDisplayName(normalized)
            });
        }

        return options;
    }

    public static string NormalizeBroker(string? broker)
    {
        if (string.IsNullOrWhiteSpace(broker))
        {
            return string.Empty;
        }

        var value = broker.Trim();
        var key = NormalizeBrokerKey(value);

        if (key.Contains("sbi", StringComparison.OrdinalIgnoreCase))
        {
            return "SBI証券";
        }

        if (key.Contains("野村", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("nomura", StringComparison.OrdinalIgnoreCase))
        {
            return "野村證券";
        }

        if (key.Contains("rakuten", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("楽天", StringComparison.OrdinalIgnoreCase))
        {
            return "楽天証券";
        }

        if (key is "その他" or "other")
        {
            return "その他";
        }

        return value;
    }

    public static string NormalizeCountry(string? country, string? currency, string? ticker)
    {
        var value = country?.Trim() ?? string.Empty;
        var symbol = ticker?.Trim() ?? string.Empty;

        if (string.Equals(currency, "JPY", StringComparison.OrdinalIgnoreCase) ||
            symbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ||
            (symbol.Length is 4 or 5 && symbol.All(char.IsDigit)) ||
            value.Contains("Japan", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("日本", StringComparison.OrdinalIgnoreCase))
        {
            return "Japan";
        }

        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("United States", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("UnitedStates", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("USA", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("米国", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("アメリカ", StringComparison.OrdinalIgnoreCase))
        {
            return "United States";
        }

        return string.IsNullOrWhiteSpace(value) ? "Other" : value;
    }

    public static string ToCountryDisplayName(string country) =>
        NormalizeCountry(country, string.Empty, string.Empty) switch
        {
            "Japan" => "日本",
            "United States" => "米国",
            _ => "その他"
        };

    private static string ToBrokerDisplayName(string broker)
    {
        var key = NormalizeBrokerKey(broker);
        if (key.Contains("sbi", StringComparison.OrdinalIgnoreCase))
        {
            return "SBI証券";
        }

        if (key.Contains("野村", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("nomura", StringComparison.OrdinalIgnoreCase))
        {
            return "野村證券";
        }

        return string.IsNullOrWhiteSpace(broker) ? "その他" : broker;
    }

    private static string NormalizeBrokerKey(string broker) =>
        broker
            .Replace("證", "証", StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
}
