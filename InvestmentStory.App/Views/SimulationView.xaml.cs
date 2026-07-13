using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Views;

public partial class SimulationView : UserControl
{
    public SimulationView()
    {
        InitializeComponent();
    }

    private void AddNewDividendStockButton_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(FocusSelectedNewDividendStockRow, DispatcherPriority.Background);
    }

    private void NewDividendSimulationGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab) || !IsTickerColumnActive())
        {
            return;
        }

        Dispatcher.BeginInvoke(FetchSelectedNewDividendStock, DispatcherPriority.Background);
    }

    private void NewDividendSimulationGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Column.DisplayIndex != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(FetchSelectedNewDividendStock, DispatcherPriority.Background);
    }

    private void FocusSelectedNewDividendStockRow()
    {
        if (DataContext is not SimulationViewModel viewModel ||
            viewModel.SelectedNewDividendSimulationRow is not { } row ||
            NewDividendSimulationGrid.Columns.Count == 0)
        {
            return;
        }

        NewDividendSimulationGrid.SelectedItem = row;
        NewDividendSimulationGrid.ScrollIntoView(row, NewDividendSimulationGrid.Columns[0]);
        NewDividendSimulationGrid.UpdateLayout();
        FindVisualChild<ScrollViewer>(NewDividendSimulationGrid)?.ScrollToHorizontalOffset(0);
        NewDividendSimulationGrid.CurrentCell = new DataGridCellInfo(row, NewDividendSimulationGrid.Columns[0]);
        NewDividendSimulationGrid.Focus();
        NewDividendSimulationGrid.BeginEdit();

        var cell = TryFindCell(row, NewDividendSimulationGrid.Columns[0]);
        var textBox = cell is null ? null : FindVisualChild<TextBox>(cell);
        textBox?.Focus();
        textBox?.SelectAll();
    }

    private void FetchSelectedNewDividendStock()
    {
        if (DataContext is not SimulationViewModel viewModel ||
            NewDividendSimulationGrid.SelectedItem is not DividendSimulationRowViewModel row)
        {
            return;
        }

        NewDividendSimulationGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        NewDividendSimulationGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (viewModel.FetchNewDividendStockCommand.CanExecute(row))
        {
            viewModel.FetchNewDividendStockCommand.Execute(row);
        }
    }

    private bool IsTickerColumnActive() =>
        NewDividendSimulationGrid.CurrentColumn?.DisplayIndex == 0;

    private DataGridCell? TryFindCell(object item, DataGridColumn column)
    {
        var row = NewDividendSimulationGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        if (row is null)
        {
            return null;
        }

        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        return presenter?.ItemContainerGenerator.ContainerFromIndex(column.DisplayIndex) as DataGridCell;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                return typed;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
