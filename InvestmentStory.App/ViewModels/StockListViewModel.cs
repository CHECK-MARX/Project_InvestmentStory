using System.Collections.ObjectModel;
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
    private readonly Action<int> _delete;
    private readonly Action<int> _refreshSelected;
    private readonly Action _refreshAll;
    private readonly Action _refreshMissing;
    private readonly List<StockSnapshot> _allSnapshots = new();
    private readonly HashSet<int> _dividendStockIds = new();
    private StockRowViewModel? _selectedRow;
    private string _selectedFilter = "現在保有のみ";
    private string _message = string.Empty;

    public StockListViewModel(
        Action createNew,
        Action<int> edit,
        Action<int> showDetail,
        Action<int> delete,
        Action<int> refreshSelected,
        Action refreshAll,
        Action refreshMissing)
    {
        _createNew = createNew;
        _edit = edit;
        _showDetail = showDetail;
        _delete = delete;
        _refreshSelected = refreshSelected;
        _refreshAll = refreshAll;
        _refreshMissing = refreshMissing;
        NewCommand = new RelayCommand(_createNew);
        EditCommand = new RelayCommand(() => ExecuteSelected(_edit), HasSelection);
        DetailCommand = new RelayCommand(() => ExecuteSelected(_showDetail), HasSelection);
        DeleteCommand = new RelayCommand(() => ExecuteSelected(_delete), HasSelection);
        RefreshSelectedCommand = new RelayCommand(() => ExecuteSelected(_refreshSelected), HasSelection);
        RefreshAllCommand = new RelayCommand(_refreshAll);
        RefreshMissingCommand = new RelayCommand(_refreshMissing);
    }

    public ObservableCollection<StockRowViewModel> Rows { get; } = new();
    public string[] Filters { get; } = { "現在保有のみ", "過去保有銘柄", "配当銘柄", "成長銘柄" };
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
        var selectedId = SelectedRow?.StockId;
        Rows.Clear();
        foreach (var snapshot in _allSnapshots.Where(ShouldShow))
        {
            Rows.Add(new StockRowViewModel(snapshot));
        }

        SelectedRow = Rows.FirstOrDefault(x => x.StockId == selectedId) ?? Rows.FirstOrDefault();
    }

    private bool ShouldShow(StockSnapshot snapshot)
    {
        var position = snapshot.Position;
        var isDividendOnly = IsDividendOnly(position);
        return SelectedFilter switch
        {
            "現在保有のみ" => position.CurrentHolding.CurrentShares > 0m && !isDividendOnly,
            "過去保有銘柄" => position.CurrentHolding.CurrentShares <= 0m && !isDividendOnly,
            "配当銘柄" => HasDividend(position),
            "成長銘柄" => !HasDividend(position) && !isDividendOnly,
            _ => position.CurrentHolding.CurrentShares > 0m && !isDividendOnly
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
        if (position.CurrentHolding.CurrentShares > 0m)
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

    private bool HasSelection() => SelectedRow is not null;

    private void ExecuteSelected(Action<int> action)
    {
        if (SelectedRow is not null)
        {
            action(SelectedRow.StockId);
        }
    }
}
