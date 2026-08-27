using System.Net;
using System.Globalization;
using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class YahooFinanceMarketDataService : IMarketDataService
{
    private const string ProviderName = "Yahoo Finance";
    private readonly Func<int, HttpClient> _httpClientFactory;

    public YahooFinanceMarketDataService()
        : this(CreateDefaultHttpClient)
    {
    }

    public YahooFinanceMarketDataService(Func<int, HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public MarketDataResult GetQuote(string symbol, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return MarketDataResult.Failure("コード、ティッカー、会社名のいずれかを入力してください。");
        }

        var timeoutSeconds = Math.Clamp(settings.ApiTimeoutSeconds, 3, 60);
        using var httpClient = _httpClientFactory(timeoutSeconds);
        var logs = new List<ApiFetchLogEntry>();

        try
        {
            var resolvedSymbol = ResolveSymbol(symbol);
            if (!LooksLikeJapaneseTicker(resolvedSymbol))
            {
                var searchedSymbol = SearchSymbol(httpClient, resolvedSymbol, logs);
                if (!string.IsNullOrWhiteSpace(searchedSymbol))
                {
                    resolvedSymbol = searchedSymbol;
                }
            }

            var quote = GetChartQuote(httpClient, resolvedSymbol, logs);
            if (quote is null)
            {
                return MarketDataResult.Failure("公開チャートAPIから銘柄情報を取得できませんでした。コード/ティッカーを確認してください。", logs.ToArray());
            }

            var validation = MarketDataSymbolResolver.ValidateQuote(quote);
            if (validation.IsFailed)
            {
                return MarketDataResult.Failure(validation.Message, logs.ToArray());
            }

            ApplyDividendEvents(httpClient, resolvedSymbol, quote, logs);
            ApplyNasdaqDividendCalendar(httpClient, resolvedSymbol, quote, logs);

            if (quote.Currency == "USD")
            {
                var fxQuote = GetChartQuote(httpClient, "JPY=X", logs);
                if (fxQuote?.CurrentPrice is not null)
                {
                    quote.UsdJpyRate = fxQuote.CurrentPrice;
                    quote.ExchangeRateAcquiredAt = fxQuote.PriceAcquiredAt ?? DateTime.Now;
                }
            }

            return MarketDataResult.Success(quote, logs.ToArray());
        }
        catch (Exception ex)
        {
            logs.Add(CreateLog("YahooFinance", symbol, null, false, ex.Message, string.Empty));
            return MarketDataResult.Failure($"公開チャートAPI取得に失敗しました: {ex.Message}", logs.ToArray());
        }
    }

    private static HttpClient CreateDefaultHttpClient(int timeoutSeconds)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 InvestmentStory/1.0");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        return httpClient;
    }

    private static string? SearchSymbol(HttpClient httpClient, string query, List<ApiFetchLogEntry> logs)
    {
        var url = $"https://query1.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=5&newsCount=0";
        using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var isSuccess = response.IsSuccessStatusCode;
        var summary = isSuccess ? SummarizeSearch(content, query) : string.Empty;
        logs.Add(CreateLog("YahooSearch", query, response.StatusCode, isSuccess, isSuccess ? string.Empty : content, summary));
        if (!isSuccess)
        {
            return null;
        }

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var exactMatch = FindQuoteSymbol(quotes, query, exactOnly: true);
        if (!string.IsNullOrWhiteSpace(exactMatch))
        {
            return exactMatch;
        }

        var equity = FindQuoteSymbol(quotes, query, exactOnly: false, preferredQuoteType: "EQUITY");
        if (!string.IsNullOrWhiteSpace(equity))
        {
            return equity;
        }

        return FindQuoteSymbol(quotes, query, exactOnly: false);
    }

    private static string? FindQuoteSymbol(
        JsonElement quotes,
        string query,
        bool exactOnly,
        string? preferredQuoteType = null)
    {
        foreach (var quote in quotes.EnumerateArray())
        {
            if (!quote.TryGetProperty("symbol", out var symbolProperty))
            {
                continue;
            }

            var symbol = symbolProperty.GetString();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            if (exactOnly && !symbol.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(preferredQuoteType) &&
                (!quote.TryGetProperty("quoteType", out var quoteType) ||
                 !string.Equals(quoteType.GetString(), preferredQuoteType, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return symbol;
        }

        return null;
    }

    private static MarketDataQuote? GetChartQuote(HttpClient httpClient, string symbol, List<ApiFetchLogEntry> logs)
    {
        var chartSymbol = ResolveSymbol(symbol);
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(chartSymbol)}?range=1d&interval=1d";
        using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var isSuccess = response.IsSuccessStatusCode;
        var summary = isSuccess ? SummarizeChart(content) : string.Empty;
        logs.Add(CreateLog("YahooChart", chartSymbol, response.StatusCode, isSuccess, isSuccess ? string.Empty : content, summary));
        if (!isSuccess)
        {
            return null;
        }

        using var document = JsonDocument.Parse(content);
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

        var resolvedSymbol = FirstNonEmpty(GetString(meta, "symbol"), chartSymbol);
        var currency = FirstNonEmpty(GetString(meta, "currency"), InferCurrency(resolvedSymbol));
        var market = FirstNonEmpty(GetString(meta, "fullExchangeName"), GetString(meta, "exchangeName"));
        var priceTime = GetUnixTime(meta, "regularMarketTime");

        return new MarketDataQuote
        {
            Symbol = NormalizeTickerForApp(resolvedSymbol),
            Name = FirstNonEmpty(GetString(meta, "longName"), GetString(meta, "shortName"), NormalizeTickerForApp(resolvedSymbol)),
            Country = InferCountry(currency, resolvedSymbol, market),
            Currency = currency,
            Market = market,
            CurrentPrice = GetDecimal(meta, "regularMarketPrice"),
            PriceAcquiredAt = priceTime,
            Source = ProviderName,
            Warning = resolvedSymbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase)
                ? "日本株は公開チャートAPIの直近データを使用します。リアルタイム株価ではありません。"
                : string.Empty
        };
    }

    private static void ApplyDividendEvents(
        HttpClient httpClient,
        string symbol,
        MarketDataQuote quote,
        List<ApiFetchLogEntry> logs)
    {
        var chartSymbol = ResolveSymbol(symbol);
        var period1 = DateTimeOffset.Now.AddYears(-1).ToUnixTimeSeconds();
        var period2 = DateTimeOffset.Now.ToUnixTimeSeconds();
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(chartSymbol)}?period1={period1}&period2={period2}&interval=1d&events=div";
        using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var isSuccess = response.IsSuccessStatusCode;
        logs.Add(CreateLog("YahooDividends", chartSymbol, response.StatusCode, isSuccess, isSuccess ? string.Empty : content, string.Empty));
        if (!isSuccess)
        {
            return;
        }

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return;
        }

        var result = results[0];
        if (!result.TryGetProperty("events", out var events) ||
            !events.TryGetProperty("dividends", out var dividends) ||
            dividends.ValueKind != JsonValueKind.Object)
        {
            quote.AnnualDividendPerShare = 0m;
            quote.DividendFrequency = "なし";
            quote.DividendInfoSource = ProviderName;
            return;
        }

        decimal total = 0m;
        var paymentCount = 0;
        DateTime? latestDate = null;
        var calendarEvents = new List<DividendCalendarEvent>();
        foreach (var dividend in dividends.EnumerateObject())
        {
            if (!dividend.Value.TryGetProperty("amount", out var amountProperty) ||
                amountProperty.ValueKind != JsonValueKind.Number ||
                !amountProperty.TryGetDecimal(out var amount))
            {
                continue;
            }

            total += amount;
            paymentCount++;
            if (dividend.Value.TryGetProperty("date", out var dateProperty) &&
                dateProperty.ValueKind == JsonValueKind.Number &&
                dateProperty.TryGetInt64(out var unixTime))
            {
                // Yahoo chart dividend event timestamps represent ex-dividend dates.
                var date = DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime.Date;
                latestDate = latestDate is null || date > latestDate.Value ? date : latestDate;
                calendarEvents.Add(new DividendCalendarEvent
                {
                    EventKey = DividendCalendarEvent.CreateEventKey(null, date, null, null, amount, quote.Currency),
                    ExDividendDate = date,
                    AmountPerShare = amount,
                    Currency = quote.Currency,
                    Source = ProviderName,
                    DataQuality = DividendPlanDataQuality.Acquired,
                    AcquiredAt = DateTime.Now,
                    IsConfirmed = true
                });
            }
        }

        quote.AnnualDividendPerShare = total;
        quote.DividendFrequency = paymentCount > 0 ? $"年{paymentCount}回" : "なし";
        quote.DividendInfoSource = ProviderName;
        quote.ExDividendDate = latestDate;
        quote.DividendEvents = MergeDividendEvents(quote.DividendEvents, calendarEvents);
    }

    private static void ApplyNasdaqDividendCalendar(
        HttpClient httpClient,
        string symbol,
        MarketDataQuote quote,
        List<ApiFetchLogEntry> logs)
    {
        if (LooksLikeJapaneseTicker(symbol) || symbol.Contains('^') || symbol.Contains('='))
        {
            return;
        }

        var displaySymbol = NormalizeTickerForApp(symbol).ToUpperInvariant();
        var url = $"https://api.nasdaq.com/api/quote/{Uri.EscapeDataString(displaySymbol)}/dividends?assetclass=stocks";
        try
        {
            using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                logs.Add(CreateLog("NasdaqDividendCalendar", displaySymbol, response.StatusCode, false, content, string.Empty));
                return;
            }

            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("dividends", out var dividendData) ||
                dividendData.ValueKind != JsonValueKind.Object ||
                !dividendData.TryGetProperty("rows", out var rows) ||
                rows.ValueKind != JsonValueKind.Array)
            {
                logs.Add(CreateLog("NasdaqDividendCalendar", displaySymbol, response.StatusCode, false,
                    "Dividend calendar rows were not available.", "rows=0"));
                return;
            }

            var acquiredAt = DateTime.Now;
            var exactEvents = new List<DividendCalendarEvent>();
            foreach (var row in rows.EnumerateArray())
            {
                var declarationDate = ParseMarketDate(GetString(row, "declarationDate"));
                var exDividendDate = ParseMarketDate(GetString(row, "exOrEffDate"));
                var recordDate = ParseMarketDate(GetString(row, "recordDate"));
                var paymentDate = ParseMarketDate(GetString(row, "paymentDate"));
                var amount = ParseMoney(GetString(row, "amount"));
                var currency = FirstNonEmpty(GetString(row, "currency"), quote.Currency).ToUpperInvariant();
                if (declarationDate is null && exDividendDate is null && recordDate is null && paymentDate is null)
                {
                    continue;
                }

                exactEvents.Add(new DividendCalendarEvent
                {
                    EventKey = DividendCalendarEvent.CreateEventKey(
                        declarationDate, exDividendDate, recordDate, paymentDate, amount, currency),
                    DeclarationDate = declarationDate,
                    ExDividendDate = exDividendDate,
                    RecordDate = recordDate,
                    PaymentDate = paymentDate,
                    AmountPerShare = amount,
                    Currency = currency,
                    Source = "Nasdaq",
                    DataQuality = DividendPlanDataQuality.Acquired,
                    AcquiredAt = acquiredAt,
                    IsConfirmed = true
                });
            }

            quote.DividendEvents = MergeDividendEvents(quote.DividendEvents, exactEvents);
            var latest = exactEvents
                .OrderByDescending(item => item.ExDividendDate ?? item.RecordDate ?? item.PaymentDate ?? item.DeclarationDate)
                .FirstOrDefault();
            if (latest is not null)
            {
                quote.DividendRecordDate = latest.RecordDate;
                quote.ExDividendDate = latest.ExDividendDate;
                quote.DividendPaymentStartDate = latest.PaymentDate;
                quote.DividendInfoSource = FirstNonEmpty(quote.DividendInfoSource, "Nasdaq");
            }

            logs.Add(CreateLog("NasdaqDividendCalendar", displaySymbol, response.StatusCode, true, string.Empty,
                $"rows={exactEvents.Count}"));
        }
        catch (Exception ex)
        {
            // This endpoint is a supplementary source. Quote acquisition must remain usable when unsupported.
            logs.Add(CreateLog("NasdaqDividendCalendar", displaySymbol, null, false, ex.Message, string.Empty));
        }
    }

    private static IReadOnlyList<DividendCalendarEvent> MergeDividendEvents(
        IEnumerable<DividendCalendarEvent> existing,
        IEnumerable<DividendCalendarEvent> incoming)
    {
        var merged = existing.Concat(incoming).ToList();
        return merged
            .GroupBy(item => new
            {
                Date = item.ExDividendDate ?? item.RecordDate ?? item.PaymentDate ?? item.DeclarationDate,
                Amount = decimal.Round(item.AmountPerShare, 8),
                Currency = item.Currency.ToUpperInvariant()
            })
            .Select(group =>
            {
                var preferred = group.OrderByDescending(item => item.Source.Equals("Nasdaq", StringComparison.OrdinalIgnoreCase)).First();
                return new DividendCalendarEvent
                {
                    EventKey = DividendCalendarEvent.CreateEventKey(
                        group.Select(item => item.DeclarationDate).FirstOrDefault(value => value.HasValue),
                        group.Select(item => item.ExDividendDate).FirstOrDefault(value => value.HasValue),
                        group.Select(item => item.RecordDate).FirstOrDefault(value => value.HasValue),
                        group.Select(item => item.PaymentDate).FirstOrDefault(value => value.HasValue),
                        preferred.AmountPerShare,
                        preferred.Currency),
                    DeclarationDate = group.Select(item => item.DeclarationDate).FirstOrDefault(value => value.HasValue),
                    ExDividendDate = group.Select(item => item.ExDividendDate).FirstOrDefault(value => value.HasValue),
                    RecordDate = group.Select(item => item.RecordDate).FirstOrDefault(value => value.HasValue),
                    PaymentDate = group.Select(item => item.PaymentDate).FirstOrDefault(value => value.HasValue),
                    AmountPerShare = preferred.AmountPerShare,
                    Currency = preferred.Currency,
                    Source = string.Join(" + ", group.Select(item => item.Source).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
                    DataQuality = preferred.DataQuality,
                    AcquiredAt = group.Max(item => item.AcquiredAt),
                    IsConfirmed = group.Any(item => item.IsConfirmed)
                };
            })
            .OrderBy(item => item.PaymentDate ?? item.ExDividendDate ?? item.RecordDate ?? item.DeclarationDate)
            .ToList();
    }

    private static DateTime? ParseMarketDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date.Date
            : null;
    }

    private static decimal ParseMoney(string value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(character => char.IsDigit(character) || character is '.' or '-' or '+')
            .ToArray());
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }

    private static string ResolveSymbol(string query)
    {
        return MarketDataSymbolResolver.ToProviderSymbol(query);
    }

    private static string NormalizeTickerForApp(string symbol) =>
        MarketDataSymbolResolver.ToDisplaySymbol(symbol);

    private static bool LooksLikeJapaneseTicker(string ticker) =>
        MarketDataSymbolResolver.LooksLikeJapaneseTicker(ticker);

    private static string InferCurrency(string symbol) =>
        symbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ? "JPY" : "USD";

    private static string InferCountry(string currency, string symbol, string market) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
        symbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ||
        market.Contains("Tokyo", StringComparison.OrdinalIgnoreCase)
            ? "日本"
            : "米国";

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

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value) ? value : null;
    }

    private static DateTime? GetUnixTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var value))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static string SummarizeSearch(string content, string query)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("quotes", out var quotes) ||
                quotes.ValueKind != JsonValueKind.Array)
            {
                return "Search quotes count=0";
            }

            var count = quotes.GetArrayLength();
            var selected = FindQuoteSymbol(quotes, query, exactOnly: true) ??
                FindQuoteSymbol(quotes, query, exactOnly: false, preferredQuoteType: "EQUITY") ??
                FindQuoteSymbol(quotes, query, exactOnly: false) ??
                string.Empty;
            return $"Search quotes count={count}; selected={selected}";
        }
        catch
        {
            return "Search response summary unavailable.";
        }
    }

    private static string SummarizeChart(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("chart", out var chart) ||
                !chart.TryGetProperty("result", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return "Chart result count=0";
            }

            var result = results[0];
            if (!result.TryGetProperty("meta", out var meta))
            {
                return "Chart result count=1; meta=false";
            }

            var symbol = GetString(meta, "symbol");
            var longName = GetString(meta, "longName");
            var shortName = GetString(meta, "shortName");
            var currency = GetString(meta, "currency");
            var price = GetDecimal(meta, "regularMarketPrice");
            return $"Chart result count={results.GetArrayLength()}; meta=true; symbol={symbol}; name={FirstNonEmpty(longName, shortName)}; price={price}; currency={currency}";
        }
        catch
        {
            return "Chart response summary unavailable.";
        }
    }
}
