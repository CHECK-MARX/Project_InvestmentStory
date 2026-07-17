using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Controls;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class DividendsViewModel : ObservableObject
{
    private readonly Action<DividendPayment> _save;
    private readonly Action<int> _delete;
    private readonly Action _updateSchedules;
    private readonly Func<IReadOnlyList<TaxProfile>> _getTaxProfiles;
    private readonly Func<TaxProfile, int> _saveTaxProfile;
    private readonly Func<int, IncomeGoal?> _getGoal;
    private readonly DividendDashboardService _dashboardService = new();
    private List<DividendPayment> _allPayments = new();
    private IncomeGoal? _fallbackGoal;
    private DateTime _asOf = DateTime.Today;
    private DividendDashboardAnalysis _analysis = new();
    private DividendPaymentRowViewModel? _selectedPayment;
    private TaxProfile? _selectedTaxProfile;
    private int _paymentId;
    private int _selectedStockId;
    private bool _showDetails;
    private string _message = string.Empty;
    private int _selectedYear = DateTime.Today.Year;

    public DividendsViewModel(
        Action<DividendPayment> save,
        Action<int> delete,
        Action? updateSchedules = null,
        Func<IReadOnlyList<TaxProfile>>? getTaxProfiles = null,
        Func<TaxProfile, int>? saveTaxProfile = null,
        Func<int, IncomeGoal?>? getGoal = null)
    {
        _save = save;
        _delete = delete;
        _updateSchedules = updateSchedules ?? (() => { });
        _getTaxProfiles = getTaxProfiles ?? (() => Array.Empty<TaxProfile>());
        _saveTaxProfile = saveTaxProfile ?? (_ => 0);
        _getGoal = getGoal ?? (_ => null);
        NewCommand = new RelayCommand(NewPayment);
        SaveCommand = new RelayCommand(Save);
        DeleteCommand = new RelayCommand(Delete, () => SelectedPayment is not null);
        ToggleDetailsCommand = new RelayCommand(ToggleDetails);
        UpdateSchedulesCommand = new RelayCommand(UpdateSchedules);
        SaveTaxProfileCommand = new RelayCommand(SaveTaxProfile, () => SelectedTaxProfile is not null);
    }

    public ObservableCollection<StockOption> StockOptions { get; } = new();
    public ObservableCollection<DividendPaymentRowViewModel> Payments { get; } = new();
    public ObservableCollection<DividendPaymentRowViewModel> ActualPayments { get; } = new();
    public ObservableCollection<DividendPaymentRowViewModel> PlannedPayments { get; } = new();
    public ObservableCollection<DividendPaymentRowViewModel> ReplacedPayments { get; } = new();
    public ObservableCollection<int> YearOptions { get; } = new();
    public ObservableCollection<DividendDashboardEntryViewModel> DividendChartEntries { get; } = new();
    public ObservableCollection<DividendMonthlySummaryRowViewModel> MonthlySummaryRows { get; } = new();
    public ObservableCollection<DividendAnnualRankingRowViewModel> AnnualRankingRows { get; } = new();
    public ObservableCollection<TaxProfile> TaxProfiles { get; } = new();
    public DividendChartInteractionState DividendChartInteraction { get; } = new();
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ToggleDetailsCommand { get; }
    public ICommand UpdateSchedulesCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveTaxProfileCommand { get; }

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (SetProperty(ref _selectedYear, value))
            {
                RebuildDashboard();
            }
        }
    }

    public string ActualNetJpy => Formatters.Jpy(_analysis.ActualNetJpy);
    public string UpcomingNetJpy => Formatters.Jpy(_analysis.UpcomingNetJpy);
    public string YearEndForecastJpy => Formatters.Jpy(_analysis.YearEndForecastJpy);
    public string AnnualGoalJpy => Formatters.Jpy(_analysis.AnnualGoalJpy);
    public string AchievementRate => Formatters.Percent(_analysis.AchievementRate);
    public string RemainingJpy => Formatters.Jpy(_analysis.RemainingJpy);
    public string GrossTotalJpy => Formatters.Jpy(_analysis.GrossTotalJpy);
    public string ForeignTaxTotalJpy => Formatters.Jpy(_analysis.ForeignTaxTotalJpy);
    public string DomesticTaxTotalJpy => Formatters.Jpy(_analysis.DomesticTaxTotalJpy);
    public string UnmatchedCount => $"{_analysis.UnmatchedCount:N0}件";
    public string MaximumMonth => $"{_analysis.Bias.MaximumMonth}月 / {Formatters.Jpy(_analysis.Bias.MaximumMonthJpy)}";
    public string MinimumMonth => $"{_analysis.Bias.MinimumMonth}月 / {Formatters.Jpy(_analysis.Bias.MinimumMonthJpy)}";
    public string MonthlyAverage => Formatters.Jpy(_analysis.Bias.MonthlyAverageJpy);
    public string MonthlyMedian => Formatters.Jpy(_analysis.Bias.MonthlyMedianJpy);
    public string ZeroDividendMonthCount => $"{_analysis.Bias.ZeroDividendMonthCount:N0}か月";
    public string TopTwoConcentration => Formatters.Percent(_analysis.Bias.TopTwoMonthConcentrationRate);

    public DividendPaymentRowViewModel? SelectedPayment
    {
        get => _selectedPayment;
        set
        {
            if (SetProperty(ref _selectedPayment, value))
            {
                DeleteCommand.RaiseCanExecuteChanged();
                if (value is not null)
                {
                    LoadPayment(value.Payment);
                }
            }
        }
    }

    public int SelectedStockId
    {
        get => _selectedStockId;
        set
        {
            if (SetProperty(ref _selectedStockId, value))
            {
                ApplySelectedStockDefaults();
            }
        }
    }

    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public string AccountType { get; set; } = DividendConstants.AccountUnknown;
    public decimal Quantity { get; set; }
    public decimal DividendPerShare { get; set; }
    public string Broker { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal ForeignTaxAmount { get; set; }
    public decimal DomesticTaxAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal ExchangeRate { get; set; } = 1m;
    public DateTime ExchangeRateAcquiredAt { get; set; } = DateTime.Now;
    public string ExchangeRateSource { get; set; } = "手入力";
    public string ExchangeRateInputType { get; set; } = "手入力";
    public decimal JpyAmount { get; set; }
    public string Memo { get; set; } = string.Empty;

    public bool ShowDetails
    {
        get => _showDetails;
        private set
        {
            if (SetProperty(ref _showDetails, value))
            {
                OnPropertyChanged(nameof(DetailsButtonText));
            }
        }
    }

    public string DetailsButtonText => ShowDetails ? "詳細項目を隠す" : "詳細項目を表示";

    public TaxProfile? SelectedTaxProfile
    {
        get => _selectedTaxProfile;
        set
        {
            if (SetProperty(ref _selectedTaxProfile, value))
            {
                SaveTaxProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public void Update(
        IEnumerable<StockPosition> positions,
        IEnumerable<DividendPayment> payments,
        IncomeGoal? currentGoal = null,
        DateTime? asOf = null)
    {
        _allPayments = payments.ToList();
        _fallbackGoal = currentGoal;
        _asOf = (asOf ?? DateTime.Today).Date;
        var selectedStockId = SelectedStockId;
        StockOptions.Clear();
        foreach (var position in positions.OrderBy(x => x.Stock.Ticker))
        {
            StockOptions.Add(new StockOption
            {
                StockId = position.Stock.Id,
                Display = $"{position.Stock.Ticker} / {position.Stock.Name}",
                Currency = position.Stock.Currency,
                Broker = position.Stock.Broker,
                ExchangeRate = position.CurrentHolding.CurrentExchangeRate
            });
        }

        ReplacedPayments.Clear();
        foreach (var payment in _allPayments
                     .Where(x => string.Equals(x.DividendStatus, DividendConstants.Replaced, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(x => x.PaymentDate))
        {
            var row = new DividendPaymentRowViewModel(payment);
            ReplacedPayments.Add(row);
        }

        var availableYears = _allPayments.Select(x => x.PaymentDate.Year)
            .Append(_asOf.Year)
            .Where(x => x > 1900)
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();
        YearOptions.Clear();
        foreach (var year in availableYears)
        {
            YearOptions.Add(year);
        }
        _selectedYear = availableYears.Contains(_selectedYear) ? _selectedYear : _asOf.Year;
        OnPropertyChanged(nameof(SelectedYear));

        TaxProfiles.Clear();
        foreach (var profile in _getTaxProfiles())
        {
            TaxProfiles.Add(profile);
        }
        SelectedTaxProfile ??= TaxProfiles.FirstOrDefault();

        SelectedStockId = StockOptions.Any(x => x.StockId == selectedStockId)
            ? selectedStockId
            : StockOptions.FirstOrDefault()?.StockId ?? 0;
        RebuildDashboard();
        RefreshAllProperties();
    }

    private void RebuildDashboard()
    {
        var goal = _getGoal(SelectedYear);
        if (goal is null && _fallbackGoal?.TargetYear == SelectedYear)
        {
            goal = _fallbackGoal;
        }

        _analysis = _dashboardService.Build(
            _allPayments,
            SelectedYear,
            goal?.AnnualPassiveIncomeGoal ?? 0m,
            _asOf);

        Payments.Clear();
        PlannedPayments.Clear();
        ActualPayments.Clear();
        DividendChartEntries.Clear();
        MonthlySummaryRows.Clear();
        AnnualRankingRows.Clear();

        foreach (var entry in _analysis.Entries)
        {
            var paymentRow = new DividendPaymentRowViewModel(
                entry.Payment,
                entry.DisplayStatus,
                entry.DataQuality);
            Payments.Add(paymentRow);
            PlannedPayments.Add(paymentRow);
            DividendChartEntries.Add(new DividendDashboardEntryViewModel(entry));
            if (entry.IsActual)
            {
                ActualPayments.Add(paymentRow);
            }
        }

        foreach (var month in _analysis.Months)
        {
            MonthlySummaryRows.Add(new DividendMonthlySummaryRowViewModel(month));
        }

        foreach (var item in _analysis.Rankings.Select((ranking, index) =>
                     new DividendAnnualRankingRowViewModel(ranking, index + 1)))
        {
            AnnualRankingRows.Add(item);
        }

        RefreshAllProperties();
    }

    private void NewPayment()
    {
        _paymentId = 0;
        PaymentDate = DateTime.Today;
        AccountType = DividendConstants.AccountUnknown;
        Quantity = 0m;
        DividendPerShare = 0m;
        GrossAmount = 0m;
        ForeignTaxAmount = 0m;
        DomesticTaxAmount = 0m;
        TaxAmount = 0m;
        NetAmount = 0m;
        ExchangeRate = 1m;
        ExchangeRateAcquiredAt = DateTime.Now;
        ExchangeRateSource = "手入力";
        ExchangeRateInputType = "手入力";
        JpyAmount = 0m;
        Memo = string.Empty;
        Message = string.Empty;
        SelectedPayment = null;
        ApplySelectedStockDefaults();
        RefreshAllProperties();
    }

    private void LoadPayment(DividendPayment payment)
    {
        _paymentId = payment.Id;
        SelectedStockId = payment.StockId;
        PaymentDate = payment.PaymentDate;
        AccountType = payment.AccountType;
        Quantity = payment.Quantity;
        DividendPerShare = payment.DividendPerShare;
        Broker = payment.Broker;
        GrossAmount = payment.GrossAmount;
        ForeignTaxAmount = payment.ForeignTaxAmount;
        DomesticTaxAmount = payment.DomesticTaxAmount;
        TaxAmount = payment.TaxAmount;
        NetAmount = payment.NetAmount;
        Currency = payment.Currency;
        ExchangeRate = payment.ExchangeRate;
        ExchangeRateAcquiredAt = payment.ExchangeRateAcquiredAt;
        ExchangeRateSource = payment.ExchangeRateSource;
        ExchangeRateInputType = payment.ExchangeRateInputType;
        JpyAmount = payment.JpyAmount;
        Memo = payment.Memo;
        Message = string.Empty;
        RefreshAllProperties();
    }

    private void Save()
    {
        if (!Validate())
        {
            return;
        }

        var option = StockOptions.FirstOrDefault(x => x.StockId == SelectedStockId);
        var currency = NormalizeCurrency(string.IsNullOrWhiteSpace(Currency) ? option?.Currency ?? "USD" : Currency);
        var exchangeRate = NormalizeExchangeRateForCurrency(currency, ExchangeRate > 0m ? ExchangeRate : option?.ExchangeRate ?? 1m);
        var grossAmount = GrossAmount > 0m ? GrossAmount : NetAmount;
        var taxAmount = TaxAmount > 0m ? TaxAmount : ForeignTaxAmount + DomesticTaxAmount;
        var jpyAmount = JpyAmount > 0m
            ? JpyAmount
            : currency == "JPY" ? NetAmount : NetAmount * exchangeRate;
        var accountType = DividendConstants.NormalizeAccountType(AccountType);

        if (taxAmount > grossAmount)
        {
            Message = "税額は税引前金額以下にしてください。";
            return;
        }

        try
        {
            _save(new DividendPayment
            {
                Id = _paymentId,
                StockId = SelectedStockId,
                AccountType = accountType,
                TaxAccountType = accountType,
                PaymentDate = PaymentDate,
                Broker = string.IsNullOrWhiteSpace(Broker) ? option?.Broker ?? string.Empty : Broker.Trim(),
                DividendStatus = DividendConstants.Actual,
                Source = DividendConstants.SourceManual,
                SourcePriority = 50,
                Quantity = Quantity,
                DividendPerShare = DividendPerShare > 0m
                    ? DividendPerShare
                    : Quantity > 0m && grossAmount > 0m ? grossAmount / Quantity : 0m,
                GrossAmount = grossAmount,
                ForeignTaxAmount = ForeignTaxAmount,
                DomesticTaxAmount = DomesticTaxAmount > 0m ? DomesticTaxAmount : taxAmount,
                TotalTaxAmount = taxAmount,
                TaxAmount = taxAmount,
                NetAmount = NetAmount,
                Currency = currency,
                ExchangeRate = exchangeRate,
                ExchangeRateAcquiredAt = ExchangeRateAcquiredAt == default ? DateTime.Now : ExchangeRateAcquiredAt,
                ExchangeRateSource = string.IsNullOrWhiteSpace(ExchangeRateSource) ? "手入力" : ExchangeRateSource.Trim(),
                ExchangeRateInputType = string.IsNullOrWhiteSpace(ExchangeRateInputType) ? "手入力" : ExchangeRateInputType.Trim(),
                GrossAmountJpy = grossAmount * exchangeRate,
                ForeignTaxAmountJpy = ForeignTaxAmount * exchangeRate,
                DomesticTaxAmountJpy = (DomesticTaxAmount > 0m ? DomesticTaxAmount : taxAmount) * exchangeRate,
                TotalTaxAmountJpy = taxAmount * exchangeRate,
                NetAmountJpy = jpyAmount,
                JpyAmount = jpyAmount,
                IsTaxEstimated = false,
                IsNisa = DividendConstants.IsNisaAccount(accountType),
                IsForeignStock = currency != "JPY",
                Memo = Memo.Trim()
            });
            Message = "保存しました。";
        }
        catch (Exception ex)
        {
            Message = $"保存に失敗しました: {ex.Message}";
        }
    }

    private void Delete()
    {
        if (SelectedPayment is null)
        {
            return;
        }

        _delete(SelectedPayment.Id);
        Message = "削除しました。";
    }

    private void UpdateSchedules()
    {
        try
        {
            _updateSchedules();
            Message = "配当予定を更新しました。実績は上書きせず、予定・見込みのみ更新します。";
        }
        catch (Exception ex)
        {
            Message = $"配当予定の更新に失敗しました: {ex.Message}";
        }
    }

    private void SaveTaxProfile()
    {
        if (SelectedTaxProfile is null)
        {
            Message = "税率設定を選択してください。";
            return;
        }

        try
        {
            SelectedTaxProfile.Id = _saveTaxProfile(SelectedTaxProfile);
            Message = "税率設定を保存しました。予定配当は「予定を更新」で再計算できます。";
        }
        catch (Exception ex)
        {
            Message = $"税率設定の保存に失敗しました: {ex.Message}";
        }
    }

    private bool Validate()
    {
        if (SelectedStockId == 0)
        {
            Message = "銘柄を選択してください。";
            return false;
        }

        if (GrossAmount < 0m || TaxAmount < 0m || NetAmount <= 0m || ExchangeRate <= 0m || JpyAmount < 0m)
        {
            Message = "税引後金額と為替レートを確認してください。";
            return false;
        }

        if (GrossAmount > 0m && TaxAmount > GrossAmount)
        {
            Message = "税額は税引前金額以下にしてください。";
            return false;
        }

        Message = string.Empty;
        return true;
    }

    private void ToggleDetails()
    {
        ShowDetails = !ShowDetails;
    }

    private void ApplySelectedStockDefaults()
    {
        var option = StockOptions.FirstOrDefault(x => x.StockId == SelectedStockId);
        if (option is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Broker))
        {
            Broker = option.Broker;
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            Currency = option.Currency;
        }

        Currency = NormalizeCurrency(Currency);

        if (ExchangeRate <= 0m || ExchangeRate == 1m)
        {
            ExchangeRate = option.ExchangeRate;
            ExchangeRateAcquiredAt = DateTime.Now;
            ExchangeRateSource = "手入力";
            ExchangeRateInputType = "手入力";
        }

        if (Currency == "JPY")
        {
            ExchangeRate = 1m;
        }

        OnPropertyChanged(nameof(Broker));
        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(ExchangeRate));
        OnPropertyChanged(nameof(ExchangeRateAcquiredAt));
        OnPropertyChanged(nameof(ExchangeRateSource));
        OnPropertyChanged(nameof(ExchangeRateInputType));
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return "USD";
        }

        var normalized = currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "JPY" or "円" ? "JPY" : normalized;
    }

    private static decimal NormalizeExchangeRateForCurrency(string currency, decimal exchangeRate)
    {
        return currency == "JPY" ? 1m : exchangeRate;
    }
}
