using System.ComponentModel;
using System.Windows;
using InvestmentStory.App.Dialogs;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is MainViewModel mainViewModel)
        {
            await mainViewModel.VerifyDividendSchedulesOnStartupAsync();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel ||
            !mainViewModel.Simulation.HasUnsavedDividendPlanChanges)
        {
            return;
        }

        var dialog = new UnsavedDividendPlanDialog { Owner = this };
        dialog.ShowDialog();
        switch (dialog.Choice)
        {
            case UnsavedDividendPlanChoice.SaveAndExit:
                if (!mainViewModel.Simulation.TrySaveDividendPurchasePlan())
                {
                    MessageBox.Show(this, "購入計画を保存できなかったため、終了を中止しました。",
                        "Investment Story", MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Cancel = true;
                }
                break;
            case UnsavedDividendPlanChoice.ExitWithoutSaving:
                break;
            default:
                e.Cancel = true;
                break;
        }
    }
}
