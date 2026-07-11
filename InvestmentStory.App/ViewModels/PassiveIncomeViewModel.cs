using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class PassiveIncomeViewModel : ObservableObject
{
    private readonly Action<IncomeGoal> _saveGoal;
    private DashboardSummary _summary = new();
    private string _goalMessage = string.Empty;
    private string _selectedRankingMode = "今年実績";
    private IReadOnlyDictionary<string, IReadOnlyList<DividendRankingItem>> _rankingsByMode =
        new Dictionary<string, IReadOnlyList<DividendRankingItem>>();

    public PassiveIncomeViewModel(Action<IncomeGoal> saveGoal)
    {
        _saveGoal = saveGoal;
        SaveGoalCommand = new RelayCommand(SaveGoal);
    }

    public ObservableCollection<DividendAggregateRowViewModel> Ranking { get; } = new();
    public ObservableCollection<MonthlyDividendBreakdownRowViewModel> MonthlyBreakdownRows { get; } = new();
    public ObservableCollection<DividendRankingItemViewModel> DividendRankingRows { get; } = new();
    public ICommand SaveGoalCommand { get; }

    public string ThisMonthPassiveIncome => Formatters.Jpy(_summary.ThisMonthPassiveIncomeJpy);
    public string ThisYearPassiveIncome => Formatters.Jpy(_summary.ThisYearPassiveIncomeJpy);
    public string ThisMonthPlannedIncome => Formatters.Jpy(_summary.ThisMonthPlannedIncomeJpy);
    public string ThisYearPlannedIncome => Formatters.Jpy(_summary.ThisYearPlannedIncomeJpy);
    public string ThisYearForecastIncludingPlanned => Formatters.Jpy(_summary.ThisYearForecastIncludingPlannedJpy);
    public string AnnualPassiveIncomeForecast => Formatters.Jpy(_summary.AnnualPassiveIncomeForecastJpy);
    public string AnnualNetDividendForecast => Formatters.Jpy(_summary.AnnualNetDividendForecastJpy);
    public string MonthlyAveragePassiveIncome => Formatters.Jpy(_summary.MonthlyAveragePassiveIncomeForecastJpy);
    public string DailyPassiveIncome => Formatters.Jpy(_summary.AnnualPassiveIncomeForecastJpy / 365m);
    public string ForeignTaxActual => Formatters.Jpy(_summary.ForeignTaxActualJpy);
    public string DomesticTaxActual => Formatters.Jpy(_summary.DomesticTaxActualJpy);
    public string TotalTaxActual => Formatters.Jpy(_summary.TotalTaxActualJpy);
    public string NisaDividendActual => Formatters.Jpy(_summary.NisaDividendActualJpy);
    public string TaxableDividendActual => Formatters.Jpy(_summary.TaxableDividendActualJpy);
    public string DomesticStockDividendActual => Formatters.Jpy(_summary.DomesticStockDividendActualJpy);
    public string ForeignStockDividendActual => Formatters.Jpy(_summary.ForeignStockDividendActualJpy);
    public string AnnualGoalAchievementRate => Formatters.Percent(_summary.AnnualGoalAchievementRate);
    public string MonthlyGoalAchievementRate => Formatters.Percent(_summary.MonthlyGoalAchievementRate);
    public string AnnualGoalGap => Formatters.Jpy(_summary.AnnualGoalGapJpy);
    public string GapToMonthly100k => Formatters.Jpy(Math.Max(100_000m - _summary.MonthlyAveragePassiveIncomeForecastJpy, 0m));

    public ObservableCollection<ChartBarRowViewModel> MonthlyBars { get; } = new();
    public ObservableCollection<ChartBarRowViewModel> YearlyBars { get; } = new();
    public string RankingTitle => $"{DateTime.Today.Year}年 {SelectedRankingMode} ランキング";
    public IReadOnlyList<string> RankingModes { get; } =
        new[]
        {
            "今年実績",
            "今年着地見込み",
            "現在保有ベース年間配当",
            "累計受取配当",
            "税引後年間見込み",
            "取得額ベース利回り",
            "配当成長率"
        };

    public string SelectedRankingMode
    {
        get => _selectedRankingMode;
        set
        {
            if (!RankingModes.Contains(value))
            {
                value = "今年実績";
            }

            if (SetProperty(ref _selectedRankingMode, value))
            {
                RebuildDividendRankingRows();
                OnPropertyChanged(nameof(RankingTitle));
            }
        }
    }

    public int TargetYear { get; set; } = DateTime.Today.Year;
    public decimal AnnualPassiveIncomeGoal { get; set; }
    public decimal MonthlyPassiveIncomeGoal { get; set; }
    public decimal TotalAssetGoal { get; set; }

    public string GoalMessage
    {
        get => _goalMessage;
        private set => SetProperty(ref _goalMessage, value);
    }

    public void Update(
        DashboardSummary summary,
        IncomeGoal? goal,
        IReadOnlyList<DividendAggregate> monthly,
        IReadOnlyList<DividendAggregate> yearly,
        IReadOnlyList<DividendAggregate> byStock,
        IReadOnlyList<MonthlyDividendBreakdown>? monthlyBreakdown = null,
        IReadOnlyDictionary<string, IReadOnlyList<DividendRankingItem>>? dividendRankings = null)
    {
        _summary = summary;
        TargetYear = goal?.TargetYear ?? DateTime.Today.Year;
        AnnualPassiveIncomeGoal = goal?.AnnualPassiveIncomeGoal ?? 0m;
        MonthlyPassiveIncomeGoal = goal?.MonthlyPassiveIncomeGoal ?? 0m;
        TotalAssetGoal = goal?.TotalAssetGoal ?? 0m;

        MonthlyBars.Clear();
        var monthlyMax = monthly.Count == 0 ? 0m : monthly.Max(x => x.AmountJpy);
        foreach (var item in monthly)
        {
            MonthlyBars.Add(new ChartBarRowViewModel(item, monthlyMax));
        }

        MonthlyBreakdownRows.Clear();
        var breakdownItems = monthlyBreakdown ?? Array.Empty<MonthlyDividendBreakdown>();
        var breakdownMax = breakdownItems.Count == 0
            ? 0m
            : breakdownItems.Max(x => Math.Max(Math.Max(x.ActualJpy, x.PlannedJpy), Math.Max(x.PreviousYearActualJpy, x.MonthlyGoalJpy)));
        foreach (var item in breakdownItems)
        {
            MonthlyBreakdownRows.Add(new MonthlyDividendBreakdownRowViewModel(item, breakdownMax));
        }

        var yearlyItems = yearly.Count == 0
            ? new[] { new DividendAggregate { Label = DateTime.Today.Year.ToString(), AmountJpy = 0m } }
            : yearly;

        YearlyBars.Clear();
        var yearlyMax = yearlyItems.Max(x => x.AmountJpy);
        foreach (var item in yearlyItems)
        {
            YearlyBars.Add(new ChartBarRowViewModel(item, yearlyMax));
        }

        Ranking.Clear();
        foreach (var item in byStock)
        {
            Ranking.Add(new DividendAggregateRowViewModel(item));
        }

        _rankingsByMode = dividendRankings ?? new Dictionary<string, IReadOnlyList<DividendRankingItem>>();
        RebuildDividendRankingRows();

        RefreshAllProperties();
    }

    private void RebuildDividendRankingRows()
    {
        DividendRankingRows.Clear();
        if (!_rankingsByMode.TryGetValue(SelectedRankingMode, out var items))
        {
            items = Array.Empty<DividendRankingItem>();
        }

        foreach (var item in items)
        {
            DividendRankingRows.Add(new DividendRankingItemViewModel(item));
        }
    }

    private void SaveGoal()
    {
        if (TargetYear < 2000 || AnnualPassiveIncomeGoal < 0m || MonthlyPassiveIncomeGoal < 0m || TotalAssetGoal < 0m)
        {
            GoalMessage = "目標値を確認してください。";
            return;
        }

        try
        {
            _saveGoal(new IncomeGoal
            {
                TargetYear = TargetYear,
                AnnualPassiveIncomeGoal = AnnualPassiveIncomeGoal,
                MonthlyPassiveIncomeGoal = MonthlyPassiveIncomeGoal,
                TotalAssetGoal = TotalAssetGoal
            });
            GoalMessage = "目標を保存しました。";
        }
        catch (Exception ex)
        {
            GoalMessage = $"保存に失敗しました: {ex.Message}";
        }
    }
}
