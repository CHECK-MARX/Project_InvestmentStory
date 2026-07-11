using System.Collections.ObjectModel;
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
    private readonly List<StockSnapshot> _allSnapshots = new();
    private readonly HashSet<int> _dividendStockIds = new();
    private StockRowViewModel? _selectedRow;
    private string _selectedFilter = "現在保有のみ";
    private string _selectedGroupingMode = "ポジション別";
    private string _selectedDisplayMode = "基本";
    private string _message = string.Empty;

    public StockListViewModel(
        Action createNew,
        Action<int> edit,
        Action<int> showDetail,
        Action<int> delete,
        Action<int> refreshSelected,
        Action refreshAll,
        Action refreshMissing,
        Action<string>? displayModeChanged = null,
        Action<StockRowViewModel>? showDetailRow = null)
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
        NewCommand = new RelayCommand(_createNew);
        EditCommand = new RelayCommand(() => ExecuteSelected(_edit), HasSelection);
        DetailCommand = new RelayCommand(ExecuteDetail, HasSelection);
        DeleteCommand = new RelayCommand(() => ExecuteSelected(_delete), HasSelection);
        RefreshSelectedCommand = new RelayCommand(() => ExecuteSelected(_refreshSelected), HasSelection);
        RefreshAllCommand = new RelayCommand(_refreshAll);
        RefreshMissingCommand = new RelayCommand(_refreshMissing);
    }

    public ObservableCollection<StockRowViewModel> Rows { get; } = new();
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
            }
        }
    }

    public void Update(IEnumerable<StockSnapshot> snapshots, IEnumerable<DividendPayment>? dividendPayments = null)
    {
        _allSnapshots.Clear();
        _allSnapshots.AddRange(snapshots);
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
    }

    private bool ShouldShow(StockSnapshot snapshot)
    {
        var position = snapshot.Position;
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
