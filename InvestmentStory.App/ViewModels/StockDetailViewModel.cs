using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using System.Windows;

namespace InvestmentStory.App.ViewModels;

public sealed class StockDetailViewModel : ObservableObject
{
    private StockSnapshot? _snapshot;
    private string _story = "銘柄一覧から銘柄を選択してください。";

    public bool HasStock => _snapshot is not null;
    public bool IsMutualFund => _snapshot?.Position.IsMutualFund == true;
    public Visibility StockDetailVisibility => IsMutualFund ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MutualFundDetailVisibility => IsMutualFund ? Visibility.Visible : Visibility.Collapsed;
    public string Title => _snapshot is null ? "銘柄詳細" : $"{_snapshot.Position.Stock.Ticker} / {_snapshot.Position.Stock.Name}";
    public string HeaderTicker => _snapshot?.Position.Stock.Ticker ?? "-";
    public string HeaderName => _snapshot?.Position.Stock.Name ?? "銘柄を選択してください";
    public string HeaderBroker => _snapshot?.Position.Stock.Broker ?? "-";
    public string CurrentPriceLabel => IsMutualFund ? "基準価額" : "現在株価";
    public string CurrentPrice => _snapshot is null
        ? "-"
        : IsMutualFund ? FundCurrentNav : Formatters.Money(_snapshot.Position.CurrentHolding.CurrentPrice, _snapshot.Position.Stock.Currency);
    public string HeaderIncomeLabel => IsMutualFund ? "分配金受取" : "年間配当";
    public string HeaderIncomeValue => IsMutualFund ? FundDistributionMethod : AnnualDividendJpy;
    public string HeaderIncomeSubValue => IsMutualFund
        ? (string.Equals(_snapshot?.Position.MutualFund.DistributionMethod, "再投資", StringComparison.Ordinal)
            ? "現金配当対象外"
            : "分配金設定")
        : YieldOnCost;
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
    public bool HasPositiveGain => _snapshot is not null && _snapshot.UnrealizedGainJpy > 0m;
    public bool HasNegativeGain => _snapshot is not null && _snapshot.UnrealizedGainJpy < 0m;
    public string FundName => _snapshot is null
        ? "-"
        : string.IsNullOrWhiteSpace(_snapshot.Position.MutualFund.FundName)
            ? _snapshot.Position.Stock.Name
            : _snapshot.Position.MutualFund.FundName;
    public string FundAccountType => EmptyToDash(_snapshot?.Position.MutualFund.AccountType);
    public string FundUnitsHeld => _snapshot is null ? "-" : $"{Formatters.Number(_snapshot.Position.MutualFund.UnitsHeld)}口";
    public string FundAverageCostNav => _snapshot is null ? "-" : FormatFundNav(_snapshot.Position.MutualFund.AverageCostNav, _snapshot.Position.MutualFund.UnitBase);
    public string FundCurrentNav => _snapshot is null || _snapshot.Position.MutualFund.CurrentNav <= 0m
        ? "基準価額未更新"
        : FormatFundNav(_snapshot.Position.MutualFund.CurrentNav, _snapshot.Position.MutualFund.UnitBase);
    public string FundAcquisitionAmount => _snapshot is null ? "-" : Formatters.Jpy(_snapshot.PurchaseTotalJpy);
    public string FundMarketValue => _snapshot is null ? "-" : Formatters.Jpy(_snapshot.CurrentMarketValueJpy);
    public string FundUnrealizedGainLoss => _snapshot is null ? "-" : Formatters.SignedJpy(_snapshot.UnrealizedGainJpy);
    public string FundUnrealizedGainLossRate => _snapshot is null ? "-" : Formatters.SignedPercent(_snapshot.UnrealizedGainRateJpy);
    public string FundNavDate => _snapshot is null || _snapshot.Position.MutualFund.NavDate == DateTime.MinValue
        ? "基準価額未更新"
        : _snapshot.Position.MutualFund.NavDate.ToString("yyyy/MM/dd");
    public string FundNavSource => EmptyToDash(_snapshot?.Position.MutualFund.NavSource, "基準価額未更新");
    public string FundDistributionMethod => EmptyToDash(_snapshot?.Position.MutualFund.DistributionMethod);
    public string FundTotalPurchaseAmount => _snapshot is null
        ? "-"
        : Formatters.Jpy(_snapshot.Position.MutualFund.TotalPurchaseAmount > 0m
            ? _snapshot.Position.MutualFund.TotalPurchaseAmount
            : _snapshot.PurchaseTotalJpy);
    public string FundTotalSaleAmount => _snapshot is null ? "-" : Formatters.Jpy(_snapshot.Position.MutualFund.TotalSaleAmount);
    public string FundTotalReturn => _snapshot is null
        ? "-"
        : Formatters.SignedJpy(_snapshot.CurrentMarketValueJpy + _snapshot.Position.MutualFund.TotalSaleAmount -
            (_snapshot.Position.MutualFund.TotalPurchaseAmount > 0m ? _snapshot.Position.MutualFund.TotalPurchaseAmount : _snapshot.PurchaseTotalJpy));

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

    private static string FormatFundNav(decimal nav, decimal unitBase)
    {
        var normalizedUnitBase = MutualFundCalculator.NormalizeUnitBase(unitBase);
        return $"{Formatters.Jpy(nav)} / {normalizedUnitBase:N0}口";
    }

    private static string EmptyToDash(string? value, string fallback = "-") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
