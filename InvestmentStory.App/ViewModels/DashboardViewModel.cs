using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly Action _recordSnapshot;
    private DashboardSummary _summary = new();
    private IReadOnlyList<StockSnapshot> _snapshots = Array.Empty<StockSnapshot>();
    private string _selectedCompositionMode = "国別";

    public DashboardViewModel(Action? recordSnapshot = null)
    {
        _recordSnapshot = recordSnapshot ?? (() => { });
        CompositionModes = new[] { "国別", "通貨別", "証券会社別" };
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
    public ICommand RecordSnapshotCommand { get; }
    public ObservableCollection<ChartBarRowViewModel> AssetBars { get; } = new();
    public ObservableCollection<PortfolioSnapshotRowViewModel> PortfolioHistoryRows { get; } = new();
    public ObservableCollection<ChartBarRowViewModel> CompositionBars { get; } = new();

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

    public void Update(
        DashboardSummary summary,
        IReadOnlyList<StockSnapshot> snapshots,
        IReadOnlyList<PortfolioSnapshot> portfolioSnapshots)
    {
        _summary = summary;
        _snapshots = snapshots;
        RebuildAssetBars();
        RebuildPortfolioHistoryRows(portfolioSnapshots);
        RebuildCompositionBars();
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
        var values = portfolioSnapshots
            .OrderByDescending(x => x.SnapshotDate)
            .Take(20)
            .OrderBy(x => x.SnapshotDate)
            .ToList();
        var max = values.Count == 0 ? 0m : values.Max(x => x.TotalMarketValueJpy);
        foreach (var value in values)
        {
            PortfolioHistoryRows.Add(new PortfolioSnapshotRowViewModel(value, max));
        }
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

        return valuedSnapshots
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Position.Stock.Country) ? "未設定" : x.Position.Stock.Country)
            .Select(x => (x.Key, x.Sum(y => y.CurrentMarketValueJpy)));
    }
}
