using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class ExchangeRateProviderFactory : IExchangeRateService
{
    private readonly Func<AppSettings> _loadSettings;
    private readonly IExchangeRateService _yahooFinanceExchangeRateService;
    private readonly IExchangeRateService _mockExchangeRateService;

    public ExchangeRateProviderFactory(Func<AppSettings> loadSettings)
        : this(loadSettings, new YahooFinanceExchangeRateService(), new MockExchangeRateService())
    {
    }

    public ExchangeRateProviderFactory(
        Func<AppSettings> loadSettings,
        IExchangeRateService yahooFinanceExchangeRateService,
        IExchangeRateService mockExchangeRateService)
    {
        _loadSettings = loadSettings;
        _yahooFinanceExchangeRateService = yahooFinanceExchangeRateService;
        _mockExchangeRateService = mockExchangeRateService;
    }

    public ExchangeRateQuote GetUsdJpyRate()
    {
        var settings = _loadSettings();
        if (settings.ExchangeRateProvider.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            return _mockExchangeRateService.GetUsdJpyRate();
        }

        try
        {
            return _yahooFinanceExchangeRateService.GetUsdJpyRate();
        }
        catch
        {
            var fallback = _mockExchangeRateService.GetUsdJpyRate();
            return new ExchangeRateQuote
            {
                BaseCurrency = fallback.BaseCurrency,
                QuoteCurrency = fallback.QuoteCurrency,
                Rate = fallback.Rate,
                AcquiredAt = fallback.AcquiredAt,
                Source = $"{fallback.Source} (Yahoo Finance failed)",
                InputType = "Mock"
            };
        }
    }
}
