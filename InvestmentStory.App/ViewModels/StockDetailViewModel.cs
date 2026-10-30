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
    private readonly Action<CorporateAction>? _saveCorporateAction;
    private readonly Func<PositionChangeAudit, string, bool>? _revertPositionChange;
    private readonly Func<int, DateTime, CorporateActionBalance>? _loadHistoricalBalance;
    private readonly Action<MarketAnomalyCandidate, string, string>? _reviewMarketAnomaly;
    private readonly Func<int, IReadOnlyList<MarketAnomalyDecision>>? _loadMarketAnomalyDecisions;
    private MarketAnomalyRowViewModel? _selectedMarketAnomaly;
    private string _marketAnomalyMessage = string.Empty;
    private string _corporateActionMessage = string.Empty;
    private CorporateActionRowViewModel? _selectedCorporateAction;
    private PositionChangeAuditRowViewModel? _selectedManualChange;
    private string _manualChangeMessage = string.Empty;
    private string _historicalBalanceStatus = string.Empty;

    public StockDetailViewModel(Action<CorporateAction>? saveCorporateAction = null,
        Func<PositionChangeAudit, string, bool>? revertPositionChange = null,
        Func<int, DateTime, CorporateActionBalance>? loadHistoricalBalance = null,
        Action<MarketAnomalyCandidate, string, string>? reviewMarketAnomaly = null,
        Func<int, IReadOnlyList<MarketAnomalyDecision>>? loadMarketAnomalyDecisions = null)
    {
        _saveCorporateAction = saveCorporateAction;
        _revertPositionChange = revertPositionChange;
        _loadHistoricalBalance = loadHistoricalBalance;
        _reviewMarketAnomaly = reviewMarketAnomaly;
        _loadMarketAnomalyDecisions = loadMarketAnomalyDecisions;
        RefreshPriceChartCommand = new RelayCommand(() => PriceChartRefreshRequested?.Invoke(this, EventArgs.Empty));
        SaveCorporateActionCommand = new RelayCommand(SaveCorporateAction);
        LoadCorporateActionCorrectionCommand = new RelayCommand(LoadCorporateActionCorrection);
        RevertManualChangeCommand = new RelayCommand(RevertManualChange);
        RefreshHistoricalBalanceCommand = new RelayCommand(RefreshHistoricalBalance);
        ConfirmMarketAnomalyCommand = new RelayCommand(() => ReviewMarketAnomaly("Confirmed"));
        AcceptMarketAnomalyCommand = new RelayCommand(() => ReviewMarketAnomaly("Dismissed"));
    }

    public event EventHandler? PriceChartRefreshRequested;

    public ObservableCollection<MarketAnomalyRowViewModel> MarketAnomalyRows { get; } = new();
    public ObservableCollection<string> MarketAnomalyDecisionRows { get; } = new();
    public MarketAnomalyRowViewModel? SelectedMarketAnomaly
    {
        get => _selectedMarketAnomaly;
        set
        {
            if (!SetProperty(ref _selectedMarketAnomaly, value)) return;
            MarketAnomalyDecisionRows.Clear();
            if (value is null || _loadMarketAnomalyDecisions is null) return;
            foreach (var decision in _loadMarketAnomalyDecisions(value.Candidate.Id))
                MarketAnomalyDecisionRows.Add($"{decision.RecordedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm} / " +
                    $"{(decision.Decision == "Confirmed" ? "異常として保留" : decision.Decision == "Dismissed" ? "確認済み・候補を除外" : "再確認待ち")} / {decision.Reason}");
        }
    }
    public string MarketAnomalyReason { get; set; } = string.Empty;
    public string MarketAnomalyMessage
    {
        get => _marketAnomalyMessage;
        private set => SetProperty(ref _marketAnomalyMessage, value);
    }
    public ICommand ConfirmMarketAnomalyCommand { get; }
    public ICommand AcceptMarketAnomalyCommand { get; }

    private void ReviewMarketAnomaly(string decision)
    {
        if (SelectedMarketAnomaly is null || _reviewMarketAnomaly is null)
        {
            MarketAnomalyMessage = "確認する候補を選択してください。";
            return;
        }
        if (string.IsNullOrWhiteSpace(MarketAnomalyReason))
        {
            MarketAnomalyMessage = "判断理由を入力してください。";
            return;
        }
        if (decision == "Dismissed" && SelectedMarketAnomaly.Category == "通貨不一致")
        {
            MarketAnomalyMessage = "通貨が異なる価格は採用できません。ティッカー・取得元または登録通貨を修正し、再取得してください。";
            return;
        }
        try
        {
            var category = SelectedMarketAnomaly.Category;
            _reviewMarketAnomaly(SelectedMarketAnomaly.Candidate, decision, MarketAnomalyReason);
            MarketAnomalyReason = string.Empty;
            OnPropertyChanged(nameof(MarketAnomalyReason));
            MarketAnomalyMessage = decision == "Dismissed"
                ? category == "株価急変"
                    ? "確認済みです。同じ価格を再取得すると採用できます。"
                    : "確認済みとして候補を除外しました。取引データは変更されていません。"
                : "異常として保留しました。元データは変更されていません。";
        }
        catch (Exception ex) { MarketAnomalyMessage = $"判断を保存できませんでした: {ex.Message}"; }
    }

    public bool HasStock => _snapshot is not null;
    public ICommand SaveCorporateActionCommand { get; }
    public ICommand RevertManualChangeCommand { get; }
    public ICommand RefreshHistoricalBalanceCommand { get; }
    public DateTime HistoricalBalanceDate { get; set; } = DateTime.Today;
    public ObservableCollection<AccountBalanceRowViewModel> HistoricalBalanceRows { get; } = new();
    public string HistoricalBalanceStatus
    {
        get => _historicalBalanceStatus;
        private set => SetProperty(ref _historicalBalanceStatus, value);
    }
    public PositionChangeAuditRowViewModel? SelectedManualChange
    {
        get => _selectedManualChange;
        set => SetProperty(ref _selectedManualChange, value);
    }
    public string RevertReason { get; set; } = string.Empty;
    public string ManualChangeMessage
    {
        get => _manualChangeMessage;
        private set => SetProperty(ref _manualChangeMessage, value);
    }
    public ICommand LoadCorporateActionCorrectionCommand { get; }
    public CorporateActionRowViewModel? SelectedCorporateAction
    {
        get => _selectedCorporateAction;
        set => SetProperty(ref _selectedCorporateAction, value);
    }
    public long? ActionSupersedesId { get; private set; }
    public string ActionCorrectionLabel => ActionSupersedesId.HasValue
        ? $"ID {ActionSupersedesId} を訂正します。元記録は履歴に残ります。" : "新規イベントを記録します。";
    public DateTime ActionDate { get; set; } = DateTime.Today;
    public string ActionKind { get; set; } = "Transfer";
    public string ActionStatus { get; set; } = "Pending";
    public string ActionSourceBroker { get; set; } = string.Empty;
    public string ActionSourceAccount { get; set; } = AccountTypes.Unknown;
    public string ActionDestinationBroker { get; set; } = string.Empty;
    public string ActionDestinationAccount { get; set; } = AccountTypes.Unknown;
    public decimal ActionQuantity { get; set; }
    public decimal ActionSplitRatio { get; set; } = 1m;
    public decimal? ActionCarriedCostJpy { get; set; }
    public string ActionOldTicker { get; set; } = string.Empty;
    public string ActionNewTicker { get; set; } = string.Empty;
    public string ActionEvidence { get; set; } = string.Empty;
    public string ActionReason { get; set; } = string.Empty;
    public string CorporateActionMessage
    {
        get => _corporateActionMessage;
        private set => SetProperty(ref _corporateActionMessage, value);
    }
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
        : IsMutualFund ? FundCurrentNav : _snapshot.Position.CurrentHolding.CurrentPrice <= 0m
            ? "未取得" : Formatters.Money(_snapshot.Position.CurrentHolding.CurrentPrice, _snapshot.Position.Stock.Currency);
    public string HeaderIncomeLabel => IsMutualFund ? "分配金受取" : "年間配当";
    public string HeaderIncomeValue => IsMutualFund ? FundDistributionMethod : AnnualDividendJpy;
    public string HeaderIncomeSubValue => IsMutualFund
        ? (FundDistributionMethod.Contains("再投資", StringComparison.Ordinal) ? "現金配当対象外" : "分配金設定")
        : YieldOnCost;

    public string PurchaseExchangeRate => _snapshot is null ? "-" :
        _snapshot.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? "対象外" :
        PositionEvidenceService.HasUsableForeignExchangeRate(_snapshot.Position.Purchase.ExchangeRate)
            ? $"{(_snapshot.HasKnownPurchaseCostJpy ? "" : "未採用 ")}{_snapshot.Position.Purchase.ExchangeRate:N2} JPY/{_snapshot.Position.Stock.Currency}" :
        _snapshot.IsReferencePurchaseCostJpy && _snapshot.Position.Purchase.ReferenceAcquisitionForeignAmount > 0m
            ? $"概算 {_snapshot.Position.Purchase.ReferenceAcquisitionAmountJpy / _snapshot.Position.Purchase.ReferenceAcquisitionForeignAmount:N2} JPY/{_snapshot.Position.Stock.Currency}"
            : "未確認";
    public string CurrentExchangeRate => _snapshot is null ? "-" :
        _snapshot.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? "対象外" :
        PositionEvidenceService.HasUsableForeignExchangeRate(_snapshot.Position.CurrentHolding.CurrentExchangeRate)
            ? $"{_snapshot.Position.CurrentHolding.CurrentExchangeRate:N2} JPY/{_snapshot.Position.Stock.Currency}" : "未確認";
    public string PurchaseExchangeInfo => _snapshot is null
        ? "-"
        : _snapshot.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? "円建て換算不要"
        : _snapshot.IsReferencePurchaseCostJpy
            ? $"{_snapshot.Position.Purchase.ReferenceAcquisitionSource} / 参考換算"
        : !_snapshot.HasKnownPurchaseCostJpy && PositionEvidenceService.HasUsableForeignExchangeRate(_snapshot.Position.Purchase.ExchangeRate)
            ? $"未採用の為替候補 / {_snapshot.Position.Purchase.ExchangeRateSource} / 記録日 {_snapshot.Position.Purchase.ExchangeRateAcquiredAt:yyyy/MM/dd}。円建て取得額の根拠には使いません。"
        : _snapshot.Position.Purchase.ExchangeRate <= 0m || _snapshot.Position.Purchase.ExchangeRate == 1m
            ? "取得時為替未確認"
        : $"{_snapshot.Position.Purchase.ExchangeRateAcquiredAt:yyyy/MM/dd HH:mm} / {_snapshot.Position.Purchase.ExchangeRateSource} / {_snapshot.Position.Purchase.ExchangeRateInputType}";
    public string CurrentExchangeInfo => _snapshot is null
        ? "-"
        : _snapshot.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? "円建て換算不要"
        : !PositionEvidenceService.HasUsableForeignExchangeRate(_snapshot.Position.CurrentHolding.CurrentExchangeRate)
            ? "現在為替未確認"
        : $"{_snapshot.Position.CurrentHolding.ExchangeRateAcquiredAt:yyyy/MM/dd HH:mm} / {_snapshot.Position.CurrentHolding.ExchangeRateSource} / {_snapshot.Position.CurrentHolding.ExchangeRateInputType}";

    public string PurchaseTotal => PurchaseTotalUsd;
    public string PurchaseTotalUsd => Money(x => x.PurchaseTotalUsd);
    public string PurchaseTotalJpy => _snapshot is null ? "-" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.Jpy(Sum(x => x.PurchaseTotalJpy));
    public string EffectiveAcquisitionPrice => _snapshot is null
        ? "-"
        : Formatters.Money(DivideOrZero(Sum(x => x.PurchaseTotal), Sum(x => x.Position.CurrentHolding.CurrentShares)), _snapshot.Position.Stock.Currency);
    public string CurrentMarketValue => CurrentMarketValueUsd;
    public string CurrentMarketValueUsd => _snapshot is null ? "-" :
        _snapshots.Any(x => !x.Position.IsMutualFund && x.Position.CurrentHolding.CurrentShares > 0m && x.Position.CurrentHolding.CurrentPrice <= 0m)
            ? "未取得" : Money(x => x.CurrentMarketValueUsd);
    public string CurrentMarketValueJpy => _snapshot is null ? "-" : HasUnknownMarketValue ? "算出不可" : Formatters.Jpy(Sum(x => x.CurrentMarketValueJpy));
    public string UnrealizedGain => UnrealizedGainUsd;
    public string UnrealizedGainUsd => _snapshot is null ? "-" :
        _snapshots.Any(x => x.PurchaseTotal <= 0m) ? "未確認" :
        _snapshots.Any(x => !x.Position.IsMutualFund && x.Position.CurrentHolding.CurrentShares > 0m && x.Position.CurrentHolding.CurrentPrice <= 0m)
            ? "算出不可" : Formatters.SignedMoney(Sum(x => x.UnrealizedGainUsd), _snapshot.Position.Stock.Currency);
    public string UnrealizedGainRate => _snapshot is null ? "-" :
        _snapshots.Any(x => x.PurchaseTotal <= 0m) ? "未確認" :
        HasUnknownMarketValue ? "算出不可" : Formatters.SignedPercent(CalculateRate(Sum(x => x.CurrentMarketValue) - Sum(x => x.PurchaseTotal), Sum(x => x.PurchaseTotal)));
    public string UnrealizedGainRateJpy => _snapshot is null ? "-" : HasUnknownMarketValue ? "算出不可" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.SignedPercent(CalculateRate(Sum(x => x.UnrealizedGainJpy), Sum(x => x.PurchaseTotalJpy)));
    public string UnrealizedGainJpy => _snapshot is null ? "-" : HasUnknownMarketValue ? "算出不可" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.SignedJpy(Sum(x => x.UnrealizedGainJpy));
    public string CurrencyImpactJpy => _snapshot is null ? "-" : HasUnknownMarketValue ? "算出不可" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.SignedJpy(Sum(x => x.CurrencyImpactJpy));
    public string Multiple => _snapshot is null ? "-" : $"{DivideOrZero(Sum(x => x.CurrentMarketValue), Sum(x => x.PurchaseTotal)):N2}倍";
    public string AnnualDividend => Money(x => x.AnnualDividendForecast);
    public string AnnualDividendJpy => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.AnnualDividendForecastJpy));
    public string MonthlyDividend => Money(x => x.MonthlyPassiveIncomeForecast);
    public string MonthlyDividendJpy => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.MonthlyPassiveIncomeForecastJpy));
    public string CurrentDividendYield => _snapshot is null ? "-" : HasUnknownMarketValue ? "算出不可" : Formatters.Percent(CalculateRate(Sum(x => x.AnnualDividendForecastJpy), Sum(x => x.CurrentMarketValueJpy)));
    public string YieldOnCost => _snapshot is null ? "-" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.Percent(CalculateRate(Sum(x => x.AnnualDividendForecastJpy), Sum(x => x.PurchaseTotalJpy)));
    public string ShareChangeRatio => _snapshot is null ? "-" : $"{_snapshot.ShareChangeRatio:N2}倍";

    public string CurrentPriceSource => _snapshot is null || string.IsNullOrWhiteSpace(_snapshot.Position.CurrentHolding.CurrentPriceSource)
        ? "未取得"
        : _snapshot.Position.CurrentHolding.CurrentPriceSource;
    public string CurrentPriceAcquiredAt => _snapshot is null || _snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt == DateTime.MinValue
        ? _snapshot?.Position.CurrentHolding.CurrentPrice > 0m ? "時刻不明" : "未取得"
        : _snapshot.Position.CurrentHolding.CurrentPriceAcquiredAt.ToString("yyyy/MM/dd HH:mm");
    public string DividendInfoSource => _snapshot is null || string.IsNullOrWhiteSpace(_snapshot.Position.CurrentHolding.DividendInfoSource)
        ? "未取得"
        : _snapshot.Position.CurrentHolding.DividendInfoSource;
    public string DividendInfoAcquiredAt => _snapshot is null || _snapshot.Position.CurrentHolding.DividendInfoAcquiredAt == DateTime.MinValue
        ? "未取得"
        : _snapshot.Position.CurrentHolding.DividendInfoAcquiredAt.ToString("yyyy/MM/dd HH:mm");

    public string Story => _story;
    public bool HasPositiveGain => _snapshot is not null && !HasUnknownPurchaseCost && !HasUnknownMarketValue && Sum(x => x.UnrealizedGainJpy) > 0m;
    public bool HasNegativeGain => _snapshot is not null && !HasUnknownPurchaseCost && !HasUnknownMarketValue && Sum(x => x.UnrealizedGainJpy) < 0m;
    private bool HasUnknownPurchaseCost => _snapshots.Any(x => !x.HasKnownPurchaseCostJpy);
    private bool HasUnknownMarketValue => _snapshots.Any(x => !x.HasKnownCurrentMarketValueJpy);
    private string ReferencePrefix => _snapshots.Any(x => x.IsReferencePurchaseCostJpy) ? "概算 " : string.Empty;
    public string ReferenceCostNote => _snapshots.Any(x => x.IsReferencePurchaseCostJpy)
        ? "取得額は参考為替による換算値です。実際の円決済額・税務上の取得価額とは異なる場合があります。"
        : string.Empty;
    public string ReferenceCostSource => string.Join(" / ", _snapshots
        .Where(x => x.IsReferencePurchaseCostJpy)
        .Select(x => x.Position.Purchase.ReferenceAcquisitionSource)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct());

    public string FundName => _snapshot is null
        ? "-"
        : string.IsNullOrWhiteSpace(_snapshot.Position.MutualFund.FundName)
            ? _snapshot.Position.Stock.Name
            : _snapshot.Position.MutualFund.FundName;
    public string FundAccountType => EmptyToDash(_snapshot?.Position.MutualFund.AccountType);
    public string FundUnitsHeld => _snapshot is null ? "-" : $"{Formatters.Number(Sum(x => x.Position.MutualFund.UnitsHeld))}口";
    public string FundAverageCostNav => _snapshot is null ? "-" : HasUnknownPurchaseCost ? "未確認" :
        _snapshots.Any(x => x.Position.MutualFund.AverageCostNav <= 0m) ? "未確認" :
        FormatFundNav(WeightedFundNav(x => x.PurchaseTotalJpy), FundUnitBase);
    public string FundCurrentNav => _snapshot is null || Sum(x => x.CurrentMarketValueJpy) <= 0m
        ? "基準価額未更新"
        : FormatFundNav(WeightedFundNav(x => x.CurrentMarketValueJpy), FundUnitBase);
    public string FundAcquisitionAmount => _snapshot is null ? "-" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.Jpy(Sum(x => x.PurchaseTotalJpy));
    public string FundMarketValue => CurrentMarketValueJpy;
    public string FundUnrealizedGainLoss => _snapshot is null ? "-" : HasUnknownMarketValue ? "算出不可" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.SignedJpy(Sum(x => x.UnrealizedGainJpy));
    public string FundUnrealizedGainLossRate => _snapshot is null ? "-" : HasUnknownMarketValue ? "算出不可" : HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.SignedPercent(CalculateRate(Sum(x => x.UnrealizedGainJpy), Sum(x => x.PurchaseTotalJpy)));
    public string FundNavDate => _snapshot is null
        ? "-"
        : _snapshots
            .Select(x => x.Position.MutualFund.NavDate)
            .Where(x => x != DateTime.MinValue)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max() == DateTime.MinValue
                ? _snapshots.Any(x => x.Position.MutualFund.CurrentNav > 0m) ? "時刻不明" : "基準価額未更新"
                : _snapshots
                    .Select(x => x.Position.MutualFund.NavDate)
                    .Where(x => x != DateTime.MinValue)
                    .Max()
                    .ToString("yyyy/MM/dd");
    public string FundNavSource => DistinctJoined(_snapshots.Select(x => x.Position.MutualFund.NavSource),
        _snapshots.Any(x => x.Position.MutualFund.CurrentNav > 0m) ? "取得元不明" : "基準価額未更新");
    public string FundDistributionMethod => DistinctJoined(_snapshots.Select(x => x.Position.MutualFund.DistributionMethod), "-");
    public string FundTotalPurchaseAmount => _snapshot is null
        ? "-"
        : _snapshots.Any(x => x.Position.MutualFund.TotalPurchaseAmount <= 0m && !x.HasKnownPurchaseCostJpy)
            ? "未確認"
        : Formatters.Jpy(Sum(x => x.Position.MutualFund.TotalPurchaseAmount > 0m
            ? x.Position.MutualFund.TotalPurchaseAmount
            : x.PurchaseTotalJpy));
    public string FundTotalSaleAmount => _snapshot is null ? "-" : Formatters.Jpy(Sum(x => x.Position.MutualFund.TotalSaleAmount));
    public string FundTotalReturn => _snapshot is null
        ? "-"
        : HasUnknownMarketValue ? "算出不可"
        : _snapshots.Any(x => x.Position.MutualFund.TotalPurchaseAmount <= 0m && !x.HasKnownPurchaseCostJpy)
            ? "未確認"
        : Formatters.SignedJpy(Sum(x => x.CurrentMarketValueJpy + x.Position.MutualFund.TotalSaleAmount -
            (x.Position.MutualFund.TotalPurchaseAmount > 0m ? x.Position.MutualFund.TotalPurchaseAmount : x.PurchaseTotalJpy)));

    public ObservableCollection<BrokerTradeRowViewModel> TradeRows { get; } = new();
    public ObservableCollection<PositionChangeAuditRowViewModel> ManualChangeRows { get; } = new();
    public ObservableCollection<CorporateActionRowViewModel> CorporateActionRows { get; } = new();
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
    public string RealizedGainLossJpy => HasUnverifiedInbound
        ? "算出不可（入庫要確認）"
        : Formatters.SignedJpy(_trades.Sum(x => x.RealizedGainLossJpy));
    public string CurrentTradeQuantity => CanUseReconstructedTradeSummary
        ? Formatters.Number(ReconstructedTradeQuantity)
        : SnapshotQuantity > 0m
            ? Formatters.Number(SnapshotQuantity)
            : _trades.Count == 0
            ? "-"
            : Formatters.Number(LatestTrade.AfterTradeQuantity);
    public string AverageTradeCost => HasUnverifiedInbound
        ? "算出不可（入庫要確認）"
        : CanUseReconstructedTradeSummary && ReconstructedAverageTradeCost > 0m
        ? Formatters.Money(ReconstructedAverageTradeCost, _snapshot?.Position.Stock.Currency ?? "JPY")
        : SnapshotQuantity > 0m
            ? Formatters.Money(SnapshotAverageCost, _snapshot?.Position.Stock.Currency ?? "JPY")
            : _trades.Count == 0
            ? "-"
            : Formatters.Money(LatestTrade.AfterTradeAverageCost, _snapshot?.Position.Stock.Currency ?? "JPY");
    public string TradeDataWarning => HasUnverifiedInbound
        ? "無償入庫の移管元・引継原価または分割根拠が未確認です。取引履歴からの平均取得単価と実現損益は確定できません。"
        : HasTradeQuantityMismatch
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

    private bool HasUnverifiedInbound => _trades.Any(x => x.TradeType is "TransferIn" or "StockSplitCandidate");

    private bool CanUseReconstructedTradeSummary => _trades.Count > 0
        && ReconstructedTradeQuantity > 0m
        && !HasUnverifiedInbound
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
        IReadOnlyList<DataQualityInfo>? dataQualityInfos = null,
        IReadOnlyList<PositionChangeAudit>? manualChanges = null,
        IReadOnlyList<CorporateAction>? corporateActions = null,
        IReadOnlyList<MarketAnomalyCandidate>? marketAnomalies = null)
    {
        Update(snapshot is null ? Array.Empty<StockSnapshot>() : new[] { snapshot }, story,
            trades, dataQualityInfos, manualChanges, corporateActions, marketAnomalies);
    }

    public void Update(
        IReadOnlyList<StockSnapshot> snapshots,
        string? story,
        IReadOnlyList<BrokerTrade>? trades = null,
        IReadOnlyList<DataQualityInfo>? dataQualityInfos = null,
        IReadOnlyList<PositionChangeAudit>? manualChanges = null,
        IReadOnlyList<CorporateAction>? corporateActions = null,
        IReadOnlyList<MarketAnomalyCandidate>? marketAnomalies = null)
    {
        var previousStockId = _snapshot?.Position.Stock.Id;
        _snapshots = snapshots;
        _snapshot = snapshots.FirstOrDefault();
        if (previousStockId != _snapshot?.Position.Stock.Id)
            ResetCorporateActionForm();
        (_trades, _hasSyntheticInitialPositionTrades) = BuildDisplayTrades(snapshots, trades ?? Array.Empty<BrokerTrade>());
        _story = (_snapshots.Any(x => x.IsReferencePurchaseCostJpy)
            ? "以下の円建て取得額・損益は公表為替に基づく概算で、実際の円決済額や税務上の取得価額ではありません。 "
            : string.Empty) + (story ?? "銘柄一覧から銘柄を選択してください。");
        UpdateChartReferenceValues();
        SetPriceChartMessage(_snapshot is null
            ? "銘柄を選択するとチャートを取得します。"
            : "チャートを取得中です。");

        TradeRows.Clear();
        var earliestUnverified = _trades
            .Where(x => x.TradeType is "TransferIn" or "StockSplitCandidate")
            .Select(x => (DateTime?)x.TradeDate.Date).Min();
        foreach (var trade in _trades.OrderByDescending(x => x.TradeDate).ThenByDescending(x => x.Id))
        {
            TradeRows.Add(new BrokerTradeRowViewModel(trade,
                earliestUnverified.HasValue && trade.TradeDate.Date >= earliestUnverified.Value));
        }

        DataQualityRows.Clear();
        foreach (var item in dataQualityInfos ?? Array.Empty<DataQualityInfo>())
        {
            DataQualityRows.Add(new DataQualityRowViewModel(item));
        }
        MarketAnomalyRows.Clear();
        foreach (var candidate in marketAnomalies ?? Array.Empty<MarketAnomalyCandidate>())
            MarketAnomalyRows.Add(new MarketAnomalyRowViewModel(candidate));
        SelectedMarketAnomaly = null;

        ManualChangeRows.Clear();
        foreach (var item in manualChanges ?? Array.Empty<PositionChangeAudit>())
            ManualChangeRows.Add(new PositionChangeAuditRowViewModel(item));
        SelectedManualChange = null;
        CorporateActionRows.Clear();
        var allActions = corporateActions ?? Array.Empty<CorporateAction>();
        var supersededIds = allActions.Where(x => x.SupersedesId.HasValue)
            .Select(x => x.SupersedesId!.Value).ToHashSet();
        foreach (var item in allActions)
            CorporateActionRows.Add(new CorporateActionRowViewModel(item, supersededIds.Contains(item.Id)));
        SelectedCorporateAction = null;
        RefreshHistoricalBalance();

        RefreshAllProperties();
    }

    private void RefreshHistoricalBalance()
    {
        HistoricalBalanceRows.Clear();
        if (_snapshot is null || _loadHistoricalBalance is null)
        {
            HistoricalBalanceStatus = "銘柄を選択してください。";
            return;
        }
        try
        {
            var balance = _loadHistoricalBalance(_snapshot.Position.Stock.Id, HistoricalBalanceDate.Date);
            if (balance.IsComplete)
                foreach (var row in balance.Accounts.OrderBy(x => x.Broker).ThenBy(x => x.Account))
                    HistoricalBalanceRows.Add(new AccountBalanceRowViewModel(row));
            HistoricalBalanceStatus = balance.IsComplete
                ? $"{HistoricalBalanceDate:yyyy/MM/dd}（受渡日基準） / 数量 {balance.TotalQuantity:N4} / " +
                  (balance.TotalCostJpy.HasValue ? $"引継原価 {balance.TotalCostJpy:N0}円" : "引継原価は不明")
                : $"{HistoricalBalanceDate:yyyy/MM/dd}の口座別残高は算出不可: {balance.Reason}";
        }
        catch (Exception ex)
        {
            HistoricalBalanceStatus = $"残高を再現できませんでした: {ex.Message}";
        }
    }

    private void RevertManualChange()
    {
        if (SelectedManualChange is null || _revertPositionChange is null)
        {
            ManualChangeMessage = "再調整する履歴行を選択してください。";
            return;
        }
        if (string.IsNullOrWhiteSpace(RevertReason))
        {
            ManualChangeMessage = "再調整の理由を入力してください。";
            return;
        }
        try
        {
            if (!_revertPositionChange(SelectedManualChange.Entry, RevertReason.Trim()))
                return;
            RevertReason = string.Empty;
            OnPropertyChanged(nameof(RevertReason));
            ManualChangeMessage = "履歴を残して再調整しました。";
        }
        catch (Exception ex)
        {
            ManualChangeMessage = $"再調整できませんでした: {ex.Message}";
        }
    }

    private void SaveCorporateAction()
    {
        if (_snapshot is null || _snapshots.Count != 1 || _saveCorporateAction is null)
        {
            CorporateActionMessage = "口座別の銘柄詳細を選択してください。";
            return;
        }

        try
        {
            var action = new CorporateAction
            {
                StockId = _snapshot.Position.Stock.Id,
                EventDate = ActionDate.Date, Kind = ActionKind, Status = ActionStatus,
                SourceBroker = ActionSourceBroker.Trim(), SourceAccount = ActionSourceAccount,
                DestinationBroker = ActionDestinationBroker.Trim(),
                DestinationAccount = ActionDestinationAccount,
                Quantity = ActionQuantity, SplitRatio = ActionSplitRatio,
                CarriedCostJpy = ActionCarriedCostJpy,
                OldTicker = ActionOldTicker.Trim(), NewTicker = ActionNewTicker.Trim(),
                Evidence = ActionEvidence.Trim(), Reason = ActionReason.Trim(),
                SupersedesId = ActionSupersedesId
            };
            CorporateActionLedgerService.Validate(action);
            _saveCorporateAction(action);
            ResetCorporateActionForm();
            CorporateActionMessage = "企業行動を履歴付きで記録しました。";
        }
        catch (Exception ex)
        {
            CorporateActionMessage = $"記録できませんでした: {ex.Message}";
        }
    }

    private void LoadCorporateActionCorrection()
    {
        if (SelectedCorporateAction is null)
        {
            CorporateActionMessage = "訂正する台帳行を選択してください。";
            return;
        }
        if (SelectedCorporateAction.Status == "訂正済み")
        {
            CorporateActionMessage = "訂正済みの行です。最新の行を選択してください。";
            return;
        }
        var item = SelectedCorporateAction.Action;
        ActionSupersedesId = item.Id;
        ActionDate = item.EventDate;
        ActionKind = item.Kind;
        ActionStatus = item.Status;
        ActionSourceBroker = item.SourceBroker;
        ActionSourceAccount = item.SourceAccount;
        ActionDestinationBroker = item.DestinationBroker;
        ActionDestinationAccount = item.DestinationAccount;
        ActionQuantity = item.Quantity;
        ActionSplitRatio = item.SplitRatio;
        ActionCarriedCostJpy = item.CarriedCostJpy;
        ActionOldTicker = item.OldTicker;
        ActionNewTicker = item.NewTicker;
        ActionEvidence = item.Evidence;
        ActionReason = string.Empty;
        CorporateActionMessage = "訂正内容と理由を入力し、台帳へ記録してください。";
        RaiseCorporateActionFormProperties();
    }

    private void ResetCorporateActionForm()
    {
        ActionDate = DateTime.Today;
        ActionKind = "Transfer";
        ActionStatus = "Pending";
        ActionSourceBroker = _snapshot?.Position.Stock.Broker ?? string.Empty;
        ActionSourceAccount = _snapshot?.Position.Stock.AccountType ?? AccountTypes.Unknown;
        ActionDestinationBroker = _snapshot?.Position.Stock.Broker ?? string.Empty;
        ActionDestinationAccount = _snapshot?.Position.Stock.AccountType ?? AccountTypes.Unknown;
        ActionQuantity = 0m;
        ActionSplitRatio = 1m;
        ActionCarriedCostJpy = null;
        ActionOldTicker = _snapshot?.Position.Stock.Ticker ?? string.Empty;
        ActionNewTicker = string.Empty;
        ActionEvidence = string.Empty;
        ActionReason = string.Empty;
        ActionSupersedesId = null;
        CorporateActionMessage = string.Empty;
        RaiseCorporateActionFormProperties();
    }

    private void RaiseCorporateActionFormProperties()
    {
        OnPropertyChanged(nameof(ActionCorrectionLabel));
        OnPropertyChanged(nameof(ActionDate));
        OnPropertyChanged(nameof(ActionKind));
        OnPropertyChanged(nameof(ActionStatus));
        OnPropertyChanged(nameof(ActionSourceBroker));
        OnPropertyChanged(nameof(ActionSourceAccount));
        OnPropertyChanged(nameof(ActionDestinationBroker));
        OnPropertyChanged(nameof(ActionDestinationAccount));
        OnPropertyChanged(nameof(ActionQuantity));
        OnPropertyChanged(nameof(ActionSplitRatio));
        OnPropertyChanged(nameof(ActionCarriedCostJpy));
        OnPropertyChanged(nameof(ActionOldTicker));
        OnPropertyChanged(nameof(ActionNewTicker));
        OnPropertyChanged(nameof(ActionEvidence));
        OnPropertyChanged(nameof(ActionReason));
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
        trade.TradeType.Equals("StockSplitCandidate", StringComparison.OrdinalIgnoreCase) ||
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
