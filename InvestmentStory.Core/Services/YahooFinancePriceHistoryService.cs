using System.Net;
using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class YahooFinancePriceHistoryService : IPriceHistoryService
{
    private const string ProviderName = "Yahoo Finance";

    public PriceHistoryResult GetHistory(string symbol, AppSettings settings, string range, string interval)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return PriceHistoryResult.Failure(string.Empty, ProviderName, "チャート取得対象のコードがありません。");
        }

        var resolvedSymbol = ResolveSymbol(symbol);
        var timeoutSeconds = Math.Clamp(settings.ApiTimeoutSeconds, 3, 60);
        using var httpClient = CreateHttpClient(timeoutSeconds);
        var logs = new List<ApiFetchLogEntry>();

        try
        {
            var url =
                $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(resolvedSymbol)}" +
                $"?range={Uri.EscapeDataString(range)}&interval={Uri.EscapeDataString(interval)}&includePrePost=false";
            using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var isSuccess = response.IsSuccessStatusCode;
            logs.Add(CreateLog("YahooHistory", resolvedSymbol, response.StatusCode, isSuccess, isSuccess ? string.Empty : content));
            if (!isSuccess)
            {
                return PriceHistoryResult.Failure(resolvedSymbol, ProviderName, $"履歴チャートAPIの取得に失敗しました: {(int)response.StatusCode} {response.StatusCode}", logs.ToArray());
            }

            var points = ParsePoints(content);
            return points.Count == 0
                ? PriceHistoryResult.Failure(resolvedSymbol, ProviderName, "チャート履歴データが空でした。", logs.ToArray())
                : PriceHistoryResult.Success(resolvedSymbol, ProviderName, points, logs.ToArray());
        }
        catch (Exception ex)
        {
            logs.Add(CreateLog("YahooHistory", resolvedSymbol, null, false, ex.Message));
            return PriceHistoryResult.Failure(resolvedSymbol, ProviderName, $"チャート取得に失敗しました: {ex.Message}", logs.ToArray());
        }
    }

    private static HttpClient CreateHttpClient(int timeoutSeconds)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 InvestmentStory/1.0");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        return httpClient;
    }

    private static IReadOnlyList<PriceHistoryPoint> ParsePoints(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return Array.Empty<PriceHistoryPoint>();
        }

        var result = results[0];
        if (!result.TryGetProperty("timestamp", out var timestamps) ||
            timestamps.ValueKind != JsonValueKind.Array ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) ||
            quotes.ValueKind != JsonValueKind.Array ||
            quotes.GetArrayLength() == 0)
        {
            return Array.Empty<PriceHistoryPoint>();
        }

        var quote = quotes[0];
        if (!quote.TryGetProperty("open", out var opens) ||
            !quote.TryGetProperty("high", out var highs) ||
            !quote.TryGetProperty("low", out var lows) ||
            !quote.TryGetProperty("close", out var closes))
        {
            return Array.Empty<PriceHistoryPoint>();
        }

        quote.TryGetProperty("volume", out var volumes);
        var count = new[]
        {
            timestamps.GetArrayLength(),
            opens.GetArrayLength(),
            highs.GetArrayLength(),
            lows.GetArrayLength(),
            closes.GetArrayLength()
        }.Min();

        var points = new List<PriceHistoryPoint>();
        for (var i = 0; i < count; i++)
        {
            if (!TryGetUnixTime(timestamps[i], out var date) ||
                !TryGetDecimal(opens[i], out var open) ||
                !TryGetDecimal(highs[i], out var high) ||
                !TryGetDecimal(lows[i], out var low) ||
                !TryGetDecimal(closes[i], out var close) ||
                open <= 0m ||
                high <= 0m ||
                low <= 0m ||
                close <= 0m)
            {
                continue;
            }

            var volume = volumes.ValueKind == JsonValueKind.Array && i < volumes.GetArrayLength() && TryGetDecimal(volumes[i], out var parsedVolume)
                ? parsedVolume
                : 0m;
            points.Add(new PriceHistoryPoint
            {
                Date = date,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        return points.OrderBy(x => x.Date).ToList();
    }

    private static bool TryGetUnixTime(JsonElement element, out DateTime value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var unixTime))
        {
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime.Date;
        return true;
    }

    private static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && decimal.TryParse(element.GetString(), out value))
        {
            return true;
        }

        return false;
    }

    private static string ResolveSymbol(string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (LooksLikeJapaneseTicker(normalized) && !normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            return $"{normalized}.T";
        }

        return normalized;
    }

    private static bool LooksLikeJapaneseTicker(string ticker)
    {
        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            normalized = normalized[..^2];
        }

        return normalized.Length is 4 or 5 && normalized.All(char.IsDigit);
    }

    private static ApiFetchLogEntry CreateLog(
        string apiType,
        string symbol,
        HttpStatusCode? statusCode,
        bool isSuccess,
        string errorMessage) =>
        new()
        {
            ApiType = apiType,
            Provider = ProviderName,
            Symbol = symbol,
            HttpStatusCode = statusCode is null ? null : (int)statusCode.Value,
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage,
            FetchedAt = DateTime.Now,
            Summary = isSuccess ? "Price history loaded." : string.Empty
        };
}
