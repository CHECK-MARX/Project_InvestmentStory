using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using InvestmentStory.App.Dialogs;
using InvestmentStory.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.App;

public partial class MainWindow : Window
{
    private System.Windows.Forms.NotifyIcon? _reminderIcon;
    private IInputElement? _focusBeforePrivacy;

    public MainWindow() : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.DataRefreshed += PresentWindowsReminders;
            mainViewModel.Reminders.NotificationModeChanged += PresentWindowsReminders;
            mainViewModel.PrivacyModeChanged += OnPrivacyModeChanged;
        }
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => _reminderIcon?.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is MainViewModel mainViewModel)
        {
            if (mainViewModel.IsPrivacyMode) PrivacyReturnButton.Focus();
            PresentWindowsReminders();
            await mainViewModel.VerifyDividendSchedulesOnStartupAsync();
        }
    }

    private void PresentWindowsReminders()
    {
        if (DataContext is not MainViewModel mainViewModel || mainViewModel.IsPrivacyMode) return;
        try
        {
            var pending = mainViewModel.Reminders.GetPendingWindowsNotifications();
            if (pending.Count == 0) return;
            _reminderIcon ??= CreateReminderIcon(mainViewModel);
            var body = string.Join("\n", pending.Select(x => $"{x.Event.EventDate:MM/dd} {x.Event.Title}"));
            _reminderIcon.ShowBalloonTip(10000, "Investment Story: 配当・更新通知", body,
                System.Windows.Forms.ToolTipIcon.Info);
            mainViewModel.Reminders.MarkWindowsNotified(pending);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException or Win32Exception or
                                   System.Runtime.InteropServices.ExternalException)
        {
            mainViewModel.Reminders.ReportWindowsError(ex.Message);
        }
    }

    private void OnPrivacyModeChanged(bool hidden)
    {
        if (_reminderIcon is not null) _reminderIcon.Visible = !hidden;
        if (hidden)
        {
            _focusBeforePrivacy = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PrivacyReturnButton.Focus());
            return;
        }
        if (_focusBeforePrivacy is UIElement { IsEnabled: true } previous)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => previous.Focus());
        _focusBeforePrivacy = null;
        PresentWindowsReminders();
    }

    private static System.Windows.Forms.NotifyIcon CreateReminderIcon(MainViewModel mainViewModel)
    {
        var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "Investment Story",
            Visible = true
        };
        icon.BalloonTipClicked += (_, _) => mainViewModel.ShowRemindersCommand.Execute(null);
        return icon;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel ||
            !mainViewModel.Simulation.HasUnsavedDividendPlanChanges)
        {
            return;
        }

        if (mainViewModel.IsPrivacyMode)
        {
            e.Cancel = true;
            MessageBox.Show(this, "画面共有モードを解除してから終了してください。",
                "Investment Story", MessageBoxButton.OK, MessageBoxImage.Information);
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
