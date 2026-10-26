using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Views;

public partial class CsvReviewView : UserControl
{
    public CsvReviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CsvReviewViewModel vm || vm.Selected is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ReviewGrid.ScrollIntoView(vm.Selected));
    }
}
