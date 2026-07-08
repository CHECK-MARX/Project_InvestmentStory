using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MockExchangeRateService : IExchangeRateService
{
    public ExchangeRateQuote GetUsdJpyRate()
    {
        return new ExchangeRateQuote
        {
            BaseCurrency = "USD",
            QuoteCurrency = "JPY",
            Rate = 160.00m,
            AcquiredAt = DateTime.Now,
            Source = "MockExchangeRateService",
            InputType = "Mock"
        };
    }
}
