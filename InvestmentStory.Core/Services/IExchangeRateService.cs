using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public interface IExchangeRateService
{
    ExchangeRateQuote GetUsdJpyRate();
}
