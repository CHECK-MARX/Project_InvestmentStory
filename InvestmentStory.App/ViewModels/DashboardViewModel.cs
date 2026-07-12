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
    private IReadOnlyList<StockSnapshot> _snapshots = Array.Empty<StockSnapshot>();
    private IReadOnlyList<PortfolioSnapshot> _portfolioSnapshots = Array.Empty<PortfolioSnapshot>();
    private string _selectedCompositionMode = "国別";
    private string _selectedHistoryPeriod = "1年";

    public DashboardViewModel(Action? recordSnapshot = null)
    {
        _recordSnapshot = recordSnapshot ?? (() => { });
        CompositionModes = new[] { "国別", "通貨別", "証券会社別", "資産種別別", "口座区分別", "セクター別" };
        HistoryPeriods = new[] { "1か月", "3か月", "6か月", "1年", "3年", "全期間" };
        RecordSnapshotCommand = new RelayCommand(_recordSnapshot);
    }

    public string TotalCurrentMarketValue => Formatters.Jpy(_summary.TotalCurrentMarketValueJpy);
    public string TotalUnrealizedGain => Formatters.SignedJpy(_summary.TotalUnrealizedGainJpy);
    public string TotalUnrealizedGainRate => Formatters.SignedPercent(_summary.TotalUnrealizedGainRate);
    public string ForeignAssetTotalUsd => Formatters.Money(_summary.ForeignAssetTotalUsd, "USD");
    public string ForeignAssetTotalJpy => Formatters.Jpy(_summary.ForeignAssetTotalJpy);
    public string FxIncludedUnrealizedGain => Formatters.SignedJpy(_summary.FxIncludedUnrealizedGainJpy);
    public string CurrentUsdJpy => _summary.CurrentUsdJpyRate == 0m ? "-" : $"{_summary.CurrentUsdJpyRate:N2} JPY/USD";
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
    public string TotalReturn => Formatters.SignedJpy(_summary.TotalReturnJpy);
    public string TotalReturnRate => Formatters.SignedPercent(_summary.TotalReturnRate);
    public string CumulativeDividend => Formatters.Jpy(_summary.CumulativeDividendJpy);
    public string RealizedGainLoss => Formatters.SignedJpy(_summary.RealizedGainLossJpy);
    public string CapitalRecoveryRate => Formatters.Percent(_summary.CapitalRecoveryRate);
    public string Top5ConcentrationRate => Formatters.Percent(_summary.Top5ConcentrationRate);
    public string TotalAssetDayChange => _summary.TotalAssetDayChangeJpy is null ? "比較データなし" : Formatters.SignedJpy(_summary.TotalAssetDayChangeJpy.Value);
    public string TotalAssetMonthChange => _summary.TotalAssetMonthChangeJpy is null ? "比較データなし" : Formatters.SignedJpy(_summary.TotalAssetMonthChangeJpy.Value);
    public double AnnualGoalProgressValue => (double)Math.Min(Math.Max(_summary.AnnualGoalAchievementRate, 0m), 100m);
    public string MarketDataStatus => $"USD/JPY {CurrentUsdJpy} / 為替更新 {ExchangeRateAcquiredAt}";
    public string DataUpdateStatus => string.IsNullOrWhiteSpace(_summary.ExchangeRateSource)
        ? "データ取得元: 未取得"
        : $"データ取得元: {_summary.ExchangeRateSource}";
    public IReadOnlyList<string> CompositionModes { get; }
    public IReadOnlyList<string> HistoryPeriods { get; }
    public ICommand RecordSnapshotCommand { get; }
    public ObservableCollection<ChartBarRowViewModel> AssetBars { get; } = new();
    public ObservableCollection<PortfolioSnapshotRowViewModel> PortfolioHistoryRows { get; } = new();
    public ObservableCollection<ChartBarRowViewModel> CompositionBars { get; } = new();
    public ObservableCollection<FxSensitivityRowViewModel> FxSensitivityRows { get; } = new();
    public ObservableCollection<NisaDashboardSummaryViewModel> NisaSummaries { get; } = new();

    public string SelectedCompositionMode
    {
        get => _selectedCompositionMode;
        set
        {
            if (SetProperty(ref _selectedCompositionMode, value))
            {
                RebuildCompositionBars();
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
                return "履歴データがまだありません。今後の起動時またはAPI/CSV更新時に日次スナップショットを記録します。";
            }

            return PortfolioHistoryRows.Count == 1
                ? "履歴が1日分だけあります。比較グラフは今後の更新で増えていきます。"
                : string.Empty;
        }
    }

    public void Update(
        DashboardSummary summary,
        IReadOnlyList<StockSnapshot> snapshots,
        IReadOnlyList<PortfolioSnapshot> portfolioSnapshots)
    {
        _summary = summary;
        _snapshots = snapshots;
        _portfolioSnapshots = portfolioSnapshots;
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
        var values = new[]
        {
            Math.Abs(_summary.TotalCurrentMarketValueJpy),
            Math.Abs(_summary.TotalPurchaseAmountJpy),
            Math.Abs(_summary.TotalUnrealizedGainJpy)
        };
        var max = values.Max();
        if (max <= 0m)
        {
            AssetBars.Add(new ChartBarRowViewModel("総資産", 0m, 1m));
            AssetBars.Add(new ChartBarRowViewModel("投資元本", 0m, 1m));
            AssetBars.Add(new ChartBarRowViewModel("含み損益", 0m, 1m, signed: true));
            return;
        }

        AssetBars.Add(new ChartBarRowViewModel("総資産", _summary.TotalCurrentMarketValueJpy, max));
        AssetBars.Add(new ChartBarRowViewModel("投資元本", _summary.TotalPurchaseAmountJpy, max));
        AssetBars.Add(new ChartBarRowViewModel("含み損益", _summary.TotalUnrealizedGainJpy, max, signed: true));
    }

    private void RebuildPortfolioHistoryRows(IReadOnlyList<PortfolioSnapshot> portfolioSnapshots)
    {
        PortfolioHistoryRows.Clear();
        var fromDate = ResolveHistoryStartDate(DateTime.Today);
        var values = portfolioSnapshots
            .Where(x => fromDate is null || x.SnapshotDate.Date >= fromDate.Value)
            .OrderByDescending(x => x.SnapshotDate)
            .Take(20)
            .OrderBy(x => x.SnapshotDate)
            .ToList();
        var max = values.Count == 0 ? 0m : values.Max(x => x.TotalMarketValueJpy);
        foreach (var value in values)
        {
            PortfolioHistoryRows.Add(new PortfolioSnapshotRowViewModel(value, max));
        }

        OnPropertyChanged(nameof(PortfolioHistoryMessage));
    }

    private void RebuildCompositionBars()
    {
        CompositionBars.Clear();
        var groups = BuildCompositionGroups().ToList();
        if (groups.Count == 0)
        {
            CompositionBars.Add(new ChartBarRowViewModel("データなし", 0m, 1m));
            return;
        }

        var top = groups
            .OrderByDescending(x => x.Amount)
            .Take(5)
            .ToList();
        var other = groups.Sum(x => x.Amount) - top.Sum(x => x.Amount);
        if (other > 0m)
        {
            top.Add(("その他", other));
        }

        var max = top.Max(x => x.Amount);
        foreach (var item in top)
        {
            CompositionBars.Add(new ChartBarRowViewModel(item.Label, item.Amount, max));
        }
    }

    private IEnumerable<(string Label, decimal Amount)> BuildCompositionGroups()
    {
        var valuedSnapshots = _snapshots
            .Where(x => x.CurrentMarketValueJpy > 0m)
            .ToList();

        if (SelectedCompositionMode == "通貨別")
        {
            return valuedSnapshots
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Position.Stock.Currency) ? "未設定" : x.Position.Stock.Currency)
                .Select(x => (x.Key, x.Sum(y => y.CurrentMarketValueJpy)));
        }

        if (SelectedCompositionMode == "証券会社別")
        {
            return valuedSnapshots
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Position.Stock.Broker) ? "未設定" : x.Position.Stock.Broker)
                .Select(x => (x.Key, x.Sum(y => y.CurrentMarketValueJpy)));
        }

        if (SelectedCompositionMode == "資産種別別")
        {
            return valuedSnapshots
                .GroupBy(x => x.Position.IsMutualFund ? "投資信託" : "株式")
                .Select(x => (x.Key, x.Sum(y => y.CurrentMarketValueJpy)));
        }

        if (SelectedCompositionMode == "口座区分別")
        {
            return valuedSnapshots
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Position.Stock.AccountType) ? "未設定" : x.Position.Stock.AccountType)
                .Select(x => (x.Key, x.Sum(y => y.CurrentMarketValueJpy)));
        }

        if (SelectedCompositionMode == "セクター別")
        {
            return valuedSnapshots
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Position.Stock.Sector) ? "未設定" : x.Position.Stock.Sector)
                .Select(x => (x.Key, x.Sum(y => y.CurrentMarketValueJpy)));
        }

        return valuedSnapshots
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Position.Stock.Country) ? "未設定" : x.Position.Stock.Country)
            .Select(x => (x.Key, x.Sum(y => y.CurrentMarketValueJpy)));
    }

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
            .Where(x => x.CurrentMarketValueJpy > 0m || x.PurchaseTotalJpy > 0m)
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
