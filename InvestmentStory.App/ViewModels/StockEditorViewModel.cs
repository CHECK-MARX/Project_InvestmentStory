using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class StockEditorViewModel : ObservableObject
{
    private readonly Action<StockPosition> _save;
    private readonly Action<int> _delete;
    private readonly Func<string, bool>? _confirmChanges;
    private int _stockId;
    private int _purchaseId;
    private int _splitId;
    private int _currentHoldingId;
    private string _currentHoldingSourceFile = string.Empty;
    private int _currentHoldingSourceRowNumber;
    private decimal _loadedPurchaseShares;
    private decimal _loadedCurrentShares;
    private decimal _loadedCurrentPrice;
    private decimal _loadedCurrentExchangeRate;
    private string _loadedBroker = string.Empty;
    private decimal _loadedPurchaseUnitPrice;
    private decimal _loadedPurchaseExchangeRate;
    private decimal _loadedAcquisitionAmountJpy;
    private decimal _loadedReferenceAcquisitionAmountJpy;
    private decimal _loadedReferenceAcquisitionForeignAmount;
    private string _loadedReferenceAcquisitionSource = string.Empty;
    private string _loadedAcquisitionAmountSource = string.Empty;
    private DateTime _loadedAcquisitionAmountConfirmedAt = DateTime.MinValue;
    private decimal _confirmedAcquisitionAmountJpy;
    private string _acquisitionEvidence = string.Empty;
    private string _loadedCurrency = string.Empty;
    private DateTime _loadedPurchaseDate;
    private string _loadedAccountType = AccountTypes.Unknown;
    private string _loadedCustodyType = string.Empty;
    private string _errorMessage = string.Empty;

    public StockEditorViewModel(Action<StockPosition> save, Action<int> delete,
        Func<string, bool>? confirmChanges = null)
    {
        _save = save;
        _delete = delete;
        _confirmChanges = confirmChanges;
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
    public string ChangeReason { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; } = DateTime.MinValue;
    public DateTime? PurchaseDateSelection
    {
        get => PurchaseDate == DateTime.MinValue ? null : PurchaseDate;
        set => PurchaseDate = value ?? DateTime.MinValue;
    }
    public decimal PurchaseShares { get; set; }
    public decimal PurchaseUnitPrice { get; set; }
    public decimal PurchaseExchangeRate { get; set; } = 1m;
    public decimal ConfirmedAcquisitionAmountJpy
    {
        get => _confirmedAcquisitionAmountJpy;
        set
        {
            if (SetProperty(ref _confirmedAcquisitionAmountJpy, value))
                OnPropertyChanged(nameof(ReferenceDifference));
        }
    }
    public string AcquisitionEvidence
    {
        get => _acquisitionEvidence;
        set => SetProperty(ref _acquisitionEvidence, value);
    }
    public string ReferenceAcquisitionSummary => _loadedReferenceAcquisitionAmountJpy > 0m
        ? $"参考換算 {_loadedReferenceAcquisitionAmountJpy:N0}円 / {_loadedReferenceAcquisitionSource}"
        : "参考換算の記録なし";
    public string ReferenceDifference => _loadedReferenceAcquisitionAmountJpy > 0m && ConfirmedAcquisitionAmountJpy > 0m
        ? $"確定額と参考換算の差 {ConfirmedAcquisitionAmountJpy - _loadedReferenceAcquisitionAmountJpy:+#,0;-#,0;0}円"
        : string.Empty;
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
        _currentHoldingSourceFile = position?.CurrentHolding.SourceFile ?? string.Empty;
        _currentHoldingSourceRowNumber = position?.CurrentHolding.SourceRowNumber ?? 0;
        Name = position?.Stock.Name ?? string.Empty;
        Ticker = position?.Stock.Ticker ?? string.Empty;
        Country = position?.Stock.Country ?? "米国";
        Currency = position?.Stock.Currency ?? "USD";
        _loadedCurrency = NormalizeCurrency(Currency);
        _loadedAccountType = position?.Stock.AccountType ?? AccountTypes.Unknown;
        _loadedCustodyType = position?.Stock.CustodyType ?? string.Empty;
        Broker = position?.Stock.Broker ?? string.Empty;
        _loadedBroker = Broker;
        ChangeReason = string.Empty;
        Sector = position?.Stock.Sector ?? string.Empty;
        Industry = position?.Stock.Industry ?? string.Empty;
        Market = position?.Stock.Market ?? string.Empty;
        DataSource = position?.Stock.DataSource ?? "手入力";
        StockMemo = position?.Stock.Memo ?? string.Empty;
        PurchaseDate = position?.Purchase.PurchaseDate ?? DateTime.MinValue;
        _loadedPurchaseDate = PurchaseDate;
        PurchaseShares = position?.Purchase.Shares ?? 0m;
        PurchaseUnitPrice = position?.Purchase.UnitPrice ?? 0m;
        PurchaseExchangeRate = position?.Purchase.ExchangeRate ?? 1m;
        _loadedPurchaseShares = PurchaseShares;
        _loadedPurchaseUnitPrice = PurchaseUnitPrice;
        _loadedPurchaseExchangeRate = PurchaseExchangeRate;
        _loadedAcquisitionAmountJpy = position?.Purchase.AcquisitionAmountJpy ?? 0m;
        ConfirmedAcquisitionAmountJpy = _loadedAcquisitionAmountJpy;
        _loadedAcquisitionAmountSource = position?.Purchase.AcquisitionAmountSource ?? string.Empty;
        _loadedAcquisitionAmountConfirmedAt = position?.Purchase.AcquisitionAmountConfirmedAt ?? DateTime.MinValue;
        AcquisitionEvidence = _loadedAcquisitionAmountSource;
        _loadedReferenceAcquisitionAmountJpy = position?.Purchase.ReferenceAcquisitionAmountJpy ?? 0m;
        _loadedReferenceAcquisitionForeignAmount = position?.Purchase.ReferenceAcquisitionForeignAmount ?? 0m;
        _loadedReferenceAcquisitionSource = position?.Purchase.ReferenceAcquisitionSource ?? string.Empty;
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
        _loadedCurrentShares = CurrentShares;
        _loadedCurrentPrice = CurrentPrice;
        _loadedCurrentExchangeRate = CurrentExchangeRate;
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
        OnPropertyChanged(nameof(ReferenceAcquisitionSummary));
        OnPropertyChanged(nameof(ReferenceDifference));
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
            var position = BuildPosition();
            if (StockId != 0 && _confirmChanges is not null)
            {
                if (string.IsNullOrWhiteSpace(ChangeReason))
                {
                    ErrorMessage = "手動修正の理由を入力してください。";
                    return;
                }
                if (!_confirmChanges(BuildChangePreview(position)))
                    return;
            }
            _save(position);
            var missingForeignCost = position.Stock.Currency != "JPY" &&
                !new InvestmentCalculator().CreateSnapshot(position).HasKnownPurchaseCostJpy;
            ErrorMessage = missingForeignCost
                ? "保存しました。外貨の取得原価は、取得日と同じ基準日の参考為替、または根拠のある円決済額がないため算出不可です。"
                : "保存しました。";
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
        var samePurchaseBasis = currency == _loadedCurrency &&
            PurchaseDate == _loadedPurchaseDate &&
            PurchaseShares == _loadedPurchaseShares &&
            PurchaseUnitPrice == _loadedPurchaseUnitPrice &&
            purchaseExchangeRate == _loadedPurchaseExchangeRate;
        var hasNewCostEvidence = ConfirmedAcquisitionAmountJpy > 0m &&
            !string.IsNullOrWhiteSpace(AcquisitionEvidence) &&
            (ConfirmedAcquisitionAmountJpy != _loadedAcquisitionAmountJpy ||
             AcquisitionEvidence.Trim() != _loadedAcquisitionAmountSource);
        var keepExactCost = ConfirmedAcquisitionAmountJpy > 0m && (samePurchaseBasis || hasNewCostEvidence);
        var changedForeignBasis = currency != "JPY" && !keepExactCost &&
            (StockId == 0 || !samePurchaseBasis);
        var referenceFromInput = changedForeignBasis &&
            PositionEvidenceService.HasDatedReferencePurchaseRate(
                PurchaseDate, PurchaseExchangeRateAcquiredAt, purchaseExchangeRate) &&
            !string.IsNullOrWhiteSpace(PurchaseExchangeRateSource) &&
            (PurchaseDate.Date == DateTime.Today ||
             !PurchaseExchangeRateSource.Trim().Equals("手入力", StringComparison.OrdinalIgnoreCase));
        var referenceForeignCost = referenceFromInput ? PurchaseShares * PurchaseUnitPrice : 0m;

        return new StockPosition
        {
            ManualChangeReason = ChangeReason.Trim(),
            Stock = new Stock
            {
                Id = StockId,
                Name = Name.Trim(),
                Ticker = Ticker.Trim().ToUpperInvariant(),
                Country = Country.Trim(),
                Currency = currency,
                Broker = Broker.Trim(),
                AccountType = _loadedAccountType,
                CustodyType = _loadedCustodyType,
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
                ExchangeRate = changedForeignBasis ? 0m : purchaseExchangeRate,
                AcquisitionAmountJpy = keepExactCost ? ConfirmedAcquisitionAmountJpy : 0m,
                AcquisitionAmountSource = keepExactCost ? AcquisitionEvidence.Trim() : string.Empty,
                AcquisitionAmountConfirmedAt = keepExactCost
                    ? hasNewCostEvidence ? DateTime.Now : _loadedAcquisitionAmountConfirmedAt : DateTime.MinValue,
                ReferenceAcquisitionAmountJpy = referenceFromInput ? referenceForeignCost * purchaseExchangeRate :
                    samePurchaseBasis ? _loadedReferenceAcquisitionAmountJpy : 0m,
                ReferenceAcquisitionForeignAmount = referenceFromInput ? referenceForeignCost :
                    samePurchaseBasis ? _loadedReferenceAcquisitionForeignAmount : 0m,
                ReferenceAcquisitionSource = referenceFromInput
                    ? $"入力した参考為替 {purchaseExchangeRate:N4} / {PurchaseExchangeRateSource.Trim()} / {PurchaseExchangeRateAcquiredAt:yyyy/MM/dd}"
                    : samePurchaseBasis ? _loadedReferenceAcquisitionSource : string.Empty,
                ExchangeRateAcquiredAt = changedForeignBasis && !referenceFromInput
                    ? DateTime.MinValue : PurchaseExchangeRateAcquiredAt,
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
                SourceFile = _currentHoldingSourceFile,
                SourceRowNumber = _currentHoldingSourceRowNumber,
                UpdatedAt = UpdatedAt
            }
        };
    }

    private string BuildChangePreview(StockPosition position)
    {
        decimal? beforeValue = _loadedCurrentPrice > 0m &&
            (_loadedCurrency == "JPY" || _loadedCurrentExchangeRate > 0m)
            ? _loadedCurrentShares * _loadedCurrentPrice *
              (_loadedCurrency == "JPY" ? 1m : _loadedCurrentExchangeRate) : null;
        decimal? afterValue = position.CurrentHolding.CurrentPrice > 0m &&
            (position.Stock.Currency == "JPY" || position.CurrentHolding.CurrentExchangeRate > 0m)
            ? position.CurrentHolding.CurrentShares * position.CurrentHolding.CurrentPrice *
              (position.Stock.Currency == "JPY" ? 1m : position.CurrentHolding.CurrentExchangeRate) : null;
        var valueDifference = beforeValue.HasValue && afterValue.HasValue
            ? $"{afterValue.Value - beforeValue.Value:+#,0;-#,0;0}円" : "算出不可（株価・為替を確認）";
        return $"手動修正の影響を確認してください。\n\n" +
               $"証券会社: {_loadedBroker} → {position.Stock.Broker}\n" +
               $"口座: {AccountTypes.DisplayName(_loadedAccountType)}（変更なし）\n" +
               $"保有数量: {_loadedCurrentShares:N4} → {position.CurrentHolding.CurrentShares:N4}\n" +
               $"確定取得額: {_loadedAcquisitionAmountJpy:N0}円 → {position.Purchase.AcquisitionAmountJpy:N0}円\n" +
               $"確定取得額の差: {position.Purchase.AcquisitionAmountJpy - _loadedAcquisitionAmountJpy:+#,0;-#,0;0}円\n" +
               $"参考取得額: {_loadedReferenceAcquisitionAmountJpy:N0}円 → {position.Purchase.ReferenceAcquisitionAmountJpy:N0}円\n" +
               $"評価額の概算差: {valueDifference}\n" +
               $"理由: {ChangeReason.Trim()}\n\n保存しますか？";
    }

    private bool Validate()
    {
        if (ConfirmedAcquisitionAmountJpy < 0m)
        {
            ErrorMessage = "確定した円決済額に負数は入力できません。";
            return false;
        }
        if (ConfirmedAcquisitionAmountJpy > 0m &&
            ConfirmedAcquisitionAmountJpy != _loadedAcquisitionAmountJpy &&
            string.IsNullOrWhiteSpace(AcquisitionEvidence))
        {
            ErrorMessage = "確定した円決済額を変更する場合は、証券会社の取引報告書など根拠を入力してください。";
            return false;
        }
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

        if (NormalizeExchangeRateForCurrency(NormalizeCurrency(Currency), CurrentExchangeRate) <= 0)
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
