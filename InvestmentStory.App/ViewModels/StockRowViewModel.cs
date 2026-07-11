using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class StockRowViewModel
{
    public StockRowViewModel(StockSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public StockSnapshot Snapshot { get; }
    public int StockId => Snapshot.Position.Stock.Id;
    public bool IsMutualFund => Snapshot.Position.IsMutualFund;
    public string AssetTypeLabel => IsMutualFund ? "投資信託" : "株式";
    public string Ticker => Snapshot.Position.Stock.Ticker;
    public string Name => Snapshot.Position.Stock.Name;
    public string FundName => string.IsNullOrWhiteSpace(Snapshot.Position.MutualFund.FundName)
        ? Snapshot.Position.Stock.Name
        : Snapshot.Position.MutualFund.FundName;
    public string Country => Snapshot.Position.Stock.Country;
    public string Currency => Snapshot.Position.Stock.Currency;
    public string Broker => Snapshot.Position.Stock.Broker;
    public string CurrentShares => IsMutualFund
        ? $"{Formatters.Number(Snapshot.Position.MutualFund.UnitsHeld)}口"
        : Formatters.Number(Snapshot.Position.CurrentHolding.CurrentShares);
    public string EffectiveAcquisitionPrice => IsMutualFund
        ? FundAverageCostNav
        : Formatters.Money(Snapshot.EffectiveAcquisitionPrice, Snapshot.Position.Stock.Currency);
    public string CurrentPrice => IsMutualFund
        ? FundCurrentNav
        : Snapshot.Position.CurrentHolding.CurrentPrice == 0m &&
        string.IsNullOrWhiteSpace(Snapshot.Position.CurrentHolding.CurrentPriceSource)
            ? "未取得"
            : Formatters.Money(Snapshot.Position.CurrentHolding.CurrentPrice, Snapshot.Position.Stock.Currency);
    public string CurrentMarketValue => Formatters.Money(Snapshot.CurrentMarketValue, Snapshot.Position.Stock.Currency);
    public string CurrentMarketValueJpy => Formatters.Jpy(Snapshot.CurrentMarketValueJpy);
    public string UnrealizedGain => Formatters.SignedMoney(Snapshot.UnrealizedGain, Snapshot.Position.Stock.Currency);
    public string UnrealizedGainRate => Formatters.SignedPercent(Snapshot.UnrealizedGainRate);
    public string UnrealizedGainJpy => Formatters.SignedJpy(Snapshot.UnrealizedGainJpy);
    public string UnrealizedGainRateJpy => Formatters.SignedPercent(Snapshot.UnrealizedGainRateJpy);
    public bool HasPositiveGain => Snapshot.UnrealizedGainJpy > 0m || Snapshot.UnrealizedGain > 0m;
    public bool HasNegativeGain => Snapshot.UnrealizedGainJpy < 0m || Snapshot.UnrealizedGain < 0m;
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
    public string CurrentPriceSource => string.IsNullOrWhiteSpace(Snapshot.Position.CurrentHolding.CurrentPriceSource)
        ? "未取得"
        : Snapshot.Position.CurrentHolding.CurrentPriceSource;
    public string DataSource => string.IsNullOrWhiteSpace(Snapshot.Position.Stock.DataSource)
        ? "未設定"
        : Snapshot.Position.Stock.DataSource;
    public string FundAccountType => string.IsNullOrWhiteSpace(Snapshot.Position.MutualFund.AccountType)
        ? "-"
        : Snapshot.Position.MutualFund.AccountType;
    public string FundUnitsHeld => $"{Formatters.Number(Snapshot.Position.MutualFund.UnitsHeld)}口";
    public string FundAverageCostNav => FormatFundNav(Snapshot.Position.MutualFund.AverageCostNav, Snapshot.Position.MutualFund.UnitBase);
    public string FundCurrentNav => Snapshot.Position.MutualFund.CurrentNav <= 0m
        ? "基準価額未更新"
        : FormatFundNav(Snapshot.Position.MutualFund.CurrentNav, Snapshot.Position.MutualFund.UnitBase);
    public string FundAcquisitionAmount => Formatters.Jpy(Snapshot.PurchaseTotalJpy);
    public string FundMarketValue => Formatters.Jpy(Snapshot.CurrentMarketValueJpy);
    public string FundUnrealizedGainLoss => Formatters.SignedJpy(Snapshot.UnrealizedGainJpy);
    public string FundUnrealizedGainLossRate => Formatters.SignedPercent(Snapshot.UnrealizedGainRateJpy);
    public string FundNavDate => Snapshot.Position.MutualFund.NavDate == DateTime.MinValue
        ? "基準価額未更新"
        : Snapshot.Position.MutualFund.NavDate.ToString("yyyy/MM/dd");
    public string FundNavSource => string.IsNullOrWhiteSpace(Snapshot.Position.MutualFund.NavSource)
        ? "基準価額未更新"
        : Snapshot.Position.MutualFund.NavSource;
    public string FundDistributionMethod => string.IsNullOrWhiteSpace(Snapshot.Position.MutualFund.DistributionMethod)
        ? "-"
        : Snapshot.Position.MutualFund.DistributionMethod;

    private static string FormatFundNav(decimal nav, decimal unitBase)
    {
        var normalizedUnitBase = MutualFundCalculator.NormalizeUnitBase(unitBase);
        return $"{Formatters.Jpy(nav)} / {normalizedUnitBase:N0}口";
    }
}
