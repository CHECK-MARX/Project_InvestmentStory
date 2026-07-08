using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly InvestmentStoryRepository _repository;
    private readonly InvestmentCalculator _calculator = new();
    private readonly StoryGenerator _storyGenerator = new();
    private readonly DividendScheduleService _dividendScheduleService = new();
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IMarketDataService _marketDataService = new MarketDataProviderFactory();
    private readonly IStockLookupService _stockLookupService = new CompositeStockLookupService(
        new LocalStockLookupService(),
        new YahooFinanceStockLookupService());
    private IReadOnlyList<StockSnapshot> _snapshots = Array.Empty<StockSnapshot>();
    private int? _selectedDetailStockId;
    private object _currentPage;
    private string _currentTitle = "ダッシュボード";

    public MainViewModel()
    {
        _repository = new InvestmentStoryRepository();
        _repository.Initialize();
        _exchangeRateService = new ExchangeRateProviderFactory(_repository.GetSettings);

        Dashboard = new DashboardViewModel();
        SimpleRegistration = new SimpleRegistrationViewModel(
            SaveStock,
            _calculator,
            _exchangeRateService,
            _stockLookupService,
            _marketDataService,
            _repository.GetSettings,
            SaveApiFetchLogs);
        StockList = new StockListViewModel(
            ShowSimpleRegistration,
            EditStock,
            ShowStockDetail,
            DeleteStock,
            RefreshSelectedMarketData,
            RefreshAllMarketData,
            RefreshMissingMarketData);
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
            _repository.GetPositions,
            _repository.SavePosition,
            payment => _repository.SaveDividendPayment(payment),
            _repository.GetDividendPayments,
            LoadData);
        BrokerIntegration = new BrokerIntegrationViewModel(
            _repository.GetSettings,
            _repository.SaveSettings,
            () => Navigate(CsvImport, "CSV取込"));
        Settings = new SettingsViewModel(_repository.GetSettings, _repository.SaveSettings, () => _repository.GetRecentApiFetchLogs(100));

        _currentPage = Dashboard;

        ShowDashboardCommand = new RelayCommand(() => Navigate(Dashboard, "ダッシュボード"));
        ShowSimpleRegistrationCommand = new RelayCommand(ShowSimpleRegistration);
        ShowStockListCommand = new RelayCommand(() => Navigate(StockList, "銘柄一覧"));
        ShowStockEditorCommand = new RelayCommand(NewDetailedStock);
        ShowStockDetailCommand = new RelayCommand(() =>
        {
            var stockId = StockList.SelectedRow?.StockId ?? _snapshots.FirstOrDefault()?.Position.Stock.Id;
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

    public string DatabasePath => _repository.DatabasePath;

    private void LoadData()
    {
        var positions = _repository.GetPositions();
        var usdJpyQuote = _exchangeRateService.GetUsdJpyRate();
        if (ApplyLatestExchangeRate(positions, usdJpyQuote))
        {
            positions = _repository.GetPositions();
        }

        _snapshots = positions.Select(_calculator.CreateSnapshot).ToList();
        var dividends = _repository.GetDividendPayments();
        var goal = _repository.GetGoal(DateTime.Today.Year);
        var summary = _calculator.CreateDashboardSummary(
            _snapshots,
            dividends,
            goal,
            DateTime.Today,
            IsLiveExchangeRate(usdJpyQuote) ? usdJpyQuote : null);
        var monthly = _calculator.AggregateMonthlyDividends(dividends, DateTime.Today.Year);
        var yearly = _calculator.AggregateYearlyDividends(dividends);
        var byStock = _calculator.AggregateDividendsByStock(dividends);

        Dashboard.Update(summary);
        StockList.Update(_snapshots);
        Dividends.Update(positions, dividends);
        PassiveIncome.Update(summary, goal, monthly, yearly, byStock);
        Simulation.UpdateCurrentAnnualIncome(summary.AnnualPassiveIncomeForecastJpy);
        BrokerIntegration.Update(positions, _repository.GetSettings());

        var detailSnapshot = ResolveDetailSnapshot();
        StockDetail.Update(detailSnapshot, detailSnapshot is null ? null : _storyGenerator.Generate(detailSnapshot));
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
        CurrentPage = page;
        CurrentTitle = title;
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
        LoadData();
        ShowStockDetail(stockId);
    }

    private void DeleteStock(int stockId)
    {
        _repository.DeleteStock(stockId);
        if (_selectedDetailStockId == stockId)
        {
            _selectedDetailStockId = null;
        }

        LoadData();
        Navigate(StockList, "銘柄一覧");
    }

    private void ShowStockDetail(int stockId)
    {
        _selectedDetailStockId = stockId;
        var snapshot = _snapshots.FirstOrDefault(x => x.Position.Stock.Id == stockId);
        StockDetail.Update(snapshot, snapshot is null ? null : _storyGenerator.Generate(snapshot));
        Navigate(StockDetail, "銘柄詳細");
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

        var settings = _repository.GetSettings();
        var updated = 0;
        var failed = 0;
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
            }
        }

        LoadData();
        var errorText = errors.Count == 0 ? string.Empty : $" 失敗例: {string.Join(" / ", errors)}";
        StockList.Message = $"API更新を実行しました。更新 {updated}件、失敗 {failed}件。{errorText}";
    }

    private static bool IsMarketDataRefreshTarget(StockPosition position)
    {
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
        _repository.SaveApiFetchLogs(logs);
    }

    private StockSnapshot? ResolveDetailSnapshot()
    {
        if (_selectedDetailStockId is not null)
        {
            var selected = _snapshots.FirstOrDefault(x => x.Position.Stock.Id == _selectedDetailStockId);
            if (selected is not null)
            {
                return selected;
            }
        }

        var first = _snapshots.FirstOrDefault();
        _selectedDetailStockId = first?.Position.Stock.Id;
        return first;
    }
}
