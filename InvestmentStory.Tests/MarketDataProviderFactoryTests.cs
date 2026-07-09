using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class MarketDataProviderFactoryTests
{
    [Fact]
    public void GetQuote_UsesYahooForJapaneseTickerWhenJapanProviderIsYahoo()
    {
        var fallback = new RecordingMarketDataService(MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = "8151",
            CurrentPrice = 1925m,
            Source = "Yahoo Finance"
        }));
        var japan = new RecordingJapanMarketDataService(MarketDataResult.Failure("J-Quants should not be called."));
        var factory = new MarketDataProviderFactory(
            new RecordingMarketDataService(MarketDataResult.Failure("Mock should not be called.")),
            fallback,
            new RecordingUsMarketDataService(MarketDataResult.Failure("US should not be called.")),
            japan);

        var result = factory.GetQuote("8151", new AppSettings
        {
            MarketDataMode = "Web/API",
            JapanMarketDataProvider = "Yahoo Finance"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fallback.CallCount);
        Assert.Equal(0, japan.CallCount);
        Assert.Equal("Yahoo Finance", result.Quote?.Source);
        Assert.Equal(1925m, result.Quote?.CurrentPrice);
    }

    [Fact]
    public void GetQuote_UsesJQuantsForJapaneseTickerWhenJapanProviderIsJQuants()
    {
        var fallback = new RecordingMarketDataService(MarketDataResult.Failure("Fallback should not be called."));
        var japan = new RecordingJapanMarketDataService(MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = "8151",
            CurrentPrice = 1900m,
            Source = "J-Quants"
        }));
        var factory = new MarketDataProviderFactory(
            new RecordingMarketDataService(MarketDataResult.Failure("Mock should not be called.")),
            fallback,
            new RecordingUsMarketDataService(MarketDataResult.Failure("US should not be called.")),
            japan);

        var result = factory.GetQuote("8151", new AppSettings
        {
            MarketDataMode = "Web/API",
            JapanMarketDataProvider = "J-Quants",
            JQuantsApiKey = "token"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fallback.CallCount);
        Assert.Equal(1, japan.CallCount);
        Assert.Equal("J-Quants", result.Quote?.Source);
    }

    private sealed class RecordingMarketDataService : IMarketDataService
    {
        private readonly MarketDataResult _result;

        public RecordingMarketDataService(MarketDataResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public MarketDataResult GetQuote(string symbol, AppSettings settings)
        {
            CallCount++;
            return _result;
        }
    }

    private sealed class RecordingUsMarketDataService : IUsMarketDataService
    {
        private readonly MarketDataResult _result;

        public RecordingUsMarketDataService(MarketDataResult result)
        {
            _result = result;
        }

        public MarketDataResult GetUsQuote(string symbol, AppSettings settings) => _result;
    }

    private sealed class RecordingJapanMarketDataService : IJapanMarketDataService
    {
        private readonly MarketDataResult _result;

        public RecordingJapanMarketDataService(MarketDataResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public MarketDataResult GetJapanQuote(string code, AppSettings settings)
        {
            CallCount++;
            return _result;
        }
    }
}
