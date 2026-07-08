using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class StockDetailViewModel : ObservableObject
{
    private StockSnapshot? _snapshot;
    private string _story = "銘柄一覧から銘柄を選択してください。";

    public bool HasStock => _snapshot is not null;
    public string Title => _snapshot is null ? "銘柄詳細" : $"{_snapshot.Position.Stock.Ticker} / {_snapshot.Position.Stock.Name}";
    public string PurchaseExchangeRate => _snapshot is null ? "-" : $"{_snapshot.Position.Purchase.ExchangeRate:N2} JPY/USD";
    public string CurrentExchangeRate => _snapshot is null ? "-" : $"{_snapshot.Position.CurrentHolding.CurrentExchangeRate:N2} JPY/USD";
    public string PurchaseExchangeInfo => _snapshot is null
        ? "-"
        : $"{_snapshot.Position.Purchase.ExchangeRateAcquiredAt:yyyy/MM/dd HH:mm} / {_snapshot.Position.Purchase.ExchangeRateSource} / {_snapshot.Position.Purchase.ExchangeRateInputType}";
    public string CurrentExchangeInfo => _snapshot is null
        ? "-"
        : $"{_snapshot.Position.CurrentHolding.ExchangeRateAcquiredAt:yyyy/MM/dd HH:mm} / {_snapshot.Position.CurrentHolding.ExchangeRateSource} / {_snapshot.Position.CurrentHolding.ExchangeRateInputType}";
    public string PurchaseTotal => PurchaseTotalUsd;
    public string PurchaseTotalUsd => Money(x => x.PurchaseTotalUsd);
    public string PurchaseTotalJpy => _snapshot is null ? "-" : Formatters.Jpy(_snapshot.PurchaseTotalJpy);
    public string EffectiveAcquisitionPrice => Money(x => x.EffectiveAcquisitionPrice);
    public string CurrentMarketValue => CurrentMarketValueUsd;
    public string CurrentMarketValueUsd => Money(x => x.CurrentMarketValueUsd);
    public string CurrentMarketValueJpy => _snapshot is null ? "-" : Formatters.Jpy(_snapshot.CurrentMarketValueJpy);
    public string UnrealizedGain => UnrealizedGainUsd;
    public string UnrealizedGainUsd => _snapshot is null ? "-" : Formatters.SignedMoney(_snapshot.UnrealizedGainUsd, _snapshot.Position.Stock.Currency);
    public string UnrealizedGainRate => _snapshot is null ? "-" : Formatters.SignedPercent(_snapshot.UnrealizedGainRate);
    public string UnrealizedGainRateJpy => _snapshot is null ? "-" : Formatters.SignedPercent(_snapshot.UnrealizedGainRateJpy);
    public string UnrealizedGainJpy => _snapshot is null ? "-" : Formatters.SignedJpy(_snapshot.UnrealizedGainJpy);
    public string CurrencyImpactJpy => _snapshot is null ? "-" : Formatters.SignedJpy(_snapshot.CurrencyImpactJpy);
    public string Multiple => _snapshot is null ? "-" : $"{_snapshot.Multiple:N2}倍";
    public string AnnualDividend => IsDividendNotEntered ? "配当情報が未入力です" : Money(x => x.AnnualDividendForecast);
    public string AnnualDividendJpy => _snapshot is null ? "-" : IsDividendNotEntered ? "配当情報が未入力です" : Formatters.Jpy(_snapshot.AnnualDividendForecastJpy);
    public string MonthlyDividend => IsDividendNotEntered ? "配当情報が未入力です" : Money(x => x.MonthlyPassiveIncomeForecast);
    public string MonthlyDividendJpy => _snapshot is null ? "-" : IsDividendNotEntered ? "配当情報が未入力です" : Formatters.Jpy(_snapshot.MonthlyPassiveIncomeForecastJpy);
    public string CurrentDividendYield => _snapshot is null ? "-" : IsDividendNotEntered ? "配当情報が未入力です" : Formatters.Percent(_snapshot.CurrentDividendYield);
    public string YieldOnCost => _snapshot is null ? "-" : IsDividendNotEntered ? "配当情報が未入力です" : Formatters.Percent(_snapshot.YieldOnCost);
    public string ShareChangeRatio => _snapshot is null ? "-" : $"{_snapshot.ShareChangeRatio:N2}倍";
    public string CurrentPriceSource => _snapshot is null || string.IsNullOrWhiteSpace(_snapshot.Position.CurrentHolding.CurrentPriceSource)
        ? "未取得"
        : _snapshot.Position.CurrentHolding.CurrentPriceSource;
    public string CurrentPriceAcquiredAt => _snapshot is null || _snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt == DateTime.MinValue
        ? "未取得"
        : _snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt.ToString("yyyy/MM/dd HH:mm");
    public string DividendInfoSource => _snapshot is null || string.IsNullOrWhiteSpace(_snapshot.Position.CurrentHolding.DividendInfoSource)
        ? "未取得"
        : _snapshot.Position.CurrentHolding.DividendInfoSource;
    public string DividendInfoAcquiredAt => _snapshot is null || _snapshot.Position.CurrentHolding.DividendInfoAcquiredAt == DateTime.MinValue
        ? "未取得"
        : _snapshot.Position.CurrentHolding.DividendInfoAcquiredAt.ToString("yyyy/MM/dd HH:mm");
    public string Story => _story;

    private bool IsDividendNotEntered => _snapshot?.Position.CurrentHolding.DividendStatus == "配当未入力";

    public void Update(StockSnapshot? snapshot, string? story)
    {
        _snapshot = snapshot;
        _story = story ?? "銘柄一覧から銘柄を選択してください。";
        RefreshAllProperties();
    }

    private string Money(Func<StockSnapshot, decimal> selector)
    {
        if (_snapshot is null)
        {
            return "-";
        }

        return Formatters.Money(selector(_snapshot), _snapshot.Position.Stock.Currency);
    }
}
