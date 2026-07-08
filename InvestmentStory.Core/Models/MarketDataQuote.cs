namespace InvestmentStory.Core.Models;

public sealed class MarketDataQuote
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public decimal? CurrentPrice { get; set; }
    public DateTime? PriceAcquiredAt { get; set; }
    public decimal? AnnualDividendPerShare { get; set; }
    public decimal? DividendYield { get; set; }
    public string DividendFrequency { get; set; } = string.Empty;
    public DateTime? DividendRecordDate { get; set; }
    public DateTime? ExDividendDate { get; set; }
    public DateTime? DividendPaymentStartDate { get; set; }
    public string DividendInfoSource { get; set; } = string.Empty;
    public decimal? UsdJpyRate { get; set; }
    public DateTime? ExchangeRateAcquiredAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
}

public sealed class MarketDataResult
{
    public bool IsSuccess { get; init; }
    public MarketDataQuote? Quote { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public IReadOnlyList<ApiFetchLogEntry> Logs { get; init; } = Array.Empty<ApiFetchLogEntry>();

    public static MarketDataResult Success(MarketDataQuote quote, params ApiFetchLogEntry[] logs) =>
        new() { IsSuccess = true, Quote = quote, Logs = logs };

    public static MarketDataResult Failure(string errorMessage, params ApiFetchLogEntry[] logs) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, Logs = logs };
}
