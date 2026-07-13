using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class SimulationViewModel : ObservableObject
{
    private readonly InvestmentCalculator _calculator;
    private readonly MutualFundAssetSimulationService _simulationService;
    private readonly DividendGrowthSimulationService _dividendSimulationService = new();
    private readonly IMarketDataService _marketDataService;
    private readonly IStockLookupService _stockLookupService;
    private readonly Func<AppSettings> _loadSettings;
    private readonly Action<AppSettings> _saveSettings;
    private IReadOnlyList<StockPosition> _positions = Array.Empty<StockPosition>();
    private IReadOnlyList<TaxProfile> _taxProfiles = Array.Empty<TaxProfile>();
    private SimulationScopeOptionViewModel? _selectedScope;
    private bool _suppressDividendAutoUpdates;
    private decimal _currentAnnualPassiveIncome;
    private decimal _monthlyAdditionalInvestment = 100_000m;
    private decimal _assumedDividendYieldRate = 4m;
    private decimal _annualDividendGrowthRate = 5m;
    private decimal _targetAnnualPassiveIncome = 1_200_000m;
    private int _passiveIncomeProjectionYears = 10;
    private string _passiveIncomePlanName = "Default";
    private string _passiveIncomeDisplayMode = DividendGrowthDisplayModes.AggregateBySecurity;
    private string _passiveIncomeCurrentGrossDividend = "0円";
    private string _passiveIncomeCurrentNetDividend = "0円";
    private string _passiveIncomePostAddGrossDividend = "0円";
    private string _passiveIncomePostAddNetDividend = "0円";
    private string _passiveIncomeExistingPlannedInvestment = "0円";
    private string _passiveIncomeNewPlannedInvestment = "0円";
    private string _passiveIncomeTotalPlannedInvestment = "0円";
    private string _passiveIncomeDividendIncrease = "0円";
    private string _passiveIncomeNetDividendIncrease = "0円";
    private string _passiveIncomeMonthlyNetDividend = "0円";
    private string _passiveIncomeInvestmentYield = "0.00%";
    private string _passiveIncomeGoalAchievement = "0.00%";
    private string _passiveIncomeGoalGap = "0円";
    private string _passiveIncomeTaxSummary = "税額 0円";
    private DividendSimulationRowViewModel? _selectedNewDividendSimulationRow;
    private string _passiveIncomeResultSummary = "条件を入力してシミュレーションしてください。";
    private string _passiveIncomeTrendStatus = "不労所得シミュレーションを実行すると、年月別の上昇トレンドを表示します。";
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
    private string _positionCount = "0件";
    private string _selectedScopeSummary = "対象：すべての投資信託";
    private string _contributionNotice = "将来想定年利、税金、信託報酬、為替はユーザー入力前提の概算です。";
    private bool _isMonthlyContributionEnabled = true;
    private string _finalAssetAmount = "0円";
    private string _targetAchievementMonth = "未達";
    private string _remainingPeriod = "未達";
    private string _requiredMonthlyContribution = "0円";
    private string _additionalMonthlyContributionNeeded = "0円";
    private string _requiredMonthlyContributionDetail = string.Empty;
    private string _stopContributionFinalAsset = "0円";
    private string _chartStatus = "投資信託の現在保有データを読み込むと、資産形成シミュレーションを表示します。";
    private string _statusMessage = "税金、信託報酬、為替は考慮していません。将来想定年利は手動で変更できます。";
    private string _mutualFundInputError = string.Empty;
    private bool _isRebuildingScopes;
    private bool _suppressMutualFundAutoUpdates;
    private bool _expectedAnnualReturnRateWasEdited;

    public SimulationViewModel(
        InvestmentCalculator calculator,
        MutualFundAssetSimulationService simulationService,
        Func<AppSettings> loadSettings,
        Action<AppSettings> saveSettings,
        IMarketDataService? marketDataService = null,
        IStockLookupService? stockLookupService = null)
    {
        _calculator = calculator;
        _simulationService = simulationService;
        _marketDataService = marketDataService ?? new MarketDataProviderFactory();
        _stockLookupService = stockLookupService ?? new CompositeStockLookupService(
            new LocalStockLookupService(),
            new YahooFinanceStockLookupService());
        _loadSettings = loadSettings;
        _saveSettings = saveSettings;
        var settings = _loadSettings();
        _monthlyContributionJpy = settings.MutualFundSimulationMonthlyContributionJpy;
        _expectedAnnualReturnRate = settings.MutualFundSimulationExpectedAnnualReturnRate;
        _targetAmountJpy = settings.MutualFundSimulationTargetAmountJpy;
        _projectionYears = settings.MutualFundSimulationProjectionYears;
        _targetYears = settings.MutualFundSimulationTargetYears;
        _passiveIncomePlanName = string.IsNullOrWhiteSpace(settings.DividendSimulationPlanName) ? "Default" : settings.DividendSimulationPlanName;
        _passiveIncomeDisplayMode = string.IsNullOrWhiteSpace(settings.DividendSimulationDisplayMode)
            ? DividendGrowthDisplayModes.AggregateBySecurity
            : settings.DividendSimulationDisplayMode;
        _targetAnnualPassiveIncome = settings.DividendSimulationTargetAnnualDividendJpy <= 0m
            ? 1_200_000m
            : settings.DividendSimulationTargetAnnualDividendJpy;
        _passiveIncomeProjectionYears = settings.DividendSimulationProjectionYears <= 0 ? 10 : settings.DividendSimulationProjectionYears;

        RunCommand = new RelayCommand(RunMutualFundSimulation);
        RunPassiveIncomeCommand = new RelayCommand(RunPassiveIncomeSimulation);
        SavePassiveIncomePlanCommand = new RelayCommand(SavePassiveIncomePlan);
        AddNewDividendStockCommand = new RelayCommand(AddNewDividendStock);
        RemoveSelectedNewDividendStockCommand = new RelayCommand(RemoveSelectedNewDividendStock, () => SelectedNewDividendSimulationRow is not null);
        FetchNewDividendStockCommand = new RelayCommand(parameter => FetchNewDividendStock(parameter as DividendSimulationRowViewModel));
        RunTsumitateNisaCommand = RunCommand;
    }

    public ObservableCollection<SimulationProjectionRowViewModel> Projections { get; } = new();
    public ObservableCollection<PriceChartPointViewModel> PassiveIncomeTrendPoints { get; } = new();
    public ObservableCollection<DividendSimulationRowViewModel> DividendSimulationRows { get; } = new();
    public ObservableCollection<DividendSimulationRowViewModel> NewDividendSimulationRows { get; } = new();
    public ObservableCollection<DividendProjectionRowViewModel> DividendProjectionRows { get; } = new();
    public ObservableCollection<DisplayOptionViewModel> PassiveIncomeDisplayModes { get; } = new()
    {
        new(DividendGrowthDisplayModes.AggregateBySecurity, "銘柄ごとに集約"),
        new(DividendGrowthDisplayModes.Position, "口座・証券会社ごと")
    };
    public ObservableCollection<string> DividendBrokerOptions { get; } = new()
    {
        "SBI証券",
        "野村證券",
        "楽天証券",
        "その他"
    };
    public ObservableCollection<string> DividendCountryOptions { get; } = new()
    {
        "Japan",
        "United States",
        "その他"
    };
    public ObservableCollection<string> DividendCurrencyOptions { get; } = new()
    {
        "JPY",
        "USD"
    };
    public ObservableCollection<DisplayOptionViewModel> DividendAccountOptions { get; } = new()
    {
        new(AccountTypes.Specific, "特定口座"),
        new(AccountTypes.General, "一般口座"),
        new(AccountTypes.NisaGrowth, "NISA成長投資枠"),
        new(AccountTypes.NisaLegacy, "旧NISA"),
        new(AccountTypes.Unknown, "その他")
    };
    public ObservableCollection<DisplayOptionViewModel> DividendPurchaseModeOptions { get; } = new()
    {
        new(DividendGrowthPurchaseModes.OneTime, "今回だけ購入"),
        new(DividendGrowthPurchaseModes.ContinueMonthly, "毎月継続"),
        new(DividendGrowthPurchaseModes.None, "購入なし")
    };
    public ObservableCollection<SimulationScopeOptionViewModel> ScopeOptions { get; } = new();
    public ObservableCollection<MutualFundAccountBreakdownRowViewModel> AccountBreakdowns { get; } = new();
    public ObservableCollection<MutualFundSimulationProjectionRowViewModel> ProjectionRows { get; } = new();
    public ObservableCollection<MutualFundContributionComparisonRowViewModel> ContributionComparisons { get; } = new();
    public ObservableCollection<PriceChartPointViewModel> TrendPoints { get; } = new();
    public ICommand RunCommand { get; }
    public ICommand RunPassiveIncomeCommand { get; }
    public ICommand SavePassiveIncomePlanCommand { get; }
    public ICommand AddNewDividendStockCommand { get; }
    public ICommand RemoveSelectedNewDividendStockCommand { get; }
    public ICommand FetchNewDividendStockCommand { get; }
    public ICommand RunTsumitateNisaCommand { get; }

    public decimal CurrentAnnualPassiveIncome
    {
        get => _currentAnnualPassiveIncome;
        set => SetProperty(ref _currentAnnualPassiveIncome, value);
    }

    public decimal MonthlyAdditionalInvestment
    {
        get => _monthlyAdditionalInvestment;
        set => SetProperty(ref _monthlyAdditionalInvestment, value);
    }

    public decimal AssumedDividendYieldRate
    {
        get => _assumedDividendYieldRate;
        set => SetProperty(ref _assumedDividendYieldRate, value);
    }

    public decimal AnnualDividendGrowthRate
    {
        get => _annualDividendGrowthRate;
        set => SetProperty(ref _annualDividendGrowthRate, value);
    }

    public decimal TargetAnnualPassiveIncome
    {
        get => _targetAnnualPassiveIncome;
        set
        {
            if (SetProperty(ref _targetAnnualPassiveIncome, value) && !_suppressDividendAutoUpdates)
            {
                RunPassiveIncomeSimulation();
            }
        }
    }

    public int PassiveIncomeProjectionYears
    {
        get => _passiveIncomeProjectionYears;
        set
        {
            if (SetProperty(ref _passiveIncomeProjectionYears, value) && !_suppressDividendAutoUpdates)
            {
                RunPassiveIncomeSimulation();
            }
        }
    }

    public string PassiveIncomePlanName
    {
        get => _passiveIncomePlanName;
        set => SetProperty(ref _passiveIncomePlanName, value);
    }

    public string PassiveIncomeDisplayMode
    {
        get => _passiveIncomeDisplayMode;
        set
        {
            if (SetProperty(ref _passiveIncomeDisplayMode, value) && !_suppressDividendAutoUpdates)
            {
                RebuildDividendPlanRows();
                RunPassiveIncomeSimulation();
            }
        }
    }

    public DividendSimulationRowViewModel? SelectedNewDividendSimulationRow
    {
        get => _selectedNewDividendSimulationRow;
        set
        {
            if (SetProperty(ref _selectedNewDividendSimulationRow, value) &&
                RemoveSelectedNewDividendStockCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public string PassiveIncomeCurrentGrossDividend
    {
        get => _passiveIncomeCurrentGrossDividend;
        private set => SetProperty(ref _passiveIncomeCurrentGrossDividend, value);
    }

    public string PassiveIncomeCurrentNetDividend
    {
        get => _passiveIncomeCurrentNetDividend;
        private set => SetProperty(ref _passiveIncomeCurrentNetDividend, value);
    }

    public string PassiveIncomePostAddGrossDividend
    {
        get => _passiveIncomePostAddGrossDividend;
        private set => SetProperty(ref _passiveIncomePostAddGrossDividend, value);
    }

    public string PassiveIncomePostAddNetDividend
    {
        get => _passiveIncomePostAddNetDividend;
        private set => SetProperty(ref _passiveIncomePostAddNetDividend, value);
    }

    public string PassiveIncomeExistingPlannedInvestment
    {
        get => _passiveIncomeExistingPlannedInvestment;
        private set => SetProperty(ref _passiveIncomeExistingPlannedInvestment, value);
    }

    public string PassiveIncomeNewPlannedInvestment
    {
        get => _passiveIncomeNewPlannedInvestment;
        private set => SetProperty(ref _passiveIncomeNewPlannedInvestment, value);
    }

    public string PassiveIncomeTotalPlannedInvestment
    {
        get => _passiveIncomeTotalPlannedInvestment;
        private set => SetProperty(ref _passiveIncomeTotalPlannedInvestment, value);
    }

    public string PassiveIncomeDividendIncrease
    {
        get => _passiveIncomeDividendIncrease;
        private set => SetProperty(ref _passiveIncomeDividendIncrease, value);
    }

    public string PassiveIncomeNetDividendIncrease
    {
        get => _passiveIncomeNetDividendIncrease;
        private set => SetProperty(ref _passiveIncomeNetDividendIncrease, value);
    }

    public string PassiveIncomeMonthlyNetDividend
    {
        get => _passiveIncomeMonthlyNetDividend;
        private set => SetProperty(ref _passiveIncomeMonthlyNetDividend, value);
    }

    public string PassiveIncomeInvestmentYield
    {
        get => _passiveIncomeInvestmentYield;
        private set => SetProperty(ref _passiveIncomeInvestmentYield, value);
    }

    public string PassiveIncomeGoalAchievement
    {
        get => _passiveIncomeGoalAchievement;
        private set => SetProperty(ref _passiveIncomeGoalAchievement, value);
    }

    public string PassiveIncomeGoalGap
    {
        get => _passiveIncomeGoalGap;
        private set => SetProperty(ref _passiveIncomeGoalGap, value);
    }

    public string PassiveIncomeTaxSummary
    {
        get => _passiveIncomeTaxSummary;
        private set => SetProperty(ref _passiveIncomeTaxSummary, value);
    }

    public string PassiveIncomeResultSummary
    {
        get => _passiveIncomeResultSummary;
        private set => SetProperty(ref _passiveIncomeResultSummary, value);
    }

    public string PassiveIncomeTrendStatus
    {
        get => _passiveIncomeTrendStatus;
        private set => SetProperty(ref _passiveIncomeTrendStatus, value);
    }

    public SimulationScopeOptionViewModel? SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (!SetProperty(ref _selectedScope, value) || _isRebuildingScopes)
            {
                return;
            }

            _expectedAnnualReturnRateWasEdited = false;
            RunMutualFundSimulation();
        }
    }

    public decimal MonthlyContributionJpy
    {
        get => _monthlyContributionJpy;
        set
        {
            if (SetProperty(ref _monthlyContributionJpy, value) && !_suppressMutualFundAutoUpdates)
            {
                OnPropertyChanged(nameof(MonthlyContributionInput));
                RunMutualFundSimulation();
            }
        }
    }

    public decimal MonthlyContributionInput
    {
        get => IsMonthlyContributionEnabled ? _monthlyContributionJpy : 0m;
        set
        {
            if (!IsMonthlyContributionEnabled)
            {
                return;
            }

            MonthlyContributionJpy = value;
        }
    }

    public bool IsMonthlyContributionEnabled
    {
        get => _isMonthlyContributionEnabled;
        private set
        {
            if (SetProperty(ref _isMonthlyContributionEnabled, value))
            {
                OnPropertyChanged(nameof(MonthlyContributionInput));
            }
        }
    }

    public decimal ExpectedAnnualReturnRate
    {
        get => _expectedAnnualReturnRate;
        set
        {
            if (SetProperty(ref _expectedAnnualReturnRate, value) && !_suppressMutualFundAutoUpdates)
            {
                _expectedAnnualReturnRateWasEdited = true;
                RunMutualFundSimulation();
            }
        }
    }

    public decimal TargetAmountJpy
    {
        get => _targetAmountJpy;
        set
        {
            if (SetProperty(ref _targetAmountJpy, value) && !_suppressMutualFundAutoUpdates)
            {
                RunMutualFundSimulation();
            }
        }
    }

    public int ProjectionYears
    {
        get => _projectionYears;
        set
        {
            if (SetProperty(ref _projectionYears, value) && !_suppressMutualFundAutoUpdates)
            {
                RaiseProjectionLabelChanges();
                RunMutualFundSimulation();
            }
        }
    }

    public int TargetYears
    {
        get => _targetYears;
        set
        {
            if (SetProperty(ref _targetYears, value) && !_suppressMutualFundAutoUpdates)
            {
                RaiseProjectionLabelChanges();
                RunMutualFundSimulation();
            }
        }
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

    public string PositionCount
    {
        get => _positionCount;
        private set => SetProperty(ref _positionCount, value);
    }

    public string SelectedScopeSummary
    {
        get => _selectedScopeSummary;
        private set => SetProperty(ref _selectedScopeSummary, value);
    }

    public string ContributionNotice
    {
        get => _contributionNotice;
        private set => SetProperty(ref _contributionNotice, value);
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

    public string AdditionalMonthlyContributionNeeded
    {
        get => _additionalMonthlyContributionNeeded;
        private set => SetProperty(ref _additionalMonthlyContributionNeeded, value);
    }

    public string RequiredMonthlyContributionDetail
    {
        get => _requiredMonthlyContributionDetail;
        private set => SetProperty(ref _requiredMonthlyContributionDetail, value);
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

    public string MutualFundInputError
    {
        get => _mutualFundInputError;
        private set => SetProperty(ref _mutualFundInputError, value);
    }

    public string ProjectionAssetLabel => $"{Math.Max(1, ProjectionYears):N0}年後の資産額";

    public string TargetAchievementLabel => "現在の積立条件での目標到達予定";

    public string RequiredMonthlyContributionLabel => $"{Math.Max(1, TargetYears):N0}年以内に目標達成するための最低必要月額";

    public string AdditionalMonthlyContributionLabel => "現在設定からの追加必要額";

    public string StopContributionFinalAssetLabel => $"積立を停止した場合の{Math.Max(1, ProjectionYears):N0}年後資産額";

    public void UpdateCurrentAnnualIncome(decimal annualIncome)
    {
        CurrentAnnualPassiveIncome = Math.Round(Math.Max(0m, annualIncome), 0, MidpointRounding.AwayFromZero);
        if (Projections.Count == 0)
        {
            RunPassiveIncomeSimulation();
        }
    }

    public void UpdateNisaPortfolio(IReadOnlyList<StockSnapshot> snapshots)
    {
        UpdateMutualFundPortfolio(snapshots.Select(x => x.Position).ToList());
    }

    public void UpdateMutualFundPortfolio(IReadOnlyList<StockPosition> positions)
    {
        _positions = positions;
        RebuildScopeOptions();
        RunMutualFundSimulation(saveSettings: false);
        RebuildDividendPlanRows();
        RunPassiveIncomeSimulation(saveSettings: false);
    }

    public void UpdateDividendPortfolio(IReadOnlyList<StockPosition> positions, IReadOnlyList<TaxProfile> taxProfiles)
    {
        _positions = positions;
        _taxProfiles = taxProfiles;
        RebuildDividendPlanRows();
        RunPassiveIncomeSimulation(saveSettings: false);
    }

    private void RunPassiveIncomeSimulation()
    {
        RunPassiveIncomeSimulation(saveSettings: true);
    }

    private void RunPassiveIncomeSimulation(bool saveSettings)
    {
        if (TargetAnnualPassiveIncome < 0m || PassiveIncomeProjectionYears <= 0)
        {
            PassiveIncomeResultSummary = "目標年間配当は0円以上、試算年数は1年以上で入力してください。";
            return;
        }

        var result = _dividendSimulationService.Simulate(
            new DividendGrowthSimulationInput
            {
                PlanName = PassiveIncomePlanName,
                DisplayMode = PassiveIncomeDisplayMode,
                TargetAnnualDividendJpy = TargetAnnualPassiveIncome,
                ProjectionYears = PassiveIncomeProjectionYears,
                StartYear = DateTime.Today.Year,
                PlanItems = BuildDividendPlanItems()
            },
            _taxProfiles);
        ApplyDividendSimulationResult(result);

        if (saveSettings)
        {
            SavePassiveIncomePlan();
        }
    }

    private void ApplyDividendSimulationResult(DividendGrowthSimulationResult result)
    {
        PassiveIncomeCurrentGrossDividend = Formatters.Jpy(result.Summary.CurrentGrossAnnualDividendJpy);
        PassiveIncomeCurrentNetDividend = Formatters.Jpy(result.Summary.CurrentNetAnnualDividendJpy);
        PassiveIncomePostAddGrossDividend = Formatters.Jpy(result.Summary.PostAddGrossAnnualDividendJpy);
        PassiveIncomePostAddNetDividend = Formatters.Jpy(result.Summary.PostAddNetAnnualDividendJpy);
        PassiveIncomeExistingPlannedInvestment = Formatters.Jpy(result.Summary.ExistingPlannedInvestmentJpy);
        PassiveIncomeNewPlannedInvestment = Formatters.Jpy(result.Summary.NewPlannedInvestmentJpy);
        PassiveIncomeTotalPlannedInvestment = Formatters.Jpy(result.Summary.TotalPlannedInvestmentJpy);
        PassiveIncomeDividendIncrease = Formatters.SignedJpy(result.Summary.AnnualDividendIncreaseJpy);
        PassiveIncomeNetDividendIncrease = Formatters.SignedJpy(result.Summary.NetAnnualDividendIncreaseJpy);
        PassiveIncomeMonthlyNetDividend = Formatters.Jpy(result.Summary.MonthlyNetDividendJpy);
        PassiveIncomeInvestmentYield = Formatters.Percent(result.Summary.InvestmentDividendYieldRate);
        PassiveIncomeGoalAchievement = Formatters.Percent(result.Summary.TargetAchievementRate);
        PassiveIncomeGoalGap = Formatters.Jpy(result.Summary.TargetGapJpy);
        PassiveIncomeTaxSummary =
            $"外国税 {Formatters.Jpy(result.Summary.ForeignTaxJpy)} / 国内税 {Formatters.Jpy(result.Summary.DomesticTaxJpy)} / 合計 {Formatters.Jpy(result.Summary.TotalTaxJpy)}";
        PassiveIncomeResultSummary = result.Summary.TargetAchievementYear is null
            ? "指定期間内では税引後配当が目標に届きません。買い増し株数、配当成長率、目標額を調整してください。"
            : $"{result.Summary.TargetAchievementYear}年に税引後配当が目標へ到達する見込みです。";

        DividendProjectionRows.Clear();
        PassiveIncomeTrendPoints.Clear();
        foreach (var projection in result.Projections)
        {
            DividendProjectionRows.Add(new DividendProjectionRowViewModel(projection));
            PassiveIncomeTrendPoints.Add(new PriceChartPointViewModel
            {
                Date = new DateTime(projection.Year, 12, 1),
                Open = projection.PlannedNetDividendJpy,
                High = Math.Max(projection.PlannedNetDividendJpy, projection.TargetAnnualDividendJpy),
                Low = Math.Min(projection.PlannedNetDividendJpy, projection.CurrentOnlyNetDividendJpy),
                Close = projection.PlannedNetDividendJpy,
                Volume = projection.CurrentOnlyNetDividendJpy
            });
        }

        PassiveIncomeTrendStatus = result.Projections.Count == 0
            ? "配当シミュレーションのグラフデータがありません。"
            : $"{result.Projections[0].Year} - {result.Projections[^1].Year} / 税引後年間配当トレンド";
    }

    private void RebuildDividendPlanRows()
    {
        if (_suppressDividendAutoUpdates)
        {
            return;
        }

        _suppressDividendAutoUpdates = true;
        var currentEdits = BuildDividendPlanItems().ToList();
        var savedItems = LoadSavedDividendPlanItems();
        var overrides = savedItems
            .Concat(currentEdits)
            .GroupBy(GetPlanOverrideKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var defaultRows = _dividendSimulationService.CreateDefaultPlanItems(_positions, PassiveIncomeDisplayMode);

        DividendSimulationRows.Clear();
        foreach (var item in defaultRows)
        {
            DividendSimulationRows.Add(new DividendSimulationRowViewModel(ApplyPlanOverride(item, overrides), OnDividendSimulationRowChanged));
        }

        NewDividendSimulationRows.Clear();
        foreach (var item in savedItems.Concat(currentEdits).Where(item => item.IsNewStock).GroupBy(GetPlanOverrideKey, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()))
        {
            NewDividendSimulationRows.Add(new DividendSimulationRowViewModel(item, OnDividendSimulationRowChanged));
        }

        _suppressDividendAutoUpdates = false;
    }

    private IReadOnlyList<DividendGrowthPlanItem> BuildDividendPlanItems() =>
        DividendSimulationRows
            .Concat(NewDividendSimulationRows)
            .Select(row => row.ToPlanItem())
            .ToList();

    private void OnDividendSimulationRowChanged()
    {
        if (_suppressDividendAutoUpdates)
        {
            return;
        }

        RunPassiveIncomeSimulation(saveSettings: false);
    }

    private void AddNewDividendStock()
    {
        var item = new DividendGrowthPlanItem
        {
            PlanKey = $"New:{Guid.NewGuid():N}",
            CanonicalKey = string.Empty,
            PositionKey = string.Empty,
            Ticker = string.Empty,
            Name = string.Empty,
            Broker = "SBI証券",
            AccountType = AccountTypes.Specific,
            Country = "United States",
            Currency = "USD",
            CurrentShares = 0m,
            CurrentPrice = 0m,
            ExchangeRate = 160m,
            AnnualDividendPerShare = 0m,
            AnnualDividendSource = "手入力",
            MarketDataSource = "Manual",
            MarketDataStatus = "手入力",
            PlannedAdditionalShares = 0m,
            PlannedBroker = "SBI証券",
            PlannedAccountType = AccountTypes.Specific,
            AnnualDividendGrowthRate = 0m,
            PurchaseMode = DividendGrowthPurchaseModes.OneTime,
            IsNewStock = true
        };
        var row = new DividendSimulationRowViewModel(item, OnDividendSimulationRowChanged);
        NewDividendSimulationRows.Add(row);
        SelectedNewDividendSimulationRow = row;
        RunPassiveIncomeSimulation(saveSettings: false);
    }

    private void RemoveSelectedNewDividendStock()
    {
        if (SelectedNewDividendSimulationRow is null)
        {
            return;
        }

        NewDividendSimulationRows.Remove(SelectedNewDividendSimulationRow);
        SelectedNewDividendSimulationRow = NewDividendSimulationRows.FirstOrDefault();
        RunPassiveIncomeSimulation(saveSettings: false);
    }

    private async void FetchNewDividendStock(DividendSimulationRowViewModel? row)
    {
        row ??= SelectedNewDividendSimulationRow;
        if (row is null || row.IsMarketDataFetching)
        {
            return;
        }

        var query = row.Ticker.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            row.SetMarketDataFailure("コード／ティッカーを入力してください。");
            return;
        }

        row.BeginMarketDataFetch();
        try
        {
            var result = await Task.Run(() => FetchDividendStockMarketData(query, row));
            row.ApplyMarketData(result);
        }
        catch (Exception ex)
        {
            row.SetMarketDataFailure($"取得失敗: {ex.Message}");
        }

        RunPassiveIncomeSimulation(saveSettings: false);
    }

    private DividendStockMarketDataFetchResult FetchDividendStockMarketData(
        string query,
        DividendSimulationRowViewModel currentRow)
    {
        var lookup = _stockLookupService.Find(query);
        var symbol = lookup?.Ticker ?? query.Trim();
        var settings = BuildLiveMarketDataSettings(_loadSettings());
        var marketResult = _marketDataService.GetQuote(symbol, settings);
        var quote = marketResult.Quote;
        var now = DateTime.Now;

        var ticker = FirstNonBlank(quote?.Symbol, lookup?.Ticker, symbol);
        if (ticker.EndsWith(".T", StringComparison.OrdinalIgnoreCase))
        {
            ticker = ticker[..^2];
        }

        var currency = NormalizeCurrency(FirstNonBlank(quote?.Currency, lookup?.Currency, currentRow.Currency));
        var country = NormalizeCountry(FirstNonBlank(quote?.Country, lookup?.Country, currentRow.Country), currency, ticker);
        var price = quote?.CurrentPrice ?? lookup?.CurrentPrice;
        var annualDividend = quote?.AnnualDividendPerShare ?? lookup?.AnnualDividendPerShare;
        var exchangeRate = currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? 1m
            : quote?.UsdJpyRate ?? currentRow.ExchangeRate;
        var source = FirstNonBlank(quote?.Source, lookup?.Source, marketResult.IsSuccess ? "MarketData" : string.Empty);
        var dividendSource = FirstNonBlank(quote?.DividendInfoSource, lookup?.Source, quote?.Source, "Manual");
        var name = FirstNonBlank(quote?.Name, lookup?.Name, currentRow.Name);

        if (marketResult.IsSuccess && quote is not null)
        {
            var status = price is > 0m && annualDividend is not null
                ? $"取得成功 {now:yyyy/MM/dd HH:mm}"
                : $"一部取得 {now:yyyy/MM/dd HH:mm}（未取得項目は手入力可）";
            return new DividendStockMarketDataFetchResult(
                true,
                ticker,
                name,
                country,
                currency,
                price,
                exchangeRate,
                annualDividend,
                dividendSource,
                source,
                quote.PriceAcquiredAt ?? now,
                status);
        }

        if (lookup is not null)
        {
            return new DividendStockMarketDataFetchResult(
                true,
                ticker,
                lookup.Name,
                country,
                currency,
                price,
                exchangeRate,
                annualDividend,
                dividendSource,
                lookup.Source,
                now,
                price is > 0m || annualDividend is not null
                    ? $"一部取得 {now:yyyy/MM/dd HH:mm}（ローカル候補）"
                    : $"一部取得 {now:yyyy/MM/dd HH:mm}（株価・配当は手入力可）");
        }

        return new DividendStockMarketDataFetchResult(
            false,
            currentRow.Ticker,
            currentRow.Name,
            currentRow.Country,
            currentRow.Currency,
            null,
            null,
            null,
            currentRow.AnnualDividendSource,
            string.Empty,
            now,
            string.IsNullOrWhiteSpace(marketResult.ErrorMessage)
                ? "取得失敗: 銘柄情報を取得できませんでした。手入力してください。"
                : $"取得失敗: {marketResult.ErrorMessage}");
    }

    private static AppSettings BuildLiveMarketDataSettings(AppSettings settings)
    {
        if (settings.MarketDataMode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            settings.MarketDataMode = "Web/API";
        }

        if (settings.JapanMarketDataProvider.Equals("J-Quants", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(settings.JQuantsApiKey))
        {
            settings.JapanMarketDataProvider = "Yahoo Finance";
        }

        if (settings.UsMarketDataProvider.Equals("Alpha Vantage", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(settings.AlphaVantageApiKey))
        {
            settings.UsMarketDataProvider = "Yahoo Finance";
        }

        return settings;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string NormalizeCurrency(string value)
    {
        if (value.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("円", StringComparison.OrdinalIgnoreCase))
        {
            return "JPY";
        }

        return value.Equals("USD", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ドル", StringComparison.OrdinalIgnoreCase)
            ? "USD"
            : string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeCountry(string value, string currency, string ticker)
    {
        if (currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
            ticker.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ||
            (ticker.Length is 4 or 5 && ticker.All(char.IsDigit)) ||
            value.Contains("Japan", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("日本", StringComparison.OrdinalIgnoreCase))
        {
            return "Japan";
        }

        if (currency.Equals("USD", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("United States", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("米国", StringComparison.OrdinalIgnoreCase))
        {
            return "United States";
        }

        return string.IsNullOrWhiteSpace(value) ? "Other" : value.Trim();
    }

    private void SavePassiveIncomePlan()
    {
        var settings = _loadSettings();
        settings.DividendSimulationPlanName = string.IsNullOrWhiteSpace(PassiveIncomePlanName) ? "Default" : PassiveIncomePlanName;
        settings.DividendSimulationDisplayMode = PassiveIncomeDisplayMode;
        settings.DividendSimulationTargetAnnualDividendJpy = TargetAnnualPassiveIncome;
        settings.DividendSimulationProjectionYears = PassiveIncomeProjectionYears;
        settings.DividendSimulationPlanJson = JsonSerializer.Serialize(BuildDividendPlanItems());
        _saveSettings(settings);
    }

    private IReadOnlyList<DividendGrowthPlanItem> LoadSavedDividendPlanItems()
    {
        var json = _loadSettings().DividendSimulationPlanJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<DividendGrowthPlanItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<DividendGrowthPlanItem>>(json) ?? new List<DividendGrowthPlanItem>();
        }
        catch (JsonException)
        {
            return Array.Empty<DividendGrowthPlanItem>();
        }
    }

    private static DividendGrowthPlanItem ApplyPlanOverride(
        DividendGrowthPlanItem source,
        IReadOnlyDictionary<string, DividendGrowthPlanItem> overrides)
    {
        if (!overrides.TryGetValue(GetPlanOverrideKey(source), out var saved) &&
            !overrides.TryGetValue(source.CanonicalKey, out saved) &&
            !overrides.TryGetValue(source.PositionKey, out saved))
        {
            return source;
        }

        return new DividendGrowthPlanItem
        {
            PlanKey = source.PlanKey,
            CanonicalKey = source.CanonicalKey,
            PositionKey = source.PositionKey,
            Ticker = source.Ticker,
            Name = source.Name,
            Broker = source.Broker,
            AccountType = source.AccountType,
            Country = source.Country,
            Currency = source.Currency,
            CurrentShares = source.CurrentShares,
            CurrentPrice = saved.CurrentPrice > 0m ? saved.CurrentPrice : source.CurrentPrice,
            ExchangeRate = saved.ExchangeRate > 0m ? saved.ExchangeRate : source.ExchangeRate,
            AnnualDividendPerShare = saved.AnnualDividendPerShare > 0m ? saved.AnnualDividendPerShare : source.AnnualDividendPerShare,
            AnnualDividendSource = string.IsNullOrWhiteSpace(saved.AnnualDividendSource) ? source.AnnualDividendSource : saved.AnnualDividendSource,
            MarketDataSource = string.IsNullOrWhiteSpace(saved.MarketDataSource) ? source.MarketDataSource : saved.MarketDataSource,
            MarketDataAcquiredAt = saved.MarketDataAcquiredAt ?? source.MarketDataAcquiredAt,
            MarketDataStatus = string.IsNullOrWhiteSpace(saved.MarketDataStatus) ? source.MarketDataStatus : saved.MarketDataStatus,
            PlannedAdditionalShares = Math.Max(0m, saved.PlannedAdditionalShares),
            PlannedBroker = string.IsNullOrWhiteSpace(saved.PlannedBroker) ? source.PlannedBroker : saved.PlannedBroker,
            PlannedAccountType = string.IsNullOrWhiteSpace(saved.PlannedAccountType) ? source.PlannedAccountType : saved.PlannedAccountType,
            AnnualDividendGrowthRate = saved.AnnualDividendGrowthRate,
            PurchaseMode = string.IsNullOrWhiteSpace(saved.PurchaseMode) ? source.PurchaseMode : saved.PurchaseMode,
            IsNewStock = source.IsNewStock,
            Components = source.Components
        };
    }

    private static string GetPlanOverrideKey(DividendGrowthPlanItem item) =>
        !string.IsNullOrWhiteSpace(item.PlanKey)
            ? item.PlanKey
            : !string.IsNullOrWhiteSpace(item.PositionKey)
                ? item.PositionKey
                : item.CanonicalKey;

    private void RunLegacyPassiveIncomeSimulation()
    {
        if (CurrentAnnualPassiveIncome < 0m ||
            MonthlyAdditionalInvestment < 0m ||
            AssumedDividendYieldRate < 0m ||
            AnnualDividendGrowthRate < -99m ||
            TargetAnnualPassiveIncome < 0m)
        {
            PassiveIncomeResultSummary = "入力値を確認してください。金額は0以上、増配率は-99%以上で入力してください。";
            return;
        }

        var result = _calculator.SimulatePassiveIncome(new PassiveIncomeSimulationInput
        {
            CurrentAnnualPassiveIncome = CurrentAnnualPassiveIncome,
            MonthlyAdditionalInvestment = MonthlyAdditionalInvestment,
            AssumedDividendYieldRate = AssumedDividendYieldRate,
            AnnualDividendGrowthRate = AnnualDividendGrowthRate,
            TargetAnnualPassiveIncome = TargetAnnualPassiveIncome,
            StartYear = DateTime.Today.Year,
            StartMonth = DateTime.Today.Month
        }, years: 10);

        Projections.Clear();
        foreach (var projection in result.Projections)
        {
            Projections.Add(new SimulationProjectionRowViewModel(projection));
        }

        PassiveIncomeTrendPoints.Clear();
        foreach (var projection in result.MonthlyProjections)
        {
            PassiveIncomeTrendPoints.Add(new PriceChartPointViewModel
            {
                Date = projection.YearMonth,
                Open = projection.AnnualPassiveIncome,
                High = projection.AnnualPassiveIncome,
                Low = projection.AnnualPassiveIncome,
                Close = projection.AnnualPassiveIncome,
                Volume = 0m
            });
        }

        PassiveIncomeTrendStatus = result.MonthlyProjections.Count == 0
            ? "不労所得のトレンドデータがありません。"
            : $"{result.MonthlyProjections[0].YearMonth:yyyy/MM} - {result.MonthlyProjections[^1].YearMonth:yyyy/MM} / 年間不労所得";
        PassiveIncomeResultSummary = result.TargetAchievementYear is null
            ? "10年以内には目標未達です。追加投資額、配当利回り、増配率を調整してください。"
            : $"{result.TargetAchievementYear}年に目標到達見込みです。あと{result.YearsToTarget}年です。";
    }

    private void RunMutualFundSimulation()
    {
        RunMutualFundSimulation(saveSettings: true);
    }

    private void RunMutualFundSimulation(bool saveSettings)
    {
        if (!ValidateMutualFundInputs())
        {
            return;
        }

        var result = SimulateWithCurrentInput();
        if (!_expectedAnnualReturnRateWasEdited &&
            result.Summary.ActualAnnualizedReturnRate is { } actualRate &&
            Math.Abs(ExpectedAnnualReturnRate - Math.Round(actualRate, 2, MidpointRounding.AwayFromZero)) >= 0.01m)
        {
            SetExpectedAnnualReturnRateSilently(Math.Round(actualRate, 2, MidpointRounding.AwayFromZero));
            result = SimulateWithCurrentInput();
        }

        ApplyResult(result);
        if (saveSettings)
        {
            SaveSimulationSettings();
        }
    }

    private bool ValidateMutualFundInputs()
    {
        if (MonthlyContributionJpy < 0m)
        {
            MutualFundInputError = "毎月積立額は0円以上で入力してください。";
            StatusMessage = MutualFundInputError;
            return false;
        }

        if (ExpectedAnnualReturnRate <= -100m)
        {
            MutualFundInputError = "将来想定年利は-100%より大きい値で入力してください。";
            StatusMessage = MutualFundInputError;
            return false;
        }

        if (TargetAmountJpy <= 0m)
        {
            MutualFundInputError = "目標金額は0円より大きい値で入力してください。";
            StatusMessage = MutualFundInputError;
            return false;
        }

        if (ProjectionYears is < 1 or > 100)
        {
            MutualFundInputError = "試算期間は1年以上100年以下で入力してください。";
            StatusMessage = MutualFundInputError;
            return false;
        }

        if (TargetYears is < 1 or > 100)
        {
            MutualFundInputError = "目標達成年数は1年以上100年以下で入力してください。";
            StatusMessage = MutualFundInputError;
            return false;
        }

        MutualFundInputError = string.Empty;
        return true;
    }

    private MutualFundAssetSimulationResult SimulateWithCurrentInput() =>
        _simulationService.Simulate(
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
        IsMonthlyContributionEnabled = summary.AllowsMonthlyContribution;
        CurrentMarketValue = Formatters.Jpy(summary.CurrentMarketValueJpy);
        CurrentCost = Formatters.Jpy(summary.CurrentCostJpy);
        UnrealizedGain = Formatters.SignedJpy(summary.UnrealizedGainJpy);
        UnrealizedGainRate = Formatters.SignedPercent(summary.UnrealizedGainRate);
        ActualAnnualizedReturn = summary.ActualAnnualizedReturnRate is { } annualizedReturn
            ? Formatters.SignedPercent(annualizedReturn)
            : "算出不可";
        FundCount = $"{summary.FundCount:N0}件";
        PositionCount = $"{summary.PositionCount:N0}件";

        AccountBreakdowns.Clear();
        foreach (var breakdown in result.AccountBreakdowns)
        {
            AccountBreakdowns.Add(new MutualFundAccountBreakdownRowViewModel(breakdown));
        }

        SelectedScopeSummary = BuildSelectedScopeSummary(result);
        ContributionNotice = BuildContributionNotice(result);
        RaiseProjectionLabelChanges();

        var finalProjection = result.Projections.LastOrDefault();
        FinalAssetAmount = finalProjection is null ? "0円" : Formatters.Jpy(finalProjection.MarketValueJpy);
        StopContributionFinalAsset = finalProjection is null ? "0円" : Formatters.Jpy(finalProjection.NoContributionMarketValueJpy);
        RequiredMonthlyContribution = result.IsRequiredMonthlyContributionApplicable
            ? Formatters.Jpy(result.RequiredMonthlyContributionJpy)
            : "対象外";
        AdditionalMonthlyContributionNeeded = result.IsRequiredMonthlyContributionApplicable
            ? Formatters.Jpy(result.AdditionalMonthlyContributionNeededJpy)
            : "対象外";
        RequiredMonthlyContributionDetail = BuildRequiredMonthlyContributionDetail(result, finalProjection);
        TargetAchievementMonth = result.TargetAchievementMonth is null
            ? "現在の条件では到達不可"
            : result.TargetAchievementMonth.Value.ToString("yyyy/MM");
        RemainingPeriod = FormatRemainingPeriod(result.MonthsToTarget);
        ChartStatus = result.Projections.Count == 0
            ? "現在保有中の投資信託がありません。CSV取込または登録後に表示されます。"
            : $"{result.Projections[0].YearMonth:yyyy/MM} - {result.Projections[^1].YearMonth:yyyy/MM} / 月次複利・月末積立";
        StatusMessage = summary.ActualAnnualizedReturnRate is null
            ? "実績年利は取得日と入出金履歴が不足しているため算出不可です。将来想定年利は手動で変更できます。税金、信託報酬、為替は考慮していません。"
            : "想定年利は実績年利の加重平均から自動入力しました。手動で変更できます。税金、信託報酬、為替は考慮していません。";

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
                High = Math.Max(projection.MarketValueJpy, projection.NoContributionMarketValueJpy),
                Low = Math.Min(projection.MarketValueJpy, projection.NoContributionMarketValueJpy),
                Close = projection.MarketValueJpy,
                SecondaryClose = projection.NoContributionMarketValueJpy,
                Volume = 0m,
                ToolTipText = BuildTrendPointToolTip(projection)
            });
        }

        OnPropertyChanged(nameof(MonthlyContributionInput));
    }

    private void RaiseProjectionLabelChanges()
    {
        OnPropertyChanged(nameof(ProjectionAssetLabel));
        OnPropertyChanged(nameof(RequiredMonthlyContributionLabel));
        OnPropertyChanged(nameof(StopContributionFinalAssetLabel));
    }

    private string BuildRequiredMonthlyContributionDetail(
        MutualFundAssetSimulationResult result,
        MutualFundMonthlyProjection? finalProjection)
    {
        var achievementRate = finalProjection is null ? 0m : finalProjection.TargetAchievementRate;
        var shortage = Math.Max(0m, TargetAmountJpy - (finalProjection?.MarketValueJpy ?? 0m));
        var baseLines = new List<string>
        {
            $"現在の積立額：{Formatters.Jpy(result.Summary.EffectiveMonthlyContributionJpy)}／月",
            $"必要積立額の目標期間：{TargetYears:N0}年",
            $"資産推移の試算期間：{ProjectionYears:N0}年",
            $"試算期間後の目標達成率：{achievementRate:N1}%",
            $"目標金額までの不足額：{Formatters.Jpy(shortage)}"
        };

        if (!result.IsRequiredMonthlyContributionApplicable)
        {
            baseLines.Insert(1, "必要な積立額：対象外");
            baseLines.Add("旧NISAは追加積立できないため、必要積立額の逆算対象外です。");
            return string.Join(Environment.NewLine, baseLines);
        }

        var required = result.RequiredMonthlyContributionJpy;
        var margin = result.MonthlyContributionMarginJpy;
        baseLines.Insert(1, $"最低必要月額：{Formatters.Jpy(required)}／月");
        baseLines.Insert(2, $"現在設定からの追加必要額：{Formatters.Jpy(result.AdditionalMonthlyContributionNeededJpy)}／月");
        baseLines.Insert(3, margin >= 0m
            ? $"差額：{Formatters.Jpy(margin)}／月の余裕"
            : $"差額：あと{Formatters.Jpy(Math.Abs(margin))}／月の増額が必要です");
        return string.Join(Environment.NewLine, baseLines);
    }

    private string BuildTrendPointToolTip(MutualFundMonthlyProjection projection)
    {
        return string.Join(
            Environment.NewLine,
            $"年月: {projection.YearMonth:yyyy/MM}",
            $"経過月数: {projection.MonthsFromNow:N0}か月",
            $"積立継続時: {Formatters.Jpy(projection.MarketValueJpy)}",
            $"積立停止時: {Formatters.Jpy(projection.NoContributionMarketValueJpy)}",
            $"累計積立額: {Formatters.Jpy(projection.CumulativeContributionJpy)}",
            $"評価益: {Formatters.SignedJpy(projection.UnrealizedGainJpy)}",
            $"目標達成率: {projection.TargetAchievementRate:N1}%");
    }

    private string BuildSelectedScopeSummary(MutualFundAssetSimulationResult result)
    {
        var target = SelectedScope?.DisplayName ?? "すべての投資信託";
        var accounts = result.AccountBreakdowns.Count == 0
            ? "対象ポジションはありません"
            : string.Join("、", result.AccountBreakdowns.Select(x => $"{x.AccountDisplayName} {x.PositionCount:N0}ポジション"));
        var total = $"対象ファンド数：{result.Summary.FundCount:N0}件 / 対象ポジション数：{result.Summary.PositionCount:N0}件";
        return $"対象：{target}\n{accounts}を合算\n{total}";
    }

    private string BuildContributionNotice(MutualFundAssetSimulationResult result)
    {
        if (!result.Summary.AllowsMonthlyContribution)
        {
            return "旧NISAには追加積立できません。保有継続シミュレーションとして、現在資産を将来想定年利で運用継続した場合を試算します。";
        }

        if (result.AccountBreakdowns.Any(x => x.AccountType == AccountTypes.NisaLegacy) &&
            result.AccountBreakdowns.Any(x => x.AllowsContribution))
        {
            var contributionAccount = result.AccountBreakdowns.First(x => x.AllowsContribution);
            return $"旧NISA残高は追加積立なしで運用し、毎月積立額は{contributionAccount.AccountDisplayName}側だけへ反映します。";
        }

        return "毎月積立額を対象口座へ反映し、月次複利・月末積立で試算します。";
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

    private void SetExpectedAnnualReturnRateSilently(decimal value)
    {
        _suppressMutualFundAutoUpdates = true;
        ExpectedAnnualReturnRate = value;
        _suppressMutualFundAutoUpdates = false;
    }

    private static string FormatRemainingPeriod(int? months)
    {
        if (months is null)
        {
            return "現在の条件では到達不可";
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
        DisplayName = option.DisplayName;
        FundCount = option.FundCount;
        PositionCount = option.PositionCount;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public int FundCount { get; }
    public int PositionCount { get; }

    public override string ToString() => DisplayName;
}

public sealed class MutualFundAccountBreakdownRowViewModel
{
    public MutualFundAccountBreakdownRowViewModel(MutualFundSimulationAccountBreakdown breakdown)
    {
        AccountDisplayName = breakdown.AccountDisplayName;
        CurrentMarketValue = Formatters.Jpy(breakdown.CurrentMarketValueJpy);
        CurrentCost = Formatters.Jpy(breakdown.CurrentCostJpy);
        UnrealizedGain = Formatters.SignedJpy(breakdown.UnrealizedGainJpy);
        Contribution = breakdown.AllowsContribution
            ? $"毎月{Formatters.Jpy(breakdown.MonthlyContributionJpy)}積立"
            : "追加積立なし";
        FundCount = $"{breakdown.FundCount:N0}件";
        PositionCount = $"{breakdown.PositionCount:N0}件";
    }

    public string AccountDisplayName { get; }
    public string CurrentMarketValue { get; }
    public string CurrentCost { get; }
    public string UnrealizedGain { get; }
    public string Contribution { get; }
    public string FundCount { get; }
    public string PositionCount { get; }
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

public sealed record DividendStockMarketDataFetchResult(
    bool IsSuccess,
    string Ticker,
    string Name,
    string Country,
    string Currency,
    decimal? CurrentPrice,
    decimal? ExchangeRate,
    decimal? AnnualDividendPerShare,
    string DividendSource,
    string Source,
    DateTime? AcquiredAt,
    string Status);

public sealed class DividendSimulationRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private string _ticker;
    private string _name;
    private string _broker;
    private string _accountType;
    private string _country;
    private string _currency;
    private decimal _currentShares;
    private decimal _currentPrice;
    private decimal _exchangeRate;
    private decimal _annualDividendPerShare;
    private string _annualDividendSource;
    private string _marketDataSource;
    private DateTime? _marketDataAcquiredAt;
    private string _marketDataFetchStatus;
    private bool _isMarketDataFetching;
    private decimal _plannedAdditionalShares;
    private string _plannedBroker;
    private string _plannedAccountType;
    private decimal _annualDividendGrowthRate;
    private string _purchaseMode;
    private string _inputError = string.Empty;
    private readonly IReadOnlyList<DividendGrowthPlanItem> _components;

    public DividendSimulationRowViewModel(DividendGrowthPlanItem item, Action changed)
    {
        _changed = changed;
        PlanKey = item.PlanKey;
        CanonicalKey = item.CanonicalKey;
        PositionKey = item.PositionKey;
        IsNewStock = item.IsNewStock;
        _ticker = item.Ticker;
        _name = item.Name;
        _currency = string.IsNullOrWhiteSpace(item.Currency) ? "JPY" : item.Currency;
        _broker = DividendSimulationSelectionOptions.NormalizeBroker(item.Broker);
        _accountType = NormalizeAccountType(item.AccountType);
        _country = DividendSimulationSelectionOptions.NormalizeCountry(item.Country, _currency, item.Ticker);
        _currentShares = item.CurrentShares;
        _currentPrice = item.CurrentPrice;
        _exchangeRate = item.ExchangeRate <= 0m ? 1m : item.ExchangeRate;
        _annualDividendPerShare = item.AnnualDividendPerShare;
        _annualDividendSource = item.AnnualDividendSource;
        _marketDataSource = item.MarketDataSource;
        _marketDataAcquiredAt = item.MarketDataAcquiredAt;
        _marketDataFetchStatus = string.IsNullOrWhiteSpace(item.MarketDataStatus)
            ? (item.IsNewStock ? "手入力" : string.Empty)
            : item.MarketDataStatus;
        _plannedAdditionalShares = item.PlannedAdditionalShares;
        _plannedBroker = DividendSimulationSelectionOptions.NormalizeBroker(FirstNonBlank(item.PlannedBroker, _broker));
        _plannedAccountType = NormalizeAccountType(FirstNonBlank(item.PlannedAccountType, _accountType));
        _annualDividendGrowthRate = item.AnnualDividendGrowthRate;
        _purchaseMode = item.PurchaseMode;
        _components = item.Components;
        BrokerOptions = DividendSimulationSelectionOptions.BuildBrokerOptions(_broker, _plannedBroker);
        CountryOptions = DividendSimulationSelectionOptions.CountryOptions;
        CurrencyOptions = DividendSimulationSelectionOptions.CurrencyOptions;
        AccountOptions = DividendSimulationSelectionOptions.AccountOptions;
        PurchaseModeOptions = DividendSimulationSelectionOptions.PurchaseModeOptions;
    }

    public string PlanKey { get; }
    public string CanonicalKey { get; }
    public string PositionKey { get; }
    public bool IsNewStock { get; }
    public IReadOnlyList<SelectionOption<string>> BrokerOptions { get; }
    public IReadOnlyList<SelectionOption<string>> CountryOptions { get; }
    public IReadOnlyList<string> CurrencyOptions { get; }
    public IReadOnlyList<SelectionOption<string>> AccountOptions { get; }
    public IReadOnlyList<SelectionOption<string>> PurchaseModeOptions { get; }

    public string Ticker
    {
        get => _ticker;
        set => SetAndNotify(ref _ticker, value);
    }

    public string Name
    {
        get => _name;
        set => SetAndNotify(ref _name, value);
    }

    public string Broker
    {
        get => _broker;
        set => SetAndNotify(ref _broker, DividendSimulationSelectionOptions.NormalizeBroker(value));
    }

    public string AccountType
    {
        get => _accountType;
        set => SetAndNotify(ref _accountType, NormalizeAccountType(value));
    }

    public string AccountTypeDisplay => AccountTypes.DisplayName(AccountType);

    public string Country
    {
        get => _country;
        set => SetAndNotify(ref _country, DividendSimulationSelectionOptions.NormalizeCountry(value, Currency, Ticker));
    }

    public string Currency
    {
        get => _currency;
        set => SetAndNotify(ref _currency, string.IsNullOrWhiteSpace(value) ? "JPY" : value.Trim().ToUpperInvariant());
    }

    public decimal CurrentShares
    {
        get => _currentShares;
        set => SetNonNegative(ref _currentShares, value, "保有株数");
    }

    public decimal CurrentPrice
    {
        get => _currentPrice;
        set => SetNonNegative(ref _currentPrice, value, "現在株価");
    }

    public decimal ExchangeRate
    {
        get => _exchangeRate;
        set => SetAndNotify(ref _exchangeRate, value <= 0m ? 1m : value);
    }

    public decimal AnnualDividendPerShare
    {
        get => _annualDividendPerShare;
        set => SetNonNegative(ref _annualDividendPerShare, value, "1株年間配当");
    }

    public string AnnualDividendSource
    {
        get => _annualDividendSource;
        set => SetAndNotify(ref _annualDividendSource, value);
    }

    public string MarketDataSource
    {
        get => _marketDataSource;
        private set => SetProperty(ref _marketDataSource, value);
    }

    public DateTime? MarketDataAcquiredAt
    {
        get => _marketDataAcquiredAt;
        private set => SetProperty(ref _marketDataAcquiredAt, value);
    }

    public string MarketDataFetchStatus
    {
        get => _marketDataFetchStatus;
        private set => SetProperty(ref _marketDataFetchStatus, value);
    }

    public bool IsMarketDataFetching
    {
        get => _isMarketDataFetching;
        private set
        {
            if (SetProperty(ref _isMarketDataFetching, value))
            {
                OnPropertyChanged(nameof(CanFetchMarketData));
            }
        }
    }

    public bool CanFetchMarketData => !IsMarketDataFetching;

    public decimal PlannedAdditionalShares
    {
        get => _plannedAdditionalShares;
        set => SetNonNegative(ref _plannedAdditionalShares, value, IsNewStock ? "予定株数" : "買い増し株数");
    }

    public string PlannedBroker
    {
        get => _plannedBroker;
        set => SetAndNotify(ref _plannedBroker, DividendSimulationSelectionOptions.NormalizeBroker(value));
    }

    public string PlannedAccountType
    {
        get => _plannedAccountType;
        set => SetAndNotify(ref _plannedAccountType, NormalizeAccountType(value));
    }

    public string PlannedAccountTypeDisplay => AccountTypes.DisplayName(PlannedAccountType);

    public decimal AnnualDividendGrowthRate
    {
        get => _annualDividendGrowthRate;
        set => SetAndNotify(ref _annualDividendGrowthRate, Math.Max(-99m, value));
    }

    public string PurchaseMode
    {
        get => _purchaseMode;
        set => SetAndNotify(ref _purchaseMode, string.IsNullOrWhiteSpace(value) ? DividendGrowthPurchaseModes.OneTime : value);
    }

    public string CurrentAnnualDividendPreview => FormatMoney(CurrentShares * AnnualDividendPerShare);
    public string AdditionalAnnualDividendPreview => Formatters.Jpy(PlannedAdditionalShares * AnnualDividendPerShare * EffectiveExchangeRate);
    public string PostAddSharesPreview => $"{CurrentShares + PlannedAdditionalShares:N2}";
    public string PlannedInvestmentPreview => Formatters.Jpy(PlannedAdditionalShares * CurrentPrice * EffectiveExchangeRate);
    public string PostAddAnnualDividendPreview => FormatMoney((CurrentShares + PlannedAdditionalShares) * AnnualDividendPerShare);
    public string InputError
    {
        get => _inputError;
        private set => SetProperty(ref _inputError, value);
    }

    public DividendGrowthPlanItem ToPlanItem() =>
        new()
        {
            PlanKey = PlanKey,
            CanonicalKey = string.IsNullOrWhiteSpace(CanonicalKey) ? Ticker.Trim() : CanonicalKey,
            PositionKey = PositionKey,
            Ticker = Ticker.Trim(),
            Name = Name.Trim(),
            Broker = Broker.Trim(),
            AccountType = AccountType,
            Country = Country.Trim(),
            Currency = Currency,
            CurrentShares = CurrentShares,
            CurrentPrice = CurrentPrice,
            ExchangeRate = EffectiveExchangeRate,
            AnnualDividendPerShare = AnnualDividendPerShare,
            AnnualDividendSource = AnnualDividendSource.Trim(),
            MarketDataSource = MarketDataSource.Trim(),
            MarketDataAcquiredAt = MarketDataAcquiredAt,
            MarketDataStatus = MarketDataFetchStatus.Trim(),
            PlannedAdditionalShares = PlannedAdditionalShares,
            PlannedBroker = string.IsNullOrWhiteSpace(PlannedBroker) ? Broker.Trim() : PlannedBroker.Trim(),
            PlannedAccountType = string.IsNullOrWhiteSpace(PlannedAccountType) ? AccountType : PlannedAccountType,
            AnnualDividendGrowthRate = AnnualDividendGrowthRate,
            PurchaseMode = PurchaseMode,
            IsNewStock = IsNewStock,
            Components = _components
        };

    public void BeginMarketDataFetch()
    {
        IsMarketDataFetching = true;
        MarketDataFetchStatus = "取得中";
        InputError = string.Empty;
    }

    public void ApplyMarketData(DividendStockMarketDataFetchResult result)
    {
        IsMarketDataFetching = false;

        if (!string.IsNullOrWhiteSpace(result.Ticker))
        {
            Ticker = result.Ticker;
        }

        if (!string.IsNullOrWhiteSpace(result.Name))
        {
            Name = result.Name;
        }

        if (!string.IsNullOrWhiteSpace(result.Country))
        {
            Country = result.Country;
        }

        if (!string.IsNullOrWhiteSpace(result.Currency))
        {
            Currency = result.Currency;
        }

        if (result.CurrentPrice is > 0m)
        {
            CurrentPrice = result.CurrentPrice.Value;
        }

        if (result.ExchangeRate is > 0m)
        {
            ExchangeRate = result.ExchangeRate.Value;
        }

        if (result.AnnualDividendPerShare is >= 0m)
        {
            AnnualDividendPerShare = result.AnnualDividendPerShare.Value;
        }

        if (!string.IsNullOrWhiteSpace(result.DividendSource))
        {
            AnnualDividendSource = result.DividendSource;
        }

        MarketDataSource = result.Source;
        MarketDataAcquiredAt = result.AcquiredAt;
        MarketDataFetchStatus = result.Status;
        InputError = result.IsSuccess ? string.Empty : result.Status;
    }

    public void SetMarketDataFailure(string message)
    {
        IsMarketDataFetching = false;
        MarketDataFetchStatus = string.IsNullOrWhiteSpace(message) ? "取得失敗" : message;
        InputError = MarketDataFetchStatus;
    }

    private decimal EffectiveExchangeRate =>
        string.Equals(Currency, "JPY", StringComparison.OrdinalIgnoreCase) ? 1m : ExchangeRate <= 0m ? 1m : ExchangeRate;

    private string FormatMoney(decimal value) => Formatters.Money(value, Currency);

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string NormalizeAccountType(string? value)
    {
        var normalized = AccountTypeNormalizer.Normalize(value ?? string.Empty);
        return string.Equals(normalized, "NISA", StringComparison.OrdinalIgnoreCase)
            ? AccountTypes.NisaLegacy
            : normalized;
    }

    private void SetNonNegative(ref decimal field, decimal value, string label)
    {
        if (value < 0m)
        {
            InputError = $"{label}は0以上で入力してください。";
            SetAndNotify(ref field, 0m);
            return;
        }

        if (!string.IsNullOrWhiteSpace(InputError) && InputError.Contains(label, StringComparison.Ordinal))
        {
            InputError = string.Empty;
        }

        SetAndNotify(ref field, value);
    }

    private void SetAndNotify<T>(ref T field, T value)
    {
        if (!SetProperty(ref field, value))
        {
            return;
        }

        OnPropertyChanged(nameof(AccountTypeDisplay));
        OnPropertyChanged(nameof(PlannedAccountTypeDisplay));
        OnPropertyChanged(nameof(CurrentAnnualDividendPreview));
        OnPropertyChanged(nameof(AdditionalAnnualDividendPreview));
        OnPropertyChanged(nameof(PostAddSharesPreview));
        OnPropertyChanged(nameof(PlannedInvestmentPreview));
        OnPropertyChanged(nameof(PostAddAnnualDividendPreview));
        _changed();
    }
}

public sealed class DividendProjectionRowViewModel
{
    public DividendProjectionRowViewModel(DividendGrowthProjection projection)
    {
        Year = projection.Year.ToString();
        YearsFromNow = projection.YearsFromNow == 0 ? "初年度" : $"{projection.YearsFromNow:N0}年後";
        CurrentOnlyNetDividend = Formatters.Jpy(projection.CurrentOnlyNetDividendJpy);
        PlannedNetDividend = Formatters.Jpy(projection.PlannedNetDividendJpy);
        MonthlyAverageNetDividend = Formatters.Jpy(projection.MonthlyAverageNetDividendJpy);
        TargetAnnualDividend = Formatters.Jpy(projection.TargetAnnualDividendJpy);
        TargetAchievementRate = Formatters.Percent(projection.TargetAchievementRate);
    }

    public string Year { get; }
    public string YearsFromNow { get; }
    public string CurrentOnlyNetDividend { get; }
    public string PlannedNetDividend { get; }
    public string MonthlyAverageNetDividend { get; }
    public string TargetAnnualDividend { get; }
    public string TargetAchievementRate { get; }
}

public sealed class DisplayOptionViewModel
{
    public DisplayOptionViewModel(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }
    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
