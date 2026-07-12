using System.Windows.Input;
using System.Windows;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly InvestmentStoryRepository _normalRepository;
    private InvestmentStoryRepository _repository;
    private readonly InvestmentCalculator _calculator = new();
    private readonly StoryGenerator _storyGenerator = new();
    private readonly DividendScheduleService _dividendScheduleService = new();
    private readonly PortfolioAnalyticsService _portfolioAnalyticsService = new();
    private readonly DataQualityService _dataQualityService = new();
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IMarketDataService _marketDataService = new MarketDataProviderFactory();
    private readonly IStockLookupService _stockLookupService = new CompositeStockLookupService(
        new LocalStockLookupService(),
        new YahooFinanceStockLookupService());
    private IReadOnlyList<StockSnapshot> _snapshots = Array.Empty<StockSnapshot>();
    private int? _selectedDetailStockId;
    private string? _selectedDetailKey;
    private bool _selectedDetailIsAggregate;
    private object _currentPage;
    private string _currentTitle = "ダッシュボード";
    private bool _isSidebarCollapsed;
    private bool _isSampleMode;

    private static readonly IReadOnlyDictionary<string, (decimal Price, decimal AnnualDividend)> SampleQuotes =
        new Dictionary<string, (decimal Price, decimal AnnualDividend)>(StringComparer.OrdinalIgnoreCase)
        {
            ["7203"] = (3050m, 90m),
            ["9433"] = (4980m, 145m),
            ["8593"] = (1382m, 48m),
            ["8058"] = (3275m, 110m),
            ["AAPL"] = (313.39m, 1.04m),
            ["MSFT"] = (503.50m, 3.32m),
            ["NVDA"] = (196m, 1m),
            ["KO"] = (82.96m, 2.08m),
            ["MO"] = (72.96m, 4.24m),
            ["NFLX"] = (76.18m, 0m),
            ["TSLA"] = (416m, 0m),
            ["AMD"] = (563m, 0m)
        };

    public MainViewModel()
    {
        _normalRepository = new InvestmentStoryRepository();
        _normalRepository.Initialize();
        _repository = _normalRepository;
        var settings = _normalRepository.GetSettings();
        ThemeManager.Apply(settings.ThemeMode);
        _isSidebarCollapsed = settings.IsSidebarCollapsed;
        _exchangeRateService = new ExchangeRateProviderFactory(() => _repository.GetSettings());

        Dashboard = new DashboardViewModel(RecordCurrentPortfolioSnapshot);
        SimpleRegistration = new SimpleRegistrationViewModel(
            SaveStock,
            _calculator,
            _exchangeRateService,
            _stockLookupService,
            _marketDataService,
            () => _repository.GetSettings(),
            SaveApiFetchLogs);
        StockList = new StockListViewModel(
            ShowSimpleRegistration,
            EditStock,
            ShowStockDetail,
            DeleteStock,
            RefreshSelectedMarketData,
            RefreshAllMarketData,
            RefreshMissingMarketData,
            SaveStockListDisplayMode,
            ShowStockDetail);
        StockList.SelectedDisplayMode = settings.StockListDisplayMode;
        StockEditor = new StockEditorViewModel(SaveStock, DeleteStock);
        StockDetail = new StockDetailViewModel();
        Dividends = new DividendsViewModel(
            SaveDividend,
            DeleteDividend,
            UpdateDividendSchedules,
            _repository.GetTaxProfiles,
            _repository.SaveTaxProfile);
        PassiveIncome = new PassiveIncomeViewModel(SaveGoal);
        Simulation = new SimulationViewModel(_calculator);
        CsvImport = new CsvImportViewModel(
            () => _repository.GetPositions(),
            position => _repository.SavePosition(position),
            payment => _repository.SaveDividendPayment(payment),
            () => _repository.GetDividendPayments(),
            records => _repository.SaveBrokerTrades(records),
            LoadData);
        BrokerIntegration = new BrokerIntegrationViewModel(
            () => _repository.GetSettings(),
            appSettings => _repository.SaveSettings(appSettings),
            () => Navigate(CsvImport, "CSV取込"));
        Settings = new SettingsViewModel(
            () => _repository.GetSettings(),
            appSettings => _repository.SaveSettings(appSettings),
            () => _repository.GetRecentApiFetchLogs(100),
            ApplySavedUiSettings);

        _currentPage = Dashboard;

        ShowDashboardCommand = new RelayCommand(() => Navigate(Dashboard, "ダッシュボード"));
        ShowSimpleRegistrationCommand = new RelayCommand(ShowSimpleRegistration);
        ShowStockListCommand = new RelayCommand(() => Navigate(StockList, "銘柄一覧"));
        ShowStockEditorCommand = new RelayCommand(NewDetailedStock);
        ShowStockDetailCommand = new RelayCommand(() =>
        {
            if (StockList.SelectedRow is not null)
            {
                ShowStockDetail(StockList.SelectedRow);
                return;
            }

            var stockId = _snapshots.FirstOrDefault()?.Position.Stock.Id;
            if (stockId is not null)
            {
                ShowStockDetail(stockId.Value);
            }
        });
        ShowDividendsCommand = new RelayCommand(() => Navigate(Dividends, "配当実績"));
        ShowPassiveIncomeCommand = new RelayCommand(() => Navigate(PassiveIncome, "不労所得"));
        ShowSimulationCommand = new RelayCommand(() => Navigate(Simulation, "未来シミュレーション"));
        ShowCsvImportCommand = new RelayCommand(() => Navigate(CsvImport, "CSV取込"));
        ShowBrokerIntegrationCommand = new RelayCommand(() => Navigate(BrokerIntegration, "取込・統合設定"));
        ShowSettingsCommand = new RelayCommand(() =>
        {
            Settings.Load();
            Navigate(Settings, "設定");
        });
        RefreshCommand = new RelayCommand(LoadData);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ToggleSampleModeCommand = new RelayCommand(ToggleSampleMode);
        ResetSampleDataCommand = new RelayCommand(ResetSampleData);

        if (DataDisplayModes.IsSample(settings.DataDisplayMode))
        {
            EnableSampleMode(settings, persistNormalSetting: false);
        }

        LoadData();
    }

    public DashboardViewModel Dashboard { get; }
    public SimpleRegistrationViewModel SimpleRegistration { get; }
    public StockListViewModel StockList { get; }
    public StockEditorViewModel StockEditor { get; }
    public StockDetailViewModel StockDetail { get; }
    public DividendsViewModel Dividends { get; }
    public PassiveIncomeViewModel PassiveIncome { get; }
    public SimulationViewModel Simulation { get; }
    public CsvImportViewModel CsvImport { get; }
    public BrokerIntegrationViewModel BrokerIntegration { get; }
    public SettingsViewModel Settings { get; }

    public ICommand ShowDashboardCommand { get; }
    public ICommand ShowSimpleRegistrationCommand { get; }
    public ICommand ShowStockListCommand { get; }
    public ICommand ShowStockEditorCommand { get; }
    public ICommand ShowStockDetailCommand { get; }
    public ICommand ShowDividendsCommand { get; }
    public ICommand ShowPassiveIncomeCommand { get; }
    public ICommand ShowSimulationCommand { get; }
    public ICommand ShowCsvImportCommand { get; }
    public ICommand ShowBrokerIntegrationCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand ToggleSampleModeCommand { get; }
    public ICommand ResetSampleDataCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => SetProperty(ref _currentTitle, value);
    }

    public string WindowTitle => IsSampleMode ? "Investment Story - Sample" : "Investment Story";
    public string DatabasePath => IsSampleMode
        ? $"{DatabasePaths.SampleSessionDatabaseFileName} / 通常DBは非表示"
        : _repository.DatabasePath;
    public string DataModeBadge => IsSampleMode ? "SAMPLE MODE" : "NORMAL MODE";
    public string DataModeDescription => IsSampleMode ? "サンプルデータ表示中" : "通常データ表示中";
    public string SampleModeToggleText => IsSampleMode ? "通常データへ戻る" : "サンプルモード";
    public Visibility SampleModeVisibility => IsSampleMode ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        private set
        {
            if (SetProperty(ref _isSidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(SidebarTextVisibility));
                OnPropertyChanged(nameof(SidebarFooterVisibility));
                OnPropertyChanged(nameof(SidebarToggleText));
            }
        }
    }

    public GridLength SidebarWidth => IsSidebarCollapsed ? new GridLength(72) : new GridLength(250);
    public Visibility SidebarTextVisibility => IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SidebarFooterVisibility => IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
    public string SidebarToggleText => IsSidebarCollapsed ? ">" : "<";

    public bool IsSampleMode
    {
        get => _isSampleMode;
        private set
        {
            if (SetProperty(ref _isSampleMode, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(DatabasePath));
                OnPropertyChanged(nameof(DataModeBadge));
                OnPropertyChanged(nameof(DataModeDescription));
                OnPropertyChanged(nameof(SampleModeToggleText));
                OnPropertyChanged(nameof(SampleModeVisibility));
            }
        }
    }

    private void LoadData()
    {
        var positions = _repository.GetPositions();
        var usdJpyQuote = GetUsdJpyRate();
        if (!IsSampleMode && ApplyLatestExchangeRate(positions, usdJpyQuote))
        {
            positions = _repository.GetPositions();
        }

        _snapshots = positions.Select(_calculator.CreateSnapshot).ToList();
        var dividends = _repository.GetDividendPayments();
        var goal = _repository.GetGoal(DateTime.Today.Year);
        var realizedGainLossJpy = _repository.GetAllBrokerTrades().Sum(x => x.RealizedGainLossJpy);
        var returnSummary = _portfolioAnalyticsService.BuildReturnSummary(_snapshots, dividends, realizedGainLossJpy);
        SaveCurrentPortfolioSnapshot(dividends, realizedGainLossJpy, usdJpyQuote);
        var portfolioSnapshots = _repository.GetPortfolioSnapshots();
        var comparison = _portfolioAnalyticsService.CompareSnapshots(portfolioSnapshots, DateTime.Today);
        var summary = _calculator.CreateDashboardSummary(
            _snapshots,
            dividends,
            goal,
            DateTime.Today,
            IsLiveExchangeRate(usdJpyQuote) ? usdJpyQuote : null,
            returnSummary,
            comparison);
        var monthly = _calculator.AggregateMonthlyDividends(dividends, DateTime.Today.Year);
        var yearly = _calculator.AggregateYearlyDividends(dividends);
        var byStock = _calculator.AggregateDividendsByStock(dividends);
        var monthlyBreakdown = _portfolioAnalyticsService.BuildMonthlyDividendBreakdown(
            dividends,
            DateTime.Today.Year,
            goal?.MonthlyPassiveIncomeGoal ?? 0m);
        var dividendRankings = PassiveIncome.RankingModes.ToDictionary(
            mode => mode,
            mode => _portfolioAnalyticsService.BuildDividendRanking(dividends, mode, DateTime.Today.Year, _snapshots));

        SaveDataQuality(positions);

        Dashboard.Update(summary, _snapshots, portfolioSnapshots);
        StockList.Update(_snapshots, dividends);
        Dividends.Update(positions, dividends);
        PassiveIncome.Update(summary, goal, monthly, yearly, byStock, monthlyBreakdown, dividendRankings);
        Simulation.UpdateCurrentAnnualIncome(summary.AnnualPassiveIncomeForecastJpy);
        BrokerIntegration.Update(positions, _repository.GetSettings());

        var detailSnapshots = ResolveDetailSnapshots();
        UpdateStockDetail(detailSnapshots);
    }

    private void RecordCurrentPortfolioSnapshot()
    {
        var dividends = _repository.GetDividendPayments();
        var realizedGainLossJpy = _repository.GetAllBrokerTrades().Sum(x => x.RealizedGainLossJpy);
        SaveCurrentPortfolioSnapshot(dividends, realizedGainLossJpy, GetUsdJpyRate());
        LoadData();
    }

    private ExchangeRateQuote GetUsdJpyRate()
    {
        if (!IsSampleMode)
        {
            return _exchangeRateService.GetUsdJpyRate();
        }

        return new ExchangeRateQuote
        {
            BaseCurrency = "USD",
            QuoteCurrency = "JPY",
            Rate = 162.35m,
            AcquiredAt = DateTime.Now,
            Source = "SampleExchangeRateService",
            InputType = "Sample"
        };
    }

    private void SaveCurrentPortfolioSnapshot(
        IReadOnlyList<DividendPayment> dividends,
        decimal realizedGainLossJpy,
        ExchangeRateQuote usdJpyQuote)
    {
        var snapshot = _portfolioAnalyticsService.CreatePortfolioSnapshot(
            _snapshots,
            dividends,
            realizedGainLossJpy,
            ResolveSnapshotUsdRate(usdJpyQuote),
            DateTime.Today);
        _repository.SavePortfolioSnapshot(snapshot);
    }

    private decimal ResolveSnapshotUsdRate(ExchangeRateQuote quote)
    {
        if (quote.Rate > 0m)
        {
            return quote.Rate;
        }

        return _snapshots
            .Where(x => x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Position.CurrentHolding.CurrentExchangeRate)
            .FirstOrDefault(x => x > 0m);
    }

    private void SaveDataQuality(IReadOnlyList<StockPosition> positions)
    {
        var items = positions
            .Where(x => x.Stock.Id > 0)
            .SelectMany(x => _dataQualityService.BuildForPosition(x, DateTime.Today))
            .ToList();
        if (items.Count > 0)
        {
            _repository.SaveDataQualityInfos(items);
        }
    }

    private bool ApplyLatestExchangeRate(IReadOnlyList<StockPosition> positions, ExchangeRateQuote quote)
    {
        if (!IsLiveExchangeRate(quote) || quote.Rate <= 0m)
        {
            return false;
        }

        var changed = false;
        foreach (var position in positions.Where(x => IsUsdCurrency(x.Stock.Currency)))
        {
            if (Math.Abs(position.CurrentHolding.CurrentExchangeRate - quote.Rate) < 0.0001m &&
                string.Equals(position.CurrentHolding.ExchangeRateSource, quote.Source, StringComparison.OrdinalIgnoreCase) &&
                position.CurrentHolding.ExchangeRateAcquiredAt.Date == quote.AcquiredAt.Date)
            {
                continue;
            }

            position.CurrentHolding.CurrentExchangeRate = quote.Rate;
            position.CurrentHolding.ExchangeRateAcquiredAt = quote.AcquiredAt;
            position.CurrentHolding.ExchangeRateSource = quote.Source;
            position.CurrentHolding.ExchangeRateInputType = quote.InputType;
            position.CurrentHolding.UpdatedAt = DateTime.Today;
            _repository.SavePosition(position);
            changed = true;
        }

        return changed;
    }

    private static bool IsLiveExchangeRate(ExchangeRateQuote quote) =>
        quote.InputType.Equals("API", StringComparison.OrdinalIgnoreCase) &&
        !quote.Source.Contains("Mock", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsdCurrency(string currency) =>
        currency.Equals("USD", StringComparison.OrdinalIgnoreCase);

    private void Navigate(object page, string title)
    {
        if (ReferenceEquals(page, StockDetail))
        {
            title = "銘柄詳細";
        }

        CurrentPage = page;
        CurrentTitle = title;
        var settings = _repository.GetSettings();
        settings.LastOpenedPage = title;
        _repository.SaveSettings(settings);
    }

    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        var settings = _repository.GetSettings();
        settings.IsSidebarCollapsed = IsSidebarCollapsed;
        _repository.SaveSettings(settings);
    }

    private void ToggleSampleMode()
    {
        if (IsSampleMode)
        {
            DisableSampleMode();
            return;
        }

        EnableSampleMode(_normalRepository.GetSettings(), persistNormalSetting: true);
        LoadData();
        Navigate(Dashboard, "ダッシュボード");
    }

    private void ResetSampleData()
    {
        if (!IsSampleMode)
        {
            return;
        }

        EnableSampleMode(_normalRepository.GetSettings(), persistNormalSetting: false);
        LoadData();
        Navigate(Dashboard, "ダッシュボード");
    }

    private void EnableSampleMode(AppSettings baseSettings, bool persistNormalSetting)
    {
        if (persistNormalSetting)
        {
            var normalSettings = CopySettings(_normalRepository.GetSettings());
            normalSettings.DataDisplayMode = DataDisplayModes.Sample;
            _normalRepository.SaveSettings(normalSettings);
        }

        var sampleSettings = CopySettings(baseSettings);
        sampleSettings.DataDisplayMode = DataDisplayModes.Sample;
        sampleSettings.MarketDataMode = "Mock";
        sampleSettings.ExchangeRateProvider = "Mock";
        sampleSettings.BrokerDataMode = "Sample";
        _repository = SampleDataSeeder.ResetSampleSessionDatabase(sampleSettings);
        _selectedDetailStockId = null;
        IsSampleMode = true;
        Settings.Load();
    }

    private void DisableSampleMode()
    {
        var normalSettings = CopySettings(_normalRepository.GetSettings());
        normalSettings.DataDisplayMode = DataDisplayModes.Normal;
        _normalRepository.SaveSettings(normalSettings);
        _repository = _normalRepository;
        _selectedDetailStockId = null;
        IsSampleMode = false;
        DatabasePaths.DeleteSampleSessionDatabase();
        Settings.Load();
        LoadData();
        Navigate(Dashboard, "ダッシュボード");
    }

    private static AppSettings CopySettings(AppSettings source) =>
        new()
        {
            DataDisplayMode = source.DataDisplayMode,
            MarketDataMode = source.MarketDataMode,
            UsMarketDataProvider = source.UsMarketDataProvider,
            JapanMarketDataProvider = source.JapanMarketDataProvider,
            ExchangeRateProvider = source.ExchangeRateProvider,
            BrokerDataMode = source.BrokerDataMode,
            AlphaVantageApiKey = source.AlphaVantageApiKey,
            JQuantsApiKey = source.JQuantsApiKey,
            ApiTimeoutSeconds = source.ApiTimeoutSeconds,
            UseLastValueOnApiFailure = source.UseLastValueOnApiFailure,
            SaveLoginCredentials = source.SaveLoginCredentials,
            EnableApiResponseLog = source.EnableApiResponseLog,
            ThemeMode = source.ThemeMode,
            IsSidebarCollapsed = source.IsSidebarCollapsed,
            StockListDisplayMode = source.StockListDisplayMode,
            LastDashboardCompositionMode = source.LastDashboardCompositionMode,
            LastOpenedPage = source.LastOpenedPage
        };

    private void SaveStockListDisplayMode(string displayMode)
    {
        var settings = _repository.GetSettings();
        settings.StockListDisplayMode = displayMode;
        _repository.SaveSettings(settings);
    }

    private void ApplySavedUiSettings(AppSettings settings)
    {
        ThemeManager.Apply(settings.ThemeMode);
        IsSidebarCollapsed = settings.IsSidebarCollapsed;
    }

    private void ShowSimpleRegistration()
    {
        Navigate(SimpleRegistration, "かんたん登録");
    }

    private void NewDetailedStock()
    {
        var quote = _exchangeRateService.GetUsdJpyRate();
        StockEditor.Load(null);
        StockEditor.ApplyDefaultExchangeRate(quote.Rate, quote.AcquiredAt, quote.Source, quote.InputType);
        Navigate(StockEditor, "詳細登録・編集");
    }

    private void EditStock(int stockId)
    {
        var position = _repository.GetPosition(stockId);
        if (position is null)
        {
            return;
        }

        StockEditor.Load(position);
        Navigate(StockEditor, "詳細登録・編集");
    }

    private void SaveStock(StockPosition position)
    {
        var stockId = _repository.SavePosition(position);
        _selectedDetailStockId = stockId;
        _selectedDetailKey = null;
        _selectedDetailIsAggregate = false;
        LoadData();
        ShowStockDetail(stockId);
    }

    private void DeleteStock(int stockId)
    {
        _repository.DeleteStock(stockId);
        if (_selectedDetailStockId == stockId)
        {
            _selectedDetailStockId = null;
            _selectedDetailKey = null;
            _selectedDetailIsAggregate = false;
        }

        LoadData();
        Navigate(StockList, "銘柄一覧");
    }

    private void ShowStockDetail(int stockId)
    {
        _selectedDetailStockId = stockId;
        _selectedDetailKey = null;
        _selectedDetailIsAggregate = false;
        var snapshot = _snapshots.FirstOrDefault(x => x.Position.Stock.Id == stockId);
        UpdateStockDetail(snapshot is null ? Array.Empty<StockSnapshot>() : new[] { snapshot });
        Navigate(StockDetail, "銘柄詳細");
    }

    private void ShowStockDetail(StockRowViewModel row)
    {
        _selectedDetailStockId = row.StockId;
        _selectedDetailKey = row.DetailKey;
        _selectedDetailIsAggregate = row.IsAggregated;
        UpdateStockDetail(row.Snapshots);
        Navigate(StockDetail, "驫俶氛隧ｳ邏ｰ");
    }

    private void UpdateStockDetail(IReadOnlyList<StockSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            StockDetail.Update((StockSnapshot?)null, null, Array.Empty<BrokerTrade>(), Array.Empty<DataQualityInfo>());
            return;
        }

        var snapshot = snapshots[0];
        var stockIds = snapshots.Select(x => x.Position.Stock.Id).ToList();
        StockDetail.Update(
            snapshots,
            snapshots.Count == 1 ? _storyGenerator.Generate(snapshot) : BuildAggregateStoryForDisplay(snapshots),
            _repository.GetBrokerTrades(stockIds),
            _repository.GetDataQualityInfos(stockIds));
    }

    private void RefreshSelectedMarketData(int stockId)
    {
        var target = _snapshots.Where(x => x.Position.Stock.Id == stockId);
        RefreshMarketData(target, missingOnly: false);
    }

    private void RefreshAllMarketData()
    {
        RefreshMarketData(_snapshots, missingOnly: false);
    }

    private void RefreshMissingMarketData()
    {
        RefreshMarketData(_snapshots, missingOnly: true);
    }

    private void RefreshMarketData(IEnumerable<StockSnapshot> snapshots, bool missingOnly)
    {
        var targets = snapshots
            .Select(x => x.Position)
            .Where(IsMarketDataRefreshTarget)
            .Where(x => !missingOnly || x.CurrentHolding.CurrentPrice <= 0m || string.IsNullOrWhiteSpace(x.CurrentHolding.CurrentPriceSource))
            .ToList();

        if (targets.Count == 0)
        {
            StockList.Message = "API更新対象の保有銘柄がありません。";
            return;
        }

        if (IsSampleMode)
        {
            RefreshSampleMarketData(targets);
            return;
        }

        var settings = BuildLiveMarketDataSettings(_repository.GetSettings());
        var updated = 0;
        var failed = 0;
        var latestPriceAt = DateTime.MinValue;
        var sources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var position in targets)
        {
            var symbol = ResolveMarketDataSymbol(position);
            if (string.IsNullOrWhiteSpace(symbol))
            {
                failed++;
                continue;
            }

            var result = _marketDataService.GetQuote(symbol, settings);
            SaveApiFetchLogs(result.Logs);
            if (!result.IsSuccess || result.Quote is null)
            {
                failed++;
                if (errors.Count < 3)
                {
                    errors.Add($"{position.Stock.Ticker}: {result.ErrorMessage}");
                }

                continue;
            }

            if (ApplyMarketQuote(position, result.Quote))
            {
                _repository.SavePosition(position);
                updated++;
                if (position.CurrentHolding.CurrentPriceAcquiredAt > latestPriceAt)
                {
                    latestPriceAt = position.CurrentHolding.CurrentPriceAcquiredAt;
                }

                if (!string.IsNullOrWhiteSpace(position.CurrentHolding.CurrentPriceSource))
                {
                    sources.Add(position.CurrentHolding.CurrentPriceSource);
                }
            }
        }

        LoadData();
        var errorText = errors.Count == 0 ? string.Empty : $" 失敗例: {string.Join(" / ", errors)}";
        var latestText = latestPriceAt == DateTime.MinValue ? string.Empty : $" 最終株価取得: {latestPriceAt:yyyy/MM/dd HH:mm}";
        var sourceText = sources.Count == 0 ? string.Empty : $" 取得元: {string.Join(", ", sources.Take(3))}";
        StockList.Message = $"API更新を実行しました。更新 {updated}件、失敗 {failed}件。{latestText}{sourceText}{errorText}";
    }

    private void RefreshSampleMarketData(IReadOnlyList<StockPosition> targets)
    {
        var updated = 0;
        foreach (var position in targets)
        {
            var symbol = ResolveMarketDataSymbol(position);
            if (!SampleQuotes.TryGetValue(symbol, out var quote))
            {
                continue;
            }

            position.CurrentHolding.CurrentPrice = quote.Price;
            position.CurrentHolding.AnnualDividendPerShare = quote.AnnualDividend;
            position.CurrentHolding.CurrentPriceAcquiredAt = DateTime.Now;
            position.CurrentHolding.CurrentPriceSource = "SampleMarketDataService";
            position.CurrentHolding.DividendInfoAcquiredAt = DateTime.Now;
            position.CurrentHolding.DividendInfoSource = "SampleDividendDataService";
            position.CurrentHolding.DividendStatus = quote.AnnualDividend > 0m ? "配当あり" : "配当なし";
            position.CurrentHolding.DividendFrequency = quote.AnnualDividend > 0m ? "年4回" : "なし";
            if (position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            {
                position.CurrentHolding.CurrentExchangeRate = 162.35m;
                position.CurrentHolding.ExchangeRateAcquiredAt = DateTime.Now;
                position.CurrentHolding.ExchangeRateSource = "SampleExchangeRateService";
                position.CurrentHolding.ExchangeRateInputType = "Sample";
            }
            else
            {
                position.CurrentHolding.CurrentExchangeRate = 1m;
            }

            position.CurrentHolding.UpdatedAt = DateTime.Today;
            _repository.SavePosition(position);
            updated++;
        }

        LoadData();
        StockList.Message = $"サンプルモードの固定データで更新しました。更新 {updated}件。外部APIは呼び出していません。";
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

    private static bool IsMarketDataRefreshTarget(StockPosition position)
    {
        if (position.IsMutualFund)
        {
            return false;
        }

        if (position.CurrentHolding.CurrentShares <= 0m)
        {
            return false;
        }

        var ticker = position.Stock.Ticker.Trim();
        return !string.IsNullOrWhiteSpace(ticker) &&
               !ticker.StartsWith("FUND:", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMarketDataSymbol(StockPosition position)
    {
        var ticker = position.Stock.Ticker.Trim().ToUpperInvariant();
        return ticker.EndsWith(".T", StringComparison.Ordinal) ? ticker[..^2] : ticker;
    }

    private static bool ApplyMarketQuote(StockPosition position, MarketDataQuote quote)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(quote.Name) && string.IsNullOrWhiteSpace(position.Stock.Name))
        {
            position.Stock.Name = quote.Name;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(quote.Country))
        {
            position.Stock.Country = quote.Country;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(quote.Currency))
        {
            position.Stock.Currency = quote.Currency.Trim().ToUpperInvariant();
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(quote.Market))
        {
            position.Stock.Market = quote.Market;
            changed = true;
        }

        if (quote.CurrentPrice is > 0m)
        {
            position.CurrentHolding.CurrentPrice = quote.CurrentPrice.Value;
            position.CurrentHolding.CurrentPriceAcquiredAt = quote.PriceAcquiredAt ?? DateTime.Now;
            position.CurrentHolding.CurrentPriceSource = quote.Source;
            position.CurrentHolding.UpdatedAt = DateTime.Today;
            changed = true;
        }

        if (quote.AnnualDividendPerShare is >= 0m)
        {
            position.CurrentHolding.AnnualDividendPerShare = quote.AnnualDividendPerShare.Value;
            position.CurrentHolding.DividendStatus = quote.AnnualDividendPerShare.Value > 0m ? "配当あり" : "配当なし";
            position.CurrentHolding.DividendInfoAcquiredAt = DateTime.Now;
            position.CurrentHolding.DividendInfoSource = string.IsNullOrWhiteSpace(quote.DividendInfoSource)
                ? quote.Source
                : quote.DividendInfoSource;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(quote.DividendFrequency))
        {
            position.CurrentHolding.DividendFrequency = quote.DividendFrequency;
            changed = true;
        }

        if (position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase))
        {
            position.Purchase.ExchangeRate = 1m;
            position.CurrentHolding.CurrentExchangeRate = 1m;
            changed = true;
        }
        else if (quote.UsdJpyRate is > 0m)
        {
            position.CurrentHolding.CurrentExchangeRate = quote.UsdJpyRate.Value;
            position.CurrentHolding.ExchangeRateAcquiredAt = quote.ExchangeRateAcquiredAt ?? DateTime.Now;
            position.CurrentHolding.ExchangeRateSource = quote.Source;
            position.CurrentHolding.ExchangeRateInputType = "API";
            changed = true;
        }

        return changed;
    }

    private void SaveDividend(DividendPayment payment)
    {
        _repository.SaveDividendPayment(payment);
        LoadData();
    }

    private void DeleteDividend(int paymentId)
    {
        _repository.DeleteDividendPayment(paymentId);
        LoadData();
    }

    private void UpdateDividendSchedules()
    {
        var result = _dividendScheduleService.BuildSchedules(
            _repository.GetPositions(),
            _repository.GetDividendPayments(),
            _repository.GetTaxProfiles(),
            DateTime.Today);

        foreach (var schedule in result.Schedules)
        {
            _repository.SaveDividendPayment(schedule);
        }

        LoadData();
    }

    private void SaveGoal(IncomeGoal goal)
    {
        _repository.SaveGoal(goal);
        LoadData();
    }

    private void SaveApiFetchLogs(IEnumerable<ApiFetchLogEntry> logs)
    {
        if (IsSampleMode)
        {
            return;
        }

        _repository.SaveApiFetchLogs(logs);
    }

    private IReadOnlyList<StockSnapshot> ResolveDetailSnapshots()
    {
        if (_selectedDetailIsAggregate && !string.IsNullOrWhiteSpace(_selectedDetailKey))
        {
            var aggregated = _snapshots
                .Where(x => string.Equals(SecurityIdentityService.BuildCanonicalKey(x.Position), _selectedDetailKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (aggregated.Count > 0)
            {
                return aggregated;
            }
        }

        if (!_selectedDetailIsAggregate && !string.IsNullOrWhiteSpace(_selectedDetailKey))
        {
            var selectedByKey = _snapshots
                .Where(x => string.Equals(SecurityIdentityService.BuildPositionKey(x.Position), _selectedDetailKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (selectedByKey.Count > 0)
            {
                return selectedByKey;
            }
        }

        if (_selectedDetailStockId is not null)
        {
            var selected = _snapshots.FirstOrDefault(x => x.Position.Stock.Id == _selectedDetailStockId);
            if (selected is not null)
            {
                return new[] { selected };
            }
        }

        var first = _snapshots.FirstOrDefault();
        _selectedDetailStockId = first?.Position.Stock.Id;
        return first is null ? Array.Empty<StockSnapshot>() : new[] { first };
    }

    private static string BuildAggregateStoryForDisplay(IReadOnlyList<StockSnapshot> snapshots)
    {
        var first = snapshots[0];
        var quantity = snapshots.Sum(x => x.Position.IsMutualFund ? x.Position.MutualFund.UnitsHeld : x.Position.CurrentHolding.CurrentShares);
        var marketValue = snapshots.Sum(x => x.CurrentMarketValueJpy);
        var costBasis = snapshots.Sum(x => x.PurchaseTotalJpy);
        var gainLoss = marketValue - costBasis;
        var brokers = snapshots
            .Select(x => x.Position.Stock.Broker)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return $"{first.Position.Stock.Ticker} / {first.Position.Stock.Name} は全口座集約で {snapshots.Count:N0} ポジション、{brokers:N0} 証券会社に分かれて保有しています。" +
               $"保有数量合計は {quantity:N2}、取得額合計は {Formatters.Jpy(costBasis)}、評価額合計は {Formatters.Jpy(marketValue)}、含み損益は {Formatters.SignedJpy(gainLoss)} です。" +
               "取引履歴、配当、データ品質は集約対象ポジションをまとめて表示しています。";
    }

    private static string BuildAggregateStory(IReadOnlyList<StockSnapshot> snapshots)
    {
        var first = snapshots[0];
        var quantity = snapshots.Sum(x => x.Position.IsMutualFund ? x.Position.MutualFund.UnitsHeld : x.Position.CurrentHolding.CurrentShares);
        var marketValue = snapshots.Sum(x => x.CurrentMarketValueJpy);
        var costBasis = snapshots.Sum(x => x.PurchaseTotalJpy);
        var gainLoss = marketValue - costBasis;
        var brokers = snapshots.Select(x => x.Position.Stock.Broker).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return $"{first.Position.Stock.Ticker} / {first.Position.Stock.Name} は全口座集約で {snapshots.Count:N0} ポジション、{brokers:N0} 証券会社に分かれています。保有数量合計は {quantity:N2}、取得金額合計は {Formatters.Jpy(costBasis)}、評価額合計は {Formatters.Jpy(marketValue)}、含み損益は {Formatters.SignedJpy(gainLoss)} です。取引履歴とデータ品質は対象ポジション全件を集約して表示しています。";
    }
}
