using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MockMarketDataService : IMarketDataService
{
    private readonly LocalStockLookupService _localLookup = new();

    public MarketDataResult GetQuote(string symbol, AppSettings settings)
    {
        var lookup = _localLookup.Find(symbol);
        if (lookup is null)
        {
            return MarketDataResult.Failure("Mock銘柄マスターに該当銘柄がありません。",
                new ApiFetchLogEntry
                {
                    ApiType = "Mock",
                    Provider = "MockMarketDataService",
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = "Symbol was not found in local mock master.",
                    FetchedAt = DateTime.Now
                });
        }

        var quote = new MarketDataQuote
        {
            Symbol = lookup.Ticker,
            Name = lookup.Name,
            Country = lookup.Country,
            Currency = lookup.Currency,
            Market = lookup.Market,
            CurrentPrice = lookup.CurrentPrice,
            PriceAcquiredAt = DateTime.Now,
            AnnualDividendPerShare = lookup.AnnualDividendPerShare,
            DividendFrequency = lookup.DividendFrequency,
            DividendInfoSource = lookup.Source,
            Source = lookup.Source,
            UsdJpyRate = lookup.Currency == "USD" ? 160m : null,
            ExchangeRateAcquiredAt = DateTime.Now
        };

        return MarketDataResult.Success(quote,
            new ApiFetchLogEntry
            {
                ApiType = "Mock",
                Provider = "MockMarketDataService",
                Symbol = lookup.Ticker,
                IsSuccess = true,
                FetchedAt = DateTime.Now,
                Summary = $"{lookup.Ticker} {lookup.Name} {lookup.Currency}"
            });
    }
}
