using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Views;

public partial class StockListView : UserControl
{
    public StockListView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StockListViewModel vm || vm.SelectedRow is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => StockGrid.ScrollIntoView(vm.SelectedRow));
    }
}
