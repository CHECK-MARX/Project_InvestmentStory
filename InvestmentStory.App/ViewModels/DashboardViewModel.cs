using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly Action _recordSnapshot;
    private readonly PortfolioAnalyticsService _analyticsService = new();
    private DashboardSummary _summary = new();
    private CashAssetSummary _cashAssets = new();
    private IReadOnlyList<StockSnapshot> _snapshots = Array.Empty<StockSnapshot>();
    private IReadOnlyList<PortfolioSnapshot> _portfolioSnapshots = Array.Empty<PortfolioSnapshot>();
    private string _selectedCompositionMode = "分野別";
    private string _selectedHistoryPeriod = "1年";
    private DateTime? _lastCsvImportAtUtc;

    public DashboardViewModel(Action? recordSnapshot = null)
    {
        _recordSnapshot = recordSnapshot ?? (() => { });
        CompositionModes = new[] { "分野別", "国別", "通貨別", "証券会社別", "資産種別別", "口座区分別" };
        HistoryPeriods = new[] { "1か月", "3か月", "6か月", "1年", "3年", "全期間" };
        RecordSnapshotCommand = new RelayCommand(_recordSnapshot,
            () => _snapshots.All(x => x.HasKnownCurrentMarketValueJpy && x.HasKnownPurchaseCostJpy));
    }

    private bool HasIncompleteValuation => _summary.UnknownCurrentMarketValuePositionCount > 0;
    private bool HasIncompleteGain => HasIncompleteValuation || _summary.UnknownPurchaseCostPositionCount > 0;
    public string TotalCurrentMarketValue => HasIncompleteValuation ? "算出不可" : Formatters.Jpy(_summary.TotalCurrentMarketValueJpy);
    public string RegisteredCashTotal => _cashAssets.TotalRegisteredCashJpy is decimal amount
        ? Formatters.Jpy(amount) : "算出不可";
    public string SecuritiesAndRegisteredCashTotal => !HasIncompleteValuation && _cashAssets.TotalRegisteredCashJpy is decimal cash
        ? Formatters.Jpy(_summary.TotalCurrentMarketValueJpy + cash) : "算出不可";
    public string CashCoverageStatus => _cashAssets.Error.Length > 0 ? _cashAssets.Error
        : _cashAssets.Balances.Count == 0 ? "現金残高未登録。0円とは扱いません。"
        : $"基準日 {_cashAssets.AsOfDate:yyyy/MM/dd} / 円換算可能 {_cashAssets.IncludedCount} 件 / 集計できない口座・通貨 {_cashAssets.Balances.Count - _cashAssets.IncludedCount} 件";
    public string KnownCashSubtotal => _cashAssets.KnownSubtotalJpy is decimal amount
        ? $"確認できた現金の小計: {Formatters.Jpy(amount)}" : "確認できた現金の小計: 未確認";
    public string CashTotalScopeNote => "株・投信の評価額＋登録口座の照合済み現金。未登録口座の現金は含みません。外貨は参考為替で換算します。受取配当は現金残高に含まれるため、別途加算しません。";
    public ObservableCollection<CashAssetBalanceRowViewModel> CashBalances { get; } = new();

    public void UpdateCashAssets(CashAssetSummary cashAssets)
    {
        _cashAssets = cashAssets;
        CashBalances.Clear();
        foreach (var balance in cashAssets.Balances) CashBalances.Add(new(balance));
        OnPropertyChanged(nameof(RegisteredCashTotal));
        OnPropertyChanged(nameof(SecuritiesAndRegisteredCashTotal));
        OnPropertyChanged(nameof(CashCoverageStatus));
        OnPropertyChanged(nameof(KnownCashSubtotal));
    }
    public string UnrealizedGainLabel => _summary.ReferencePurchaseCostPositionCount > 0 ? "含み損益（概算）" : "含み損益";
    public string TotalUnrealizedGain => HasIncompleteGain
        ? "算出不可" : Formatters.SignedJpy(_summary.TotalUnrealizedGainJpy);
    public bool HasPositiveGain => !HasIncompleteGain && _summary.TotalUnrealizedGainJpy > 0m;
    public bool HasNegativeGain => !HasIncompleteGain && _summary.TotalUnrealizedGainJpy < 0m;
    public bool HasPositiveReturn => !HasIncompleteGain && _summary.TotalReturnJpy > 0m;
    public bool HasNegativeReturn => !HasIncompleteGain && _summary.TotalReturnJpy < 0m;
    public string TotalUnrealizedGainRate => HasIncompleteValuation
        ? $"評価額未取得 {_summary.UnknownCurrentMarketValuePositionCount}件"
        : _summary.UnknownPurchaseCostPositionCount > 0
        ? $"取得原価未確認 {_summary.UnknownPurchaseCostPositionCount}件"
        : _summary.ReferencePurchaseCostPositionCount > 0
            ? $"参考取得額 {_summary.ReferencePurchaseCostPositionCount}件 / {Formatters.SignedPercent(_summary.TotalUnrealizedGainRate)}"
            : Formatters.SignedPercent(_summary.TotalUnrealizedGainRate);
    public string UnrealizedGainRateExplanation => "現在保有の含み損益 ÷ 現在保有の取得額（期間利回りではありません）";
    public string ReferencePurchaseCostNote => _summary.ReferencePurchaseCostPositionCount == 0
        ? string.Empty
        : "一部の取得額は参考為替で換算した値です。証券会社の円建て決済額・税務上の取得価額ではありません。";
    public string ForeignAssetTotalUsd => Formatters.Money(_summary.ForeignAssetTotalUsd, "USD");
    public string ForeignAssetTotalJpy => _snapshots.Any(x =>
        x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase) && !x.HasKnownCurrentMarketValueJpy)
        ? "算出不可" : Formatters.Jpy(_summary.ForeignAssetTotalJpy);
    public string FxIncludedUnrealizedGain => HasIncompleteGain ? "算出不可"
        : (_summary.ReferencePurchaseCostPositionCount > 0 ? "概算 " : string.Empty) + Formatters.SignedJpy(_summary.FxIncludedUnrealizedGainJpy);
    public string CurrentUsdJpy => !PositionEvidenceService.HasUsableForeignExchangeRate(_summary.CurrentUsdJpyRate)
        ? "未確認" : $"{_summary.CurrentUsdJpyRate:N2} JPY/USD";
    public string ExchangeRateAcquiredAt => _summary.ExchangeRateAcquiredAt == DateTime.MinValue
        ? "-"
        : _summary.ExchangeRateAcquiredAt.ToString("yyyy/MM/dd HH:mm");
    public string ExchangeRateSource => string.IsNullOrWhiteSpace(_summary.ExchangeRateSource)
        ? "-"
        : $"{_summary.ExchangeRateSource} / {_summary.ExchangeRateInputType}";
    public string ThisYearPassiveIncome => Formatters.Jpy(_summary.ThisYearPassiveIncomeJpy);
    public string AnnualPassiveIncomeForecast => Formatters.Jpy(_summary.AnnualPassiveIncomeForecastJpy);
    public string MonthlyAveragePassiveIncomeForecast => Formatters.Jpy(_summary.MonthlyAveragePassiveIncomeForecastJpy);
    public string DailyPassiveIncomeForecast => Formatters.Jpy(_summary.AnnualPassiveIncomeForecastJpy / 365m);
    public string AnnualGoalAchievementRate => Formatters.Percent(_summary.AnnualGoalAchievementRate);
    public string AnnualGoalGap => Formatters.Jpy(_summary.AnnualGoalGapJpy);
    public string Monthly100kGap => Formatters.Jpy(Math.Max(0m, 100_000m - _summary.MonthlyAveragePassiveIncomeForecastJpy));
    public string TotalReturn => HasIncompleteGain
        ? "算出不可" : Formatters.SignedJpy(_summary.TotalReturnJpy);
    public string TotalReturnLabel => _summary.ReferencePurchaseCostPositionCount > 0
        ? "記録済み損益合計（概算）" : "記録済み損益合計（参考）";
    public string TotalReturnExplanation => "現在保有の含み損益＋記録済み売却損益＋確認済み受取配当。未記録の取引・配当は含みません。";
    public string PeriodReturnAvailability => "期間利回り: 算出不可（現金・評価履歴の期間照合は未対応）";
    public string CumulativeDividend => Formatters.Jpy(_summary.CumulativeDividendJpy);
    public string RealizedGainLoss => Formatters.SignedJpy(_summary.RealizedGainLossJpy);
    public string CapitalRecoveryAvailability => "元本回収率: 通算投下元本が未確認のため算出不可";
    public string Top5ConcentrationRate => HasIncompleteValuation ? "算出不可" : Formatters.Percent(_summary.Top5ConcentrationRate);
    public string TotalAssetDayChange => HasIncompleteValuation || _summary.TotalAssetDayChangeJpy is null ? "比較データなし" : Formatters.SignedJpy(_summary.TotalAssetDayChangeJpy.Value);
    public string TotalAssetMonthChange => HasIncompleteValuation || _summary.TotalAssetMonthChangeJpy is null ? "比較データなし" : Formatters.SignedJpy(_summary.TotalAssetMonthChangeJpy.Value);
    public double AnnualGoalProgressValue => (double)Math.Min(Math.Max(_summary.AnnualGoalAchievementRate, 0m), 100m);
    public string MarketDataStatus => $"USD/JPY {CurrentUsdJpy} / 為替更新 {ExchangeRateAcquiredAt}";
    public string DataUpdateStatus
    {
        get
        {
            var current = _snapshots.Where(x => x.Position.CurrentHolding.CurrentShares > 0m ||
                x.Position.MutualFund.UnitsHeld > 0m).ToList();
            var holdingDate = LatestDate(current.Select(x => x.Position.CurrentHolding.UpdatedAt));
            var priceDate = LatestDate(current.Select(x => x.Position.IsMutualFund
                ? x.Position.MutualFund.NavDate : x.Position.CurrentHolding.CurrentPriceAcquiredAt));
            var fxDate = LatestDate(current.Where(x => !x.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Position.CurrentHolding.ExchangeRateAcquiredAt));
            var importDate = _lastCsvImportAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "記録なし";
            return $"保有残高基準日 {holdingDate} / 株価・基準価額 {priceDate} / 為替 {fxDate} / CSV取込 {importDate}";
        }
    }

    public string DataCoverageStatus
    {
        get
        {
            var current = _snapshots.Where(x => x.Position.CurrentHolding.CurrentShares > 0m ||
                x.Position.MutualFund.UnitsHeld > 0m).ToList();
            var estimated = current.Where(x => PositionEvidenceService.CostQuality(x) == AcquisitionCostQuality.Estimated).ToList();
            var unknown = current.Where(x => PositionEvidenceService.CostQuality(x) == AcquisitionCostQuality.Unconfirmed).ToList();
            var unknownValuation = current.Count(x => !x.HasKnownCurrentMarketValueJpy);
            var knownPrices = current.Where(x => x.Position.IsMutualFund
                ? x.Position.MutualFund.CurrentNav > 0m : x.Position.CurrentHolding.CurrentPrice > 0m).ToList();
            var priceFreshness = knownPrices.Select(x => PositionEvidenceService.Freshness(
                x.Position.IsMutualFund ? x.Position.MutualFund.NavDate : x.Position.CurrentHolding.CurrentPriceAcquiredAt,
                DateTime.Today)).ToList();
            var foreign = current.Where(x => !x.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)).ToList();
            var fxFreshness = foreign.Where(x => PositionEvidenceService.HasUsableForeignExchangeRate(x.Position.CurrentHolding.CurrentExchangeRate))
                .Select(x => PositionEvidenceService.Freshness(x.Position.CurrentHolding.ExchangeRateAcquiredAt, DateTime.Today))
                .ToList();
            return $"取得額: 確定 {current.Count - estimated.Count - unknown.Count} 件 / " +
                   $"概算 {estimated.Count} 件（評価額 {CoverageAmount(estimated)}） / " +
                   $"未確認 {unknown.Count} 件（損益集計対象外の評価額 {CoverageAmount(unknown)}） / " +
                   $"評価額未取得 {unknownValuation} 件 / " +
                   $"株価・基準価額: 未取得 {current.Count - knownPrices.Count} 件 / 更新遅れ {priceFreshness.Count(x => x == DataFreshness.Delayed)} 件 / 時刻不明 {priceFreshness.Count(x => x == DataFreshness.TimeUnknown)} 件 / " +
                   $"為替: 未取得 {foreign.Count - fxFreshness.Count} 件 / 更新遅れ {fxFreshness.Count(x => x == DataFreshness.Delayed)} 件 / 時刻不明 {fxFreshness.Count(x => x == DataFreshness.TimeUnknown)} 件";
        }
    }

    private static string CoverageAmount(IReadOnlyList<StockSnapshot> group) =>
        group.Any(x => !x.HasKnownCurrentMarketValueJpy) ? "算出不可" : Formatters.Jpy(group.Sum(x => x.CurrentMarketValueJpy));

    private static string LatestDate(IEnumerable<DateTime> dates)
    {
        var latest = dates.Where(x => x != DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max();
        return latest == DateTime.MinValue ? "未取得" : latest.ToString("yyyy/MM/dd");
    }
    public IReadOnlyList<string> CompositionModes { get; }
    public IReadOnlyList<string> HistoryPeriods { get; }
    public ICommand RecordSnapshotCommand { get; }
    public string RecordSnapshotStatus => _snapshots.Any(x => !x.HasKnownCurrentMarketValueJpy || !x.HasKnownPurchaseCostJpy)
        ? "取得額・評価額・現在為替が未確認の銘柄があるため、今日の資産履歴は記録できません。" : string.Empty;
    public ObservableCollection<ChartBarRowViewModel> AssetBars { get; } = new();
    public ObservableCollection<PortfolioSnapshotRowViewModel> PortfolioHistoryRows { get; } = new();
    public ObservableCollection<CompositionCategoryViewModel> CompositionCategories { get; } = new();
    public string CompositionTotal => HasIncompleteValuation ? "算出不可" :
        Formatters.Jpy(_snapshots.Where(x => x.CurrentMarketValueJpy > 0m).Sum(x => x.CurrentMarketValueJpy));
    public string CompositionSummary => CompositionCategories.Count == 0
        ? HasIncompleteValuation
            ? $"評価額未取得が{_summary.UnknownCurrentMarketValuePositionCount}件あるため、資産構成を算出できません。"
            : "評価額のある保有銘柄がありません。"
        : $"{CompositionCategories.Count}分野・区分 / {CompositionCategories.SelectMany(x => x.Holdings.Select(h => h.Label)).Distinct().Count()}銘柄";
    public ObservableCollection<FxSensitivityRowViewModel> FxSensitivityRows { get; } = new();
    public string CompositionBasisNote => SelectedCompositionMode == "国別"
        ? "国別は銘柄の登録国による分類です。日本で保有する海外株式型投信も、登録国に従って表示します。"
        : "保有株・投信の評価額に対する割合です。現金・MRFは含みません。";
    public ObservableCollection<NisaDashboardSummaryViewModel> NisaSummaries { get; } = new();

    public string SelectedCompositionMode
    {
        get => _selectedCompositionMode;
        set
        {
            if (SetProperty(ref _selectedCompositionMode, value))
            {
                RebuildCompositionBars();
                OnPropertyChanged(nameof(CompositionBasisNote));
            }
        }
    }

    public string SelectedHistoryPeriod
    {
        get => _selectedHistoryPeriod;
        set
        {
            if (SetProperty(ref _selectedHistoryPeriod, value))
            {
                RebuildPortfolioHistoryRows(_portfolioSnapshots);
            }
        }
    }

    public string PortfolioHistoryMessage
    {
        get
        {
            if (PortfolioHistoryRows.Count == 0)
            {
                return "履歴データがまだありません。取得額と評価額が揃った日の起動時またはAPI/CSV更新時に記録します。";
            }

            return PortfolioHistoryRows.Count == 1
                ? "履歴が1日分だけあります。比較グラフは今後の更新で増えていきます。"
                : string.Empty;
        }
    }

    public void Update(
        DashboardSummary summary,
        IReadOnlyList<StockSnapshot> snapshots,
        IReadOnlyList<PortfolioSnapshot> portfolioSnapshots,
        DateTime? lastCsvImportAtUtc = null,
        CashAssetSummary? cashAssets = null)
    {
        _summary = summary;
        UpdateCashAssets(cashAssets ?? new CashAssetSummary());
        _snapshots = snapshots;
        _portfolioSnapshots = portfolioSnapshots;
        _lastCsvImportAtUtc = lastCsvImportAtUtc;
        ((RelayCommand)RecordSnapshotCommand).RaiseCanExecuteChanged();
        RebuildAssetBars();
        RebuildPortfolioHistoryRows(portfolioSnapshots);
        RebuildCompositionBars();
        RebuildFxSensitivityRows();
        RebuildNisaSummaries();
        RefreshAllProperties();
    }

    private void RebuildAssetBars()
    {
        AssetBars.Clear();
        if (HasIncompleteValuation)
        {
            return;
        }
        var values = new[]
        {
            Math.Abs(_summary.TotalCurrentMarketValueJpy),
            Math.Abs(_summary.TotalPurchaseAmountJpy),
            Math.Abs(_summary.TotalUnrealizedGainJpy)
        };
        var max = values.Max();
        if (max <= 0m)
        {
            AssetBars.Add(new ChartBarRowViewModel("株・投信の評価額", 0m, 1m));
            AssetBars.Add(new ChartBarRowViewModel("投資元本", 0m, 1m));
            AssetBars.Add(new ChartBarRowViewModel("含み損益", 0m, 1m, signed: true));
            return;
        }

        AssetBars.Add(new ChartBarRowViewModel("株・投信の評価額", _summary.TotalCurrentMarketValueJpy, max));
        AssetBars.Add(new ChartBarRowViewModel("投資元本", _summary.TotalPurchaseAmountJpy, max));
        AssetBars.Add(new ChartBarRowViewModel("含み損益", _summary.TotalUnrealizedGainJpy, max, signed: true));
    }

    private void RebuildPortfolioHistoryRows(IReadOnlyList<PortfolioSnapshot> portfolioSnapshots)
    {
        PortfolioHistoryRows.Clear();
        var fromDate = ResolveHistoryStartDate(DateTime.Today);
        var values = portfolioSnapshots
            .Where(x => fromDate is null || x.SnapshotDate.Date >= fromDate.Value)
            .GroupBy(x => x.SnapshotDate.Date)
            .Select(group => group.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).First())
            .OrderBy(x => x.SnapshotDate)
            .ToList();
        foreach (var value in values)
        {
            PortfolioHistoryRows.Add(new PortfolioSnapshotRowViewModel(value));
        }

        OnPropertyChanged(nameof(PortfolioHistoryMessage));
    }

    private void RebuildCompositionBars()
    {
        CompositionCategories.Clear();
        if (HasIncompleteValuation)
        {
            OnPropertyChanged(nameof(CompositionTotal));
            OnPropertyChanged(nameof(CompositionSummary));
            return;
        }
        var valuedSnapshots = _snapshots.Where(x => x.CurrentMarketValueJpy > 0m).ToList();
        var total = valuedSnapshots.Sum(x => x.CurrentMarketValueJpy);
        var groups = valuedSnapshots.GroupBy(ResolveCompositionLabel)
            .Select(group => new
            {
                Label = group.Key,
                Amount = group.Sum(x => x.CurrentMarketValueJpy),
                Holdings = group.GroupBy(x => SecuritySymbolNormalizer.NormalizeTicker(x.Position.Stock.Ticker))
                    .Select(holding => new CompositionHoldingViewModel(holding.Key,
                        holding.First().Position.Stock.Name, holding.Sum(x => x.CurrentMarketValueJpy)))
                    .OrderByDescending(x => x.Amount)
                    .ToList()
            })
            .OrderByDescending(x => x.Amount)
            .ToList();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            CompositionCategories.Add(new CompositionCategoryViewModel(
                group.Label, group.Amount, total, group.Holdings, index));
        }
        OnPropertyChanged(nameof(CompositionTotal));
        OnPropertyChanged(nameof(CompositionSummary));
    }

    private string ResolveCompositionLabel(StockSnapshot snapshot)
    {
        var stock = snapshot.Position.Stock;
        return SelectedCompositionMode switch
        {
            "分野別" or "セクター別" => snapshot.Position.IsMutualFund
                ? "投資信託" : StockSectorClassifier.Classify(stock).Sector,
            "通貨別" => NonEmpty(stock.Currency),
            "証券会社別" => NonEmpty(stock.Broker),
            "資産種別別" => snapshot.Position.IsMutualFund ? "投資信託" : "株式",
            "口座区分別" => NonEmpty(ResolveAccountType(snapshot)),
            _ => NonEmpty(stock.Country)
        };
    }

    private static string NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "未設定" : value.Trim();

    private void RebuildFxSensitivityRows()
    {
        FxSensitivityRows.Clear();
        var rows = _analyticsService.BuildFxSensitivity(_snapshots, _summary.CurrentUsdJpyRate);
        var maxChange = rows.Count == 0 ? 0m : rows.Max(x => Math.Abs(x.ChangeFromCurrentJpy));
        foreach (var row in rows)
        {
            FxSensitivityRows.Add(new FxSensitivityRowViewModel(row, maxChange));
        }
    }

    private void RebuildNisaSummaries()
    {
        NisaSummaries.Clear();
        var order = new[] { AccountTypes.NisaGrowth, AccountTypes.NisaAccumulation, AccountTypes.NisaLegacy };
        var groups = _snapshots
            .Where(x => x.Position.IsMutualFund ? x.Position.MutualFund.UnitsHeld > 0m : x.Position.CurrentHolding.CurrentShares > 0m)
            .GroupBy(ResolveAccountType)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var accountType in order)
        {
            if (!groups.TryGetValue(accountType, out var rows) || rows.Count == 0)
            {
                continue;
            }

            NisaSummaries.Add(new NisaDashboardSummaryViewModel(accountType, rows));
        }
    }

    private static string ResolveAccountType(StockSnapshot snapshot)
    {
        var stock = snapshot.Position.Stock;
        return snapshot.Position.IsMutualFund
            ? AccountTypeNormalizer.NormalizeForMutualFund(stock.AccountType, stock.CustodyType)
            : AccountTypeNormalizer.Normalize(stock.AccountType);
    }

    private DateTime? ResolveHistoryStartDate(DateTime today) =>
        SelectedHistoryPeriod switch
        {
            "1か月" => today.AddMonths(-1),
            "3か月" => today.AddMonths(-3),
            "6か月" => today.AddMonths(-6),
            "1年" => today.AddYears(-1),
            "3年" => today.AddYears(-3),
            _ => null
        };
}
