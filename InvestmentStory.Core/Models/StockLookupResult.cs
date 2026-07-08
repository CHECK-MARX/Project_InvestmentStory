namespace InvestmentStory.Core.Models;

public sealed record StockLookupResult(
    string Ticker,
    string Name,
    string Country,
    string Currency,
    string Market,
    string Source,
    decimal? CurrentPrice = null,
    decimal? AnnualDividendPerShare = null,
    string DividendFrequency = "");
