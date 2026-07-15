using System.Windows;

namespace InvestmentStory.App.Dialogs;

public enum UnsavedDividendPlanChoice
{
    Cancel,
    SaveAndExit,
    ExitWithoutSaving
}

public partial class UnsavedDividendPlanDialog : Window
{
    public UnsavedDividendPlanChoice Choice { get; private set; } = UnsavedDividendPlanChoice.Cancel;

    public UnsavedDividendPlanDialog()
    {
        InitializeComponent();
    }

    private void SaveAndExit_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedDividendPlanChoice.SaveAndExit;
        DialogResult = true;
    }

    private void ExitWithoutSaving_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedDividendPlanChoice.ExitWithoutSaving;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedDividendPlanChoice.Cancel;
        DialogResult = false;
    }
}
