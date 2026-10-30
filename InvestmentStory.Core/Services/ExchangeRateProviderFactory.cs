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
            // A sample quote must never masquerade as the last confirmed market rate.
            // Callers retain their persisted quote and show its original acquisition time.
            return new ExchangeRateQuote
            {
                Rate = 0m,
                AcquiredAt = DateTime.MinValue,
                Source = "Yahoo Finance (取得失敗)",
                InputType = "Unavailable"
            };
        }
    }
}
