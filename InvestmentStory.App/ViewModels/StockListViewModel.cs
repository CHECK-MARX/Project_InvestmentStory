using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class StockListViewModel : ObservableObject
{
    private readonly Action _createNew;
    private readonly Action<int> _edit;
    private readonly Action<int> _showDetail;
    private readonly Action<StockRowViewModel>? _showDetailRow;
    private readonly Action<int> _delete;
    private readonly Action<int> _refreshSelected;
    private readonly Action _refreshAll;
    private readonly Action _refreshMissing;
    private readonly Action<string> _displayModeChanged;
    private readonly Action<string> _saveFilterState;
    private readonly List<StockSnapshot> _allSnapshots = new();
    private readonly List<BrokerTrade> _allTrades = new();
    private readonly HashSet<int> _dividendStockIds = new();
    private readonly HashSet<int> _acquisitionDateReviewStockIds = new();
    private StockRowViewModel? _selectedRow;
    private string _selectedFilter = "現在保有のみ";
    private string _selectedGroupingMode = "ポジション別";
    private string _selectedDisplayMode = "基本";
    private string _message = string.Empty;
    private string _searchText = string.Empty;
    private string _selectedBroker = "すべて";
    private string _selectedAccount = "すべて";
    private string _selectedSector = "すべて";
    private string _selectedQuality = "すべて";
    private string _visibleSummary = string.Empty;
    private string _columnLayoutJson = string.Empty;

    public StockListViewModel(
        Action createNew,
        Action<int> edit,
        Action<int> showDetail,
        Action<int> delete,
        Action<int> refreshSelected,
        Action refreshAll,
        Action refreshMissing,
        Action<string>? displayModeChanged = null,
        Action<StockRowViewModel>? showDetailRow = null,
        string? savedFilterJson = null,
        Action<string>? saveFilterState = null)
    {
        _createNew = createNew;
        _edit = edit;
        _showDetail = showDetail;
        _showDetailRow = showDetailRow;
        _delete = delete;
        _refreshSelected = refreshSelected;
        _refreshAll = refreshAll;
        _refreshMissing = refreshMissing;
        _displayModeChanged = displayModeChanged ?? (_ => { });
        _saveFilterState = saveFilterState ?? (_ => { });
        NewCommand = new RelayCommand(_createNew);
        EditCommand = new RelayCommand(() => ExecuteSelected(_edit), HasSelection);
        DetailCommand = new RelayCommand(ExecuteDetail, HasSelection);
        DeleteCommand = new RelayCommand(() => ExecuteSelected(_delete), HasSelection);
        RefreshSelectedCommand = new RelayCommand(() => ExecuteSelected(_refreshSelected), HasSelection);
        RefreshAllCommand = new RelayCommand(_refreshAll);
        RefreshMissingCommand = new RelayCommand(_refreshMissing);
        ResetFiltersCommand = new RelayCommand(ResetFilters);
        SaveFiltersCommand = new RelayCommand(SaveFilters);
        ExpandSelectedCommand = new RelayCommand(ExpandSelectedAccount, () => SelectedRow?.IsAggregated == true);
        if (!string.IsNullOrWhiteSpace(savedFilterJson))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<StockListFilterState>(savedFilterJson);
                if (saved is not null)
                {
                    _searchText = saved.SearchText ?? string.Empty;
                    _selectedBroker = saved.Broker ?? "すべて";
                    _selectedAccount = saved.Account ?? "すべて";
                    _selectedSector = saved.Sector ?? "すべて";
                    _selectedQuality = saved.Quality ?? "すべて";
                    if (Filters.Contains(saved.Filter)) _selectedFilter = saved.Filter;
                    if (GroupingModes.Contains(saved.Grouping)) _selectedGroupingMode = saved.Grouping;
                    _columnLayoutJson = saved.ColumnLayoutJson ?? string.Empty;
                }
            }
            catch (JsonException) { /* Ignore obsolete filter settings. */ }
        }
    }

    public ObservableCollection<StockRowViewModel> Rows { get; } = new();
    public string ColumnLayoutJson => _columnLayoutJson;
    public bool HasAdvancedFilters => SelectedBroker != "すべて" || SelectedAccount != "すべて" ||
                                      SelectedSector != "すべて" || SelectedQuality != "すべて";

    public void SaveColumnLayout(string json)
    {
        if (string.Equals(_columnLayoutJson, json, StringComparison.Ordinal)) return;
        _columnLayoutJson = json;
        _saveFilterState(SerializeFilterState());
    }

    public bool FocusPosition(string positionKey)
    {
        if (string.IsNullOrWhiteSpace(positionKey)) return false;
        SelectedGroupingMode = "ポジション別";
        SelectedFilter = "現在保有のみ";
        SearchText = string.Empty;
        SelectedBroker = "すべて";
        SelectedAccount = "すべて";
        SelectedSector = "すべて";
        SelectedQuality = "すべて";
        var row = Rows.FirstOrDefault(x => x.DetailKey == positionKey);
        if (row is null) { SelectedRow = null; return false; }
        SelectedRow = row;
        return true;
    }
    public string[] Filters { get; } = { "現在保有のみ", "過去保有銘柄", "配当銘柄", "成長銘柄" };
    public string[] GroupingModes { get; } = { "ポジション別", "銘柄集約" };
    public string[] DisplayModes { get; } = { "基本", "株式", "損益", "配当", "投資信託", "為替", "データ取得", "全項目" };
    public ICommand NewCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DetailCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RefreshSelectedCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public ICommand RefreshMissingCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand SaveFiltersCommand { get; }
    public RelayCommand ExpandSelectedCommand { get; }
    public IReadOnlyList<string> BrokerFilters { get; private set; } = new[] { "すべて" };
    public IReadOnlyList<string> AccountFilters { get; private set; } = new[] { "すべて" };
    public IReadOnlyList<string> SectorFilters { get; private set; } = new[] { "すべて" };
    public IReadOnlyList<string> QualityFilters { get; } = new[] { "すべて", "確定", "概算", "未確認", "取得日要確認" };

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) RebuildRows(); }
    }

    public string SelectedBroker
    {
        get => _selectedBroker;
        set { if (SetProperty(ref _selectedBroker, value)) RebuildRows(); }
    }

    public string SelectedAccount
    {
        get => _selectedAccount;
        set { if (SetProperty(ref _selectedAccount, value)) RebuildRows(); }
    }

    public string SelectedSector
    {
        get => _selectedSector;
        set { if (SetProperty(ref _selectedSector, value)) RebuildRows(); }
    }

    public string SelectedQuality
    {
        get => _selectedQuality;
        set { if (SetProperty(ref _selectedQuality, value)) RebuildRows(); }
    }

    public string VisibleSummary
    {
        get => _visibleSummary;
        private set => SetProperty(ref _visibleSummary, value);
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                RebuildRows();
            }
        }
    }

    public string SelectedDisplayMode
    {
        get => _selectedDisplayMode;
        set
        {
            if (!DisplayModes.Contains(value))
            {
                value = "基本";
            }

            if (SetProperty(ref _selectedDisplayMode, value))
            {
                OnPropertyChanged(nameof(ShowBasicColumns));
                OnPropertyChanged(nameof(ShowStockColumns));
                OnPropertyChanged(nameof(ShowProfitColumns));
                OnPropertyChanged(nameof(ShowDividendColumns));
                OnPropertyChanged(nameof(ShowFundColumns));
                OnPropertyChanged(nameof(ShowExchangeColumns));
                OnPropertyChanged(nameof(ShowDataColumns));
                RebuildRows();
                _displayModeChanged(value);
            }
        }
    }

    public string SelectedGroupingMode
    {
        get => _selectedGroupingMode;
        set
        {
            if (!GroupingModes.Contains(value))
            {
                value = "ポジション別";
            }

            if (SetProperty(ref _selectedGroupingMode, value))
            {
                RebuildRows();
            }
        }
    }

    public Visibility ShowBasicColumns => IsMode("基本") || IsMode("全項目") ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowStockColumns => IsMode("株式") || IsMode("全項目") ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowProfitColumns => IsMode("損益") || IsMode("全項目") ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowDividendColumns => IsMode("配当") || IsMode("全項目") ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowFundColumns => IsMode("投資信託") || IsMode("全項目") ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowExchangeColumns => IsMode("為替") || IsMode("全項目") ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowDataColumns => IsMode("データ取得") || IsMode("全項目") ? Visibility.Visible : Visibility.Collapsed;

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public StockRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                EditCommand.RaiseCanExecuteChanged();
                DetailCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                RefreshSelectedCommand.RaiseCanExecuteChanged();
                ExpandSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Update(
        IEnumerable<StockSnapshot> snapshots,
        IEnumerable<DividendPayment>? dividendPayments = null,
        IEnumerable<BrokerTrade>? trades = null)
    {
        _allSnapshots.Clear();
        _allSnapshots.AddRange(snapshots);
        _allTrades.Clear();
        _allTrades.AddRange(trades ?? Array.Empty<BrokerTrade>());
        var tradesByStockId = _allTrades
            .GroupBy(x => x.StockId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<BrokerTrade>)x.ToList());
        _acquisitionDateReviewStockIds.Clear();
        foreach (var snapshot in _allSnapshots)
        {
            if (PositionEvidenceService.AcquisitionDateNeedsReview(
                snapshot.Position, tradesByStockId.GetValueOrDefault(snapshot.Position.Stock.Id) ?? Array.Empty<BrokerTrade>()))
                _acquisitionDateReviewStockIds.Add(snapshot.Position.Stock.Id);
        }
        BrokerFilters = BuildOptions(_allSnapshots.Select(x => x.Position.Stock.Broker));
        AccountFilters = BuildOptions(_allSnapshots.Select(x => AccountOf(x.Position)));
        SectorFilters = BuildOptions(_allSnapshots.Select(x => SectorOf(x.Position)));
        OnPropertyChanged(nameof(BrokerFilters));
        OnPropertyChanged(nameof(AccountFilters));
        OnPropertyChanged(nameof(SectorFilters));
        if (!BrokerFilters.Contains(SelectedBroker)) _selectedBroker = "すべて";
        if (!AccountFilters.Contains(SelectedAccount)) _selectedAccount = "すべて";
        if (!SectorFilters.Contains(SelectedSector)) _selectedSector = "すべて";
        _dividendStockIds.Clear();
        if (dividendPayments is not null)
        {
            foreach (var stockId in dividendPayments
                         .Where(x => !string.Equals(x.DividendStatus, DividendConstants.Replaced, StringComparison.OrdinalIgnoreCase))
                         .Select(x => x.StockId)
                         .Distinct())
            {
                _dividendStockIds.Add(stockId);
            }
        }

        RebuildRows();
    }

    private void RebuildRows()
    {
        var selectedKey = SelectedRow?.DetailKey;
        Rows.Clear();
        var visibleSnapshots = _allSnapshots.Where(ShouldShow).ToList();
        var rows = SelectedGroupingMode == "銘柄集約"
            ? visibleSnapshots
                .GroupBy(x => AggregateKey(x), StringComparer.OrdinalIgnoreCase)
                .Select(x => new StockRowViewModel(x.ToList()))
            : visibleSnapshots.Select(x => new StockRowViewModel(x));

        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        SelectedRow = Rows.FirstOrDefault(x => x.DetailKey == selectedKey) ?? Rows.FirstOrDefault();
        var knownMarketValue = visibleSnapshots.Where(x => x.HasKnownCurrentMarketValueJpy)
            .Sum(x => x.CurrentMarketValueJpy);
        var unknownMarketValueCount = visibleSnapshots.Count(x => !x.HasKnownCurrentMarketValueJpy);
        var marketValueSummary = unknownMarketValueCount == 0
            ? $"評価額 {Formatters.Jpy(knownMarketValue)}"
            : $"評価額合計 算出不可（確認済み {Formatters.Jpy(knownMarketValue)} / 未確認 {unknownMarketValueCount:N0} 件）";
        var coverage = PositionEvidenceCoverage.Build(visibleSnapshots, DateTime.Today);
        var gainCoverage = new KnownGainCoverageService().Build(visibleSnapshots, _allTrades);
        var gainSummary = gainCoverage.HasConfirmedSubtotal
            ? gainCoverage.IsComplete
                ? $" ・ 含み損益合計 {Formatters.SignedJpy(gainCoverage.ConfirmedGainJpy)}"
                : $" ・ 含み損益の根拠あり分 {Formatters.SignedJpy(gainCoverage.ConfirmedGainJpy)}" +
                  $"（{gainCoverage.ConfirmedCount}/{gainCoverage.ActiveCount}件、全体額ではありません）"
            : string.Empty;
        VisibleSummary = $"表示 {Rows.Count:N0} 行（口座別 {visibleSnapshots.Count:N0} 件） / 全 {_allSnapshots.Count:N0} 件 ・ {marketValueSummary} ・ " +
            $"取得額 概算 {coverage.EstimatedCostCount:N0} 件 / 未確認 {coverage.UnconfirmedCostCount:N0} 件（損益集計対象外の評価額 " +
            $"{(coverage.ExcludedMarketValueJpy is decimal excluded ? Formatters.Jpy(excluded) : "算出不可")}） ・ " +
            $"価格更新遅れ {coverage.DelayedPriceCount:N0} 件 / 時刻不明 {coverage.UnknownPriceTimeCount:N0} 件 ・ " +
            $"取得日要確認（全件） {_acquisitionDateReviewStockIds.Count:N0} 件{gainSummary}";
    }

    private bool ShouldShow(StockSnapshot snapshot)
    {
        var position = snapshot.Position;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !new[] { position.Stock.Ticker, position.Stock.Name, position.MutualFund.FundName }
                .Any(x => x.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
        if (SelectedBroker != "すべて" && !string.Equals(position.Stock.Broker, SelectedBroker, StringComparison.Ordinal)) return false;
        if (SelectedAccount != "すべて" && AccountOf(position) != SelectedAccount) return false;
        if (SelectedSector != "すべて" && SectorOf(position) != SelectedSector) return false;
        if (SelectedQuality == "取得日要確認" && !_acquisitionDateReviewStockIds.Contains(position.Stock.Id)) return false;
        if (SelectedQuality != "すべて" && SelectedQuality != "取得日要確認" && QualityOf(snapshot) != SelectedQuality) return false;
        var isDividendOnly = IsDividendOnly(position);
        if (IsMode("投資信託") && !position.IsMutualFund)
        {
            return false;
        }

        if (IsMode("株式") && position.IsMutualFund)
        {
            return false;
        }

        return SelectedFilter switch
        {
            "現在保有のみ" => CurrentQuantity(position) > 0m && !isDividendOnly,
            "過去保有銘柄" => CurrentQuantity(position) <= 0m && !isDividendOnly,
            "配当銘柄" => HasDividend(position),
            "成長銘柄" => !HasDividend(position) && !isDividendOnly,
            _ => CurrentQuantity(position) > 0m && !isDividendOnly
        };
    }

    private bool HasDividend(StockPosition position)
    {
        if (_dividendStockIds.Contains(position.Stock.Id))
        {
            return true;
        }

        if (position.CurrentHolding.AnnualDividendPerShare > 0m)
        {
            return true;
        }

        return string.Equals(position.CurrentHolding.DividendStatus, "配当あり", StringComparison.Ordinal) ||
               IsDividendOnly(position);
    }

    private static bool IsDividendOnly(StockPosition position)
    {
        var source = position.Stock.DataSource ?? string.Empty;
        var memo = position.Stock.Memo ?? string.Empty;
        if (CurrentQuantity(position) > 0m)
        {
            return false;
        }

        if (position.CurrentHolding.CurrentPrice > 0m || position.Purchase.UnitPrice > 0m)
        {
            return false;
        }

        return source.Contains("配当CSV", StringComparison.Ordinal) ||
               memo.Contains("配当CSV取込時", StringComparison.Ordinal);
    }

    private static decimal CurrentQuantity(StockPosition position) =>
        position.IsMutualFund ? position.MutualFund.UnitsHeld : position.CurrentHolding.CurrentShares;

    private static string SectorOf(StockPosition position) =>
        string.IsNullOrWhiteSpace(position.Stock.Sector) ? "未分類" : position.Stock.Sector;

    private static string AccountOf(StockPosition position) => AccountTypes.DisplayName(
        position.IsMutualFund && !string.IsNullOrWhiteSpace(position.MutualFund.AccountType)
            ? position.MutualFund.AccountType : position.Stock.AccountType);

    private static string QualityOf(StockSnapshot snapshot) => PositionEvidenceService.CostQuality(snapshot) switch
    {
        AcquisitionCostQuality.Unconfirmed => "未確認",
        AcquisitionCostQuality.Estimated => "概算",
        _ => "確定"
    };

    private static IReadOnlyList<string> BuildOptions(IEnumerable<string> values) =>
        new[] { "すべて" }.Concat(values.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture)).ToArray();

    private void ResetFilters()
    {
        SearchText = string.Empty;
        SelectedBroker = "すべて";
        SelectedAccount = "すべて";
        SelectedSector = "すべて";
        SelectedQuality = "すべて";
        SelectedFilter = Filters[0];
        RebuildRows();
    }

    private void ExpandSelectedAccount()
    {
        if (SelectedRow is not { IsAggregated: true } aggregate) return;
        var positionKey = SecurityIdentityService.BuildPositionKey(aggregate.Snapshots[0].Position);
        SelectedGroupingMode = "ポジション別";
        SelectedRow = Rows.FirstOrDefault(x => x.DetailKey == positionKey);
    }

    private void SaveFilters()
    {
        _saveFilterState(SerializeFilterState());
        Message = "検索条件を保存しました。";
    }

    private string SerializeFilterState() => JsonSerializer.Serialize(new StockListFilterState(
        SearchText, SelectedBroker, SelectedAccount, SelectedSector, SelectedQuality,
        SelectedFilter, SelectedGroupingMode, _columnLayoutJson));

    private sealed record StockListFilterState(string SearchText, string Broker, string Account,
        string Sector, string Quality, string Filter, string Grouping, string ColumnLayoutJson = "");

    private static string AggregateKey(StockSnapshot snapshot)
    {
        return SecurityIdentityService.BuildCanonicalKey(snapshot.Position);
    }

    private bool HasSelection() => SelectedRow is not null;

    private bool IsMode(string mode) => string.Equals(SelectedDisplayMode, mode, StringComparison.Ordinal);

    private void ExecuteSelected(Action<int> action)
    {
        if (SelectedRow is not null)
        {
            action(SelectedRow.StockId);
        }
    }

    private void ExecuteDetail()
    {
        if (SelectedRow is null)
        {
            return;
        }

        if (_showDetailRow is not null)
        {
            _showDetailRow(SelectedRow);
            return;
        }

        _showDetail(SelectedRow.StockId);
    }
}
