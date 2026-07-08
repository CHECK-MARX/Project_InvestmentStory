using System.Globalization;
using System.Net;
using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class AlphaVantageMarketDataService : IUsMarketDataService
{
    private const string ProviderName = "Alpha Vantage";

    public MarketDataResult GetUsQuote(string symbol, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AlphaVantageApiKey))
        {
            return MarketDataResult.Failure("Alpha Vantage API Key が未設定です。設定画面で登録してください。",
                CreateLog("US株式", symbol, null, false, "API key is not configured.", string.Empty));
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var timeoutSeconds = Math.Clamp(settings.ApiTimeoutSeconds, 3, 60);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        var logs = new List<ApiFetchLogEntry>();
        var quote = new MarketDataQuote
        {
            Symbol = normalizedSymbol,
            Country = "米国",
            Currency = "USD",
            Source = ProviderName
        };

        try
        {
            var overview = GetJson(httpClient, "OVERVIEW", normalizedSymbol, settings.AlphaVantageApiKey, logs);
            if (overview is not null)
            {
                quote.Name = GetString(overview.RootElement, "Name");
                quote.Currency = FirstNonEmpty(GetString(overview.RootElement, "Currency"), "USD");
                quote.Sector = GetString(overview.RootElement, "Sector");
                quote.Industry = GetString(overview.RootElement, "Industry");
                quote.AnnualDividendPerShare = GetDecimal(overview.RootElement, "DividendPerShare");
                quote.DividendYield = GetDecimal(overview.RootElement, "DividendYield");
                overview.Dispose();
            }

            var globalQuote = GetJson(httpClient, "GLOBAL_QUOTE", normalizedSymbol, settings.AlphaVantageApiKey, logs);
            if (globalQuote is not null)
            {
                if (globalQuote.RootElement.TryGetProperty("Global Quote", out var globalQuoteElement))
                {
                    quote.CurrentPrice = GetDecimal(globalQuoteElement, "05. price");
                    quote.PriceAcquiredAt = ParseDate(GetString(globalQuoteElement, "07. latest trading day")) ?? DateTime.Now;
                }

                globalQuote.Dispose();
            }

            var fx = GetJson(httpClient, "CURRENCY_EXCHANGE_RATE", "USD", settings.AlphaVantageApiKey, logs, ("to_currency", "JPY"));
            if (fx is not null)
            {
                if (fx.RootElement.TryGetProperty("Realtime Currency Exchange Rate", out var fxElement))
                {
                    quote.UsdJpyRate = GetDecimal(fxElement, "5. Exchange Rate");
                    quote.ExchangeRateAcquiredAt = ParseDateTime(GetString(fxElement, "6. Last Refreshed")) ?? DateTime.Now;
                }

                fx.Dispose();
            }

            if (string.IsNullOrWhiteSpace(quote.Name) && quote.CurrentPrice is null)
            {
                return MarketDataResult.Failure("Alpha Vantage から銘柄情報を取得できませんでした。ティッカーとAPIキーを確認してください。", logs.ToArray());
            }

            return MarketDataResult.Success(quote, logs.ToArray());
        }
        catch (Exception ex)
        {
            logs.Add(CreateLog("US株式", normalizedSymbol, null, false, ex.Message, string.Empty));
            return MarketDataResult.Failure($"Alpha Vantage取得に失敗しました: {ex.Message}", logs.ToArray());
        }
    }

    private static JsonDocument? GetJson(
        HttpClient httpClient,
        string function,
        string symbol,
        string apiKey,
        List<ApiFetchLogEntry> logs,
        params (string Name, string Value)[] extraParameters)
    {
        var parameters = new List<string>
        {
            $"function={Uri.EscapeDataString(function)}",
            function == "CURRENCY_EXCHANGE_RATE"
                ? $"from_currency={Uri.EscapeDataString(symbol)}"
                : $"symbol={Uri.EscapeDataString(symbol)}"
        };
        parameters.AddRange(extraParameters.Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(x.Value)}"));
        parameters.Add($"apikey={Uri.EscapeDataString(apiKey)}");

        using var response = httpClient.GetAsync($"https://www.alphavantage.co/query?{string.Join("&", parameters)}")
            .GetAwaiter()
            .GetResult();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var sanitizedSummary = SummarizeJson(content);
        var isSuccess = response.IsSuccessStatusCode && !ContainsAlphaVantageError(content);
        logs.Add(CreateLog(function, symbol, response.StatusCode, isSuccess, isSuccess ? string.Empty : ExtractAlphaVantageError(content), sanitizedSummary));

        if (!isSuccess)
        {
            return null;
        }

        return JsonDocument.Parse(content);
    }

    private static bool ContainsAlphaVantageError(string content) =>
        content.Contains("Error Message", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("Information", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("Note", StringComparison.OrdinalIgnoreCase);

    private static string ExtractAlphaVantageError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            foreach (var name in new[] { "Error Message", "Information", "Note" })
            {
                if (document.RootElement.TryGetProperty(name, out var property))
                {
                    return property.GetString() ?? "Alpha Vantage returned an error.";
                }
            }
        }
        catch
        {
            return "Alpha Vantage returned an error.";
        }

        return "Alpha Vantage returned an error.";
    }

    private static ApiFetchLogEntry CreateLog(
        string apiType,
        string symbol,
        HttpStatusCode? statusCode,
        bool isSuccess,
        string errorMessage,
        string summary) =>
        new()
        {
            ApiType = apiType,
            Provider = ProviderName,
            Symbol = symbol,
            HttpStatusCode = statusCode is null ? null : (int)statusCode.Value,
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage,
            FetchedAt = DateTime.Now,
            Summary = summary
        };

    private static string SummarizeJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var compact = content.Replace("\r", string.Empty).Replace("\n", string.Empty);
        return compact.Length <= 500 ? compact : compact[..500];
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

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numeric))
        {
            return numeric;
        }

        return property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date.Date : null;

    private static DateTime? ParseDateTime(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
}
