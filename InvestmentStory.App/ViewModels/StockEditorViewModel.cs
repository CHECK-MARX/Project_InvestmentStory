using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class StockEditorViewModel : ObservableObject
{
    private readonly Action<StockPosition> _save;
    private readonly Action<int> _delete;
    private int _stockId;
    private int _purchaseId;
    private int _splitId;
    private int _currentHoldingId;
    private string _errorMessage = string.Empty;

    public StockEditorViewModel(Action<StockPosition> save, Action<int> delete)
    {
        _save = save;
        _delete = delete;
        SaveCommand = new RelayCommand(Save);
        NewCommand = new RelayCommand(() => Load(null));
        DeleteCommand = new RelayCommand(Delete, () => StockId != 0);
        Load(null);
    }

    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public string PageTitle => StockId == 0 ? "詳細登録" : "詳細登録・編集";
    public bool IsExisting => StockId != 0;

    public int StockId
    {
        get => _stockId;
        private set
        {
            if (SetProperty(ref _stockId, value))
            {
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(IsExisting));
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Name { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Country { get; set; } = "米国";
    public string Currency { get; set; } = "USD";
    public string Broker { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string DataSource { get; set; } = "手入力";
    public string StockMemo { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; } = DateTime.Today;
    public decimal PurchaseShares { get; set; }
    public decimal PurchaseUnitPrice { get; set; }
    public decimal PurchaseExchangeRate { get; set; } = 1m;
    public DateTime PurchaseExchangeRateAcquiredAt { get; set; } = DateTime.Now;
    public string PurchaseExchangeRateSource { get; set; } = "手入力";
    public string PurchaseExchangeRateInputType { get; set; } = "手入力";
    public decimal PurchaseFee { get; set; }
    public string PurchaseMemo { get; set; } = string.Empty;
    public DateTime SplitDate { get; set; } = DateTime.Today;
    public decimal SplitRatio { get; set; } = 1m;
    public string SplitMemo { get; set; } = string.Empty;
    public decimal CurrentShares { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CurrentExchangeRate { get; set; } = 1m;
    public DateTime CurrentExchangeRateAcquiredAt { get; set; } = DateTime.Now;
    public string CurrentExchangeRateSource { get; set; } = "手入力";
    public string CurrentExchangeRateInputType { get; set; } = "手入力";
    public decimal AnnualDividendPerShare { get; set; }
    public string DividendStatus { get; set; } = "配当未入力";
    public string DividendFrequency { get; set; } = "年4回";
    public string DividendMonths { get; set; } = string.Empty;
    public DateTime CurrentPriceAcquiredAt { get; set; } = DateTime.MinValue;
    public string CurrentPriceSource { get; set; } = string.Empty;
    public DateTime DividendInfoAcquiredAt { get; set; } = DateTime.MinValue;
    public string DividendInfoSource { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Today;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public void Load(StockPosition? position)
    {
        StockId = position?.Stock.Id ?? 0;
        _purchaseId = position?.Purchase.Id ?? 0;
        _splitId = position?.Split.Id ?? 0;
        _currentHoldingId = position?.CurrentHolding.Id ?? 0;
        Name = position?.Stock.Name ?? string.Empty;
        Ticker = position?.Stock.Ticker ?? string.Empty;
        Country = position?.Stock.Country ?? "米国";
        Currency = position?.Stock.Currency ?? "USD";
        Broker = position?.Stock.Broker ?? string.Empty;
        Sector = position?.Stock.Sector ?? string.Empty;
        Industry = position?.Stock.Industry ?? string.Empty;
        Market = position?.Stock.Market ?? string.Empty;
        DataSource = position?.Stock.DataSource ?? "手入力";
        StockMemo = position?.Stock.Memo ?? string.Empty;
        PurchaseDate = position?.Purchase.PurchaseDate ?? DateTime.Today;
        PurchaseShares = position?.Purchase.Shares ?? 0m;
        PurchaseUnitPrice = position?.Purchase.UnitPrice ?? 0m;
        PurchaseExchangeRate = position?.Purchase.ExchangeRate ?? 1m;
        PurchaseExchangeRateAcquiredAt = position?.Purchase.ExchangeRateAcquiredAt ?? DateTime.Now;
        PurchaseExchangeRateSource = position?.Purchase.ExchangeRateSource ?? "手入力";
        PurchaseExchangeRateInputType = position?.Purchase.ExchangeRateInputType ?? "手入力";
        PurchaseFee = position?.Purchase.Fee ?? 0m;
        PurchaseMemo = position?.Purchase.Memo ?? string.Empty;
        SplitDate = position?.Split.SplitDate ?? DateTime.Today;
        SplitRatio = position?.Split.SplitRatio ?? 1m;
        SplitMemo = position?.Split.Memo ?? string.Empty;
        CurrentShares = position?.CurrentHolding.CurrentShares ?? 0m;
        CurrentPrice = position?.CurrentHolding.CurrentPrice ?? 0m;
        CurrentExchangeRate = position?.CurrentHolding.CurrentExchangeRate ?? 1m;
        CurrentExchangeRateAcquiredAt = position?.CurrentHolding.ExchangeRateAcquiredAt ?? DateTime.Now;
        CurrentExchangeRateSource = position?.CurrentHolding.ExchangeRateSource ?? "手入力";
        CurrentExchangeRateInputType = position?.CurrentHolding.ExchangeRateInputType ?? "手入力";
        AnnualDividendPerShare = position?.CurrentHolding.AnnualDividendPerShare ?? 0m;
        DividendStatus = position?.CurrentHolding.DividendStatus ?? "配当未入力";
        DividendFrequency = position?.CurrentHolding.DividendFrequency ?? "年4回";
        DividendMonths = position?.CurrentHolding.DividendMonths ?? string.Empty;
        CurrentPriceAcquiredAt = position?.CurrentHolding.CurrentPriceAcquiredAt ?? DateTime.MinValue;
        CurrentPriceSource = position?.CurrentHolding.CurrentPriceSource ?? string.Empty;
        DividendInfoAcquiredAt = position?.CurrentHolding.DividendInfoAcquiredAt ?? DateTime.MinValue;
        DividendInfoSource = position?.CurrentHolding.DividendInfoSource ?? string.Empty;
        UpdatedAt = position?.CurrentHolding.UpdatedAt ?? DateTime.Today;
        ErrorMessage = string.Empty;
        RefreshAllProperties();
    }

    public void ApplyDefaultExchangeRate(decimal usdJpyRate, DateTime acquiredAt, string source, string inputType)
    {
        if (StockId != 0)
        {
            return;
        }

        PurchaseExchangeRate = usdJpyRate;
        PurchaseExchangeRateAcquiredAt = acquiredAt;
        PurchaseExchangeRateSource = source;
        PurchaseExchangeRateInputType = inputType;
        CurrentExchangeRate = usdJpyRate;
        CurrentExchangeRateAcquiredAt = acquiredAt;
        CurrentExchangeRateSource = source;
        CurrentExchangeRateInputType = inputType;
        RefreshAllProperties();
    }

    private void Save()
    {
        NormalizeCurrencyInputs();
        if (!Validate())
        {
            return;
        }

        try
        {
            _save(BuildPosition());
            ErrorMessage = "保存しました。";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存に失敗しました: {ex.Message}";
        }
    }

    private void Delete()
    {
        if (StockId == 0)
        {
            return;
        }

        _delete(StockId);
    }

    private StockPosition BuildPosition()
    {
        var currency = NormalizeCurrency(Currency);
        var purchaseExchangeRate = NormalizeExchangeRateForCurrency(currency, PurchaseExchangeRate);
        var currentExchangeRate = NormalizeExchangeRateForCurrency(currency, CurrentExchangeRate);

        return new StockPosition
        {
            Stock = new Stock
            {
                Id = StockId,
                Name = Name.Trim(),
                Ticker = Ticker.Trim().ToUpperInvariant(),
                Country = Country.Trim(),
                Currency = currency,
                Broker = Broker.Trim(),
                Sector = Sector.Trim(),
                Industry = Industry.Trim(),
                Market = Market.Trim(),
                DataSource = string.IsNullOrWhiteSpace(DataSource) ? "手入力" : DataSource.Trim(),
                Memo = StockMemo.Trim()
            },
            Purchase = new Purchase
            {
                Id = _purchaseId,
                StockId = StockId,
                PurchaseDate = PurchaseDate,
                Shares = PurchaseShares,
                UnitPrice = PurchaseUnitPrice,
                ExchangeRate = purchaseExchangeRate,
                ExchangeRateAcquiredAt = PurchaseExchangeRateAcquiredAt,
                ExchangeRateSource = PurchaseExchangeRateSource.Trim(),
                ExchangeRateInputType = PurchaseExchangeRateInputType.Trim(),
                Fee = PurchaseFee,
                Memo = PurchaseMemo.Trim()
            },
            Split = new StockSplit
            {
                Id = _splitId,
                StockId = StockId,
                SplitDate = SplitDate,
                SplitRatio = SplitRatio,
                Memo = SplitMemo.Trim()
            },
            CurrentHolding = new CurrentHolding
            {
                Id = _currentHoldingId,
                StockId = StockId,
                CurrentShares = CurrentShares,
                CurrentPrice = CurrentPrice,
                CurrentExchangeRate = currentExchangeRate,
                ExchangeRateAcquiredAt = CurrentExchangeRateAcquiredAt,
                ExchangeRateSource = CurrentExchangeRateSource.Trim(),
                ExchangeRateInputType = CurrentExchangeRateInputType.Trim(),
                AnnualDividendPerShare = AnnualDividendPerShare,
                DividendStatus = string.IsNullOrWhiteSpace(DividendStatus) ? "配当未入力" : DividendStatus.Trim(),
                DividendFrequency = DividendFrequency.Trim(),
                DividendMonths = DividendMonths.Trim(),
                CurrentPriceAcquiredAt = CurrentPriceAcquiredAt,
                CurrentPriceSource = CurrentPriceSource.Trim(),
                DividendInfoAcquiredAt = DividendInfoAcquiredAt,
                DividendInfoSource = DividendInfoSource.Trim(),
                UpdatedAt = UpdatedAt
            }
        };
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "銘柄名を入力してください。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Ticker))
        {
            ErrorMessage = "ティッカーを入力してください。";
            return false;
        }

        if (PurchaseShares <= 0 || PurchaseUnitPrice <= 0)
        {
            ErrorMessage = "購入株数と購入単価は0より大きい値を入力してください。";
            return false;
        }

        if (NormalizeExchangeRateForCurrency(NormalizeCurrency(Currency), PurchaseExchangeRate) <= 0 ||
            NormalizeExchangeRateForCurrency(NormalizeCurrency(Currency), CurrentExchangeRate) <= 0)
        {
            ErrorMessage = "為替レートは0より大きい値を入力してください。";
            return false;
        }

        if (SplitRatio <= 0)
        {
            ErrorMessage = "分割倍率は0より大きい値を入力してください。";
            return false;
        }

        if (CurrentShares < 0 || CurrentPrice < 0 || AnnualDividendPerShare < 0 || PurchaseFee < 0)
        {
            ErrorMessage = "株数、株価、配当、手数料にはマイナス値を入力できません。";
            return false;
        }

        if (DividendStatus == "配当あり" && AnnualDividendPerShare <= 0m)
        {
            ErrorMessage = "配当ありの場合は、1株あたり年間配当（年間合計）を0より大きい値で入力してください。";
            return false;
        }

        ErrorMessage = string.Empty;
        return true;
    }

    private void NormalizeCurrencyInputs()
    {
        var normalizedCurrency = NormalizeCurrency(Currency);
        Currency = normalizedCurrency;

        if (normalizedCurrency == "JPY")
        {
            PurchaseExchangeRate = 1m;
            CurrentExchangeRate = 1m;
        }

        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(PurchaseExchangeRate));
        OnPropertyChanged(nameof(CurrentExchangeRate));
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
