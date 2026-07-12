using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class SimulationViewModel : ObservableObject
{
    private readonly MutualFundAssetSimulationService _simulationService;
    private readonly Func<AppSettings> _loadSettings;
    private readonly Action<AppSettings> _saveSettings;
    private IReadOnlyList<StockPosition> _positions = Array.Empty<StockPosition>();
    private SimulationScopeOptionViewModel? _selectedScope;
    private decimal _monthlyContributionJpy = 100_000m;
    private decimal _expectedAnnualReturnRate = 5m;
    private decimal _targetAmountJpy = 20_000_000m;
    private int _projectionYears = 20;
    private int _targetYears = 20;
    private string _currentMarketValue = "0円";
    private string _currentCost = "0円";
    private string _unrealizedGain = "0円";
    private string _unrealizedGainRate = "0.00%";
    private string _actualAnnualizedReturn = "算出不可";
    private string _fundCount = "0件";
    private string _finalAssetAmount = "0円";
    private string _targetAchievementMonth = "未達";
    private string _remainingPeriod = "未達";
    private string _requiredMonthlyContribution = "0円";
    private string _stopContributionFinalAsset = "0円";
    private string _chartStatus = "投資信託の現在保有データを読み込むと、資産形成シミュレーションを表示します。";
    private string _statusMessage = "税金、信託報酬、為替は考慮していません。将来想定年利はユーザー入力値で、外部APIから断定しません。";
    private bool _isRebuildingScopes;

    public SimulationViewModel(
        MutualFundAssetSimulationService simulationService,
        Func<AppSettings> loadSettings,
        Action<AppSettings> saveSettings)
    {
        _simulationService = simulationService;
        _loadSettings = loadSettings;
        _saveSettings = saveSettings;
        var settings = _loadSettings();
        _monthlyContributionJpy = settings.MutualFundSimulationMonthlyContributionJpy;
        _expectedAnnualReturnRate = settings.MutualFundSimulationExpectedAnnualReturnRate;
        _targetAmountJpy = settings.MutualFundSimulationTargetAmountJpy;
        _projectionYears = settings.MutualFundSimulationProjectionYears;
        _targetYears = settings.MutualFundSimulationTargetYears;
        RunCommand = new RelayCommand(RunSimulation);
        RunPassiveIncomeCommand = RunCommand;
        RunTsumitateNisaCommand = RunCommand;
    }

    public ObservableCollection<SimulationScopeOptionViewModel> ScopeOptions { get; } = new();
    public ObservableCollection<MutualFundSimulationProjectionRowViewModel> ProjectionRows { get; } = new();
    public ObservableCollection<MutualFundContributionComparisonRowViewModel> ContributionComparisons { get; } = new();
    public ObservableCollection<PriceChartPointViewModel> TrendPoints { get; } = new();
    public ICommand RunCommand { get; }
    public ICommand RunPassiveIncomeCommand { get; }
    public ICommand RunTsumitateNisaCommand { get; }

    public SimulationScopeOptionViewModel? SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (!SetProperty(ref _selectedScope, value) || _isRebuildingScopes)
            {
                return;
            }

            RunSimulation();
        }
    }

    public decimal MonthlyContributionJpy
    {
        get => _monthlyContributionJpy;
        set => SetProperty(ref _monthlyContributionJpy, value);
    }

    public decimal ExpectedAnnualReturnRate
    {
        get => _expectedAnnualReturnRate;
        set => SetProperty(ref _expectedAnnualReturnRate, value);
    }

    public decimal TargetAmountJpy
    {
        get => _targetAmountJpy;
        set => SetProperty(ref _targetAmountJpy, value);
    }

    public int ProjectionYears
    {
        get => _projectionYears;
        set => SetProperty(ref _projectionYears, value);
    }

    public int TargetYears
    {
        get => _targetYears;
        set => SetProperty(ref _targetYears, value);
    }

    public string CurrentMarketValue
    {
        get => _currentMarketValue;
        private set => SetProperty(ref _currentMarketValue, value);
    }

    public string CurrentCost
    {
        get => _currentCost;
        private set => SetProperty(ref _currentCost, value);
    }

    public string UnrealizedGain
    {
        get => _unrealizedGain;
        private set => SetProperty(ref _unrealizedGain, value);
    }

    public string UnrealizedGainRate
    {
        get => _unrealizedGainRate;
        private set => SetProperty(ref _unrealizedGainRate, value);
    }

    public string ActualAnnualizedReturn
    {
        get => _actualAnnualizedReturn;
        private set => SetProperty(ref _actualAnnualizedReturn, value);
    }

    public string FundCount
    {
        get => _fundCount;
        private set => SetProperty(ref _fundCount, value);
    }

    public string FinalAssetAmount
    {
        get => _finalAssetAmount;
        private set => SetProperty(ref _finalAssetAmount, value);
    }

    public string TargetAchievementMonth
    {
        get => _targetAchievementMonth;
        private set => SetProperty(ref _targetAchievementMonth, value);
    }

    public string RemainingPeriod
    {
        get => _remainingPeriod;
        private set => SetProperty(ref _remainingPeriod, value);
    }

    public string RequiredMonthlyContribution
    {
        get => _requiredMonthlyContribution;
        private set => SetProperty(ref _requiredMonthlyContribution, value);
    }

    public string StopContributionFinalAsset
    {
        get => _stopContributionFinalAsset;
        private set => SetProperty(ref _stopContributionFinalAsset, value);
    }

    public string ChartStatus
    {
        get => _chartStatus;
        private set => SetProperty(ref _chartStatus, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // 既存MainViewModelから呼ばれる互換用。FIRE配当シミュレーションは今回は実装しない。
    public void UpdateCurrentAnnualIncome(decimal annualIncome)
    {
    }

    public void UpdateNisaPortfolio(IReadOnlyList<StockSnapshot> snapshots)
    {
        UpdateMutualFundPortfolio(snapshots.Select(x => x.Position).ToList());
    }

    public void UpdateMutualFundPortfolio(IReadOnlyList<StockPosition> positions)
    {
        _positions = positions;
        RebuildScopeOptions();
        RunSimulation(saveSettings: false);
    }

    private void RunSimulation()
    {
        RunSimulation(saveSettings: true);
    }

    private void RunSimulation(bool saveSettings)
    {
        if (MonthlyContributionJpy < 0m || TargetAmountJpy < 0m || ProjectionYears <= 0 || TargetYears <= 0)
        {
            StatusMessage = "毎月積立額、目標金額は0以上、試算年数と目標達成年数は1年以上で入力してください。";
            return;
        }

        var result = _simulationService.Simulate(
            _positions,
            SelectedScope?.Key ?? MutualFundSimulationScopeKeys.AllAccounts,
            new MutualFundSimulationInput
            {
                MonthlyContributionJpy = MonthlyContributionJpy,
                ExpectedAnnualReturnRate = ExpectedAnnualReturnRate,
                TargetAmountJpy = TargetAmountJpy,
                ProjectionYears = ProjectionYears,
                TargetYears = TargetYears,
                StartYear = DateTime.Today.Year,
                StartMonth = DateTime.Today.Month
            });

        ApplyResult(result);
        if (saveSettings)
        {
            SaveSimulationSettings();
        }
    }

    private void RebuildScopeOptions()
    {
        var currentKey = SelectedScope?.Key;
        var savedKey = _loadSettings().MutualFundSimulationScopeKey;
        var preferredKey = string.IsNullOrWhiteSpace(currentKey) ? savedKey : currentKey;
        var options = _simulationService.CreateScopeOptions(_positions);

        _isRebuildingScopes = true;
        ScopeOptions.Clear();
        foreach (var option in options)
        {
            ScopeOptions.Add(new SimulationScopeOptionViewModel(option));
        }

        SelectedScope = ScopeOptions.FirstOrDefault(x => string.Equals(x.Key, preferredKey, StringComparison.OrdinalIgnoreCase))
            ?? ScopeOptions.FirstOrDefault(x => string.Equals(x.Key, MutualFundSimulationScopeKeys.AllAccounts, StringComparison.OrdinalIgnoreCase))
            ?? ScopeOptions.FirstOrDefault();
        _isRebuildingScopes = false;
    }

    private void ApplyResult(MutualFundAssetSimulationResult result)
    {
        var summary = result.Summary;
        CurrentMarketValue = Formatters.Jpy(summary.CurrentMarketValueJpy);
        CurrentCost = Formatters.Jpy(summary.CurrentCostJpy);
        UnrealizedGain = Formatters.SignedJpy(summary.UnrealizedGainJpy);
        UnrealizedGainRate = Formatters.SignedPercent(summary.UnrealizedGainRate);
        ActualAnnualizedReturn = summary.ActualAnnualizedReturnRate is { } annualizedReturn
            ? Formatters.SignedPercent(annualizedReturn)
            : "算出不可";
        FundCount = $"{summary.FundCount:N0}件";

        var finalProjection = result.Projections.LastOrDefault();
        FinalAssetAmount = finalProjection is null ? "0円" : Formatters.Jpy(finalProjection.MarketValueJpy);
        StopContributionFinalAsset = finalProjection is null ? "0円" : Formatters.Jpy(finalProjection.NoContributionMarketValueJpy);
        RequiredMonthlyContribution = Formatters.Jpy(result.RequiredMonthlyContributionJpy);
        TargetAchievementMonth = result.TargetAchievementMonth is null
            ? "指定期間内では未達"
            : result.TargetAchievementMonth.Value.ToString("yyyy/MM");
        RemainingPeriod = FormatRemainingPeriod(result.MonthsToTarget);
        ChartStatus = result.Projections.Count == 0
            ? "現在保有中の投資信託がありません。CSV取込または登録後に表示されます。"
            : $"{result.Projections[0].YearMonth:yyyy/MM} - {result.Projections[^1].YearMonth:yyyy/MM} / 月次複利・月末積立";
        StatusMessage = summary.ActualAnnualizedReturnRate is null
            ? "実績年利は、取得日と入出金履歴が不足しているため算出不可です。将来想定年利はユーザー入力値です。税金、信託報酬、為替は考慮していません。"
            : "実績年利と将来想定年利は別の値です。将来想定年利はユーザー入力値です。税金、信託報酬、為替は考慮していません。";

        ProjectionRows.Clear();
        foreach (var projection in result.Projections)
        {
            ProjectionRows.Add(new MutualFundSimulationProjectionRowViewModel(projection));
        }

        ContributionComparisons.Clear();
        foreach (var comparison in result.ContributionComparisons)
        {
            ContributionComparisons.Add(new MutualFundContributionComparisonRowViewModel(comparison));
        }

        TrendPoints.Clear();
        foreach (var projection in result.Projections)
        {
            TrendPoints.Add(new PriceChartPointViewModel
            {
                Date = projection.YearMonth,
                Open = projection.MarketValueJpy,
                High = projection.MarketValueJpy,
                Low = projection.MarketValueJpy,
                Close = projection.MarketValueJpy,
                Volume = 0m
            });
        }
    }

    private void SaveSimulationSettings()
    {
        var settings = _loadSettings();
        settings.MutualFundSimulationScopeKey = SelectedScope?.Key ?? MutualFundSimulationScopeKeys.AllAccounts;
        settings.MutualFundSimulationMonthlyContributionJpy = MonthlyContributionJpy;
        settings.MutualFundSimulationExpectedAnnualReturnRate = ExpectedAnnualReturnRate;
        settings.MutualFundSimulationTargetAmountJpy = TargetAmountJpy;
        settings.MutualFundSimulationProjectionYears = ProjectionYears;
        settings.MutualFundSimulationTargetYears = TargetYears;
        _saveSettings(settings);
    }

    private static string FormatRemainingPeriod(int? months)
    {
        if (months is null)
        {
            return "指定期間内では未達";
        }

        if (months == 0)
        {
            return "すでに到達済み";
        }

        var years = months.Value / 12;
        var remainingMonths = months.Value % 12;
        if (years == 0)
        {
            return $"{remainingMonths}か月";
        }

        return remainingMonths == 0
            ? $"{years}年"
            : $"{years}年{remainingMonths}か月";
    }
}

public sealed class SimulationScopeOptionViewModel
{
    public SimulationScopeOptionViewModel(MutualFundSimulationScopeOption option)
    {
        Key = option.Key;
        DisplayName = $"{option.DisplayName} ({option.PositionCount:N0})";
    }

    public string Key { get; }
    public string DisplayName { get; }
}

public sealed class MutualFundSimulationProjectionRowViewModel
{
    public MutualFundSimulationProjectionRowViewModel(MutualFundMonthlyProjection projection)
    {
        YearMonth = projection.YearMonth.ToString("yyyy/MM");
        MonthsFromNow = $"{projection.MonthsFromNow:N0}か月";
        MarketValue = Formatters.Jpy(projection.MarketValueJpy);
        NoContributionMarketValue = Formatters.Jpy(projection.NoContributionMarketValueJpy);
        Cost = Formatters.Jpy(projection.CostJpy);
        CumulativeContribution = Formatters.Jpy(projection.CumulativeContributionJpy);
        UnrealizedGain = Formatters.SignedJpy(projection.UnrealizedGainJpy);
        TargetAchievementRate = Formatters.Percent(projection.TargetAchievementRate);
    }

    public string YearMonth { get; }
    public string MonthsFromNow { get; }
    public string MarketValue { get; }
    public string NoContributionMarketValue { get; }
    public string Cost { get; }
    public string CumulativeContribution { get; }
    public string UnrealizedGain { get; }
    public string TargetAchievementRate { get; }
}

public sealed class MutualFundContributionComparisonRowViewModel
{
    public MutualFundContributionComparisonRowViewModel(MutualFundContributionComparison comparison)
    {
        Label = comparison.Label;
        MonthlyContribution = Formatters.Jpy(comparison.MonthlyContributionJpy);
        FinalMarketValue = Formatters.Jpy(comparison.FinalMarketValueJpy);
    }

    public string Label { get; }
    public string MonthlyContribution { get; }
    public string FinalMarketValue { get; }
}
