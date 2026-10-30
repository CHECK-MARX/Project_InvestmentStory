using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class ExchangeRateProviderFactoryTests
{
    [Fact]
    public void LiveProviderFailure_DoesNotReturnSampleRateAsMarketData()
    {
        var service = new ExchangeRateProviderFactory(
            () => new AppSettings { ExchangeRateProvider = "YahooFinance" },
            new FailingRateService(), new MockExchangeRateService());

        var quote = service.GetUsdJpyRate();

        Assert.Equal(0m, quote.Rate);
        Assert.Equal(DateTime.MinValue, quote.AcquiredAt);
        Assert.Equal("Unavailable", quote.InputType);
        Assert.DoesNotContain("Mock", quote.Source);
    }

    private sealed class FailingRateService : IExchangeRateService
    {
        public ExchangeRateQuote GetUsdJpyRate() => throw new InvalidOperationException("offline");
    }
}
