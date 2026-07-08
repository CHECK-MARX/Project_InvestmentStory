using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class StockRowViewModel
{
    public StockRowViewModel(StockSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public StockSnapshot Snapshot { get; }
    public int StockId => Snapshot.Position.Stock.Id;
    public string Ticker => Snapshot.Position.Stock.Ticker;
    public string Name => Snapshot.Position.Stock.Name;
    public string Country => Snapshot.Position.Stock.Country;
    public string Currency => Snapshot.Position.Stock.Currency;
    public string Broker => Snapshot.Position.Stock.Broker;
    public string CurrentShares => Formatters.Number(Snapshot.Position.CurrentHolding.CurrentShares);
    public string EffectiveAcquisitionPrice => Formatters.Money(Snapshot.EffectiveAcquisitionPrice, Snapshot.Position.Stock.Currency);
    public string CurrentPrice => Snapshot.Position.CurrentHolding.CurrentPrice == 0m &&
        string.IsNullOrWhiteSpace(Snapshot.Position.CurrentHolding.CurrentPriceSource)
            ? "未取得"
            : Formatters.Money(Snapshot.Position.CurrentHolding.CurrentPrice, Snapshot.Position.Stock.Currency);
    public string CurrentMarketValue => Formatters.Money(Snapshot.CurrentMarketValue, Snapshot.Position.Stock.Currency);
    public string CurrentMarketValueJpy => Formatters.Jpy(Snapshot.CurrentMarketValueJpy);
    public string UnrealizedGain => Formatters.SignedMoney(Snapshot.UnrealizedGain, Snapshot.Position.Stock.Currency);
    public string UnrealizedGainRate => Formatters.SignedPercent(Snapshot.UnrealizedGainRate);
    public string UnrealizedGainJpy => Formatters.SignedJpy(Snapshot.UnrealizedGainJpy);
    public string UnrealizedGainRateJpy => Formatters.SignedPercent(Snapshot.UnrealizedGainRateJpy);
    public string PurchaseExchangeRate => $"{Snapshot.Position.Purchase.ExchangeRate:N2}";
    public string CurrentExchangeRate => $"{Snapshot.Position.CurrentHolding.CurrentExchangeRate:N2}";
    public string AnnualDividendForecast => Snapshot.Position.CurrentHolding.DividendStatus == "配当未入力"
        ? "未入力"
        : Formatters.Money(Snapshot.AnnualDividendForecast, Snapshot.Position.Stock.Currency);
    public string AnnualDividendForecastJpy => Snapshot.Position.CurrentHolding.DividendStatus == "配当未入力"
        ? "未入力"
        : Formatters.Jpy(Snapshot.AnnualDividendForecastJpy);
    public string YieldOnCost => Snapshot.Position.CurrentHolding.DividendStatus == "配当未入力"
        ? "未入力"
        : Formatters.Percent(Snapshot.YieldOnCost);
    public string CurrentDividendYield => Snapshot.Position.CurrentHolding.DividendStatus == "配当未入力"
        ? "未入力"
        : Formatters.Percent(Snapshot.CurrentDividendYield);
    public string PriceAcquiredAt => Snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt == DateTime.MinValue
        ? "未取得"
        : Snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt.ToString("yyyy/MM/dd HH:mm");
    public string DataSource => string.IsNullOrWhiteSpace(Snapshot.Position.Stock.DataSource)
        ? Snapshot.Position.CurrentHolding.CurrentPriceSource
        : Snapshot.Position.Stock.DataSource;
}
