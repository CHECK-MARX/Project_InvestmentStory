using System.Windows.Controls;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Views;

public partial class BrokerIntegrationView : UserControl
{
    public BrokerIntegrationView()
    {
        InitializeComponent();
    }

    private void OnChooseCsvFolder(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not BrokerIntegrationViewModel vm) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "新しいCSVを確認するフォルダーを選択",
            SelectedPath = vm.CsvWatchFolder
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            vm.CsvWatchFolder = dialog.SelectedPath;
    }
}
