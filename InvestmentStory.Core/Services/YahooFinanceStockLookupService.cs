using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class YahooFinanceStockLookupService : IStockLookupService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    static YahooFinanceStockLookupService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 InvestmentStory/1.0");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
    }

    public StockLookupResult? Find(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        try
        {
            var symbol = ResolveSymbol(query);
            var quote = FindQuote(symbol);
            if (quote is not null)
            {
                symbol = quote.Symbol;
            }

            var chart = GetChart(symbol);
            if (chart is null && quote is null)
            {
                return null;
            }

            var resolvedSymbol = chart?.Symbol ?? quote?.Symbol ?? symbol;
            var currency = chart?.Currency ?? quote?.Currency ?? InferCurrency(resolvedSymbol);
            var market = chart?.Market ?? quote?.Market ?? string.Empty;
            var name = chart?.Name ?? quote?.Name ?? resolvedSymbol;

            return new StockLookupResult(
                NormalizeTickerForApp(resolvedSymbol),
                name,
                InferCountry(currency, resolvedSymbol, market),
                currency,
                market,
                "Yahoo Finance API",
                chart?.CurrentPrice);
        }
        catch
        {
            return null;
        }
    }

    private static QuoteSearchResult? FindQuote(string query)
    {
        var url = $"https://query1.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=5&newsCount=0";
        var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var quote in quotes.EnumerateArray())
        {
            if (!quote.TryGetProperty("quoteType", out var quoteType) ||
                !string.Equals(quoteType.GetString(), "EQUITY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var symbol = GetString(quote, "symbol");
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var name = FirstNonEmpty(
                GetString(quote, "longname"),
                GetString(quote, "shortname"),
                symbol);
            var market = FirstNonEmpty(
                GetString(quote, "exchDisp"),
                GetString(quote, "exchange"));

            return new QuoteSearchResult(symbol, name, null, market);
        }

        return null;
    }

    private static ChartResult? GetChart(string symbol)
    {
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=1d";
        var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var result = results[0];
        if (!result.TryGetProperty("meta", out var meta))
        {
            return null;
        }

        var resolvedSymbol = FirstNonEmpty(GetString(meta, "symbol"), symbol);
        var name = FirstNonEmpty(GetString(meta, "longName"), GetString(meta, "shortName"), resolvedSymbol);
        var currency = FirstNonEmpty(GetString(meta, "currency"), InferCurrency(resolvedSymbol));
        var market = FirstNonEmpty(GetString(meta, "fullExchangeName"), GetString(meta, "exchangeName"));
        var currentPrice = GetDecimal(meta, "regularMarketPrice");

        return new ChartResult(resolvedSymbol, name, currency, market, currentPrice);
    }

    private static string ResolveSymbol(string query)
    {
        var normalized = query.Trim().ToUpperInvariant();
        if (LooksLikeJapaneseTicker(normalized) && !normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            return $"{normalized}.T";
        }

        return normalized;
    }

    private static string NormalizeTickerForApp(string symbol)
    {
        return symbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase)
            ? symbol[..^2]
            : symbol;
    }

    private static string InferCurrency(string symbol)
    {
        return symbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ? "JPY" : "USD";
    }

    private static string InferCountry(string currency, string symbol, string market)
    {
        if (currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
            symbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ||
            market.Contains("Tokyo", StringComparison.OrdinalIgnoreCase) ||
            market.Contains("JPX", StringComparison.OrdinalIgnoreCase))
        {
            return "日本";
        }

        return "米国";
    }

    private static bool LooksLikeJapaneseTicker(string ticker)
    {
        var normalized = ticker.EndsWith(".T", StringComparison.Ordinal) ? ticker[..^2] : ticker;
        return normalized.Length is 4 or 5 && normalized.All(char.IsDigit);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value) ? value : null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    }

    private sealed record QuoteSearchResult(string Symbol, string Name, string? Currency, string Market);

    private sealed record ChartResult(string Symbol, string Name, string Currency, string Market, decimal? CurrentPrice);
}
