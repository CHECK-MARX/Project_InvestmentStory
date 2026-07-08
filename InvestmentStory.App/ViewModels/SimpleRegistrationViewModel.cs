using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class SimpleRegistrationViewModel : ObservableObject
{
    private readonly Action<StockPosition> _save;
    private readonly InvestmentCalculator _calculator;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IStockLookupService _stockLookupService;
    private readonly IMarketDataService _marketDataService;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<IEnumerable<ApiFetchLogEntry>> _saveApiLogs;
    private StockSnapshot? _preview;
    private bool _isApplyingAutoDefaults;
    private string _message = string.Empty;
    private string _ticker = string.Empty;
    private string _name = string.Empty;

    public SimpleRegistrationViewModel(
        Action<StockPosition> save,
        InvestmentCalculator calculator,
        IExchangeRateService exchangeRateService,
        IStockLookupService stockLookupService,
        IMarketDataService marketDataService,
        Func<AppSettings> getSettings,
        Action<IEnumerable<ApiFetchLogEntry>> saveApiLogs)
    {
        _save = save;
        _calculator = calculator;
        _exchangeRateService = exchangeRateService;
        _stockLookupService = stockLookupService;
        _marketDataService = marketDataService;
        _getSettings = getSettings;
        _saveApiLogs = saveApiLogs;
        AutoFillCommand = new RelayCommand(AutoFill);
        PreviewCommand = new RelayCommand(UpdatePreview);
        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(Reset);
        Reset();
    }

    public ICommand AutoFillCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }

    public string Ticker
    {
        get => _ticker;
        set
        {
            if (SetProperty(ref _ticker, value) && !_isApplyingAutoDefaults && CanLookupWhileTyping(value))
            {
                ApplyLookupDefaults(value, updateTicker: true, showMessage: true);
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value) && !_isApplyingAutoDefaults && CanLookupWhileTyping(value))
            {
                ApplyLookupDefaults(value, updateTicker: true, showMessage: true);
            }
        }
    }

    public string Country { get; set; } = "米国";
    public string Currency { get; set; } = "USD";
    public string Broker { get; set; } = string.Empty;
    public decimal PurchaseShares { get; set; }
    public decimal PurchaseUnitPrice { get; set; }
    public decimal PurchaseExchangeRate { get; set; } = 160m;
    public decimal CurrentShares { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CurrentExchangeRate { get; set; } = 160m;
    public decimal AnnualDividendPerShare { get; set; }
    public string DividendStatus { get; set; } = "配当未入力";
    public string DividendFrequency { get; set; } = "年4回";
    public string Sector { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string DataSource { get; set; } = "手入力";
    public DateTime CurrentPriceAcquiredAt { get; set; } = DateTime.MinValue;
    public string CurrentPriceSource { get; set; } = string.Empty;
    public DateTime DividendInfoAcquiredAt { get; set; } = DateTime.MinValue;
    public string DividendInfoSource { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string ShareChangeRatio => Formatters.Number(CalculateShareChangeRatio());
    public string PurchaseTotalUsd => PurchaseUnitPrice <= 0m ? "購入単価が未入力です" : Money(x => x.PurchaseTotalUsd);
    public string PurchaseTotalJpy => PurchaseUnitPrice <= 0m ? "購入単価が未入力です" : Jpy(x => x.PurchaseTotalJpy);
    public string EffectiveAcquisitionPrice => Money(x => x.EffectiveAcquisitionPrice);
    public string CurrentMarketValueUsd => CurrentPrice <= 0m ? "現在株価が未入力です" : Money(x => x.CurrentMarketValueUsd);
    public string CurrentMarketValueJpy => CurrentPrice <= 0m ? "現在株価が未入力です" : Jpy(x => x.CurrentMarketValueJpy);
    public string UnrealizedGainUsd => SignedMoney(x => x.UnrealizedGainUsd);
    public string UnrealizedGainJpy => SignedJpy(x => x.UnrealizedGainJpy);
    public string UnrealizedGainRateUsd => SignedPercent(x => x.UnrealizedGainRateUsd);
    public string UnrealizedGainRateJpy => SignedPercent(x => x.UnrealizedGainRateJpy);
    public string AnnualDividendForecast => DividendStatus == "配当未入力" ? "配当情報が未入力です" : Money(x => x.AnnualDividendForecast);
    public string AnnualDividendForecastJpy => DividendStatus == "配当未入力" ? "配当情報が未入力です" : Jpy(x => x.AnnualDividendForecastJpy);
    public string MonthlyDividendForecast => DividendStatus == "配当未入力" ? "配当情報が未入力です" : Money(x => x.MonthlyPassiveIncomeForecast);
    public string MonthlyDividendForecastJpy => DividendStatus == "配当未入力" ? "配当情報が未入力です" : Jpy(x => x.MonthlyPassiveIncomeForecastJpy);
    public string YieldOnCost => DividendStatus == "配当未入力" ? "配当情報が未入力です" : Percent(x => x.YieldOnCost);
    public string CurrentDividendYield => DividendStatus == "配当未入力" ? "配当情報が未入力です" : Percent(x => x.CurrentDividendYield);

    public void Reset()
    {
        var quote = _exchangeRateService.GetUsdJpyRate();
        Ticker = string.Empty;
        Name = string.Empty;
        Country = "米国";
        Currency = "USD";
        Broker = string.Empty;
        PurchaseShares = 0m;
        PurchaseUnitPrice = 0m;
        PurchaseExchangeRate = quote.Rate;
        CurrentShares = 0m;
        CurrentPrice = 0m;
        CurrentExchangeRate = quote.Rate;
        AnnualDividendPerShare = 0m;
        DividendStatus = "配当未入力";
        DividendFrequency = "年4回";
        Sector = string.Empty;
        Industry = string.Empty;
        Market = string.Empty;
        DataSource = "手入力";
        CurrentPriceAcquiredAt = DateTime.MinValue;
        CurrentPriceSource = string.Empty;
        DividendInfoAcquiredAt = DateTime.MinValue;
        DividendInfoSource = string.Empty;
        Memo = string.Empty;
        _preview = null;
        Message = string.Empty;
        RefreshAllProperties();
    }

    private void UpdatePreview()
    {
        ApplyAutoDefaults();
        NormalizeCurrencyInputs();
        if (!Validate(requireTicker: false))
        {
            _preview = null;
            RefreshAllProperties();
            return;
        }

        _preview = _calculator.CreateSnapshot(BuildPosition());
        Message = HasProvisionalPrices()
            ? "仮登録用のプレビューです。購入単価または現在株価が0のため、一部の計算値は0表示になります。"
            : "計算プレビューを更新しました。";
        RefreshAllProperties();
    }

    private void Save()
    {
        ApplyAutoDefaults();
        NormalizeCurrencyInputs();
        if (!Validate(requireTicker: true))
        {
            return;
        }

        try
        {
            var position = BuildPosition();
            _preview = _calculator.CreateSnapshot(position);
            _save(position);
            Message = HasProvisionalPrices()
                ? "仮登録しました。購入単価や現在株価は後で詳細登録・編集から更新できます。"
                : "保存しました。銘柄詳細で計算結果を確認できます。";
            RefreshAllProperties();
        }
        catch (Exception ex)
        {
            Message = $"保存に失敗しました: {ex.Message}";
        }
    }

    private void AutoFill()
    {
        ApplyMarketDataDefaults();
        var message = Message;
        NormalizeCurrencyInputs();
        UpdatePreviewWithoutChangingMessage(message);
        RefreshAllProperties();
    }

    private void UpdatePreviewWithoutChangingMessage(string message)
    {
        if (Validate(requireTicker: false))
        {
            _preview = _calculator.CreateSnapshot(BuildPosition());
        }

        Message = message;
    }

    private StockPosition BuildPosition()
    {
        var now = DateTime.Now;
        var today = DateTime.Today;
        var currentShares = CurrentSharesForCalculation();
        var splitRatio = CalculateShareChangeRatio();
        var stockName = string.IsNullOrWhiteSpace(Name) ? Ticker.Trim().ToUpperInvariant() : Name.Trim();
        var currency = NormalizeCurrency(Currency);
        var purchaseExchangeRate = NormalizeExchangeRateForCurrency(currency, PurchaseExchangeRate);
        var currentExchangeRate = NormalizeExchangeRateForCurrency(currency, CurrentExchangeRate);

        return new StockPosition
        {
            Stock = new Stock
            {
                Name = stockName,
                Ticker = Ticker.Trim().ToUpperInvariant(),
                Country = string.IsNullOrWhiteSpace(Country) ? "米国" : Country.Trim(),
                Currency = currency,
                Broker = Broker.Trim(),
                Sector = Sector.Trim(),
                Industry = Industry.Trim(),
                Market = Market.Trim(),
                DataSource = DataSource.Trim(),
                Memo = Memo.Trim()
            },
            Purchase = new Purchase
            {
                PurchaseDate = today,
                Shares = PurchaseShares,
                UnitPrice = PurchaseUnitPrice,
                ExchangeRate = purchaseExchangeRate,
                ExchangeRateAcquiredAt = now,
                ExchangeRateSource = "手入力",
                ExchangeRateInputType = "手入力",
                Fee = 0m,
                Memo = "かんたん登録"
            },
            Split = new StockSplit
            {
                SplitDate = today,
                SplitRatio = splitRatio <= 0m ? 1m : splitRatio,
                Memo = string.Empty
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = currentShares,
                CurrentPrice = CurrentPrice,
                CurrentExchangeRate = currentExchangeRate,
                ExchangeRateAcquiredAt = now,
                ExchangeRateSource = "手入力",
                ExchangeRateInputType = "手入力",
                AnnualDividendPerShare = AnnualDividendPerShare,
                DividendStatus = string.IsNullOrWhiteSpace(DividendStatus) ? "配当未入力" : DividendStatus.Trim(),
                DividendFrequency = string.IsNullOrWhiteSpace(DividendFrequency)
                    ? (currency == "JPY" ? "未確認" : "年4回")
                    : DividendFrequency.Trim(),
                DividendMonths = string.Empty,
                CurrentPriceAcquiredAt = CurrentPriceAcquiredAt == DateTime.MinValue ? DateTime.Now : CurrentPriceAcquiredAt,
                CurrentPriceSource = CurrentPriceSource,
                DividendInfoAcquiredAt = DividendInfoAcquiredAt == DateTime.MinValue ? DateTime.Now : DividendInfoAcquiredAt,
                DividendInfoSource = DividendInfoSource,
                UpdatedAt = today
            }
        };
    }

    private bool Validate(bool requireTicker)
    {
        if (requireTicker && string.IsNullOrWhiteSpace(Ticker))
        {
            Message = "ティッカーを入力してください。";
            return false;
        }

        if (PurchaseShares <= 0m)
        {
            Message = "購入株数は0より大きい値を入力してください。";
            return false;
        }

        if (PurchaseUnitPrice < 0m || CurrentPrice < 0m)
        {
            Message = "購入単価と現在株価にはマイナス値を入力できません。未確定の場合は0のまま保存できます。";
            return false;
        }

        if (NormalizeExchangeRateForCurrency(NormalizeCurrency(Currency), PurchaseExchangeRate) <= 0m ||
            NormalizeExchangeRateForCurrency(NormalizeCurrency(Currency), CurrentExchangeRate) <= 0m)
        {
            Message = "為替レートは0より大きい値を入力してください。";
            return false;
        }

        if (AnnualDividendPerShare < 0m)
        {
            Message = "1株あたり年間配当にはマイナス値を入力できません。";
            return false;
        }

        if (DividendStatus == "配当あり" && AnnualDividendPerShare <= 0m)
        {
            Message = "配当ありの場合は、1株あたり年間配当（年間合計）を0より大きい値で入力してください。";
            return false;
        }

        Message = string.Empty;
        return true;
    }

    private decimal CalculateShareChangeRatio() => PurchaseShares <= 0m ? 0m : CurrentSharesForCalculation() / PurchaseShares;

    private decimal CurrentSharesForCalculation() => CurrentShares > 0m ? CurrentShares : PurchaseShares;

    private bool HasProvisionalPrices() => PurchaseUnitPrice == 0m || CurrentPrice == 0m;

    private void ApplyAutoDefaults()
    {
        var lookup = ApplyLookupDefaults(Ticker, updateTicker: true, showMessage: false)
            ?? ApplyLookupDefaults(Name, updateTicker: true, showMessage: false);
        var ticker = Ticker.Trim().ToUpperInvariant();
        var looksJapaneseStock = LooksLikeJapaneseTicker(ticker);

        if (lookup is null && string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(ticker))
        {
            Name = ticker;
        }

        if (lookup is null && looksJapaneseStock)
        {
            if (string.IsNullOrWhiteSpace(Country) || Country.Trim() == "米国")
            {
                Country = "日本";
            }

            if (string.IsNullOrWhiteSpace(Currency) || Currency.Trim().Equals("USD", StringComparison.OrdinalIgnoreCase))
            {
                Currency = "JPY";
            }
        }

        var currency = NormalizeCurrency(Currency);
        if (string.IsNullOrWhiteSpace(Country))
        {
            Country = currency == "JPY" ? "日本" : "米国";
        }

        if (CurrentShares <= 0m && PurchaseShares > 0m)
        {
            CurrentShares = PurchaseShares;
        }

        if (CurrentPrice == 0m && PurchaseUnitPrice > 0m)
        {
            CurrentPrice = PurchaseUnitPrice;
        }
        else if (PurchaseUnitPrice == 0m && CurrentPrice > 0m)
        {
            PurchaseUnitPrice = CurrentPrice;
        }

        if (string.IsNullOrWhiteSpace(DividendFrequency))
        {
            DividendFrequency = NormalizeCurrency(Currency) == "JPY" ? "未確認" : "年4回";
        }
        else if (NormalizeCurrency(Currency) == "JPY" &&
                 DividendFrequency == "年4回" &&
                 DividendStatus == "配当未入力")
        {
            DividendFrequency = "未確認";
        }

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Country));
        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(CurrentShares));
        OnPropertyChanged(nameof(PurchaseUnitPrice));
        OnPropertyChanged(nameof(CurrentPrice));
        OnPropertyChanged(nameof(DividendFrequency));
    }

    private bool ApplyMarketDataDefaults()
    {
        var query = string.IsNullOrWhiteSpace(Ticker) ? Name : Ticker;
        if (string.IsNullOrWhiteSpace(query))
        {
            Message = "コード、ティッカー、会社名のいずれかを入力してください。";
            return false;
        }

        var settings = _getSettings();
        if (settings.MarketDataMode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            settings.MarketDataMode = "Web/API";
        }

        var lookup = _stockLookupService.Find(query);
        var symbol = lookup?.Ticker ?? query;
        var result = _marketDataService.GetQuote(symbol, settings);
        SaveApiLogs(result.Logs, settings.EnableApiResponseLog);
        if (!result.IsSuccess || result.Quote is null)
        {
            Message = result.ErrorMessage;
            return false;
        }

        if (lookup is not null)
        {
            result.Quote.Name = lookup.Name;
            result.Quote.Country = string.IsNullOrWhiteSpace(result.Quote.Country) ? lookup.Country : result.Quote.Country;
            result.Quote.Currency = string.IsNullOrWhiteSpace(result.Quote.Currency) ? lookup.Currency : result.Quote.Currency;
            if ((result.Quote.AnnualDividendPerShare is null || result.Quote.AnnualDividendPerShare <= 0m) &&
                lookup.AnnualDividendPerShare is not null)
            {
                result.Quote.AnnualDividendPerShare = lookup.AnnualDividendPerShare;
                result.Quote.DividendInfoSource = lookup.Source;
            }

            if (string.IsNullOrWhiteSpace(result.Quote.DividendFrequency) &&
                !string.IsNullOrWhiteSpace(lookup.DividendFrequency))
            {
                result.Quote.DividendFrequency = lookup.DividendFrequency;
            }
        }

        ApplyMarketQuote(result.Quote);
        var warning = string.IsNullOrWhiteSpace(result.Quote.Warning) ? string.Empty : $" {result.Quote.Warning}";
        Message = $"{result.Quote.Symbol} の情報をWeb/APIから取得しました。取得元: {result.Quote.Source}.{warning}";
        return true;
    }

    private void ApplyMarketQuote(MarketDataQuote quote)
    {
        _isApplyingAutoDefaults = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(quote.Symbol))
            {
                Ticker = quote.Symbol;
            }

            if (!string.IsNullOrWhiteSpace(quote.Name))
            {
                Name = quote.Name;
            }

            if (!string.IsNullOrWhiteSpace(quote.Country))
            {
                Country = quote.Country;
            }

            if (!string.IsNullOrWhiteSpace(quote.Currency))
            {
                Currency = NormalizeCurrency(quote.Currency);
            }

            Sector = quote.Sector;
            Industry = quote.Industry;
            Market = quote.Market;
            DataSource = quote.Source;

            if (quote.CurrentPrice is not null)
            {
                CurrentPrice = quote.CurrentPrice.Value;
                CurrentPriceAcquiredAt = quote.PriceAcquiredAt ?? DateTime.Now;
                CurrentPriceSource = quote.Source;
            }

            if (quote.AnnualDividendPerShare is not null)
            {
                AnnualDividendPerShare = quote.AnnualDividendPerShare.Value;
                DividendStatus = quote.AnnualDividendPerShare.Value > 0m ? "配当あり" : "配当なし";
                DividendInfoAcquiredAt = DateTime.Now;
                DividendInfoSource = string.IsNullOrWhiteSpace(quote.DividendInfoSource) ? quote.Source : quote.DividendInfoSource;
            }

            if (!string.IsNullOrWhiteSpace(quote.DividendFrequency))
            {
                DividendFrequency = quote.DividendFrequency;
            }

            var currency = NormalizeCurrency(Currency);
            if (currency == "JPY")
            {
                PurchaseExchangeRate = 1m;
                CurrentExchangeRate = 1m;
            }
            else if (currency == "USD" && quote.UsdJpyRate is not null)
            {
                if (PurchaseExchangeRate <= 0m)
                {
                    PurchaseExchangeRate = quote.UsdJpyRate.Value;
                }

                CurrentExchangeRate = quote.UsdJpyRate.Value;
            }
        }
        finally
        {
            _isApplyingAutoDefaults = false;
        }

        OnPropertyChanged(nameof(Ticker));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Country));
        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(Sector));
        OnPropertyChanged(nameof(Industry));
        OnPropertyChanged(nameof(Market));
        OnPropertyChanged(nameof(DataSource));
        OnPropertyChanged(nameof(CurrentPrice));
        OnPropertyChanged(nameof(CurrentPriceAcquiredAt));
        OnPropertyChanged(nameof(CurrentPriceSource));
        OnPropertyChanged(nameof(AnnualDividendPerShare));
        OnPropertyChanged(nameof(DividendStatus));
        OnPropertyChanged(nameof(DividendFrequency));
        OnPropertyChanged(nameof(DividendInfoAcquiredAt));
        OnPropertyChanged(nameof(DividendInfoSource));
        OnPropertyChanged(nameof(PurchaseExchangeRate));
        OnPropertyChanged(nameof(CurrentExchangeRate));
    }

    private void SaveApiLogs(IEnumerable<ApiFetchLogEntry> logs, bool includeResponseSummary)
    {
        var sanitizedLogs = includeResponseSummary
            ? logs
            : logs.Select(x => new ApiFetchLogEntry
            {
                ApiType = x.ApiType,
                Provider = x.Provider,
                Symbol = x.Symbol,
                HttpStatusCode = x.HttpStatusCode,
                IsSuccess = x.IsSuccess,
                ErrorMessage = x.ErrorMessage,
                FetchedAt = x.FetchedAt,
                Summary = string.Empty
            });
        _saveApiLogs(sanitizedLogs);
    }

    private StockLookupResult? ApplyLookupDefaults(string query, bool updateTicker, bool showMessage)
    {
        var lookup = _stockLookupService.Find(query);
        if (lookup is null)
        {
            return null;
        }

        var queryText = query.Trim();
        _isApplyingAutoDefaults = true;
        try
        {
            if (updateTicker && !string.Equals(Ticker, lookup.Ticker, StringComparison.OrdinalIgnoreCase))
            {
                Ticker = lookup.Ticker;
            }

            if (string.IsNullOrWhiteSpace(Name) ||
                string.Equals(Name.Trim(), queryText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Name.Trim(), lookup.Ticker, StringComparison.OrdinalIgnoreCase))
            {
                Name = lookup.Name;
            }

            Country = lookup.Country;
            Currency = lookup.Currency;

            if (CurrentPrice == 0m && lookup.CurrentPrice is not null)
            {
                CurrentPrice = lookup.CurrentPrice.Value;
            }

            if (AnnualDividendPerShare == 0m && lookup.AnnualDividendPerShare is not null)
            {
                AnnualDividendPerShare = lookup.AnnualDividendPerShare.Value;
                DividendStatus = lookup.AnnualDividendPerShare.Value > 0m ? "配当あり" : "配当なし";
            }

            if (!string.IsNullOrWhiteSpace(lookup.DividendFrequency) &&
                (string.IsNullOrWhiteSpace(DividendFrequency) ||
                 DividendFrequency == "年4回" ||
                 DividendStatus == "配当未入力"))
            {
                DividendFrequency = lookup.DividendFrequency;
            }

            if (NormalizeCurrency(Currency) == "JPY")
            {
                PurchaseExchangeRate = 1m;
                CurrentExchangeRate = 1m;
            }
        }
        finally
        {
            _isApplyingAutoDefaults = false;
        }

        if (showMessage)
        {
            Message = $"{lookup.Ticker} の銘柄情報を補完しました。取得元: {lookup.Source}";
        }

        OnPropertyChanged(nameof(Ticker));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Country));
        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(CurrentPrice));
        OnPropertyChanged(nameof(AnnualDividendPerShare));
        OnPropertyChanged(nameof(DividendStatus));
        OnPropertyChanged(nameof(DividendFrequency));
        OnPropertyChanged(nameof(PurchaseExchangeRate));
        OnPropertyChanged(nameof(CurrentExchangeRate));

        return lookup;
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

    private string Money(Func<StockSnapshot, decimal> selector) =>
        _preview is null ? "-" : Formatters.Money(selector(_preview), CurrencyOrDefault());

    private string SignedMoney(Func<StockSnapshot, decimal> selector) =>
        _preview is null ? "-" : Formatters.SignedMoney(selector(_preview), CurrencyOrDefault());

    private string Jpy(Func<StockSnapshot, decimal> selector) =>
        _preview is null ? "-" : Formatters.Jpy(selector(_preview));

    private string SignedJpy(Func<StockSnapshot, decimal> selector) =>
        _preview is null ? "-" : Formatters.SignedJpy(selector(_preview));

    private string Percent(Func<StockSnapshot, decimal> selector) =>
        _preview is null ? "-" : Formatters.Percent(selector(_preview));

    private string SignedPercent(Func<StockSnapshot, decimal> selector) =>
        _preview is null ? "-" : Formatters.SignedPercent(selector(_preview));

    private string CurrencyOrDefault() => NormalizeCurrency(Currency);

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return "USD";
        }

        var normalized = currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "JPY" or "円" ? "JPY" : normalized;
    }

    private static bool LooksLikeJapaneseTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return false;
        }

        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.EndsWith(".T", StringComparison.Ordinal))
        {
            normalized = normalized[..^2];
        }

        return normalized.Length is 4 or 5 && normalized.All(char.IsDigit);
    }

    private static bool CanLookupWhileTyping(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var normalized = query.Trim();
        if (LooksLikeJapaneseTicker(normalized))
        {
            return true;
        }

        var asciiTicker = normalized.All(c => char.IsAsciiLetter(c) || c == '.' || c == '-');
        if (asciiTicker)
        {
            return normalized.Length is >= 2 and <= 8;
        }

        return normalized.Length >= 3;
    }

    private static decimal NormalizeExchangeRateForCurrency(string currency, decimal exchangeRate)
    {
        return currency == "JPY" ? 1m : exchangeRate;
    }
}
