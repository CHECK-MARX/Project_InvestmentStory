namespace InvestmentStory.Core.Models;

public sealed class AppSettings
{
    public string DataDisplayMode { get; set; } = "Normal";
    public string MarketDataMode { get; set; } = "Web/API";
    public string UsMarketDataProvider { get; set; } = "Alpha Vantage";
    public string JapanMarketDataProvider { get; set; } = "Yahoo Finance";
    public string ExchangeRateProvider { get; set; } = "Yahoo Finance";
    public string BrokerDataMode { get; set; } = "手入力";
    public string AlphaVantageApiKey { get; set; } = string.Empty;
    public string JQuantsApiKey { get; set; } = string.Empty;
    public int ApiTimeoutSeconds { get; set; } = 10;
    public bool UseLastValueOnApiFailure { get; set; } = true;
    public bool SaveLoginCredentials { get; set; }
    public bool EnableApiResponseLog { get; set; }
    public string ThemeMode { get; set; } = "Light";
    public bool IsSidebarCollapsed { get; set; }
    public string StockListDisplayMode { get; set; } = "基本";
    public string LastDashboardCompositionMode { get; set; } = "Country";
    public string LastOpenedPage { get; set; } = "Dashboard";
    public string MutualFundSimulationScopeKey { get; set; } = MutualFundSimulationScopeKeys.AllAccounts;
    public decimal MutualFundSimulationMonthlyContributionJpy { get; set; } = 100_000m;
    public decimal MutualFundSimulationExpectedAnnualReturnRate { get; set; } = 5m;
    public decimal MutualFundSimulationTargetAmountJpy { get; set; } = 20_000_000m;
    public int MutualFundSimulationProjectionYears { get; set; } = 20;
    public int MutualFundSimulationTargetYears { get; set; } = 20;
}
