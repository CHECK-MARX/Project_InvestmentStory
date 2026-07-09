using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MarketDataProviderFactory : IMarketDataService
{
    private readonly IMarketDataService _mockMarketDataService;
    private readonly IMarketDataService _fallbackMarketDataService;
    private readonly IUsMarketDataService _usMarketDataService;
    private readonly IJapanMarketDataService _japanMarketDataService;

    public MarketDataProviderFactory()
        : this(
            new MockMarketDataService(),
            new YahooFinanceMarketDataService(),
            new AlphaVantageMarketDataService(),
            new JQuantsMarketDataService())
    {
    }

    public MarketDataProviderFactory(
        IMarketDataService mockMarketDataService,
        IMarketDataService fallbackMarketDataService,
        IUsMarketDataService usMarketDataService,
        IJapanMarketDataService japanMarketDataService)
    {
        _mockMarketDataService = mockMarketDataService;
        _fallbackMarketDataService = fallbackMarketDataService;
        _usMarketDataService = usMarketDataService;
        _japanMarketDataService = japanMarketDataService;
    }

    public MarketDataResult GetQuote(string symbol, AppSettings settings)
    {
        if (settings.MarketDataMode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            var mockResult = _mockMarketDataService.GetQuote(symbol, settings);
            return mockResult.IsSuccess && mockResult.Quote?.CurrentPrice is not null
                ? mockResult
                : MergeWithFallback(symbol, settings, mockResult.IsSuccess
                    ? MarketDataResult.Failure("Mock銘柄マスターに現在株価がありません。公開チャートAPIで補完します。", mockResult.Logs.ToArray())
                    : mockResult);
        }

        var apiResult = LooksLikeJapaneseTicker(symbol)
            ? GetJapaneseQuote(symbol, settings)
            : GetUsQuote(symbol, settings);
        return apiResult.IsSuccess ? apiResult : MergeWithFallback(symbol, settings, apiResult);
    }

    private MarketDataResult GetJapaneseQuote(string symbol, AppSettings settings)
    {
        if (settings.JapanMarketDataProvider.Equals("Yahoo Finance", StringComparison.OrdinalIgnoreCase))
        {
            return _fallbackMarketDataService.GetQuote(symbol, settings);
        }

        return _japanMarketDataService.GetJapanQuote(symbol, settings);
    }

    private MarketDataResult GetUsQuote(string symbol, AppSettings settings) =>
        _usMarketDataService.GetUsQuote(symbol, settings);

    private MarketDataResult MergeWithFallback(string symbol, AppSettings settings, MarketDataResult primaryResult)
    {
        var fallbackResult = _fallbackMarketDataService.GetQuote(symbol, settings);
        var logs = primaryResult.Logs.Concat(fallbackResult.Logs).ToArray();
        if (!fallbackResult.IsSuccess)
        {
            return MarketDataResult.Failure(
                $"{primaryResult.ErrorMessage} / フォールバック取得も失敗しました: {fallbackResult.ErrorMessage}",
                logs);
        }

        if (fallbackResult.Quote is not null)
        {
            fallbackResult.Quote.Warning = string.IsNullOrWhiteSpace(fallbackResult.Quote.Warning)
                ? "設定された取得方式で取得できなかったため、公開チャートAPIで補完しました。"
                : $"{fallbackResult.Quote.Warning} 設定された取得方式で取得できなかったため、公開チャートAPIで補完しました。";
        }

        return new MarketDataResult
        {
            IsSuccess = true,
            Quote = fallbackResult.Quote,
            ErrorMessage = string.Empty,
            Logs = logs
        };
    }

    private static bool LooksLikeJapaneseTicker(string ticker)
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

        return normalized.Length is 4 or 5 && normalized.All(char.IsDigit);
    }
}
