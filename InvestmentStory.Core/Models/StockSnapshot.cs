namespace InvestmentStory.Core.Models;

public sealed class StockSnapshot
{
    public required StockPosition Position { get; init; }
    public decimal PurchaseTotal { get; init; }
    public decimal PurchaseTotalUsd => PurchaseTotal;
    public decimal EffectiveAcquisitionPrice { get; init; }
    public decimal CurrentMarketValue { get; init; }
    public decimal CurrentMarketValueUsd => CurrentMarketValue;
    public decimal UnrealizedGain { get; init; }
    public decimal UnrealizedGainUsd => UnrealizedGain;
    public decimal UnrealizedGainRate { get; init; }
    public decimal UnrealizedGainRateUsd => UnrealizedGainRate;
    public decimal UnrealizedGainRateJpy { get; init; }
    public decimal Multiple { get; init; }
    public decimal AnnualDividendForecast { get; init; }
    public decimal AnnualDividendForecastJpy { get; init; }
    public decimal MonthlyPassiveIncomeForecast { get; init; }
    public decimal MonthlyPassiveIncomeForecastJpy { get; init; }
    public decimal YieldOnCost { get; init; }
    public decimal CurrentDividendYield { get; init; }
    public decimal ShareChangeRatio { get; init; }
    public decimal PurchaseTotalJpy { get; init; }
    public decimal CurrentMarketValueJpy { get; init; }
    public decimal UnrealizedGainJpy { get; init; }
    public decimal CurrencyImpactJpy { get; init; }
}
