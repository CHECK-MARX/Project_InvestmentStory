namespace InvestmentStory.Core.Models;

public sealed class AppSettings
{
    public string MarketDataMode { get; set; } = "Mock";
    public string UsMarketDataProvider { get; set; } = "Alpha Vantage";
    public string JapanMarketDataProvider { get; set; } = "J-Quants";
    public string ExchangeRateProvider { get; set; } = "Yahoo Finance";
    public string BrokerDataMode { get; set; } = "手入力";
    public string AlphaVantageApiKey { get; set; } = string.Empty;
    public string JQuantsApiKey { get; set; } = string.Empty;
    public int ApiTimeoutSeconds { get; set; } = 10;
    public bool UseLastValueOnApiFailure { get; set; } = true;
    public bool SaveLoginCredentials { get; set; }
    public bool EnableApiResponseLog { get; set; }
}
