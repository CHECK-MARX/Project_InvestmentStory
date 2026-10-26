using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.SelectedApiLog is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ApiLogGrid.BringIntoView();
            ApiLogGrid.ScrollIntoView(vm.SelectedApiLog);
        });
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not SettingsViewModel viewModel)
        {
            return;
        }

        AlphaVantagePasswordBox.Password = viewModel.AlphaVantageApiKey;
        JQuantsPasswordBox.Password = viewModel.JQuantsApiKey;
    }

    private void AlphaVantagePasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.AlphaVantageApiKey = AlphaVantagePasswordBox.Password;
        }
    }

    private void JQuantsPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.JQuantsApiKey = JQuantsPasswordBox.Password;
        }
    }

    private async void RestoreBackup_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        RestoreBackupButton.IsEnabled = false;
        RestoreDrillButton.IsEnabled = false;
        try
        {
            var backupFileName = viewModel.SelectedBackup?.BackupKey
                ?? throw new InvalidOperationException("復元するバックアップを選択してください。");
            var impact = await viewModel.GetRestoreImpactAsync(backupFileName);
            var confirmation = MessageBox.Show(Window.GetWindow(this),
                impact + "\n\nこのバックアップへの復元を予約しますか？",
                "DB全体の復元", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes) return;
            viewModel.ScheduleBackupRestore(backupFileName);
            MessageBox.Show(Window.GetWindow(this),
                "復元を予約しました。入力中の作業を保存してアプリを終了し、再起動してください。起動時に復元します。",
                "DB全体の復元", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "復元できません",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RestoreBackupButton.IsEnabled = true;
            RestoreDrillButton.IsEnabled = true;
        }
    }

    private async void RestoreDrill_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        RestoreDrillButton.IsEnabled = false;
        RestoreBackupButton.IsEnabled = false;
        try
        {
            var summary = await viewModel.RunSelectedBackupDrillAsync();
            MessageBox.Show(Window.GetWindow(this), summary, "復元演習",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "復元演習に失敗しました",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RestoreDrillButton.IsEnabled = true;
            RestoreBackupButton.IsEnabled = true;
        }
    }
}
