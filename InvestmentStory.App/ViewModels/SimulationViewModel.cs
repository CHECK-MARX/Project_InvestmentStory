using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
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
    private readonly DividendPurchasePlanSimulationService _dividendPurchasePlanService = new();
    private readonly IMarketDataService _marketDataService;
    private readonly IStockLookupService _stockLookupService;
    private readonly IFundMarketDataService? _fundMarketDataService;
    private readonly Func<AppSettings> _loadSettings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly Action<int>? _openStockDetail;
    private static readonly TimeSpan NewDividendStockFetchTimeout = TimeSpan.FromSeconds(20);
    private IReadOnlyList<StockPosition> _positions = Array.Empty<StockPosition>();
    private IReadOnlyList<BrokerTrade> _brokerTrades = Array.Empty<BrokerTrade>();
    private IReadOnlyList<TaxProfile> _taxProfiles = Array.Empty<TaxProfile>();
    private IReadOnlyList<DividendPayment> _dividendPayments = Array.Empty<DividendPayment>();
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
    private int _dividendPlanTargetYear = DateTime.Today.Year;
    private DateTime _dividendPlanPurchaseDate = DateTime.Today;
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
    private string _passiveIncomeMissedDividend = "0円";
    private string _passiveIncomeNextYearDividend = "0円";
    private string _passiveIncomeCurrentYield = "0.00%";
    private string _passiveIncomeYieldOnCost = "0.00%";
    private string _passiveIncomePostAddPortfolioYield = "0.00%";
    private string _passiveIncomePaybackYears = "対象外";
    private decimal _passiveIncomeCurrentAnnualValue;
    private decimal _passiveIncomePlannedAnnualValue;
    private string _dividendStrategyCommentTitle = "購入計画を入力してください";
    private string _dividendStrategyComment = "買い増し株数または新規購入予定を入力すると、配当戦略を分析します。";
    private string _dividendStrategyRating = "-";
    private DividendCalendarEventViewModel? _selectedDividendCalendarEvent;
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
    private string _actualAnnualizedReturnBasis = "算出不可";
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
    private string _actualAnnualizedReturnMethod = "算出不可";
    private string _actualAnnualizedReturnPeriod = "-";
    private string _actualAnnualizedReturnPrecision = "-";
    private string _actualAnnualizedReturnNote = string.Empty;
    private MutualFundScenarioComparisonResult? _lastScenarioResult;
    private DisplayOptionViewModel? _selectedStoryScenario;
    private string _targetAsset = "0円";
    private string _goalRemaining = "0円";
    private string _currentMonthlyContribution = "0円";
    private string _selectedScenarioReturn = "標準 5.00%";
    private string _storyTargetDate = "未達";
    private string _storyRemainingPeriod = "未達";
    private string _motivationHeadline = "条件を入力して未来の資産ストーリーを確認します。";
    private string _motivationDetail = string.Empty;
    private string _chartHorizon = string.Empty;

    public SimulationViewModel(
        InvestmentCalculator calculator,
        MutualFundAssetSimulationService simulationService,
        Func<AppSettings> loadSettings,
        Action<AppSettings> saveSettings,
        IMarketDataService? marketDataService = null,
        IStockLookupService? stockLookupService = null,
        IFundMarketDataService? fundMarketDataService = null,
        Action<int>? openStockDetail = null)
    {
        _calculator = calculator;
        _simulationService = simulationService;
        _marketDataService = marketDataService ?? new MarketDataProviderFactory();
        _stockLookupService = stockLookupService ?? new CompositeStockLookupService(
            new LocalStockLookupService(),
            new YahooFinanceStockLookupService());
        _fundMarketDataService = fundMarketDataService;
        _openStockDetail = openStockDetail;
        _loadSettings = loadSettings;
        _saveSettings = saveSettings;
        var settings = _loadSettings();
        _monthlyContributionJpy = settings.MutualFundSimulationMonthlyContributionJpy;
        _expectedAnnualReturnRate = settings.MutualFundSimulationExpectedAnnualReturnRate;
        _targetAmountJpy = settings.MutualFundSimulationTargetAmountJpy;
        _projectionYears = settings.MutualFundSimulationProjectionYears;
        _targetYears = settings.MutualFundSimulationTargetYears;
        _passiveIncomePlanName = string.IsNullOrWhiteSpace(settings.DividendSimulationPlanName) ? "Default" : settings.DividendSimulationPlanName;
        _passiveIncomeDisplayMode = NormalizeDividendPlanDisplayUnit(settings.DividendSimulationDisplayMode);
        _targetAnnualPassiveIncome = settings.DividendSimulationTargetAnnualDividendJpy <= 0m
            ? 1_200_000m
            : settings.DividendSimulationTargetAnnualDividendJpy;
        _passiveIncomeProjectionYears = settings.DividendSimulationProjectionYears <= 0 ? 10 : settings.DividendSimulationProjectionYears;
        _dividendPlanTargetYear = settings.DividendSimulationTargetYear is >= 2000 and <= 2200
            ? settings.DividendSimulationTargetYear
            : DateTime.Today.Year;
        _dividendPlanPurchaseDate = DateTime.TryParse(settings.DividendSimulationPlannedPurchaseDate, out var savedPurchaseDate)
            ? savedPurchaseDate.Date
            : DateTime.Today;

        RunCommand = new RelayCommand(RunMutualFundSimulation);
        RunPassiveIncomeCommand = new RelayCommand(RunPassiveIncomeSimulation);
        SavePassiveIncomePlanCommand = new RelayCommand(SavePassiveIncomePlan);
        AddNewDividendStockCommand = new RelayCommand(AddNewDividendStock);
        RemoveSelectedNewDividendStockCommand = new RelayCommand(RemoveSelectedNewDividendStock, () => SelectedNewDividendSimulationRow is not null);
        FetchNewDividendStockCommand = new RelayCommand(parameter => FetchNewDividendStock(parameter as DividendSimulationRowViewModel));
        OpenDividendCalendarStockCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is int stockId && stockId > 0)
                {
                    _openStockDetail?.Invoke(stockId);
                }
            },
            parameter => parameter is int stockId && stockId > 0 && _openStockDetail is not null);
        SelectDividendCalendarEventCommand = new RelayCommand(
            parameter => SelectedDividendCalendarEvent = parameter as DividendCalendarEventViewModel);
        RunTsumitateNisaCommand = RunCommand;
        InitializeScenarioSettings();
        InitializeStoryScenarioOptions();
    }

    public ObservableCollection<SimulationProjectionRowViewModel> Projections { get; } = new();
    public ObservableCollection<PriceChartPointViewModel> PassiveIncomeTrendPoints { get; } = new();
    public ObservableCollection<DividendSimulationRowViewModel> DividendSimulationRows { get; } = new();
    public ObservableCollection<DividendSimulationRowViewModel> NewDividendSimulationRows { get; } = new();
    public ObservableCollection<DividendProjectionRowViewModel> DividendProjectionRows { get; } = new();
    public ObservableCollection<DividendPlanMonthlyRowViewModel> DividendPlanMonthlyRows { get; } = new();
    public ObservableCollection<DividendPlanRankingRowViewModel> DividendIncreaseRankingRows { get; } = new();
    public ObservableCollection<DividendPlanRankingRowViewModel> DividendInvestmentRankingRows { get; } = new();
    public ObservableCollection<DividendPlanCompositionRowViewModel> DividendCompositionRows { get; } = new();
    public ObservableCollection<DividendPlanStockMonthlyRowViewModel> DividendStockMonthlyRows { get; } = new();
    public ObservableCollection<DividendCalendarMonthViewModel> DividendCalendarMonths { get; } = new();
    public ObservableCollection<DisplayOptionViewModel> PassiveIncomeDisplayModes { get; } = new()
    {
        new(DividendPurchasePlanDisplayUnits.AllAccounts, "全口座"),
        new(DividendPurchasePlanDisplayUnits.Broker, "証券会社別"),
        new(DividendPurchasePlanDisplayUnits.Account, "口座別")
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
    public ObservableCollection<MutualFundScenarioSettingViewModel> ScenarioSettings { get; } = new();
    public ObservableCollection<MutualFundScenarioComparisonRowViewModel> ScenarioComparisonRows { get; } = new();
    public ObservableCollection<MutualFundScenarioMonthlyRowViewModel> ScenarioMonthlyRows { get; } = new();
    public ObservableCollection<MutualFundScenarioMonthlyRowViewModel> ScenarioAnnualRows { get; } = new();
    public ObservableCollection<MutualFundScenarioChartSeriesViewModel> ScenarioChartSeries { get; } = new();
    public ObservableCollection<MutualFundScenarioRankingRowViewModel> ScenarioRankingRows { get; } = new();
    public ObservableCollection<DisplayOptionViewModel> StoryScenarioOptions { get; } = new();
    public ICommand RunCommand { get; }
    public ICommand RunPassiveIncomeCommand { get; }
    public ICommand SavePassiveIncomePlanCommand { get; }
    public ICommand AddNewDividendStockCommand { get; }
    public ICommand RemoveSelectedNewDividendStockCommand { get; }
    public ICommand FetchNewDividendStockCommand { get; }
    public ICommand OpenDividendCalendarStockCommand { get; }
    public ICommand SelectDividendCalendarEventCommand { get; }
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

    public int DividendPlanTargetYear
    {
        get => _dividendPlanTargetYear;
        set
        {
            var normalized = Math.Clamp(value, 2000, 2200);
            if (!SetProperty(ref _dividendPlanTargetYear, normalized))
            {
                return;
            }

            var day = Math.Min(DividendPlanPurchaseDate.Day, DateTime.DaysInMonth(normalized, DividendPlanPurchaseDate.Month));
            _dividendPlanPurchaseDate = new DateTime(normalized, DividendPlanPurchaseDate.Month, day);
            OnPropertyChanged(nameof(DividendPlanPurchaseDate));
            if (!_suppressDividendAutoUpdates)
            {
                RunPassiveIncomeSimulation(saveSettings: false);
            }
        }
    }

    public DateTime DividendPlanPurchaseDate
    {
        get => _dividendPlanPurchaseDate;
        set
        {
            if (!SetProperty(ref _dividendPlanPurchaseDate, value.Date))
            {
                return;
            }

            if (_dividendPlanTargetYear != value.Year)
            {
                _dividendPlanTargetYear = value.Year;
                OnPropertyChanged(nameof(DividendPlanTargetYear));
            }
            if (!_suppressDividendAutoUpdates)
            {
                RunPassiveIncomeSimulation(saveSettings: false);
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

    public string PassiveIncomeMissedDividend
    {
        get => _passiveIncomeMissedDividend;
        private set => SetProperty(ref _passiveIncomeMissedDividend, value);
    }

    public string PassiveIncomeNextYearDividend
    {
        get => _passiveIncomeNextYearDividend;
        private set => SetProperty(ref _passiveIncomeNextYearDividend, value);
    }

    public string PassiveIncomeCurrentYield
    {
        get => _passiveIncomeCurrentYield;
        private set => SetProperty(ref _passiveIncomeCurrentYield, value);
    }

    public string PassiveIncomeYieldOnCost
    {
        get => _passiveIncomeYieldOnCost;
        private set => SetProperty(ref _passiveIncomeYieldOnCost, value);
    }

    public string PassiveIncomePostAddPortfolioYield
    {
        get => _passiveIncomePostAddPortfolioYield;
        private set => SetProperty(ref _passiveIncomePostAddPortfolioYield, value);
    }

    public string PassiveIncomePaybackYears
    {
        get => _passiveIncomePaybackYears;
        private set => SetProperty(ref _passiveIncomePaybackYears, value);
    }

    public decimal PassiveIncomeCurrentAnnualValue
    {
        get => _passiveIncomeCurrentAnnualValue;
        private set => SetProperty(ref _passiveIncomeCurrentAnnualValue, value);
    }

    public decimal PassiveIncomePlannedAnnualValue
    {
        get => _passiveIncomePlannedAnnualValue;
        private set => SetProperty(ref _passiveIncomePlannedAnnualValue, value);
    }

    public string DividendStrategyCommentTitle
    {
        get => _dividendStrategyCommentTitle;
        private set => SetProperty(ref _dividendStrategyCommentTitle, value);
    }

    public string DividendStrategyComment
    {
        get => _dividendStrategyComment;
        private set => SetProperty(ref _dividendStrategyComment, value);
    }

    public string DividendStrategyRating
    {
        get => _dividendStrategyRating;
        private set => SetProperty(ref _dividendStrategyRating, value);
    }

    public DividendCalendarEventViewModel? SelectedDividendCalendarEvent
    {
        get => _selectedDividendCalendarEvent;
        private set => SetProperty(ref _selectedDividendCalendarEvent, value);
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

    public string ActualAnnualizedReturnBasis
    {
        get => _actualAnnualizedReturnBasis;
        private set => SetProperty(ref _actualAnnualizedReturnBasis, value);
    }

    public string ActualAnnualizedReturnMethod
    {
        get => _actualAnnualizedReturnMethod;
        private set => SetProperty(ref _actualAnnualizedReturnMethod, value);
    }

    public string ActualAnnualizedReturnPeriod
    {
        get => _actualAnnualizedReturnPeriod;
        private set => SetProperty(ref _actualAnnualizedReturnPeriod, value);
    }

    public string ActualAnnualizedReturnPrecision
    {
        get => _actualAnnualizedReturnPrecision;
        private set => SetProperty(ref _actualAnnualizedReturnPrecision, value);
    }

    public string ActualAnnualizedReturnNote
    {
        get => _actualAnnualizedReturnNote;
        private set => SetProperty(ref _actualAnnualizedReturnNote, value);
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

    public DisplayOptionViewModel? SelectedStoryScenario
    {
        get => _selectedStoryScenario;
        set
        {
            if (SetProperty(ref _selectedStoryScenario, value) && _lastScenarioResult is not null)
            {
                UpdateFocusedScenario(_lastScenarioResult);
            }
        }
    }

    public string TargetAsset
    {
        get => _targetAsset;
        private set => SetProperty(ref _targetAsset, value);
    }

    public string GoalRemaining
    {
        get => _goalRemaining;
        private set => SetProperty(ref _goalRemaining, value);
    }

    public string CurrentMonthlyContribution
    {
        get => _currentMonthlyContribution;
        private set => SetProperty(ref _currentMonthlyContribution, value);
    }

    public string SelectedScenarioReturn
    {
        get => _selectedScenarioReturn;
        private set => SetProperty(ref _selectedScenarioReturn, value);
    }

    public string StoryTargetDate
    {
        get => _storyTargetDate;
        private set => SetProperty(ref _storyTargetDate, value);
    }

    public string StoryRemainingPeriod
    {
        get => _storyRemainingPeriod;
        private set => SetProperty(ref _storyRemainingPeriod, value);
    }

    public string MotivationHeadline
    {
        get => _motivationHeadline;
        private set => SetProperty(ref _motivationHeadline, value);
    }

    public string MotivationDetail
    {
        get => _motivationDetail;
        private set => SetProperty(ref _motivationDetail, value);
    }

    public string ChartHorizon
    {
        get => _chartHorizon;
        private set => SetProperty(ref _chartHorizon, value);
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

    public void UpdateMutualFundPortfolio(IReadOnlyList<StockPosition> positions, IReadOnlyList<BrokerTrade>? brokerTrades = null)
    {
        _positions = positions;
        _brokerTrades = brokerTrades ?? Array.Empty<BrokerTrade>();
        RebuildScopeOptions();
        RunMutualFundSimulation(saveSettings: false);
        RebuildDividendPlanRows();
        RunPassiveIncomeSimulation(saveSettings: false);
    }

    public void UpdateDividendPortfolio(
        IReadOnlyList<StockPosition> positions,
        IReadOnlyList<TaxProfile> taxProfiles,
        IReadOnlyList<DividendPayment>? dividendPayments = null)
    {
        _positions = positions;
        _taxProfiles = taxProfiles;
        _dividendPayments = dividendPayments ?? Array.Empty<DividendPayment>();
        RebuildDividendPlanRows();
        RunPassiveIncomeSimulation(saveSettings: false);
    }

    private void RunPassiveIncomeSimulation()
    {
        RunPassiveIncomeSimulation(saveSettings: true);
    }

    private void RunPassiveIncomeSimulation(bool saveSettings)
    {
        if (TargetAnnualPassiveIncome < 0m)
        {
            PassiveIncomeResultSummary = "目標年間配当は0円以上で入力してください。";
            return;
        }

        var result = _dividendPurchasePlanService.Simulate(
            new DividendPurchasePlanInput
            {
                PlanName = PassiveIncomePlanName,
                TargetYear = DividendPlanTargetYear,
                PlannedPurchaseDate = DividendPlanPurchaseDate,
                DisplayUnit = PassiveIncomeDisplayMode,
                TargetAnnualNetDividendJpy = TargetAnnualPassiveIncome,
                PlanItems = BuildDividendPlanItems(),
                DividendPayments = _dividendPayments
            },
            _taxProfiles);
        ApplyDividendPurchasePlanResult(result);

        if (saveSettings)
        {
            SavePassiveIncomePlan();
        }
    }

    private void ApplyDividendPurchasePlanResult(DividendPurchasePlanResult result)
    {
        var summary = result.Summary;
        PassiveIncomeCurrentGrossDividend = Formatters.Jpy(summary.CurrentTargetYearNetDividendJpy);
        PassiveIncomeCurrentNetDividend = Formatters.Jpy(summary.CurrentTargetYearNetDividendJpy);
        PassiveIncomePostAddGrossDividend = Formatters.Jpy(summary.PlannedTargetYearNetDividendJpy);
        PassiveIncomePostAddNetDividend = Formatters.Jpy(summary.PlannedTargetYearNetDividendJpy);
        PassiveIncomeExistingPlannedInvestment = Formatters.Jpy(result.Holdings.Where(x => !x.IsNewStock).Sum(x => x.PlannedPurchaseAmountJpy));
        PassiveIncomeNewPlannedInvestment = Formatters.Jpy(result.Holdings.Where(x => x.IsNewStock).Sum(x => x.PlannedPurchaseAmountJpy));
        PassiveIncomeTotalPlannedInvestment = Formatters.Jpy(summary.PlannedInvestmentJpy);
        PassiveIncomeDividendIncrease = Formatters.SignedJpy(summary.TargetYearDividendIncreaseJpy);
        PassiveIncomeNetDividendIncrease = Formatters.SignedJpy(summary.TargetYearDividendIncreaseJpy);
        PassiveIncomeMonthlyNetDividend = Formatters.Jpy(summary.PlannedTargetYearNetDividendJpy / 12m);
        PassiveIncomeInvestmentYield = Formatters.Percent(summary.AdditionalInvestmentYieldRate);
        PassiveIncomeGoalAchievement = Formatters.Percent(result.Summary.TargetAchievementRate);
        PassiveIncomeGoalGap = Formatters.Jpy(Math.Max(0m, TargetAnnualPassiveIncome - summary.NextYearAnnualNetDividendJpy));
        PassiveIncomeMissedDividend = Formatters.Jpy(summary.MissedTargetYearNetDividendJpy);
        PassiveIncomeNextYearDividend = Formatters.Jpy(summary.NextYearAnnualNetDividendJpy);
        PassiveIncomeCurrentYield = Formatters.Percent(summary.CurrentYieldRate);
        PassiveIncomeYieldOnCost = Formatters.Percent(summary.YieldOnCostRate);
        PassiveIncomePostAddPortfolioYield = Formatters.Percent(summary.PostAddPortfolioYieldRate);
        PassiveIncomePaybackYears = summary.AdditionalInvestmentPaybackYears <= 0m
            ? "対象外"
            : $"{summary.AdditionalInvestmentPaybackYears:N1}年";
        PassiveIncomeCurrentAnnualValue = summary.CurrentTargetYearNetDividendJpy;
        PassiveIncomePlannedAnnualValue = summary.NextYearAnnualNetDividendJpy;
        PassiveIncomeTaxSummary =
            $"外国税 {Formatters.Jpy(summary.ForeignTaxJpy)} / 国内税 {Formatters.Jpy(summary.DomesticTaxJpy)} / 合計 {Formatters.Jpy(summary.TotalTaxJpy)}";
        PassiveIncomeResultSummary =
            $"{DividendPlanTargetYear}年は購入予定日 {DividendPlanPurchaseDate:yyyy/MM/dd} と権利付き最終日から判定。" +
            $"今年の追加配当 {Formatters.SignedJpy(summary.TargetYearDividendIncreaseJpy)}、翌年通期 {Formatters.Jpy(summary.NextYearAnnualNetDividendJpy)}。";

        DividendProjectionRows.Clear();
        PassiveIncomeTrendPoints.Clear();
        DividendPlanMonthlyRows.Clear();
        foreach (var month in result.Months)
        {
            DividendPlanMonthlyRows.Add(new DividendPlanMonthlyRowViewModel(month));
        }

        DividendIncreaseRankingRows.Clear();
        foreach (var holding in result.Holdings.Where(x => x.NextYearAdditionalNetDividendJpy > 0m)
                     .OrderByDescending(x => x.NextYearAdditionalNetDividendJpy))
        {
            DividendIncreaseRankingRows.Add(new DividendPlanRankingRowViewModel(holding));
        }

        DividendInvestmentRankingRows.Clear();
        foreach (var holding in result.Holdings.Where(x => x.PlannedPurchaseAmountJpy > 0m)
                     .OrderByDescending(x => x.PlannedPurchaseAmountJpy))
        {
            DividendInvestmentRankingRows.Add(new DividendPlanRankingRowViewModel(holding));
        }

        DividendCompositionRows.Clear();
        foreach (var item in result.Composition)
        {
            DividendCompositionRows.Add(new DividendPlanCompositionRowViewModel(item));
        }

        DividendCalendarMonths.Clear();
        foreach (var month in result.Months)
        {
            DividendCalendarMonths.Add(new DividendCalendarMonthViewModel(month));
        }

        SelectedDividendCalendarEvent = null;

        DividendStockMonthlyRows.Clear();
        var eventsByTicker = result.Months
            .SelectMany(x => x.Events)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Ticker) ? x.Name : x.Ticker, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(x => x.CurrentNetDividendJpy + x.AdditionalNetDividendJpy));
        foreach (var group in eventsByTicker)
        {
            DividendStockMonthlyRows.Add(new DividendPlanStockMonthlyRowViewModel(
                group.Key,
                group.First().Name,
                group.ToList()));
        }

        var topContribution = result.Holdings
            .Where(x => x.NextYearAdditionalNetDividendJpy > 0m)
            .OrderByDescending(x => x.NextYearAdditionalNetDividendJpy)
            .FirstOrDefault();
        var strongestMonth = result.Months
            .OrderByDescending(x => x.AdditionalNetDividendJpy)
            .FirstOrDefault();
        DividendStrategyRating = summary.AdditionalInvestmentYieldRate switch
        {
            >= 5m => "★★★★★",
            >= 4m => "★★★★☆",
            >= 3m => "★★★☆☆",
            > 0m => "★★☆☆☆",
            _ => "-"
        };
        DividendStrategyCommentTitle = topContribution is null
            ? "購入予定はまだありません"
            : $"配当増加への最大貢献は {topContribution.Ticker}";
        DividendStrategyComment = topContribution is null
            ? "買い増し株数または新規購入予定を入力すると、投資額・今年の受取可否・翌年配当を同じ条件で比較できます。"
            : $"追加投資 {Formatters.Jpy(summary.PlannedInvestmentJpy)} により、今年は {Formatters.SignedJpy(summary.TargetYearDividendIncreaseJpy)}、" +
              $"翌年通期は {Formatters.Jpy(summary.NextYearAnnualNetDividendJpy)} の税引後配当見込みです。" +
              $"追加投資利回りは {Formatters.Percent(summary.AdditionalInvestmentYieldRate)}、回収年数は {PassiveIncomePaybackYears}。" +
              (strongestMonth is null || strongestMonth.AdditionalNetDividendJpy <= 0m
                  ? string.Empty
                  : $" 最も改善する月は {strongestMonth.Month}月（{Formatters.SignedJpy(strongestMonth.AdditionalNetDividendJpy)}）です。") +
              $" この分析は取得済み・過去実績・推定の配当日程を使用し、実際の入金額は証券会社実績を正とします。";

        var byKey = result.Holdings
            .GroupBy(x => x.PlanKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in DividendSimulationRows.Concat(NewDividendSimulationRows))
        {
            row.ApplyPlanResult(byKey.GetValueOrDefault(row.PlanKey));
        }

        PassiveIncomeTrendStatus = $"{DividendPlanTargetYear}年 月別税引後配当（現在保有・買い増し・新規購入）";
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
        var defaultRows = _dividendSimulationService.CreateDefaultPlanItems(
            _positions,
            PassiveIncomeDisplayMode switch
            {
                DividendPurchasePlanDisplayUnits.Broker => DividendGrowthDisplayModes.AggregateByBroker,
                DividendPurchasePlanDisplayUnits.Account => DividendGrowthDisplayModes.AggregateByAccount,
                _ => DividendGrowthDisplayModes.AggregateBySecurity
            });

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

        var query = MarketDataSymbolResolver.NormalizeInput(row.Ticker);
        if (string.IsNullOrWhiteSpace(query))
        {
            row.SetMarketDataFailure("コード／ティッカーを入力してください。");
            return;
        }

        if (!IsPlausibleMarketDataQuery(query))
        {
            row.SetMarketDataFailure("取得失敗: コード、ティッカー、会社名のいずれかを入力してください。");
            return;
        }

        var request = new DividendStockMarketDataFetchRequest(
            query,
            row.Ticker,
            row.Name,
            row.Country,
            row.Currency,
            row.ExchangeRate,
            row.AnnualDividendSource,
            BuildLiveMarketDataSettings(_loadSettings()));

        row.BeginMarketDataFetch();
        try
        {
            var fetchTask = Task.Run(() => FetchDividendStockMarketData(request));
            var completedTask = await Task.WhenAny(fetchTask, Task.Delay(NewDividendStockFetchTimeout));
            if (completedTask != fetchTask)
            {
                row.SetMarketDataFailure("取得失敗: データ取得がタイムアウトしました。入力内容を確認して再実行してください。");
                return;
            }

            var result = await fetchTask;
            row.ApplyMarketData(result);
        }
        catch (Exception ex)
        {
            row.SetMarketDataFailure($"取得失敗: {ex.Message}");
        }
        finally
        {
            if (row.IsMarketDataFetching)
            {
                row.SetMarketDataFailure("取得失敗: データ取得を完了できませんでした。");
            }

            RunPassiveIncomeSimulation(saveSettings: false);
        }
    }

    private DividendStockMarketDataFetchResult FetchDividendStockMarketData(DividendStockMarketDataFetchRequest request)
    {
        var lookup = _stockLookupService.Find(request.Query);
        var symbol = MarketDataSymbolResolver.ToProviderSymbol(FirstNonBlank(lookup?.Ticker, request.Query));
        var marketResult = _marketDataService.GetQuote(symbol, request.Settings);
        var quote = marketResult.Quote;
        var now = DateTime.Now;

        var ticker = MarketDataSymbolResolver.ToDisplaySymbol(FirstNonBlank(quote?.Symbol, lookup?.Ticker, symbol));

        var currency = MarketDataSymbolResolver.NormalizeCurrency(FirstNonBlank(quote?.Currency, lookup?.Currency, request.Currency));
        var country = MarketDataSymbolResolver.NormalizeCountry(FirstNonBlank(quote?.Country, lookup?.Country, request.Country), currency, ticker);
        var price = quote?.CurrentPrice ?? lookup?.CurrentPrice;
        var annualDividend = quote?.AnnualDividendPerShare ?? lookup?.AnnualDividendPerShare;
        var dividendFrequency = FirstNonBlank(quote?.DividendFrequency, lookup?.DividendFrequency);
        var exchangeRate = currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? 1m
            : quote?.UsdJpyRate ?? request.ExchangeRate;
        var source = FirstNonBlank(quote?.Source, lookup?.Source, marketResult.IsSuccess ? "MarketData" : string.Empty);
        var dividendSource = FirstNonBlank(quote?.DividendInfoSource, lookup?.Source, quote?.Source, "Manual");
        var name = FirstNonBlank(quote?.Name, lookup?.Name, request.Name);

        if (marketResult.IsSuccess && quote is not null)
        {
            var validatedQuote = new MarketDataQuote
            {
                Symbol = ticker,
                Name = name,
                Country = country,
                Currency = currency,
                Market = quote.Market,
                CurrentPrice = price,
                AnnualDividendPerShare = annualDividend,
                Source = source
            };
            var validation = MarketDataSymbolResolver.ValidateQuote(validatedQuote);
            var status = BuildMarketDataStatus(validation, source, now);
            return new DividendStockMarketDataFetchResult(
                validation.IsSuccess,
                validation.StatusKind,
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
                status,
                dividendFrequency,
                quote.DividendRecordDate,
                quote.ExDividendDate,
                quote.DividendPaymentStartDate);
        }

        if (lookup is not null)
        {
            var validation = MarketDataSymbolResolver.ValidateQuote(new MarketDataQuote
            {
                Symbol = ticker,
                Name = lookup.Name,
                Country = country,
                Currency = currency,
                Market = lookup.Market,
                CurrentPrice = price,
                AnnualDividendPerShare = annualDividend,
                Source = lookup.Source
            });
            return new DividendStockMarketDataFetchResult(
                validation.IsSuccess,
                validation.StatusKind,
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
                BuildMarketDataStatus(validation, lookup.Source, now),
                dividendFrequency);
        }

        return new DividendStockMarketDataFetchResult(
            false,
            MarketDataFetchStatusKinds.Failed,
            request.CurrentTicker,
            request.Name,
            request.Country,
            request.Currency,
            null,
            null,
            null,
            request.AnnualDividendSource,
            string.Empty,
            now,
            BuildMarketDataFailureStatus(marketResult.ErrorMessage));
    }

    private static string BuildMarketDataStatus(MarketDataQuoteValidation validation, string source, DateTime now)
    {
        var sourceText = string.IsNullOrWhiteSpace(source) ? string.Empty : $" / {source}";
        if (validation.IsSuccess)
        {
            return $"取得成功 {now:yyyy/MM/dd HH:mm}{sourceText} / {validation.Message}";
        }

        if (validation.IsPartial)
        {
            return $"一部取得 {now:yyyy/MM/dd HH:mm}{sourceText} / {validation.Message}";
        }

        return $"取得失敗 {now:yyyy/MM/dd HH:mm} / {validation.Message}";
    }

    private static string BuildMarketDataFailureStatus(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "取得失敗: 銘柄情報を取得できませんでした。手入力してください。";
        }

        if (errorMessage.Contains("Alpha Vantage API Key", StringComparison.OrdinalIgnoreCase) &&
            errorMessage.Contains("フォールバック取得も失敗", StringComparison.OrdinalIgnoreCase))
        {
            return "取得失敗: 銘柄情報を取得できませんでした。コード／ティッカーを確認するか、手入力してください。";
        }

        return $"取得失敗: {errorMessage}";
    }

    private static bool IsPlausibleMarketDataQuery(string query) =>
        query.Length <= 40 && query.Any(char.IsLetterOrDigit);
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

    private void SavePassiveIncomePlan()
    {
        var settings = _loadSettings();
        settings.DividendSimulationPlanName = string.IsNullOrWhiteSpace(PassiveIncomePlanName) ? "Default" : PassiveIncomePlanName;
        settings.DividendSimulationDisplayMode = PassiveIncomeDisplayMode;
        settings.DividendSimulationTargetAnnualDividendJpy = TargetAnnualPassiveIncome;
        settings.DividendSimulationTargetYear = DividendPlanTargetYear;
        settings.DividendSimulationPlannedPurchaseDate = DividendPlanPurchaseDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
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
            !overrides.TryGetValue(source.PositionKey, out saved))
        {
            return source;
        }

        return new DividendGrowthPlanItem
        {
            StockId = source.StockId,
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
            CurrentCostJpy = source.CurrentCostJpy,
            CurrentMarketValueJpy = source.CurrentMarketValueJpy,
            DividendFrequency = string.IsNullOrWhiteSpace(saved.DividendFrequency) ? source.DividendFrequency : saved.DividendFrequency,
            DividendMonths = string.IsNullOrWhiteSpace(saved.DividendMonths) ? source.DividendMonths : saved.DividendMonths,
            DividendRecordDate = saved.DividendRecordDate ?? source.DividendRecordDate,
            ExDividendDate = saved.ExDividendDate ?? source.ExDividendDate,
            DividendPaymentDate = saved.DividendPaymentDate ?? source.DividendPaymentDate,
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

    private static string NormalizeDividendPlanDisplayUnit(string? value) =>
        value switch
        {
            DividendPurchasePlanDisplayUnits.Broker => DividendPurchasePlanDisplayUnits.Broker,
            DividendPurchasePlanDisplayUnits.Account => DividendPurchasePlanDisplayUnits.Account,
            _ => DividendPurchasePlanDisplayUnits.AllAccounts
        };

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
        _ = RunMutualFundSimulationAsync(saveSettings: true);
    }

    private void RunMutualFundSimulation(bool saveSettings)
    {
        _ = RunMutualFundSimulationAsync(saveSettings);
    }

    private async Task RunMutualFundSimulationAsync(bool saveSettings)
    {
        if (!ValidateMutualFundInputs())
        {
            return;
        }

        var result = SimulateScenarioComparison();
        var actualEstimate = result.Summary.ActualAnnualizedReturnEstimate ??
            await EstimateFundAnnualizedReturnFromNavHistoryAsync();
        _suppressMutualFundAutoUpdates = true;
        try
        {
            UpdateActualScenarioSetting(actualEstimate);
        }
        finally
        {
            _suppressMutualFundAutoUpdates = false;
        }

        result = SimulateScenarioComparison();
        ApplyScenarioResult(result, actualEstimate);
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

        foreach (var scenario in ScenarioSettings.Where(x => x.IsEnabled && x.IsEditableRate))
        {
            if (scenario.AnnualReturnRate <= -100m)
            {
                MutualFundInputError = $"{scenario.Name}の年利は-100%より大きい値で入力してください。";
                StatusMessage = MutualFundInputError;
                return false;
            }
        }

        MutualFundInputError = string.Empty;
        return true;
    }

    private MutualFundScenarioComparisonResult SimulateScenarioComparison() =>
        _simulationService.SimulateScenarios(
            _positions,
            SelectedScope?.Key ?? MutualFundSimulationScopeKeys.AllAccounts,
            new MutualFundSimulationInput
            {
                MonthlyContributionJpy = MonthlyContributionJpy,
                TargetAmountJpy = TargetAmountJpy,
                ProjectionYears = ProjectionYears,
                TargetYears = TargetYears,
                StartYear = DateTime.Today.Year,
                StartMonth = DateTime.Today.Month
            },
            BuildScenarioInputs(),
            _brokerTrades);

    private async Task<MutualFundActualAnnualizedReturnEstimate?> EstimateFundAnnualizedReturnFromNavHistoryAsync()
    {
        if (_fundMarketDataService is null)
        {
            return null;
        }

        var selectedPositions = _simulationService
            .SelectPositions(_positions, SelectedScope?.Key ?? MutualFundSimulationScopeKeys.AllAccounts)
            .ToList();
        if (selectedPositions.Count == 0 ||
            selectedPositions.Select(position => PositionIdentityService.ResolveSecurityId(position))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != 1)
        {
            return null;
        }

        var today = DateTime.Today;
        var from = today.AddYears(-30);
        foreach (var query in BuildFundHistoryQueries(selectedPositions[0]))
        {
            IReadOnlyList<FundNavHistoryPoint> points;
            try
            {
                points = await _fundMarketDataService.GetNavHistoryAsync(query, from, today);
            }
            catch
            {
                continue;
            }

            var ordered = points
                .Where(point => point.Nav > 0m)
                .OrderBy(point => point.Date)
                .ToList();
            if (ordered.Count < 2)
            {
                continue;
            }

            var first = ordered[0];
            var latest = ordered[^1];
            var years = (latest.Date - first.Date).TotalDays / 365.2425d;
            if (years <= 0d)
            {
                continue;
            }

            var annualized = (decimal)((Math.Pow((double)(latest.Nav / first.Nav), 1d / years) - 1d) * 100d);
            return new MutualFundActualAnnualizedReturnEstimate
            {
                AnnualizedReturnRate = annualized,
                DisplayName = "ファンド実績参考年利",
                Method = $"基準価額{FormatPeriodYears(years)}リターン",
                PeriodStart = first.Date,
                PeriodEnd = latest.Date,
                Precision = "ファンド実績",
                Note = "保有者本人の入出金実績ではなく、ファンドの基準価額履歴から算出しています。"
            };
        }

        return null;
    }

    private static IEnumerable<string> BuildFundHistoryQueries(StockPosition position)
    {
        var candidates = new[]
        {
            position.MutualFund.FundCode,
            position.MutualFund.AssociationCode,
            position.Stock.Ticker,
            position.MutualFund.FundName,
            position.Stock.Name
        };

        return candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatPeriodYears(double years) =>
        years >= 1d ? $"{Math.Round(years, 1):0.#}年" : $"{Math.Round(years * 12d, 0):0}か月";

    private IReadOnlyList<MutualFundScenarioInput> BuildScenarioInputs() =>
        ScenarioSettings
            .Select(setting => new MutualFundScenarioInput
            {
                Key = setting.Key,
                Name = setting.Name,
                AnnualReturnRate = setting.IsAvailable ? setting.AnnualReturnRate : null,
                IsEnabled = setting.IsEnabled && setting.IsAvailable,
                Basis = setting.Basis,
                UnavailableReason = setting.UnavailableReason
            })
            .ToList();

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
            },
            _brokerTrades);

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

        SelectedScope = ScopeOptions.FirstOrDefault(x => IsScopeOptionMatch(x, preferredKey))
            ?? ScopeOptions.FirstOrDefault(x => string.Equals(x.Key, MutualFundSimulationScopeKeys.AllAccounts, StringComparison.OrdinalIgnoreCase))
            ?? ScopeOptions.FirstOrDefault();
        _isRebuildingScopes = false;
    }

    private static bool IsScopeOptionMatch(SimulationScopeOptionViewModel option, string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        (string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(option.LegacyKey, key, StringComparison.OrdinalIgnoreCase));

    private void InitializeScenarioSettings()
    {
        ScenarioSettings.Clear();
        ScenarioSettings.Add(new MutualFundScenarioSettingViewModel("Conservative", "保守的", 3.00m, true, "固定シナリオ", OnMutualFundScenarioChanged));
        ScenarioSettings.Add(new MutualFundScenarioSettingViewModel("Standard", "標準", 5.00m, true, "固定シナリオ", OnMutualFundScenarioChanged));
        ScenarioSettings.Add(new MutualFundScenarioSettingViewModel("Aggressive", "積極的", 7.00m, true, "固定シナリオ", OnMutualFundScenarioChanged));
        ScenarioSettings.Add(new MutualFundScenarioSettingViewModel(
            "Actual",
            "実績参考",
            null,
            true,
            "算出不可",
            OnMutualFundScenarioChanged,
            "購入日・入出金・積立履歴が不足しているため、実績年利を算出できません。"));
    }

    private void InitializeStoryScenarioOptions()
    {
        StoryScenarioOptions.Clear();
        StoryScenarioOptions.Add(new DisplayOptionViewModel("Conservative", "保守的（3%）"));
        StoryScenarioOptions.Add(new DisplayOptionViewModel("Standard", "標準（5%）"));
        StoryScenarioOptions.Add(new DisplayOptionViewModel("Aggressive", "積極的（7%）"));
        StoryScenarioOptions.Add(new DisplayOptionViewModel("Actual", "実績参考（XIRR）"));
        SelectedStoryScenario = StoryScenarioOptions.First(x => x.Value == "Standard");
    }

    private void OnMutualFundScenarioChanged()
    {
        if (!_suppressMutualFundAutoUpdates)
        {
            RunMutualFundSimulation(saveSettings: false);
        }
    }

    private void UpdateActualScenarioSetting(MutualFundActualAnnualizedReturnEstimate? estimate)
    {
        var actual = ScenarioSettings.FirstOrDefault(x => x.Key == "Actual");
        if (actual is null)
        {
            return;
        }

        if (estimate is { } actualEstimate)
        {
            actual.SetAutoCalculatedRate(
                Math.Round(actualEstimate.AnnualizedReturnRate, 2, MidpointRounding.AwayFromZero),
                "保有期間年率換算",
                warning: Math.Abs(actualEstimate.AnnualizedReturnRate) >= 20m
                    ? "実績参考年利は過去の運用結果に基づく参考値です。将来の運用成果を保証するものではありません。"
                    : string.Empty);
            actual.SetBasis(actualEstimate.Method);
            return;
        }

        actual.SetUnavailable(
            "購入日・入出金・積立履歴が不足しているため、実績年利を算出できません。",
            "算出不可");
    }

    private void ApplyScenarioResult(
        MutualFundScenarioComparisonResult result,
        MutualFundActualAnnualizedReturnEstimate? actualEstimate = null)
    {
        _lastScenarioResult = result;
        var summary = result.Summary;
        IsMonthlyContributionEnabled = summary.AllowsMonthlyContribution;
        CurrentMarketValue = Formatters.Jpy(summary.CurrentMarketValueJpy);
        CurrentCost = Formatters.Jpy(summary.CurrentCostJpy);
        UnrealizedGain = Formatters.SignedJpy(summary.UnrealizedGainJpy);
        UnrealizedGainRate = Formatters.SignedPercent(summary.UnrealizedGainRate);
        ActualAnnualizedReturn = summary.ActualAnnualizedReturnRate is { } annualizedReturn
            ? Formatters.SignedPercent(annualizedReturn)
            : "算出不可";
        ActualAnnualizedReturnBasis = summary.ActualAnnualizedReturnRate is null ? "算出不可" : "保有期間年率換算";
        FundCount = $"{summary.FundCount:N0}件";
        PositionCount = $"{summary.PositionCount:N0}件";
        TargetAsset = Formatters.Jpy(TargetAmountJpy);
        GoalRemaining = Formatters.Jpy(Math.Max(0m, TargetAmountJpy - summary.CurrentMarketValueJpy));
        CurrentMonthlyContribution = summary.AllowsMonthlyContribution
            ? Formatters.Jpy(summary.EffectiveMonthlyContributionJpy)
            : "追加積立なし";

        actualEstimate ??= summary.ActualAnnualizedReturnEstimate;
        if (actualEstimate is not null)
        {
            ActualAnnualizedReturn = Formatters.SignedPercent(actualEstimate.AnnualizedReturnRate);
            ActualAnnualizedReturnBasis = actualEstimate.Method;
            ActualAnnualizedReturnMethod = actualEstimate.Method;
            ActualAnnualizedReturnPeriod = string.IsNullOrWhiteSpace(actualEstimate.PeriodDisplay) ? "-" : actualEstimate.PeriodDisplay;
            ActualAnnualizedReturnPrecision = string.IsNullOrWhiteSpace(actualEstimate.Precision) ? "-" : actualEstimate.Precision;
            ActualAnnualizedReturnNote = actualEstimate.Note;
        }
        else
        {
            ActualAnnualizedReturnMethod = "算出不可";
            ActualAnnualizedReturnPeriod = "-";
            ActualAnnualizedReturnPrecision = "-";
            ActualAnnualizedReturnNote = "購入日・入出金・積立履歴・基準価額履歴が不足しているため、実績年利を算出できません。";
        }

        AccountBreakdowns.Clear();
        foreach (var breakdown in result.AccountBreakdowns)
        {
            AccountBreakdowns.Add(new MutualFundAccountBreakdownRowViewModel(breakdown));
        }

        SelectedScopeSummary = BuildSelectedScopeSummary(result);
        ContributionNotice = BuildContributionNotice(result);
        RaiseProjectionLabelChanges();

        ScenarioComparisonRows.Clear();
        var chartFinalValues = result.Scenarios
            .Where(x => x.IsAvailable)
            .ToDictionary(x => x.Key, x => x.ChartFinalMarketValueJpy, StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in result.Scenarios)
        {
            ScenarioComparisonRows.Add(new MutualFundScenarioComparisonRowViewModel(scenario, ProjectionYears, chartFinalValues));
        }

        ScenarioMonthlyRows.Clear();
        foreach (var row in result.MonthlyComparisons)
        {
            ScenarioMonthlyRows.Add(new MutualFundScenarioMonthlyRowViewModel(row));
        }

        ScenarioAnnualRows.Clear();
        var chartRows = result.ChartMonthlyComparisons;
        foreach (var row in chartRows.Where((row, index) =>
                     row.MonthsFromNow % 12 == 0 || index == chartRows.Count - 1))
        {
            ScenarioAnnualRows.Add(new MutualFundScenarioMonthlyRowViewModel(row));
        }

        ScenarioChartSeries.Clear();
        foreach (var scenario in result.Scenarios.Where(x => x.IsAvailable))
        {
            ScenarioChartSeries.Add(new MutualFundScenarioChartSeriesViewModel(scenario, TargetAmountJpy));
        }


        ScenarioRankingRows.Clear();
        var ranked = result.Scenarios
            .Where(x => x.IsAvailable)
            .OrderByDescending(x => x.ChartFinalMarketValueJpy)
            .ToList();
        for (var index = 0; index < ranked.Count; index++)
        {
            ScenarioRankingRows.Add(new MutualFundScenarioRankingRowViewModel(index + 1, ranked[index]));
        }

        var standard = result.Scenarios.FirstOrDefault(x => x.Key == "Standard" && x.IsAvailable) ??
            result.Scenarios.FirstOrDefault(x => x.IsAvailable);
        FinalAssetAmount = standard is null ? "0円" : Formatters.Jpy(standard.FinalMarketValueJpy);
        StopContributionFinalAsset = standard is null ? "0円" : Formatters.Jpy(standard.NoContributionFinalMarketValueJpy);
        RequiredMonthlyContribution = "比較表を参照";
        AdditionalMonthlyContributionNeeded = "比較表を参照";
        RequiredMonthlyContributionDetail = "目標達成年月は各シナリオの年利から自動算出します。";
        TargetAchievementMonth = standard?.TargetAchievementMonth is null
            ? "試算期間内では未達"
            : standard.TargetAchievementMonth.Value.ToString("yyyy/MM");
        RemainingPeriod = standard is null ? "未達" : FormatRemainingPeriod(standard.MonthsToTarget);

        ChartStatus = result.ChartMonthlyComparisons.Count == 0
            ? "現在保有中の投資信託がありません。CSV取込または登録後に表示されます。"
            : $"{result.ChartMonthlyComparisons[0].YearMonth:yyyy/MM} - {result.ChartMonthlyComparisons[^1].YearMonth:yyyy/MM} / 4シナリオ月次複利比較";
        ChartHorizon = result.UsesConservativeTargetHorizon && result.ConservativeTargetMonth is { } conservativeTarget
            ? $"保守的シナリオの目標到達 {conservativeTarget:yyyy/MM} まで表示"
            : $"表示期間 {Math.Max(1, ProjectionYears):N0}年";
        UpdateFocusedScenario(result);
        StatusMessage = summary.ActualAnnualizedReturnRate is null
            ? "実績参考年利は購入日・入出金・積立履歴が不足しているため算出不可です。税金、信託報酬、為替は考慮していません。"
            : "実績参考年利は過去の運用結果に基づく参考値です。将来の運用成果を保証するものではありません。税金、信託報酬、為替は考慮していません。";

        ProjectionRows.Clear();
        ContributionComparisons.Clear();
        TrendPoints.Clear();

        OnPropertyChanged(nameof(MonthlyContributionInput));
    }

    private void UpdateFocusedScenario(MutualFundScenarioComparisonResult result)
    {
        var selectedKey = SelectedStoryScenario?.Value ?? "Standard";
        var scenario = result.Scenarios.FirstOrDefault(x =>
                           x.IsAvailable && string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                       ?? result.Scenarios.FirstOrDefault(x => x.IsAvailable && x.Key == "Standard")
                       ?? result.Scenarios.FirstOrDefault(x => x.IsAvailable);
        if (scenario is null)
        {
            SelectedScenarioReturn = "算出不可";
            StoryTargetDate = "未達";
            StoryRemainingPeriod = "未達";
            MotivationHeadline = "表示できるシナリオがありません。";
            MotivationDetail = "年利設定と投資信託の現在保有データを確認してください。";
            return;
        }

        SelectedScenarioReturn = scenario.AnnualReturnRate is { } rate
            ? $"{scenario.Name} {rate:N2}%"
            : $"{scenario.Name} 算出不可";
        StoryTargetDate = scenario.TargetAchievementMonth?.ToString("yyyy/MM") ?? "未達";
        StoryRemainingPeriod = FormatRemainingPeriod(scenario.MonthsToTarget);
        FinalAssetAmount = Formatters.Jpy(scenario.FinalMarketValueJpy);
        StopContributionFinalAsset = Formatters.Jpy(scenario.NoContributionFinalMarketValueJpy);
        TargetAchievementMonth = StoryTargetDate;
        RemainingPeriod = StoryRemainingPeriod;

        var gap = Math.Max(0m, TargetAmountJpy - result.Summary.CurrentMarketValueJpy);
        if (gap == 0m)
        {
            MotivationHeadline = "目標資産を達成しています。";
            MotivationDetail = $"現在資産は目標を{Formatters.Jpy(result.Summary.CurrentMarketValueJpy - TargetAmountJpy)}上回っています。";
        }
        else if (scenario.TargetAchievementMonth is { } targetMonth)
        {
            MotivationHeadline = $"目標まであと{Formatters.Jpy(gap)}";
            MotivationDetail = $"このまま積み立てると、{targetMonth:yyyy年MM月}に目標達成予定です。あと{StoryRemainingPeriod}です。";
        }
        else
        {
            MotivationHeadline = $"目標まであと{Formatters.Jpy(gap)}";
            MotivationDetail = "現在の条件では100年以内に到達しません。積立額または想定年利を見直してください。";
        }
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

    private string BuildSelectedScopeSummary(MutualFundScenarioComparisonResult result)
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

    private string BuildContributionNotice(MutualFundScenarioComparisonResult result)
    {
        if (!result.Summary.AllowsMonthlyContribution)
        {
            return "旧NISAには追加積立できません。保有継続シミュレーションとして、現在資産を各シナリオ年利で運用継続した場合を試算します。";
        }

        if (result.AccountBreakdowns.Any(x => x.AccountType == AccountTypes.NisaLegacy) &&
            result.AccountBreakdowns.Any(x => x.AllowsContribution))
        {
            var contributionAccount = result.AccountBreakdowns.First(x => x.AllowsContribution);
            return $"旧NISA残高は追加積立なしで運用し、毎月積立額は{contributionAccount.AccountDisplayName}側だけへ反映します。";
        }

        return "毎月積立額を対象口座へ反映し、月次複利・月末積立で4シナリオを比較します。";
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
        LegacyKey = option.LegacyKey;
        DisplayName = option.DisplayName;
        FundCount = option.FundCount;
        PositionCount = option.PositionCount;
    }

    public string Key { get; }
    public string LegacyKey { get; }
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

public sealed class MutualFundScenarioSettingViewModel : ObservableObject
{
    private readonly decimal? _defaultRate;
    private readonly Action _changed;
    private bool _isEnabled;
    private decimal _annualReturnRate;
    private bool _isAvailable;
    private string _basis;
    private string _unavailableReason;
    private string _warning = string.Empty;
    private bool _isManualOverride;
    private bool _isInternalRateUpdate;

    public MutualFundScenarioSettingViewModel(
        string key,
        string name,
        decimal? defaultRate,
        bool isEnabled,
        string basis,
        Action changed,
        string unavailableReason = "")
    {
        Key = key;
        Name = name;
        _defaultRate = defaultRate;
        _annualReturnRate = defaultRate ?? 0m;
        _isEnabled = isEnabled;
        _isAvailable = defaultRate is not null;
        _basis = basis;
        _unavailableReason = unavailableReason;
        _changed = changed;
        ResetCommand = new RelayCommand(ResetToDefault);
    }

    public string Key { get; }
    public string Name { get; }
    public RelayCommand ResetCommand { get; }
    public bool IsEditableRate => IsAvailable;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _changed();
            }
        }
    }

    public decimal AnnualReturnRate
    {
        get => _annualReturnRate;
        set
        {
            if (SetProperty(ref _annualReturnRate, value))
            {
                if (!_isInternalRateUpdate)
                {
                    _isManualOverride = Key == "Actual";
                }

                OnPropertyChanged(nameof(RateDisplay));
                OnPropertyChanged(nameof(AnnualReturnRateInput));
                OnPropertyChanged(nameof(BasisDisplay));
                _changed();
            }
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(IsEditableRate));
                OnPropertyChanged(nameof(RateDisplay));
                OnPropertyChanged(nameof(AnnualReturnRateInput));
                OnPropertyChanged(nameof(IsRateInputReadOnly));
            }
        }
    }

    public string Basis
    {
        get => _basis;
        private set
        {
            if (SetProperty(ref _basis, value))
            {
                OnPropertyChanged(nameof(BasisDisplay));
            }
        }
    }

    public string UnavailableReason
    {
        get => _unavailableReason;
        private set => SetProperty(ref _unavailableReason, value);
    }

    public string Warning
    {
        get => _warning;
        private set => SetProperty(ref _warning, value);
    }

    public string RateDisplay => IsAvailable ? $"{AnnualReturnRate:N2}%" : "算出不可";

    public string AnnualReturnRateInput
    {
        get => IsAvailable ? AnnualReturnRate.ToString("N2") : "算出不可";
        set
        {
            if (!IsAvailable)
            {
                return;
            }

            var normalized = value.Replace("%", string.Empty).Trim();
            if (decimal.TryParse(normalized, out var parsed))
            {
                AnnualReturnRate = parsed;
            }
        }
    }

    public bool IsRateInputReadOnly => !IsAvailable;

    public string BasisDisplay => Key == "Actual" && _isManualOverride
        ? $"{Basis} / 手動変更値"
        : Basis;

    public void SetAutoCalculatedRate(decimal rate, string basis, string warning)
    {
        if (_isManualOverride && IsAvailable)
        {
            Basis = basis;
            Warning = warning;
            return;
        }

        IsAvailable = true;
        SetAnnualReturnRateFromModel(rate);
        IsEnabled = true;
        Basis = basis;
        UnavailableReason = string.Empty;
        Warning = warning;
    }

    public void SetUnavailable(string reason, string basis)
    {
        if (_isManualOverride && IsAvailable)
        {
            return;
        }

        IsAvailable = false;
        IsEnabled = false;
        Basis = basis;
        UnavailableReason = reason;
        Warning = string.Empty;
    }

    public void SetBasis(string basis)
    {
        Basis = basis;
    }

    private void ResetToDefault()
    {
        _isManualOverride = false;
        if (_defaultRate is not null)
        {
            SetAnnualReturnRateFromModel(_defaultRate.Value);
            IsEnabled = true;
        }
        else
        {
            _changed();
        }
    }

    private void SetAnnualReturnRateFromModel(decimal rate)
    {
        _isInternalRateUpdate = true;
        try
        {
            AnnualReturnRate = rate;
        }
        finally
        {
            _isInternalRateUpdate = false;
        }
    }
}

public sealed class MutualFundScenarioComparisonRowViewModel
{
    public MutualFundScenarioComparisonRowViewModel(
        MutualFundScenarioResult scenario,
        int projectionYears,
        IReadOnlyDictionary<string, decimal> chartFinalValues)
    {
        Scenario = scenario.Name;
        AnnualReturnRate = scenario.AnnualReturnRate is null ? "算出不可" : $"{scenario.AnnualReturnRate.Value:N2}%";
        FiveYearMarketValue = scenario.FiveYearMarketValueJpy <= 0m ? "-" : Formatters.Jpy(scenario.FiveYearMarketValueJpy);
        TenYearMarketValue = scenario.TenYearMarketValueJpy <= 0m ? "-" : Formatters.Jpy(scenario.TenYearMarketValueJpy);
        FinalMarketValue = scenario.IsAvailable ? Formatters.Jpy(scenario.FinalMarketValueJpy) : "算出不可";
        TargetAchievement = scenario.TargetAchievementMonth is null
            ? "試算期間内では未達"
            : scenario.TargetAchievementMonth.Value.ToString("yyyy/MM");
        PeriodToTarget = FormatMonths(scenario.MonthsToTarget);
        CumulativeContribution = scenario.TargetAchievementMonth is null ? "-" : Formatters.Jpy(scenario.CumulativeContributionAtTargetJpy);
        InvestmentGain = scenario.TargetAchievementMonth is null ? "-" : Formatters.SignedJpy(scenario.InvestmentGainAtTargetJpy);
        Basis = string.IsNullOrWhiteSpace(scenario.UnavailableReason)
            ? scenario.Basis
            : $"{scenario.Basis}: {scenario.UnavailableReason}";
        DifferenceFromConservative = FormatDifference(scenario, chartFinalValues, "Conservative");
        DifferenceFromStandard = FormatDifference(scenario, chartFinalValues, "Standard");
        DifferenceFromAggressive = FormatDifference(scenario, chartFinalValues, "Aggressive");
        DifferenceFromActual = FormatDifference(scenario, chartFinalValues, "Actual");
    }

    public string Scenario { get; }
    public string AnnualReturnRate { get; }
    public string FiveYearMarketValue { get; }
    public string TenYearMarketValue { get; }
    public string FinalMarketValue { get; }
    public string TargetAchievement { get; }
    public string PeriodToTarget { get; }
    public string CumulativeContribution { get; }
    public string InvestmentGain { get; }
    public string Basis { get; }
    public string DifferenceFromConservative { get; }
    public string DifferenceFromStandard { get; }
    public string DifferenceFromAggressive { get; }
    public string DifferenceFromActual { get; }

    private static string FormatDifference(
        MutualFundScenarioResult scenario,
        IReadOnlyDictionary<string, decimal> values,
        string comparisonKey)
    {
        if (!scenario.IsAvailable ||
            !values.TryGetValue(scenario.Key, out var current) ||
            !values.TryGetValue(comparisonKey, out var comparison))
        {
            return "-";
        }

        return Formatters.SignedJpy(current - comparison);
    }

    private static string FormatMonths(int? months)
    {
        if (months is null)
        {
            return "未達";
        }

        if (months == 0)
        {
            return "現在時点で達成済み";
        }

        return $"{months.Value / 12:N0}年{months.Value % 12:N0}か月";
    }
}

public sealed class MutualFundScenarioMonthlyRowViewModel
{
    public MutualFundScenarioMonthlyRowViewModel(MutualFundScenarioMonthlyComparison row)
    {
        YearMonth = row.YearMonth.ToString("yyyy/MM");
        MonthsFromNow = $"{row.MonthsFromNow:N0}か月";
        CumulativeContribution = Formatters.Jpy(row.CumulativeContributionJpy);
        ConservativeMarketValue = FormatValue(row, "Conservative", x => x.MarketValueJpy);
        StandardMarketValue = FormatValue(row, "Standard", x => x.MarketValueJpy);
        AggressiveMarketValue = FormatValue(row, "Aggressive", x => x.MarketValueJpy);
        ActualMarketValue = FormatValue(row, "Actual", x => x.MarketValueJpy);
        ConservativeGain = FormatValue(row, "Conservative", x => x.UnrealizedGainJpy, signed: true);
        StandardGain = FormatValue(row, "Standard", x => x.UnrealizedGainJpy, signed: true);
        AggressiveGain = FormatValue(row, "Aggressive", x => x.UnrealizedGainJpy, signed: true);
        ActualGain = FormatValue(row, "Actual", x => x.UnrealizedGainJpy, signed: true);
        ConservativeAchievement = FormatPercent(row, "Conservative");
        StandardAchievement = FormatPercent(row, "Standard");
        AggressiveAchievement = FormatPercent(row, "Aggressive");
        ActualAchievement = FormatPercent(row, "Actual");
    }

    public string YearMonth { get; }
    public string MonthsFromNow { get; }
    public string CumulativeContribution { get; }
    public string ConservativeMarketValue { get; }
    public string StandardMarketValue { get; }
    public string AggressiveMarketValue { get; }
    public string ActualMarketValue { get; }
    public string ConservativeGain { get; }
    public string StandardGain { get; }
    public string AggressiveGain { get; }
    public string ActualGain { get; }
    public string ConservativeAchievement { get; }
    public string StandardAchievement { get; }
    public string AggressiveAchievement { get; }
    public string ActualAchievement { get; }

    private static string FormatValue(
        MutualFundScenarioMonthlyComparison row,
        string key,
        Func<MutualFundScenarioMonthlyProjection, decimal> selector,
        bool signed = false) =>
        row.ScenarioValues.TryGetValue(key, out var value)
            ? signed ? Formatters.SignedJpy(selector(value)) : Formatters.Jpy(selector(value))
            : "-";

    private static string FormatPercent(MutualFundScenarioMonthlyComparison row, string key) =>
        row.ScenarioValues.TryGetValue(key, out var value)
            ? Formatters.Percent(value.TargetAchievementRate)
            : "-";
}

public sealed class MutualFundScenarioChartSeriesViewModel : ObservableObject
{
    private bool _isVisible = true;

    public MutualFundScenarioChartSeriesViewModel(MutualFundScenarioResult scenario, decimal targetAmount)
    {
        Key = scenario.Key;
        Name = scenario.Name;
        AnnualReturnRate = scenario.AnnualReturnRate is null ? string.Empty : $"{scenario.AnnualReturnRate.Value:N2}%";
        TargetAmountJpy = targetAmount;
        TargetAchievementMonth = scenario.TargetAchievementMonth;
        Points = scenario.ChartProjections
            .Select(row => new MutualFundScenarioChartPointViewModel(
                row.YearMonth,
                row.MarketValueJpy,
                row.CumulativeContributionJpy,
                row.UnrealizedGainJpy,
                row.TargetAchievementRate))
            .ToList();
    }

    public string Key { get; }
    public string Name { get; }
    public string AnnualReturnRate { get; }
    public decimal TargetAmountJpy { get; }
    public DateTime? TargetAchievementMonth { get; }
    public IReadOnlyList<MutualFundScenarioChartPointViewModel> Points { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

public sealed record MutualFundScenarioChartPointViewModel(
    DateTime YearMonth,
    decimal MarketValueJpy,
    decimal CumulativeContributionJpy,
    decimal UnrealizedGainJpy,
    decimal TargetAchievementRate);

public sealed class MutualFundScenarioRankingRowViewModel
{
    public MutualFundScenarioRankingRowViewModel(int rank, MutualFundScenarioResult scenario)
    {
        Rank = $"{rank:N0}位";
        Scenario = scenario.Name;
        AnnualReturnRate = scenario.AnnualReturnRate is { } rate ? $"{rate:N2}%" : "算出不可";
        FinalMarketValue = Formatters.Jpy(scenario.ChartFinalMarketValueJpy);
        TargetAchievement = scenario.TargetAchievementMonth?.ToString("yyyy/MM") ?? "未達";
    }

    public string Rank { get; }
    public string Scenario { get; }
    public string AnnualReturnRate { get; }
    public string FinalMarketValue { get; }
    public string TargetAchievement { get; }
}

public sealed record DividendStockMarketDataFetchResult(
    bool IsSuccess,
    string StatusKind,
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
    string Status,
    string DividendFrequency = "",
    DateTime? DividendRecordDate = null,
    DateTime? ExDividendDate = null,
    DateTime? DividendPaymentDate = null);

public sealed record DividendStockMarketDataFetchRequest(
    string Query,
    string CurrentTicker,
    string Name,
    string Country,
    string Currency,
    decimal ExchangeRate,
    string AnnualDividendSource,
    AppSettings Settings);

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
    private string _marketDataFetchState;
    private bool _isMarketDataFetching;
    private decimal _plannedAdditionalShares;
    private string _plannedBroker;
    private string _plannedAccountType;
    private decimal _annualDividendGrowthRate;
    private string _purchaseMode;
    private string _dividendFrequency;
    private string _dividendMonths;
    private DateTime? _dividendRecordDate;
    private DateTime? _exDividendDate;
    private DateTime? _dividendPaymentDate;
    private string _inputError = string.Empty;
    private readonly IReadOnlyList<DividendGrowthPlanItem> _components;
    private DividendPurchasePlanHolding? _planResult;

    public DividendSimulationRowViewModel(DividendGrowthPlanItem item, Action changed)
    {
        _changed = changed;
        PlanKey = item.PlanKey;
        StockId = item.StockId;
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
        _marketDataFetchState = ResolveInitialFetchState(_marketDataFetchStatus);
        _plannedAdditionalShares = item.PlannedAdditionalShares;
        _plannedBroker = DividendSimulationSelectionOptions.NormalizeBroker(FirstNonBlank(item.PlannedBroker, _broker));
        _plannedAccountType = NormalizeAccountType(FirstNonBlank(item.PlannedAccountType, _accountType));
        _annualDividendGrowthRate = item.AnnualDividendGrowthRate;
        _purchaseMode = item.PurchaseMode;
        _components = item.Components;
        CurrentCostJpy = item.CurrentCostJpy;
        CurrentMarketValueJpy = item.CurrentMarketValueJpy;
        _dividendFrequency = item.DividendFrequency;
        _dividendMonths = item.DividendMonths;
        _dividendRecordDate = item.DividendRecordDate;
        _exDividendDate = item.ExDividendDate;
        _dividendPaymentDate = item.DividendPaymentDate;
        BrokerOptions = DividendSimulationSelectionOptions.BuildBrokerOptions(_broker, _plannedBroker);
        CountryOptions = DividendSimulationSelectionOptions.CountryOptions;
        CurrencyOptions = DividendSimulationSelectionOptions.CurrencyOptions;
        AccountOptions = DividendSimulationSelectionOptions.AccountOptions;
        PurchaseModeOptions = DividendSimulationSelectionOptions.PurchaseModeOptions;
    }

    public string PlanKey { get; }
    public int StockId { get; }
    public string CanonicalKey { get; }
    public string PositionKey { get; }
    public bool IsNewStock { get; }
    public IReadOnlyList<SelectionOption<string>> BrokerOptions { get; }
    public IReadOnlyList<SelectionOption<string>> CountryOptions { get; }
    public IReadOnlyList<string> CurrencyOptions { get; }
    public IReadOnlyList<SelectionOption<string>> AccountOptions { get; }
    public IReadOnlyList<SelectionOption<string>> PurchaseModeOptions { get; }
    public decimal CurrentCostJpy { get; }
    public decimal CurrentMarketValueJpy { get; }
    public string DividendFrequency
    {
        get => _dividendFrequency;
        private set => SetProperty(ref _dividendFrequency, value);
    }

    public string DividendMonths
    {
        get => _dividendMonths;
        private set => SetProperty(ref _dividendMonths, value);
    }

    public DateTime? DividendRecordDate
    {
        get => _dividendRecordDate;
        private set => SetProperty(ref _dividendRecordDate, value);
    }

    public DateTime? ExDividendDate
    {
        get => _exDividendDate;
        private set => SetProperty(ref _exDividendDate, value);
    }

    public DateTime? DividendPaymentDate
    {
        get => _dividendPaymentDate;
        private set => SetProperty(ref _dividendPaymentDate, value);
    }

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

    public string MarketDataFetchState
    {
        get => _marketDataFetchState;
        private set => SetProperty(ref _marketDataFetchState, value);
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
    public string CurrentYield => _planResult is null ? "-" : Formatters.Percent(_planResult.CurrentYieldRate);
    public string YieldOnCost => _planResult is null ? "-" : Formatters.Percent(_planResult.YieldOnCostRate);
    public string ThisYearDividend => _planResult is null ? "-" : Formatters.Jpy(_planResult.TargetYearPlannedNetDividendJpy);
    public string NextYearDividend => _planResult is null ? "-" : Formatters.Jpy(_planResult.PostAddNextYearNetDividendJpy);
    public string AnnualDividendIncrease => _planResult is null ? "-" : Formatters.SignedJpy(_planResult.NextYearAdditionalNetDividendJpy);
    public string AdditionalInvestmentYield => _planResult is null ? "-" : Formatters.Percent(_planResult.AdditionalInvestmentYieldRate);
    public string DividendComposition => _planResult is null ? "-" : Formatters.Percent(_planResult.DividendCompositionRate);
    public string DividendMonthsDisplay => _planResult is null || string.IsNullOrWhiteSpace(_planResult.DividendMonths) ? "未取得" : _planResult.DividendMonths;
    public string NextLastRightsDate => _planResult?.NextLastRightsDate?.ToString("yyyy/MM/dd") ?? "未取得";
    public string NextPaymentDate => _planResult?.NextPaymentDate?.ToString("yyyy/MM/dd") ?? "未取得";
    public string EligibilityStatus => _planResult?.EligibilityStatus switch
    {
        DividendPlanEligibility.Eligible => "受取可能",
        DividendPlanEligibility.Ineligible => "受取不可",
        DividendPlanEligibility.Estimated => "推定",
        _ => "未取得"
    };
    public string DataQuality => _planResult?.DataQuality switch
    {
        DividendPlanDataQuality.Acquired => "配当情報取得済",
        DividendPlanDataQuality.Estimated => "推定",
        _ => "未取得"
    };
    public string DividendPaybackYears => _planResult is null || _planResult.DividendPaybackYears <= 0m ? "-" : $"{_planResult.DividendPaybackYears:N1}年";
    public string TargetContribution => _planResult is null ? "-" : Formatters.Jpy(_planResult.TargetContributionJpy);
    public string InputError
    {
        get => _inputError;
        private set => SetProperty(ref _inputError, value);
    }

    public DividendGrowthPlanItem ToPlanItem() =>
        new()
        {
            StockId = StockId,
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
            CurrentCostJpy = CurrentCostJpy,
            CurrentMarketValueJpy = CurrentMarketValueJpy,
            DividendFrequency = DividendFrequency,
            DividendMonths = DividendMonths,
            DividendRecordDate = DividendRecordDate,
            ExDividendDate = ExDividendDate,
            DividendPaymentDate = DividendPaymentDate,
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

    public void ApplyPlanResult(DividendPurchasePlanHolding? result)
    {
        _planResult = result;
        OnPropertyChanged(nameof(CurrentYield));
        OnPropertyChanged(nameof(YieldOnCost));
        OnPropertyChanged(nameof(ThisYearDividend));
        OnPropertyChanged(nameof(NextYearDividend));
        OnPropertyChanged(nameof(AnnualDividendIncrease));
        OnPropertyChanged(nameof(AdditionalInvestmentYield));
        OnPropertyChanged(nameof(DividendComposition));
        OnPropertyChanged(nameof(DividendMonthsDisplay));
        OnPropertyChanged(nameof(NextLastRightsDate));
        OnPropertyChanged(nameof(NextPaymentDate));
        OnPropertyChanged(nameof(EligibilityStatus));
        OnPropertyChanged(nameof(DataQuality));
        OnPropertyChanged(nameof(DividendPaybackYears));
        OnPropertyChanged(nameof(TargetContribution));
    }

    public void BeginMarketDataFetch()
    {
        IsMarketDataFetching = true;
        MarketDataFetchStatus = "取得中";
        MarketDataFetchState = MarketDataFetchStatusKinds.Fetching;
        InputError = string.Empty;
    }

    public void ApplyMarketData(DividendStockMarketDataFetchResult result)
    {
        IsMarketDataFetching = false;
        MarketDataFetchState = result.StatusKind;

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && !string.IsNullOrWhiteSpace(result.Ticker))
        {
            Ticker = result.Ticker;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && !string.IsNullOrWhiteSpace(result.Name))
        {
            Name = result.Name;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && !string.IsNullOrWhiteSpace(result.Country))
        {
            Country = result.Country;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && !string.IsNullOrWhiteSpace(result.Currency))
        {
            Currency = result.Currency;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && result.CurrentPrice is > 0m)
        {
            CurrentPrice = result.CurrentPrice.Value;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && result.ExchangeRate is > 0m)
        {
            ExchangeRate = result.ExchangeRate.Value;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && result.AnnualDividendPerShare is >= 0m)
        {
            AnnualDividendPerShare = result.AnnualDividendPerShare.Value;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && !string.IsNullOrWhiteSpace(result.DividendSource))
        {
            AnnualDividendSource = result.DividendSource;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed && !string.IsNullOrWhiteSpace(result.DividendFrequency))
        {
            DividendFrequency = result.DividendFrequency;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed)
        {
            DividendRecordDate = result.DividendRecordDate ?? DividendRecordDate;
            ExDividendDate = result.ExDividendDate ?? ExDividendDate;
            DividendPaymentDate = result.DividendPaymentDate ?? DividendPaymentDate;
        }

        if (result.StatusKind != MarketDataFetchStatusKinds.Failed)
        {
            MarketDataSource = result.Source;
            MarketDataAcquiredAt = result.AcquiredAt;
        }

        MarketDataFetchStatus = result.Status;
        InputError = result.IsSuccess ? string.Empty : result.Status;
    }

    public void SetMarketDataFailure(string message)
    {
        IsMarketDataFetching = false;
        MarketDataFetchState = MarketDataFetchStatusKinds.Failed;
        MarketDataFetchStatus = string.IsNullOrWhiteSpace(message) ? "取得失敗" : message;
        InputError = MarketDataFetchStatus;
    }

    private static string ResolveInitialFetchState(string status)
    {
        if (status.StartsWith("取得成功", StringComparison.Ordinal))
        {
            return MarketDataFetchStatusKinds.Success;
        }

        if (status.StartsWith("一部取得", StringComparison.Ordinal))
        {
            return MarketDataFetchStatusKinds.Partial;
        }

        return status.StartsWith("取得失敗", StringComparison.Ordinal)
            ? MarketDataFetchStatusKinds.Failed
            : MarketDataFetchStatusKinds.Idle;
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

    private void SetNonNegative(ref decimal field, decimal value, string label, [CallerMemberName] string? propertyName = null)
    {
        if (value < 0m)
        {
            InputError = $"{label}は0以上で入力してください。";
            SetAndNotify(ref field, 0m, propertyName);
            return;
        }

        if (!string.IsNullOrWhiteSpace(InputError) && InputError.Contains(label, StringComparison.Ordinal))
        {
            InputError = string.Empty;
        }

        SetAndNotify(ref field, value, propertyName);
    }

    private void SetAndNotify<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
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

public sealed class DividendPlanMonthlyRowViewModel
{
    public DividendPlanMonthlyRowViewModel(DividendPurchasePlanMonthlyResult row)
    {
        Year = row.Year;
        Month = row.Month;
        CurrentValue = row.CurrentNetDividendJpy;
        ExistingAdditionalValue = row.ExistingAdditionalNetDividendJpy;
        NewPurchaseValue = row.NewPurchaseNetDividendJpy;
        MissedValue = row.MissedNetDividendJpy;
        TargetValue = row.TargetNetDividendJpy;
        PlannedValue = row.PlannedNetDividendJpy;
        CurrentCumulativeValue = row.CurrentCumulativeNetDividendJpy;
        PlannedCumulativeValue = row.CumulativeNetDividendJpy;
        YearMonth = $"{row.Year}/{row.Month:00}";
        CurrentDividend = Formatters.Jpy(row.CurrentNetDividendJpy);
        PlannedDividend = Formatters.Jpy(row.PlannedNetDividendJpy);
        AdditionalDividend = Formatters.SignedJpy(row.AdditionalNetDividendJpy);
        CumulativeDividend = Formatters.Jpy(row.CumulativeNetDividendJpy);
        CurrentCumulativeDividend = Formatters.Jpy(row.CurrentCumulativeNetDividendJpy);
        TargetDividend = Formatters.Jpy(row.TargetNetDividendJpy);
        TargetAchievement = Formatters.Percent(row.TargetAchievementRate);
        TargetDifference = row.TargetDifferenceJpy >= 0m
            ? $"余剰 {Formatters.Jpy(row.TargetDifferenceJpy)}"
            : $"不足 {Formatters.Jpy(Math.Abs(row.TargetDifferenceJpy))}";
        Breakdown = row.Events.Count == 0
            ? "配当予定なし"
            : string.Join(" / ", row.Events.GroupBy(x => x.Ticker).Select(g => $"{g.Key} {Formatters.Jpy(g.Sum(x => x.CurrentNetDividendJpy + x.AdditionalNetDividendJpy))}"));
        ToolTipText = $"{YearMonth}\n合計 {PlannedDividend}\n現在保有 {CurrentDividend}\n買い増し {Formatters.Jpy(row.ExistingAdditionalNetDividendJpy)}\n新規購入 {Formatters.Jpy(row.NewPurchaseNetDividendJpy)}\n受取不可 {Formatters.Jpy(row.MissedNetDividendJpy)}\n{Breakdown}";
    }

    public int Year { get; }
    public int Month { get; }
    public decimal CurrentValue { get; }
    public decimal ExistingAdditionalValue { get; }
    public decimal NewPurchaseValue { get; }
    public decimal MissedValue { get; }
    public decimal TargetValue { get; }
    public decimal PlannedValue { get; }
    public decimal CurrentCumulativeValue { get; }
    public decimal PlannedCumulativeValue { get; }
    public string YearMonth { get; }
    public string CurrentDividend { get; }
    public string PlannedDividend { get; }
    public string AdditionalDividend { get; }
    public string CumulativeDividend { get; }
    public string CurrentCumulativeDividend { get; }
    public string TargetDividend { get; }
    public string TargetAchievement { get; }
    public string TargetDifference { get; }
    public string Breakdown { get; }
    public string ToolTipText { get; }
}

public sealed class DividendPlanRankingRowViewModel
{
    public DividendPlanRankingRowViewModel(DividendPurchasePlanHolding row)
    {
        Ticker = row.Ticker;
        Name = row.Name;
        Value = row.NextYearAdditionalNetDividendJpy;
        Amount = Formatters.SignedJpy(Value);
        InvestmentValue = row.PlannedPurchaseAmountJpy;
        InvestmentAmount = Formatters.Jpy(InvestmentValue);
        Yield = Formatters.Percent(row.AdditionalInvestmentYieldRate);
        PaybackYears = row.DividendPaybackYears <= 0m ? "対象外" : $"{row.DividendPaybackYears:N1}年";
        PlannedShares = $"{row.PlannedAdditionalShares:N2}株";
        ToolTipText = $"{Ticker} {Name}\n追加配当 {Amount}\n追加投資額 {InvestmentAmount}\n追加投資利回り {Yield}\n回収年数 {PaybackYears}\n予定株数 {PlannedShares}";
    }
    public string Ticker { get; }
    public string Name { get; }
    public decimal Value { get; }
    public string Amount { get; }
    public decimal InvestmentValue { get; }
    public string InvestmentAmount { get; }
    public string Yield { get; }
    public string PaybackYears { get; }
    public string PlannedShares { get; }
    public string ToolTipText { get; }
}

public sealed class DividendPlanCompositionRowViewModel
{
    public DividendPlanCompositionRowViewModel(DividendPurchasePlanComposition row)
        : this(row.Ticker, row.Name, row.AnnualNetDividendJpy, row.CompositionRate,
            row.CurrentCompositionRate, row.CompositionRateChange)
    {
    }

    public DividendPlanCompositionRowViewModel(
        string ticker,
        string name,
        decimal value,
        decimal rate,
        decimal currentRate = 0m,
        decimal rateChange = 0m)
    {
        Ticker = ticker;
        Name = name;
        Value = value;
        Amount = Formatters.Jpy(Value);
        Rate = Formatters.Percent(rate);
        CurrentRate = Formatters.Percent(currentRate);
        RateChange = $"{rateChange:+0.00;-0.00;0.00}pt";
    }
    public string Ticker { get; }
    public string Name { get; }
    public decimal Value { get; }
    public string Amount { get; }
    public string Rate { get; }
    public string CurrentRate { get; }
    public string RateChange { get; }
}

public sealed class DividendPlanStockMonthlyRowViewModel
{
    public DividendPlanStockMonthlyRowViewModel(
        string ticker,
        string name,
        IReadOnlyList<DividendPurchasePlanEvent> events)
    {
        Ticker = ticker;
        Name = name;
        CurrentValues = BuildValues(events, x => x.CurrentNetDividendJpy);
        ExistingAdditionalValues = BuildValues(events.Where(x => !x.IsNewStock).ToList(), x => x.AdditionalNetDividendJpy);
        NewPurchaseValues = BuildValues(events.Where(x => x.IsNewStock).ToList(), x => x.AdditionalNetDividendJpy);
        MissedValues = BuildValues(events, x => x.MissedNetDividendJpy);
        PlannedValues = Enumerable.Range(0, 12)
            .Select(index => CurrentValues[index] + ExistingAdditionalValues[index] + NewPurchaseValues[index])
            .ToArray();
        Statuses = Enumerable.Range(1, 12)
            .Select(month => ResolveStatus(events.Where(x => x.Month == month).ToList()))
            .ToArray();
        TotalValue = PlannedValues.Sum();
    }

    public string Ticker { get; }
    public string Name { get; }
    public IReadOnlyList<decimal> CurrentValues { get; }
    public IReadOnlyList<decimal> ExistingAdditionalValues { get; }
    public IReadOnlyList<decimal> NewPurchaseValues { get; }
    public IReadOnlyList<decimal> PlannedValues { get; }
    public IReadOnlyList<decimal> MissedValues { get; }
    public IReadOnlyList<string> Statuses { get; }
    public decimal TotalValue { get; }

    public string ToolTipForMonth(int month)
    {
        var index = Math.Clamp(month, 1, 12) - 1;
        return $"{Ticker} {Name}\n{month}月 合計 {Formatters.Jpy(PlannedValues[index])}\n" +
               $"現在 {Formatters.Jpy(CurrentValues[index])}\n" +
               $"買い増し {Formatters.Jpy(ExistingAdditionalValues[index])}\n" +
               $"新規購入 {Formatters.Jpy(NewPurchaseValues[index])}\n" +
               $"受取不可 {Formatters.Jpy(MissedValues[index])}\n{Statuses[index]}";
    }

    private static IReadOnlyList<decimal> BuildValues(
        IReadOnlyList<DividendPurchasePlanEvent> events,
        Func<DividendPurchasePlanEvent, decimal> selector) =>
        Enumerable.Range(1, 12)
            .Select(month => events.Where(x => x.Month == month).Sum(selector))
            .ToArray();

    private static string ResolveStatus(IReadOnlyList<DividendPurchasePlanEvent> events)
    {
        if (events.Count == 0) return "予定なし";
        if (events.Any(x => x.EligibilityStatus == DividendPlanEligibility.Missing)) return "情報未取得";
        if (events.Any(x => x.EligibilityStatus == DividendPlanEligibility.Ineligible)) return "購入が間に合わない";
        if (events.Any(x => x.EligibilityStatus == DividendPlanEligibility.Estimated)) return "推定";
        return "受取見込み";
    }
}

public sealed class DividendCalendarMonthViewModel
{
    public DividendCalendarMonthViewModel(DividendPurchasePlanMonthlyResult row)
    {
        MonthLabel = $"{row.Month}月";
        Items = row.Events.Count == 0
            ? "予定なし"
            : string.Join("\n", row.Events.OrderBy(x => x.PaymentDate).Select(x =>
                $"{x.PaymentDate:MM/dd} {x.Ticker} {Status(x)} {Formatters.Jpy(x.CurrentNetDividendJpy + x.AdditionalNetDividendJpy)}"));
        ToolTipText = $"{row.Year}年{row.Month}月\n{Items}";
        Events = row.Events
            .OrderBy(x => x.PaymentDate)
            .Select(x => new DividendCalendarEventViewModel(x))
            .ToList();
    }

    public string MonthLabel { get; }
    public string Items { get; }
    public string ToolTipText { get; }
    public IReadOnlyList<DividendCalendarEventViewModel> Events { get; }

    private static string Status(DividendPurchasePlanEvent item) => item.EligibilityStatus switch
    {
        DividendPlanEligibility.Eligible => "確定",
        DividendPlanEligibility.Estimated => "見込み",
        DividendPlanEligibility.Ineligible => "間に合わない",
        _ => "未取得"
    };
}

public sealed class DividendCalendarEventViewModel
{
    public DividendCalendarEventViewModel(DividendPurchasePlanEvent item)
    {
        StockId = item.StockId;
        Ticker = item.Ticker;
        Name = item.Name;
        PaymentDate = item.PaymentDate.ToString("yyyy/MM/dd");
        CurrentDividend = Formatters.Jpy(item.CurrentNetDividendJpy);
        AdditionalDividend = Formatters.SignedJpy(item.AdditionalNetDividendJpy);
        PlannedDividend = Formatters.Jpy(item.CurrentNetDividendJpy + item.AdditionalNetDividendJpy);
        MissedDividend = Formatters.Jpy(item.MissedNetDividendJpy);
        StatusDisplay = Status(item);
        DataSource = item.Source;
        DisplayText = $"{item.PaymentDate:MM/dd} {item.Ticker} {Status(item)} {Formatters.Jpy(item.CurrentNetDividendJpy + item.AdditionalNetDividendJpy)}";
        ToolTipText = $"{item.Name}\n支払予定日 {item.PaymentDate:yyyy/MM/dd}\n権利付き最終日 {item.LastRightsDate:yyyy/MM/dd}\n{Status(item)} / {item.Source}";
    }

    public int StockId { get; }
    public string Ticker { get; }
    public string Name { get; }
    public string PaymentDate { get; }
    public string CurrentDividend { get; }
    public string AdditionalDividend { get; }
    public string PlannedDividend { get; }
    public string MissedDividend { get; }
    public string StatusDisplay { get; }
    public string DataSource { get; }
    public string DisplayText { get; }
    public string ToolTipText { get; }

    private static string Status(DividendPurchasePlanEvent item) => item.EligibilityStatus switch
    {
        DividendPlanEligibility.Eligible => "受取確定",
        DividendPlanEligibility.Estimated => "受取見込み",
        DividendPlanEligibility.Ineligible => "購入が間に合わない",
        _ => "情報未取得"
    };
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
