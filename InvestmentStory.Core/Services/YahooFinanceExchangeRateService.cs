using System.Net;
using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class YahooFinanceExchangeRateService : IExchangeRateService
{
    private const string ProviderName = "Yahoo Finance";

    public ExchangeRateQuote GetUsdJpyRate()
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 InvestmentStory/1.0");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");

        var url = "https://query1.finance.yahoo.com/v8/finance/chart/JPY=X?range=1d&interval=1m";
        using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Yahoo Finance USD/JPY API error: {(int)response.StatusCode} {response.StatusCode}");
        }

        using var document = JsonDocument.Parse(content);
        var meta = document.RootElement
            .GetProperty("chart")
            .GetProperty("result")[0]
            .GetProperty("meta");

        var rate = GetDecimal(meta, "regularMarketPrice")
            ?? GetDecimal(meta, "chartPreviousClose")
            ?? throw new InvalidOperationException("Yahoo Finance USD/JPY API response does not contain a price.");
        var acquiredAt = GetUnixTime(meta, "regularMarketTime") ?? DateTime.Now;

        return new ExchangeRateQuote
        {
            BaseCurrency = "USD",
            QuoteCurrency = "JPY",
            Rate = rate,
            AcquiredAt = acquiredAt,
            Source = ProviderName,
            InputType = "API"
        };
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDecimal(out var value)
            ? value
            : null;
    }

    private static DateTime? GetUnixTime(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out var value)
            ? DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime
            : null;
    }
}
