using System.Globalization;
using System.Net;
using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class JQuantsMarketDataService : IJapanMarketDataService
{
    private const string ProviderName = "J-Quants";
    private const string BaseUrl = "https://api.jquants.com/v1";

    public MarketDataResult GetJapanQuote(string code, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.JQuantsApiKey))
        {
            return MarketDataResult.Failure("J-Quants API Key / トークンが未設定です。設定画面で登録してください。",
                CreateLog("日本株式", code, null, false, "API key/token is not configured.", string.Empty));
        }

        var normalizedCode = NormalizeCode(code);
        var timeoutSeconds = Math.Clamp(settings.ApiTimeoutSeconds, 3, 60);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        httpClient.DefaultRequestHeaders.Add("x-api-key", settings.JQuantsApiKey);

        var logs = new List<ApiFetchLogEntry>();
        var quote = new MarketDataQuote
        {
            Symbol = normalizedCode.Length >= 4 ? normalizedCode[..4] : normalizedCode,
            Country = "日本",
            Currency = "JPY",
            Source = ProviderName,
            Warning = "日本株はJ-Quantsの直近データを使用します。リアルタイム株価ではありません。"
        };

        try
        {
            var info = GetJson(httpClient, "listed/info", $"{BaseUrl}/listed/info?code={Uri.EscapeDataString(normalizedCode)}", normalizedCode, logs);
            if (info is not null)
            {
                var item = FirstArrayItem(info.RootElement, "info");
                if (item is not null)
                {
                    quote.Name = FirstNonEmpty(
                        GetString(item.Value, "CompanyName"),
                        GetString(item.Value, "CompanyNameEnglish"),
                        quote.Symbol);
                    quote.Market = FirstNonEmpty(GetString(item.Value, "MarketCodeName"), GetString(item.Value, "ScaleCategory"));
                    quote.Sector = FirstNonEmpty(GetString(item.Value, "Sector17CodeName"), GetString(item.Value, "Sector33CodeName"));
                }

                info.Dispose();
            }

            var daily = GetJson(httpClient, "prices/daily_quotes", $"{BaseUrl}/prices/daily_quotes?code={Uri.EscapeDataString(normalizedCode)}", normalizedCode, logs);
            if (daily is not null)
            {
                var item = LastArrayItem(daily.RootElement, "daily_quotes");
                if (item is not null)
                {
                    quote.CurrentPrice = FirstDecimal(item.Value, "AdjustmentClose", "Close");
                    quote.PriceAcquiredAt = ParseDate(GetString(item.Value, "Date")) ?? DateTime.Now;
                }

                daily.Dispose();
            }

            var dividend = GetJson(httpClient, "fins/dividend", $"{BaseUrl}/fins/dividend?code={Uri.EscapeDataString(normalizedCode)}", normalizedCode, logs);
            if (dividend is not null)
            {
                var item = LastArrayItem(dividend.RootElement, "dividend");
                if (item is not null)
                {
                    quote.AnnualDividendPerShare = FirstDecimal(
                        item.Value,
                        "ForecastAnnualDividendPerShare",
                        "AnnualDividendPerShare",
                        "CashDividendPerShare");
                    quote.DividendRecordDate = ParseDate(FirstNonEmpty(GetString(item.Value, "RecordDate"), GetString(item.Value, "DividendRecordDate")));
                    quote.ExDividendDate = ParseDate(GetString(item.Value, "ExDividendDate"));
                    quote.DividendPaymentStartDate = ParseDate(FirstNonEmpty(GetString(item.Value, "ScheduledPaymentDate"), GetString(item.Value, "PaymentStartDate")));
                    quote.DividendInfoSource = ProviderName;
                }

                dividend.Dispose();
            }

            if (string.IsNullOrWhiteSpace(quote.Name) && quote.CurrentPrice is null)
            {
                return MarketDataResult.Failure("J-Quantsから銘柄情報を取得できませんでした。銘柄コードとAPIキー/トークンを確認してください。", logs.ToArray());
            }

            return MarketDataResult.Success(quote, logs.ToArray());
        }
        catch (Exception ex)
        {
            logs.Add(CreateLog("日本株式", normalizedCode, null, false, ex.Message, string.Empty));
            return MarketDataResult.Failure($"J-Quants取得に失敗しました: {ex.Message}", logs.ToArray());
        }
    }

    private static JsonDocument? GetJson(HttpClient httpClient, string apiType, string url, string code, List<ApiFetchLogEntry> logs)
    {
        using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var isSuccess = response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(content);
        logs.Add(CreateLog(apiType, code, response.StatusCode, isSuccess, isSuccess ? string.Empty : content, SummarizeJson(content)));

        if (!isSuccess)
        {
            return null;
        }

        return JsonDocument.Parse(content);
    }

    private static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            normalized = normalized[..^2];
        }

        return normalized.Length == 4 && normalized.All(char.IsDigit) ? $"{normalized}0" : normalized;
    }

    private static JsonElement? FirstArrayItem(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() == 0)
        {
            return null;
        }

        return array[0];
    }

    private static JsonElement? LastArrayItem(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() == 0)
        {
            return null;
        }

        return array[array.GetArrayLength() - 1];
    }

    private static decimal? FirstDecimal(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetDecimal(element, propertyName);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
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

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date.Date : null;

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

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
}
