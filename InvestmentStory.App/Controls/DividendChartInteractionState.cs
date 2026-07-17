using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.Controls;

public sealed class DividendChartInteractionState : INotifyPropertyChanged
{
    private string _selectedTicker = string.Empty;
    private int? _selectedMonth;
    private DividendScheduleStatus? _selectedStatus;
    private string _detailText = "グラフの項目をクリックすると、関連グラフを連動して絞り込みます。";
    private readonly HashSet<string> _hiddenSeries = new(StringComparer.OrdinalIgnoreCase);

    public DividendChartInteractionState() => ClearCommand = new InteractionCommand(Clear);

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand ClearCommand { get; }
    public string SelectedTicker => _selectedTicker;
    public int? SelectedMonth => _selectedMonth;
    public DividendScheduleStatus? SelectedStatus => _selectedStatus;
    public string DetailText { get => _detailText; private set => Set(ref _detailText, value); }
    public bool HasSelection => !string.IsNullOrWhiteSpace(_selectedTicker) || _selectedMonth is not null || _selectedStatus is not null;

    public void Select(string ticker, int? month, DividendScheduleStatus? status, string detailText)
    {
        _selectedTicker = ticker?.Trim() ?? string.Empty;
        _selectedMonth = month;
        _selectedStatus = status;
        DetailText = string.IsNullOrWhiteSpace(detailText) ? "選択中" : detailText;
        RaiseSelectionChanged();
    }

    public void Clear()
    {
        _selectedTicker = string.Empty;
        _selectedMonth = null;
        _selectedStatus = null;
        DetailText = "グラフの項目をクリックすると、関連グラフを連動して絞り込みます。";
        RaiseSelectionChanged();
    }

    public void ToggleSeries(string seriesKey)
    {
        if (string.IsNullOrWhiteSpace(seriesKey)) return;
        if (!_hiddenSeries.Add(seriesKey)) _hiddenSeries.Remove(seriesKey);
        OnPropertyChanged(nameof(HiddenSeries));
    }

    public IReadOnlyCollection<string> HiddenSeries => _hiddenSeries;
    public bool IsSeriesVisible(string seriesKey) => !_hiddenSeries.Contains(seriesKey);

    public double OpacityFor(string? ticker, int? month, DividendScheduleStatus? status)
    {
        if (!HasSelection) return 1d;
        if (!string.IsNullOrWhiteSpace(_selectedTicker)
            && !string.IsNullOrWhiteSpace(ticker)
            && !string.Equals(_selectedTicker, ticker, StringComparison.OrdinalIgnoreCase)) return .18d;
        if (_selectedMonth is not null && month is not null && _selectedMonth != month) return .18d;
        if (_selectedStatus is not null && status is not null && _selectedStatus != status) return .18d;
        return 1d;
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedTicker));
        OnPropertyChanged(nameof(SelectedMonth));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(HasSelection));
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class InteractionCommand(Action execute) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
