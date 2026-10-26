using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.Json;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Views;

public partial class StockListView : UserControl
{
    private bool _layoutReady;

    public StockListView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => { SaveColumnLayout(); _layoutReady = false; };
        StockGrid.ColumnReordered += (_, _) => QueueSaveColumnLayout();
        StockGrid.PreviewMouseLeftButtonUp += OnGridMouseUp;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StockListViewModel vm) return;
        AdvancedFiltersExpander.IsExpanded = vm.HasAdvancedFilters;
        RestoreColumnLayout(vm.ColumnLayoutJson);
        _layoutReady = true;
        if (vm.SelectedRow is not null)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => StockGrid.ScrollIntoView(vm.SelectedRow));
    }

    private void OnGridMouseUp(object sender, MouseButtonEventArgs e) => QueueSaveColumnLayout();

    private void QueueSaveColumnLayout()
    {
        if (_layoutReady)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => SaveColumnLayout());
    }

    private void SaveColumnLayout()
    {
        if (!_layoutReady || DataContext is not StockListViewModel vm) return;
        var columns = StockGrid.Columns.Select((column, index) => new ColumnLayout(
            index, Math.Round(column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth, 1),
            column.DisplayIndex)).ToArray();
        vm.SaveColumnLayout(JsonSerializer.Serialize(columns));
    }

    private void RestoreColumnLayout(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        ColumnLayout[]? saved;
        try { saved = JsonSerializer.Deserialize<ColumnLayout[]>(json); }
        catch (JsonException) { return; }
        if (saved is null || saved.Length != StockGrid.Columns.Count ||
            saved.Select(x => x.Index).Distinct().Count() != saved.Length ||
            saved.Select(x => x.DisplayIndex).Distinct().Count() != saved.Length ||
            saved.Any(x => x.Index < 0 || x.Index >= saved.Length ||
                           x.DisplayIndex < 0 || x.DisplayIndex >= saved.Length) ||
            saved.Any(x => x.Index < 3 && x.DisplayIndex != x.Index)) return;

        foreach (var item in saved.Where(x => x.Index >= 0 && x.Index < StockGrid.Columns.Count &&
                                               x.Width is >= 60 and <= 600))
            StockGrid.Columns[item.Index].Width = new DataGridLength(item.Width);

        // The first three identification columns must remain frozen in every layout.
        foreach (var item in saved.Where(x => x.Index >= 3 && x.Index < StockGrid.Columns.Count &&
                                               x.DisplayIndex >= 3 && x.DisplayIndex < StockGrid.Columns.Count)
                                  .OrderBy(x => x.DisplayIndex))
            StockGrid.Columns[item.Index].DisplayIndex = item.DisplayIndex;
    }

    private sealed record ColumnLayout(int Index, double Width, int DisplayIndex);
}
