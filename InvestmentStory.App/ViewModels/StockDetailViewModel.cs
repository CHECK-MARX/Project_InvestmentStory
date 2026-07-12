using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace InvestmentStory.App.ViewModels;

public sealed class StockDetailViewModel : ObservableObject
{
    private StockSnapshot? _snapshot;
    private IReadOnlyList<StockSnapshot> _snapshots = Array.Empty<StockSnapshot>();
    private IReadOnlyList<BrokerTrade> _trades = Array.Empty<BrokerTrade>();
    private bool _hasSyntheticInitialPositionTrades;
    private string _story = "銘柄一覧から銘柄を選択してください。";
    private IReadOnlyList<PriceChartPointViewModel> _priceChartPoints = Array.Empty<PriceChartPointViewModel>();
    private string _selectedChartPeriod = "6か月";
    private string _priceChartStatus = "銘柄を選択するとチャートを取得します。";
    private decimal _chartCurrentPriceValue;
    private decimal _chartAverageCostValue;
    private string _chartCurrency = "JPY";

    public StockDetailViewModel()
    {
        RefreshPriceChartCommand = new RelayCommand(() => PriceChartRefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? PriceChartRefreshRequested;

    public bool HasStock => _snapshot is not null;
    public bool IsAggregated => _snapshots.Count > 1;
    public bool IsMutualFund => _snapshot?.Position.IsMutualFund == true;
    public Visibility StockDetailVisibility => IsMutualFund ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MutualFundDetailVisibility => IsMutualFund ? Visibility.Visible : Visibility.Collapsed;
    public string Title => _snapshot is null ? "銘柄詳細" : $"{_snapshot.Position.Stock.Ticker} / {_snapshot.Position.Stock.Name}";
    public string HeaderTicker => _snapshot?.Position.Stock.Ticker ?? "-";
    public string HeaderName => _snapshot?.Position.Stock.Name ?? "銘柄を選択してください";
    public string HeaderBroker => IsAggregated ? "全口座集約" : _snapshot?.Position.Stock.Broker ?? "-";
    public string DetailScope => IsAggregated ? $"全口座集約 / {_snapshots.Count:N0}ポジション" : HeaderBroker;
    public string PositionBreakdown => _snapshots.Count == 0
        ? "-"
        : string.Join(" / ", _snapshots.Select(x =>
        {
            var quantity = x.Position.IsMutualFund ? x.Position.MutualFund.UnitsHeld : x.Position.CurrentHolding.CurrentShares;
            return $"{x.Position.Stock.Broker}:{AccountTypes.DisplayName(x.Position.Stock.AccountType)}:{quantity:N2}";
        }));

    public string CurrentPriceLabel => IsMutualFund ? "基準価額" : "現在株価";
    public string CurrentPrice => _snapshot is null
        ? "-"
        : IsMutualFund ? FundCurrentNav : Formatters.Money(_snapshot.Position.CurrentHolding.CurrentPrice, _snapshot.Position.Stock.Currency);
    public string HeaderIncomeLabel => IsMutualFund ? "分配金受取" : "年間配当";
    public string HeaderIncomeValue => IsMutualFund ? FundDistributionMethod : AnnualDividendJpy;
    public string HeaderIncomeSubValue => IsMutualFund
        ? (FundDistributionMethod.Contains("再投資", StringComparison.Ordinal) ? "現金配当対象外" : "分配金設定")
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
    public string PurchaseTotalJpy => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.PurchaseTotalJpy));
    public string EffectiveAcquisitionPrice => _snapshot is null
        ? "-"
        : Formatters.Money(DivideOrZero(Sum(x => x.PurchaseTotal), Sum(x => x.Position.CurrentHolding.CurrentShares)), _snapshot.Position.Stock.Currency);
    public string CurrentMarketValue => CurrentMarketValueUsd;
    public string CurrentMarketValueUsd => Money(x => x.CurrentMarketValueUsd);
    public string CurrentMarketValueJpy => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.CurrentMarketValueJpy));
    public string UnrealizedGain => UnrealizedGainUsd;
    public string UnrealizedGainUsd => _snapshot is null ? "-" : Formatters.SignedMoney(Sum(x => x.UnrealizedGainUsd), _snapshot.Position.Stock.Currency);
    public string UnrealizedGainRate => _snapshot is null ? "-" : Formatters.SignedPercent(CalculateRate(Sum(x => x.CurrentMarketValue) - Sum(x => x.PurchaseTotal), Sum(x => x.PurchaseTotal)));
    public string UnrealizedGainRateJpy => _snapshot is null ? "-" : Formatters.SignedPercent(CalculateRate(Sum(x => x.CurrentMarketValueJpy) - Sum(x => x.PurchaseTotalJpy), Sum(x => x.PurchaseTotalJpy)));
    public string UnrealizedGainJpy => _snapshot is null ? "-" : Formatters.SignedJpy(Sum(x => x.CurrentMarketValueJpy) - Sum(x => x.PurchaseTotalJpy));
    public string CurrencyImpactJpy => _snapshot is null ? "-" : Formatters.SignedJpy(Sum(x => x.CurrencyImpactJpy));
    public string Multiple => _snapshot is null ? "-" : $"{DivideOrZero(Sum(x => x.CurrentMarketValue), Sum(x => x.PurchaseTotal)):N2}倍";
    public string AnnualDividend => Money(x => x.AnnualDividendForecast);
    public string AnnualDividendJpy => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.AnnualDividendForecastJpy));
    public string MonthlyDividend => Money(x => x.MonthlyPassiveIncomeForecast);
    public string MonthlyDividendJpy => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.MonthlyPassiveIncomeForecastJpy));
    public string CurrentDividendYield => _snapshot is null ? "-" : Formatters.Percent(CalculateRate(Sum(x => x.AnnualDividendForecastJpy), Sum(x => x.CurrentMarketValueJpy)));
    public string YieldOnCost => _snapshot is null ? "-" : Formatters.Percent(CalculateRate(Sum(x => x.AnnualDividendForecastJpy), Sum(x => x.PurchaseTotalJpy)));
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
    public bool HasPositiveGain => _snapshot is not null && Sum(x => x.CurrentMarketValueJpy) - Sum(x => x.PurchaseTotalJpy) > 0m;
    public bool HasNegativeGain => _snapshot is not null && Sum(x => x.CurrentMarketValueJpy) - Sum(x => x.PurchaseTotalJpy) < 0m;

    public string FundName => _snapshot is null
        ? "-"
        : string.IsNullOrWhiteSpace(_snapshot.Position.MutualFund.FundName)
            ? _snapshot.Position.Stock.Name
            : _snapshot.Position.MutualFund.FundName;
    public string FundAccountType => EmptyToDash(_snapshot?.Position.MutualFund.AccountType);
    public string FundUnitsHeld => _snapshot is null ? "-" : $"{Formatters.Number(Sum(x => x.Position.MutualFund.UnitsHeld))}口";
    public string FundAverageCostNav => _snapshot is null ? "-" : FormatFundNav(WeightedFundNav(x => x.PurchaseTotalJpy), FundUnitBase);
    public string FundCurrentNav => _snapshot is null || Sum(x => x.CurrentMarketValueJpy) <= 0m
        ? "基準価額未更新"
        : FormatFundNav(WeightedFundNav(x => x.CurrentMarketValueJpy), FundUnitBase);
    public string FundAcquisitionAmount => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.PurchaseTotalJpy));
    public string FundMarketValue => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.CurrentMarketValueJpy));
    public string FundUnrealizedGainLoss => _snapshot is null ? "-" : Formatters.SignedJpy(Sum(x => x.UnrealizedGainJpy));
    public string FundUnrealizedGainLossRate => _snapshot is null ? "-" : Formatters.SignedPercent(CalculateRate(Sum(x => x.UnrealizedGainJpy), Sum(x => x.PurchaseTotalJpy)));
    public string FundNavDate => _snapshot is null
        ? "-"
        : _snapshots
            .Select(x => x.Position.MutualFund.NavDate)
            .Where(x => x != DateTime.MinValue)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max() == DateTime.MinValue
                ? "基準価額未更新"
                : _snapshots
                    .Select(x => x.Position.MutualFund.NavDate)
                    .Where(x => x != DateTime.MinValue)
                    .Max()
                    .ToString("yyyy/MM/dd");
    public string FundNavSource => DistinctJoined(_snapshots.Select(x => x.Position.MutualFund.NavSource), "基準価額未更新");
    public string FundDistributionMethod => DistinctJoined(_snapshots.Select(x => x.Position.MutualFund.DistributionMethod), "-");
    public string FundTotalPurchaseAmount => _snapshot is null
        ? "-"
        : Formatters.Jpy(Sum(x => x.Position.MutualFund.TotalPurchaseAmount > 0m
            ? x.Position.MutualFund.TotalPurchaseAmount
            : x.PurchaseTotalJpy));
    public string FundTotalSaleAmount => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.Position.MutualFund.TotalSaleAmount));
    public string FundTotalReturn => _snapshot is null
        ? "-"
        : Formatters.SignedJpy(Sum(x => x.CurrentMarketValueJpy + x.Position.MutualFund.TotalSaleAmount -
            (x.Position.MutualFund.TotalPurchaseAmount > 0m ? x.Position.MutualFund.TotalPurchaseAmount : x.PurchaseTotalJpy)));

    public ObservableCollection<BrokerTradeRowViewModel> TradeRows { get; } = new();
    public ObservableCollection<DataQualityRowViewModel> DataQualityRows { get; } = new();
    public IReadOnlyList<string> ChartPeriods { get; } = new[] { "1か月", "3か月", "6か月", "1年", "3年", "5年", "全期間" };
    public ICommand RefreshPriceChartCommand { get; }
    public string PriceChartTitle => IsMutualFund ? "基準価額チャート" : "価格チャート";
    public bool IsPriceChartLineOnly => IsMutualFund;
    public IReadOnlyList<PriceChartPointViewModel> PriceChartPoints => _priceChartPoints;
    public string SelectedChartPeriod
    {
        get => _selectedChartPeriod;
        set
        {
            if (SetProperty(ref _selectedChartPeriod, value))
            {
                PriceChartRefreshRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string PriceChartStatus
    {
        get => _priceChartStatus;
        private set => SetProperty(ref _priceChartStatus, value);
    }

    public decimal ChartCurrentPriceValue
    {
        get => _chartCurrentPriceValue;
        private set => SetProperty(ref _chartCurrentPriceValue, value);
    }

    public decimal ChartAverageCostValue
    {
        get => _chartAverageCostValue;
        private set => SetProperty(ref _chartAverageCostValue, value);
    }

    public string ChartCurrency
    {
        get => _chartCurrency;
        private set => SetProperty(ref _chartCurrency, value);
    }

    public string TradeCount => $"{_trades.Count:N0}件";
    public string TotalBuyAmountJpy => Formatters.Jpy(TotalBuyAmountForDisplayJpy);
    public string TotalSaleAmountJpy => Formatters.Jpy(_trades.Where(IsSellTrade).Sum(x => Math.Abs(x.SettlementAmountJpy)));
    public string RealizedGainLossJpy => Formatters.SignedJpy(_trades.Sum(x => x.RealizedGainLossJpy));
    public string CurrentTradeQuantity => CanUseReconstructedTradeSummary
        ? Formatters.Number(ReconstructedTradeQuantity)
        : SnapshotQuantity > 0m
            ? Formatters.Number(SnapshotQuantity)
            : _trades.Count == 0
            ? "-"
            : Formatters.Number(LatestTrade.AfterTradeQuantity);
    public string AverageTradeCost => CanUseReconstructedTradeSummary && ReconstructedAverageTradeCost > 0m
        ? Formatters.Money(ReconstructedAverageTradeCost, _snapshot?.Position.Stock.Currency ?? "JPY")
        : SnapshotQuantity > 0m
            ? Formatters.Money(SnapshotAverageCost, _snapshot?.Position.Stock.Currency ?? "JPY")
            : _trades.Count == 0
            ? "-"
            : Formatters.Money(LatestTrade.AfterTradeAverageCost, _snapshot?.Position.Stock.Currency ?? "JPY");
    public string TradeDataWarning => HasTradeQuantityMismatch
        ? "取引履歴CSVの復元数量と保有残高CSVの数量が一致しないため、現在数量・平均取得単価・取得額は保有残高CSVを優先しています。実現損益は取引履歴CSVから計算した参考値です。"
        : ShouldUseSnapshotCostBasisForBuyAmount && _trades.Count > 0 && ActualBuyAmountJpy <= 0m
            ? "取引履歴CSVの約定金額が不足しているため、累計買付額は保有残高CSVの取得額を優先しています。実現損益は取引履歴CSVから計算した参考値です。"
            : _trades.Count == 0
        ? "取引履歴CSVが未取込です。保有残高CSVまたは手入力の数量・取得単価を優先して表示しています。"
        : _hasSyntheticInitialPositionTrades
            ? "取引履歴CSVがないポジションは、保有残高CSVまたは手入力値から「初期保有」として表示しています。通常の買付・売却件数や実現損益には含めません。"
            : "実現損益はCSV取引履歴から計算した参考値です。税務上の正式な金額は証券会社の報告書を確認してください。";

    private decimal FundUnitBase => _snapshot is null
        ? 10000m
        : MutualFundCalculator.NormalizeUnitBase(_snapshot.Position.MutualFund.UnitBase);

    private BrokerTrade LatestTrade => _trades
        .OrderByDescending(x => x.TradeDate)
        .ThenByDescending(x => x.Id)
        .First();

    private decimal SnapshotQuantity => _snapshot is null
        ? 0m
        : Sum(x => x.Position.IsMutualFund
            ? x.Position.MutualFund.UnitsHeld
            : x.Position.CurrentHolding.CurrentShares);

    private decimal SnapshotAverageCost
    {
        get
        {
            if (_snapshot is null || SnapshotQuantity <= 0m)
            {
                return 0m;
            }

            return IsMutualFund
                ? WeightedFundNav(x => x.PurchaseTotalJpy)
                : DivideOrZero(Sum(x => x.PurchaseTotal), SnapshotQuantity);
        }
    }

    private decimal SnapshotCostBasisJpy => _snapshot is null || SnapshotQuantity <= 0m
        ? 0m
        : Sum(x => x.PurchaseTotalJpy);

    private decimal ActualBuyAmountJpy => _trades
        .Where(IsBuyTrade)
        .Sum(x => Math.Abs(x.SettlementAmountJpy));

    private decimal ReconstructedTradeQuantity => _trades.Count == 0
        ? 0m
        : _trades
            .GroupBy(x => x.StockId)
            .Select(x => x
                .OrderByDescending(y => y.TradeDate)
                .ThenByDescending(y => y.Id)
                .First()
                .AfterTradeQuantity)
            .Sum();

    private decimal ReconstructedAverageTradeCost
    {
        get
        {
            var latestRows = _trades
                .GroupBy(x => x.StockId)
                .Select(x => x
                    .OrderByDescending(y => y.TradeDate)
                    .ThenByDescending(y => y.Id)
                    .First())
                .Where(x => x.AfterTradeQuantity > 0m)
                .ToList();

            var quantity = latestRows.Sum(x => x.AfterTradeQuantity);
            return quantity <= 0m
                ? 0m
                : latestRows.Sum(x => x.AfterTradeQuantity * x.AfterTradeAverageCost) / quantity;
        }
    }

    private bool HasTradeQuantityMismatch => _trades.Count > 0
        && SnapshotQuantity > 0m
        && Math.Abs(ReconstructedTradeQuantity - SnapshotQuantity) > 0.0001m;

    private bool CanUseReconstructedTradeSummary => _trades.Count > 0
        && ReconstructedTradeQuantity > 0m
        && !HasTradeQuantityMismatch;

    private bool ShouldUseSnapshotCostBasisForBuyAmount => _snapshot is not null
        && SnapshotQuantity > 0m
        && (ActualBuyAmountJpy <= 0m || HasTradeQuantityMismatch || _hasSyntheticInitialPositionTrades);

    private decimal TotalBuyAmountForDisplayJpy => ShouldUseSnapshotCostBasisForBuyAmount
        ? SnapshotCostBasisJpy
        : ActualBuyAmountJpy;

    public void Update(
        StockSnapshot? snapshot,
        string? story,
        IReadOnlyList<BrokerTrade>? trades = null,
        IReadOnlyList<DataQualityInfo>? dataQualityInfos = null)
    {
        Update(snapshot is null ? Array.Empty<StockSnapshot>() : new[] { snapshot }, story, trades, dataQualityInfos);
    }

    public void Update(
        IReadOnlyList<StockSnapshot> snapshots,
        string? story,
        IReadOnlyList<BrokerTrade>? trades = null,
        IReadOnlyList<DataQualityInfo>? dataQualityInfos = null)
    {
        _snapshots = snapshots;
        _snapshot = snapshots.FirstOrDefault();
        (_trades, _hasSyntheticInitialPositionTrades) = BuildDisplayTrades(snapshots, trades ?? Array.Empty<BrokerTrade>());
        _story = story ?? "銘柄一覧から銘柄を選択してください。";
        UpdateChartReferenceValues();
        SetPriceChartMessage(_snapshot is null
            ? "銘柄を選択するとチャートを取得します。"
            : "チャートを取得中です。");

        TradeRows.Clear();
        foreach (var trade in _trades.OrderByDescending(x => x.TradeDate).ThenByDescending(x => x.Id))
        {
            TradeRows.Add(new BrokerTradeRowViewModel(trade));
        }

        DataQualityRows.Clear();
        foreach (var item in dataQualityInfos ?? Array.Empty<DataQualityInfo>())
        {
            DataQualityRows.Add(new DataQualityRowViewModel(item));
        }

        RefreshAllProperties();
    }

    public void SetPriceChartLoading(string symbol)
    {
        _priceChartPoints = Array.Empty<PriceChartPointViewModel>();
        OnPropertyChanged(nameof(PriceChartPoints));
        PriceChartStatus = $"{symbol} のチャートを取得中です。";
    }

    public void SetPriceChartMessage(string message)
    {
        _priceChartPoints = Array.Empty<PriceChartPointViewModel>();
        OnPropertyChanged(nameof(PriceChartPoints));
        PriceChartStatus = message;
    }

    public void SetPriceHistory(PriceHistoryResult result)
    {
        if (!result.IsSuccess || result.Points.Count == 0)
        {
            SetPriceChartMessage(string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "チャート履歴データを取得できませんでした。"
                : result.ErrorMessage);
            return;
        }

        _priceChartPoints = BuildPriceChartPoints(result.Points);
        OnPropertyChanged(nameof(PriceChartPoints));
        var first = _priceChartPoints.First();
        var last = _priceChartPoints.Last();
        PriceChartStatus = $"{result.Source} / {result.Symbol} / {first.Date:yyyy/MM/dd} - {last.Date:yyyy/MM/dd} / {_priceChartPoints.Count:N0}本";
    }

    private void UpdateChartReferenceValues()
    {
        if (_snapshot is null)
        {
            ChartCurrentPriceValue = 0m;
            ChartAverageCostValue = 0m;
            ChartCurrency = "JPY";
            return;
        }

        ChartCurrency = _snapshot.Position.Stock.Currency;
        ChartCurrentPriceValue = IsMutualFund
            ? WeightedFundNav(x => x.CurrentMarketValueJpy)
            : _snapshot.Position.CurrentHolding.CurrentPrice;
        ChartAverageCostValue = SnapshotAverageCost;
    }

    private static IReadOnlyList<PriceChartPointViewModel> BuildPriceChartPoints(IReadOnlyList<PriceHistoryPoint> points)
    {
        var ordered = points.OrderBy(x => x.Date).ToList();
        var rows = new List<PriceChartPointViewModel>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var point = ordered[i];
            rows.Add(new PriceChartPointViewModel
            {
                Date = point.Date,
                Open = point.Open,
                High = point.High,
                Low = point.Low,
                Close = point.Close,
                Volume = point.Volume,
                MovingAverage5 = MovingAverage(ordered, i, 5),
                MovingAverage25 = MovingAverage(ordered, i, 25)
            });
        }

        return rows;
    }

    private static decimal? MovingAverage(IReadOnlyList<PriceHistoryPoint> points, int index, int period)
    {
        if (index + 1 < period)
        {
            return null;
        }

        return points.Skip(index + 1 - period).Take(period).Average(x => x.Close);
    }

    private string Money(Func<StockSnapshot, decimal> selector)
    {
        if (_snapshot is null)
        {
            return "-";
        }

        return Formatters.Money(Sum(selector), _snapshot.Position.Stock.Currency);
    }

    private decimal Sum(Func<StockSnapshot, decimal> selector) => _snapshots.Sum(selector);

    private decimal WeightedFundNav(Func<StockSnapshot, decimal> amountSelector)
    {
        var units = Sum(x => x.Position.MutualFund.UnitsHeld);
        return units <= 0m ? 0m : Sum(amountSelector) / units * FundUnitBase;
    }

    private static decimal DivideOrZero(decimal numerator, decimal denominator) =>
        denominator == 0m ? 0m : numerator / denominator;

    private static decimal CalculateRate(decimal numerator, decimal denominator) =>
        denominator == 0m ? 0m : numerator / denominator * 100m;

    private static string FormatFundNav(decimal nav, decimal unitBase)
    {
        var normalizedUnitBase = MutualFundCalculator.NormalizeUnitBase(unitBase);
        return $"{Formatters.Jpy(nav)} / {normalizedUnitBase:N0}口";
    }

    private static string EmptyToDash(string? value, string fallback = "-") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string DistinctJoined(IEnumerable<string> values, string fallback)
    {
        var items = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return items.Count == 0 ? fallback : string.Join(" / ", items);
    }

    private static bool IsBuyTrade(BrokerTrade trade) =>
        !IsNonBuyInbound(trade) && (
            trade.SignedQuantity > 0m ||
            trade.TradeType.Contains("買", StringComparison.Ordinal) ||
            trade.TradeType.Contains("Buy", StringComparison.OrdinalIgnoreCase));

    private static bool IsNonBuyInbound(BrokerTrade trade) =>
        trade.TradeType.Equals("InitialPosition", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("初期保有", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("TransferIn", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("OpeningBalance", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("StockSplit", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("ReverseSplit", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("ReverseStockSplit", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("StockConsolidation", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Equals("UnknownAdjustment", StringComparison.OrdinalIgnoreCase) ||
        trade.TradeType.Contains("入庫", StringComparison.Ordinal) ||
        trade.TradeType.Contains("出庫", StringComparison.Ordinal) ||
        trade.TradeType.Contains("移管", StringComparison.Ordinal) ||
        trade.TradeType.Contains("分割", StringComparison.Ordinal) ||
        trade.TradeType.Contains("併合", StringComparison.Ordinal);

    private static bool IsSellTrade(BrokerTrade trade) =>
        trade.SignedQuantity < 0m ||
        trade.TradeType.Contains("売", StringComparison.Ordinal) ||
        trade.TradeType.Contains("Sell", StringComparison.OrdinalIgnoreCase);

    private static (IReadOnlyList<BrokerTrade> Trades, bool HasSyntheticInitialPosition) BuildDisplayTrades(
        IReadOnlyList<StockSnapshot> snapshots,
        IReadOnlyList<BrokerTrade> trades)
    {
        if (snapshots.Count == 0)
        {
            return (trades, false);
        }

        var result = trades.ToList();
        var tradeStockIds = trades.Select(x => x.StockId).ToHashSet();
        var hasSynthetic = false;
        foreach (var snapshot in snapshots)
        {
            var stockId = snapshot.Position.Stock.Id;
            if (stockId <= 0 || tradeStockIds.Contains(stockId))
            {
                continue;
            }

            var initialTrade = BuildInitialPositionTrade(snapshot);
            if (initialTrade.Quantity <= 0m)
            {
                continue;
            }

            result.Add(initialTrade);
            hasSynthetic = true;
        }

        return (result, hasSynthetic);
    }

    private static BrokerTrade BuildInitialPositionTrade(StockSnapshot snapshot)
    {
        var position = snapshot.Position;
        var quantity = position.IsMutualFund
            ? position.MutualFund.UnitsHeld
            : position.CurrentHolding.CurrentShares;
        var unitPrice = position.IsMutualFund
            ? position.MutualFund.AverageCostNav
            : DivideOrZero(snapshot.PurchaseTotal, quantity);
        var exchangeRate = position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? 1m
            : position.Purchase.ExchangeRate > 0m
                ? position.Purchase.ExchangeRate
                : position.CurrentHolding.CurrentExchangeRate > 0m
                    ? position.CurrentHolding.CurrentExchangeRate
                    : 1m;
        var eventDate = position.IsMutualFund && position.MutualFund.NavDate != DateTime.MinValue
            ? position.MutualFund.NavDate
            : position.Purchase.PurchaseDate == DateTime.MinValue
                ? DateTime.Today
                : position.Purchase.PurchaseDate;

        return new BrokerTrade
        {
            StockId = position.Stock.Id,
            TradeDate = eventDate,
            SettlementDate = eventDate,
            Broker = position.Stock.Broker,
            AccountType = position.Stock.AccountType,
            CustodyType = position.Stock.CustodyType,
            TradeType = "初期保有",
            Quantity = quantity,
            SignedQuantity = quantity,
            UnitPrice = unitPrice,
            Currency = position.Stock.Currency,
            ExchangeRate = exchangeRate,
            SettlementAmountJpy = snapshot.PurchaseTotalJpy,
            FeeJpy = 0m,
            TaxJpy = 0m,
            RealizedGainLoss = 0m,
            RealizedGainLossJpy = 0m,
            AfterTradeQuantity = quantity,
            AfterTradeAverageCost = unitPrice,
            Source = "保有残高CSV/手入力",
            SourceFile = string.Empty
        };
    }
}
