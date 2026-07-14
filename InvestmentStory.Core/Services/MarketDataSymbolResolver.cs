using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class MarketDataSymbolResolver
{
    public static string NormalizeInput(string query)
    {
        var normalized = query.Trim().Replace("　", " ").ToUpperInvariant();
        return normalized.EndsWith(".T", StringComparison.Ordinal)
            ? normalized[..^2]
            : normalized;
    }

    public static string ToProviderSymbol(string symbol)
    {
        var normalized = symbol.Trim().Replace("　", " ").ToUpperInvariant();
        if (LooksLikeJapaneseTicker(normalized) && !normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            return $"{normalized[..4]}.T";
        }

        return normalized;
    }

    public static string ToDisplaySymbol(string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        return normalized.EndsWith(".T", StringComparison.Ordinal) ? normalized[..^2] : normalized;
    }

    public static bool LooksLikeJapaneseTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return false;
        }

        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            normalized = normalized[..^2];
        }

        return normalized.Length == 4 && normalized.All(char.IsDigit);
    }

    public static string NormalizeCurrency(string value)
    {
        if (value.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("円", StringComparison.OrdinalIgnoreCase))
        {
            return "JPY";
        }

        return value.Equals("USD", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ドル", StringComparison.OrdinalIgnoreCase)
            ? "USD"
            : string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();
    }

    public static string NormalizeCountry(string value, string currency, string ticker)
    {
        if (currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
            ticker.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ||
            LooksLikeJapaneseTicker(ticker) ||
            value.Contains("Japan", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("日本", StringComparison.OrdinalIgnoreCase))
        {
            return "Japan";
        }

        if (currency.Equals("USD", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("United States", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("米国", StringComparison.OrdinalIgnoreCase))
        {
            return "United States";
        }

        return string.IsNullOrWhiteSpace(value) ? "Other" : value.Trim();
    }

    public static MarketDataQuoteValidation ValidateQuote(MarketDataQuote? quote)
    {
        if (quote is null)
        {
            return MarketDataQuoteValidation.Failed("銘柄が見つかりません。上場廃止、シンボル変更、入力誤りの可能性があります。");
        }

        var name = quote.Name.Trim();
        var price = quote.CurrentPrice;
        var displaySymbol = ToDisplaySymbol(quote.Symbol);
        var hasName = !string.IsNullOrWhiteSpace(name) &&
            !name.Equals(displaySymbol, StringComparison.OrdinalIgnoreCase);
        var hasPrice = price is > 0m;
        if (hasName && hasPrice)
        {
            return MarketDataQuoteValidation.Success();
        }

        if (hasName || hasPrice)
        {
            return MarketDataQuoteValidation.Partial(hasName
                ? "株価を取得できませんでした。"
                : "銘柄名を取得できませんでした。");
        }

        return MarketDataQuoteValidation.Failed("銘柄が見つかりません。上場廃止、シンボル変更、入力誤りの可能性があります。");
    }
}

public sealed record MarketDataQuoteValidation(string StatusKind, string Message)
{
    public bool IsSuccess => StatusKind == MarketDataFetchStatusKinds.Success;
    public bool IsPartial => StatusKind == MarketDataFetchStatusKinds.Partial;
    public bool IsFailed => StatusKind == MarketDataFetchStatusKinds.Failed;

    public static MarketDataQuoteValidation Success() =>
        new(MarketDataFetchStatusKinds.Success, "銘柄名・株価・配当を取得");

    public static MarketDataQuoteValidation Partial(string message) =>
        new(MarketDataFetchStatusKinds.Partial, message);

    public static MarketDataQuoteValidation Failed(string message) =>
        new(MarketDataFetchStatusKinds.Failed, message);
}

public static class MarketDataFetchStatusKinds
{
    public const string Idle = "Idle";
    public const string Fetching = "Fetching";
    public const string Success = "Success";
    public const string Partial = "Partial";
    public const string Failed = "Failed";
}
