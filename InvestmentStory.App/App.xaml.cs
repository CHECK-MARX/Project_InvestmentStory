using System.Configuration;
using System.Data;
using System.Windows;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Data;

namespace InvestmentStory.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, @"Local\InvestmentStory.App", out var isFirstInstance);
        _ownsInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            MessageBox.Show("Investment Story は既に起動しています。先に開いている画面を閉じてください。",
                "Investment Story", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        string? isolatedDatabasePath;
        try
        {
            isolatedDatabasePath = StartupDatabaseSelection.ResolveIsolatedPath(
                e.Args, DatabasePaths.GetDefaultDatabasePath());
            new DatabaseRestoreService(isolatedDatabasePath ?? DatabasePaths.GetDefaultDatabasePath())
                .ApplyPendingRestore();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"バックアップの復元に失敗しました。データベースの状態を確認してください。\n{ex.Message}",
                "Investment Story", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = isolatedDatabasePath is null
            ? new MainWindow()
            : new MainWindow(new MainViewModel(new InvestmentStoryRepository(isolatedDatabasePath)));
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
