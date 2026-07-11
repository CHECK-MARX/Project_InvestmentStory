using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class StockRowViewModel
{
    public StockRowViewModel(StockSnapshot snapshot)
        : this(new[] { snapshot })
    {
    }

    public StockRowViewModel(IReadOnlyList<StockSnapshot> snapshots)
    {
        Snapshots = snapshots.Count == 0 ? throw new ArgumentException("At least one snapshot is required.", nameof(snapshots)) : snapshots;
        Snapshot = Snapshots[0];
    }

    public IReadOnlyList<StockSnapshot> Snapshots { get; }
    public StockSnapshot Snapshot { get; }
    public bool IsAggregated => Snapshots.Count > 1;
    public int StockId => Snapshot.Position.Stock.Id;
    public string CanonicalSecurityKey => SecurityIdentityService.BuildCanonicalKey(Snapshot.Position);
    public string DetailKey => IsAggregated
        ? CanonicalSecurityKey
        : SecurityIdentityService.BuildPositionKey(Snapshot.Position);
    public string DetailScopeLabel => IsAggregated ? "全口座集約" : $"{Broker} / {AccountType}";
    public bool IsMutualFund => Snapshots.All(x => x.Position.IsMutualFund);
    public string AssetTypeLabel => IsMutualFund ? "投資信託" : "株式";
    public string Ticker => Snapshot.Position.Stock.Ticker;
    public string Name => IsAggregated ? $"{Snapshot.Position.Stock.Name}（集約）" : Snapshot.Position.Stock.Name;
    public string FundName => string.IsNullOrWhiteSpace(Snapshot.Position.MutualFund.FundName)
        ? Snapshot.Position.Stock.Name
        : Snapshot.Position.MutualFund.FundName;
    public string Country => Snapshot.Position.Stock.Country;
    public string Currency => Snapshot.Position.Stock.Currency;
    public string Broker => SameOrMultiple(Snapshots.Select(x => x.Position.Stock.Broker));
    public string AccountType => SameOrMultiple(Snapshots.Select(x => ResolveAccountType(x.Position)));
    public string CostBasisJpy => Formatters.Jpy(PurchaseTotalJpy);
    public string MarketValueJpy => Formatters.Jpy(CurrentMarketValueJpyValue);
    public string GainLossJpy => Formatters.SignedJpy(UnrealizedGainJpyValue);
    public string GainLossRateJpy => Formatters.SignedPercent(UnrealizedGainRateJpyValue);
    public string CurrentShares => IsMutualFund
        ? $"{Formatters.Number(Snapshots.Sum(x => x.Position.MutualFund.UnitsHeld))}口"
        : Formatters.Number(Snapshots.Sum(x => x.Position.CurrentHolding.CurrentShares));
    public string EffectiveAcquisitionPrice => IsMutualFund
        ? FundAverageCostNav
        : Formatters.Money(Snapshot.EffectiveAcquisitionPrice, Snapshot.Position.Stock.Currency);
    public string CurrentPrice => IsMutualFund
        ? FundCurrentNav
        : Snapshot.Position.CurrentHolding.CurrentPrice == 0m &&
        string.IsNullOrWhiteSpace(Snapshot.Position.CurrentHolding.CurrentPriceSource)
            ? "未取得"
            : Formatters.Money(Snapshot.Position.CurrentHolding.CurrentPrice, Snapshot.Position.Stock.Currency);
    public string CurrentMarketValue => IsAggregated
        ? Formatters.Jpy(CurrentMarketValueJpyValue)
        : Formatters.Money(Snapshot.CurrentMarketValue, Snapshot.Position.Stock.Currency);
    public string CurrentMarketValueJpy => Formatters.Jpy(CurrentMarketValueJpyValue);
    public string UnrealizedGain => IsAggregated
        ? Formatters.SignedJpy(UnrealizedGainJpyValue)
        : Formatters.SignedMoney(Snapshot.UnrealizedGain, Snapshot.Position.Stock.Currency);
    public string UnrealizedGainRate => IsAggregated
        ? Formatters.SignedPercent(UnrealizedGainRateJpyValue)
        : Formatters.SignedPercent(Snapshot.UnrealizedGainRate);
    public string UnrealizedGainJpy => Formatters.SignedJpy(UnrealizedGainJpyValue);
    public string UnrealizedGainRateJpy => Formatters.SignedPercent(UnrealizedGainRateJpyValue);
    public bool HasPositiveGain => UnrealizedGainJpyValue > 0m;
    public bool HasNegativeGain => UnrealizedGainJpyValue < 0m;
    public string PurchaseExchangeRate => $"{Snapshot.Position.Purchase.ExchangeRate:N2}";
    public string CurrentExchangeRate => $"{Snapshot.Position.CurrentHolding.CurrentExchangeRate:N2}";
    public string AnnualDividendForecast => HasMissingDividendInfo
        ? "未入力"
        : IsAggregated ? Formatters.Jpy(Snapshots.Sum(x => x.AnnualDividendForecastJpy)) : Formatters.Money(Snapshot.AnnualDividendForecast, Snapshot.Position.Stock.Currency);
    public string AnnualDividendForecastJpy => HasMissingDividendInfo
        ? "未入力"
        : Formatters.Jpy(Snapshots.Sum(x => x.AnnualDividendForecastJpy));
    public string YieldOnCost => HasMissingDividendInfo
        ? "未入力"
        : Formatters.Percent(PurchaseTotalJpy <= 0m ? 0m : Snapshots.Sum(x => x.AnnualDividendForecastJpy) / PurchaseTotalJpy * 100m);
    public string CurrentDividendYield => HasMissingDividendInfo
        ? "未入力"
        : Formatters.Percent(CurrentMarketValueJpyValue <= 0m ? 0m : Snapshots.Sum(x => x.AnnualDividendForecastJpy) / CurrentMarketValueJpyValue * 100m);
    public string PriceAcquiredAt => Snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt == DateTime.MinValue
        ? "未取得"
        : Snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt.ToString("yyyy/MM/dd HH:mm");
    public string CurrentPriceSource => string.IsNullOrWhiteSpace(Snapshot.Position.CurrentHolding.CurrentPriceSource)
        ? "未取得"
        : Snapshot.Position.CurrentHolding.CurrentPriceSource;
    public string DataSource => string.IsNullOrWhiteSpace(Snapshot.Position.Stock.DataSource)
        ? "未設定"
        : Snapshot.Position.Stock.DataSource;
    public string FundAccountType => string.IsNullOrWhiteSpace(SameOrMultiple(Snapshots.Select(x => x.Position.MutualFund.AccountType)))
        ? "-"
        : SameOrMultiple(Snapshots.Select(x => x.Position.MutualFund.AccountType));
    public string FundUnitsHeld => $"{Formatters.Number(Snapshots.Sum(x => x.Position.MutualFund.UnitsHeld))}口";
    public string FundAverageCostNav => FormatFundNav(Snapshot.Position.MutualFund.AverageCostNav, Snapshot.Position.MutualFund.UnitBase);
    public string FundCurrentNav => Snapshot.Position.MutualFund.CurrentNav <= 0m
        ? "基準価額未更新"
        : FormatFundNav(Snapshot.Position.MutualFund.CurrentNav, Snapshot.Position.MutualFund.UnitBase);
    public string FundAcquisitionAmount => Formatters.Jpy(PurchaseTotalJpy);
    public string FundMarketValue => Formatters.Jpy(CurrentMarketValueJpyValue);
    public string FundUnrealizedGainLoss => Formatters.SignedJpy(UnrealizedGainJpyValue);
    public string FundUnrealizedGainLossRate => Formatters.SignedPercent(UnrealizedGainRateJpyValue);
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

    private decimal PurchaseTotalJpy => Snapshots.Sum(x => x.PurchaseTotalJpy);
    private decimal CurrentMarketValueJpyValue => Snapshots.Sum(x => x.CurrentMarketValueJpy);
    private decimal UnrealizedGainJpyValue => CurrentMarketValueJpyValue - PurchaseTotalJpy;
    private decimal UnrealizedGainRateJpyValue => PurchaseTotalJpy <= 0m ? 0m : UnrealizedGainJpyValue / PurchaseTotalJpy * 100m;
    private bool HasMissingDividendInfo => Snapshots.Any(x => x.Position.CurrentHolding.DividendStatus == "配当未入力");

    private static string ResolveAccountType(StockPosition position)
    {
        if (position.IsMutualFund && !string.IsNullOrWhiteSpace(position.MutualFund.AccountType))
        {
            return position.MutualFund.AccountType;
        }

        return position.Stock.AccountType;
    }

    private static string SameOrMultiple(IEnumerable<string> values)
    {
        var normalized = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count switch
        {
            0 => "-",
            1 => normalized[0],
            _ => "複数"
        };
    }
}
