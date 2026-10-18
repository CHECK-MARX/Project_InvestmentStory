using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly Func<AppSettings> _loadSettings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly Func<IReadOnlyList<ApiFetchLogEntry>> _loadLogs;
    private readonly Action<AppSettings> _afterSave;
    private readonly DatabaseRestoreService? _restoreService;
    private string _message = string.Empty;
    private DatabaseBackupInfo? _selectedBackup;

    public SettingsViewModel(
        Func<AppSettings> loadSettings,
        Action<AppSettings> saveSettings,
        Func<IReadOnlyList<ApiFetchLogEntry>> loadLogs,
        Action<AppSettings>? afterSave = null,
        DatabaseRestoreService? restoreService = null)
    {
        _loadSettings = loadSettings;
        _saveSettings = saveSettings;
        _loadLogs = loadLogs;
        _afterSave = afterSave ?? (_ => { });
        _restoreService = restoreService;
        SaveCommand = new RelayCommand(Save);
        ReloadLogsCommand = new RelayCommand(ReloadLogs);
        ReloadBackupsCommand = new RelayCommand(ReloadBackups);
        Load();
    }

    public ICommand SaveCommand { get; }
    public ICommand ReloadLogsCommand { get; }
    public ICommand ReloadBackupsCommand { get; }
    public ObservableCollection<ApiFetchLogRowViewModel> ApiLogs { get; } = new();
    public ObservableCollection<DatabaseBackupInfo> Backups { get; } = new();
    public string BackupStorageSummary { get; private set; } = "バックアップはありません。";
    public DatabaseBackupInfo? SelectedBackup
    {
        get => _selectedBackup;
        set => SetProperty(ref _selectedBackup, value);
    }

    public Task<string> GetRestoreImpactAsync(string backupFileName)
    {
        if (_restoreService is null || string.IsNullOrWhiteSpace(backupFileName))
            throw new InvalidOperationException("復元するバックアップを選択してください。");
        return Task.Run(() => _restoreService.GetRestoreImpact(backupFileName));
    }

    public void ScheduleBackupRestore(string backupFileName)
    {
        if (_restoreService is null || string.IsNullOrWhiteSpace(backupFileName))
            throw new InvalidOperationException("復元するバックアップを選択してください。");
        _restoreService.ScheduleRestore(backupFileName);
        Message = "復元を予約しました。アプリを終了し、再起動すると復元します。";
    }

    public async Task<string> RunSelectedBackupDrillAsync()
    {
        if (_restoreService is null || SelectedBackup is null)
            throw new InvalidOperationException("演習するバックアップを選択してください。");
        var fileName = SelectedBackup.FileName;
        var result = await Task.Run(() => _restoreService.RunRestoreDrill(fileName));
        ReloadBackups();
        SelectedBackup = Backups.FirstOrDefault(backup => backup.FileName == fileName);
        Message = result.Summary;
        return result.Summary;
    }

    public string[] MarketDataModes { get; } = { "Mock", "Web/API" };
    public string[] UsProviders { get; } = { "Alpha Vantage" };
    public string[] JapanProviders { get; } = { "Yahoo Finance", "J-Quants" };
    public string[] ExchangeProviders { get; } = { "Yahoo Finance", "Mock" };
    public string[] BrokerDataModes { get; } = { "CSV取込", "手入力", "公式API（市場データのみ）" };
    public string[] ThemeModes { get; } = { "ライト", "ダーク", "Windowsの設定に従う" };

    public string MarketDataMode { get; set; } = "Web/API";
    public string UsMarketDataProvider { get; set; } = "Alpha Vantage";
    public string JapanMarketDataProvider { get; set; } = "Yahoo Finance";
    public string ExchangeRateProvider { get; set; } = "Yahoo Finance";
    public string BrokerDataMode { get; set; } = "手入力";
    public string AlphaVantageApiKey { get; set; } = string.Empty;
    public string JQuantsApiKey { get; set; } = string.Empty;
    public int ApiTimeoutSeconds { get; set; } = 10;
    public bool UseLastValueOnApiFailure { get; set; } = true;
    public bool SaveLoginCredentials { get; set; }
    public bool EnableApiResponseLog { get; set; }
    public string ThemeMode { get; set; } = "ライト";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public void Load()
    {
        var settings = _loadSettings();
        MarketDataMode = MarketDataModes.Contains(settings.MarketDataMode) ? settings.MarketDataMode : "Web/API";
        UsMarketDataProvider = settings.UsMarketDataProvider;
        JapanMarketDataProvider = JapanProviders.Contains(settings.JapanMarketDataProvider) ? settings.JapanMarketDataProvider : "Yahoo Finance";
        ExchangeRateProvider = settings.ExchangeRateProvider;
        BrokerDataMode = BrokerDataModes.Contains(settings.BrokerDataMode) ? settings.BrokerDataMode : "CSV取込";
        AlphaVantageApiKey = settings.AlphaVantageApiKey;
        JQuantsApiKey = settings.JQuantsApiKey;
        ApiTimeoutSeconds = settings.ApiTimeoutSeconds;
        UseLastValueOnApiFailure = settings.UseLastValueOnApiFailure;
        SaveLoginCredentials = settings.SaveLoginCredentials;
        EnableApiResponseLog = settings.EnableApiResponseLog;
        ThemeMode = ToDisplayThemeMode(settings.ThemeMode);
        ReloadLogs();
        ReloadBackups();
        RefreshAllProperties();
    }

    private void Save()
    {
        if (ApiTimeoutSeconds < 3 || ApiTimeoutSeconds > 60)
        {
            Message = "APIタイムアウト秒数は3から60の範囲で入力してください。";
            return;
        }

        var settings = new AppSettings
        {
            DataDisplayMode = _loadSettings().DataDisplayMode,
            MarketDataMode = MarketDataMode,
            UsMarketDataProvider = UsMarketDataProvider,
            JapanMarketDataProvider = JapanMarketDataProvider,
            ExchangeRateProvider = ExchangeRateProvider,
            BrokerDataMode = BrokerDataMode,
            AlphaVantageApiKey = AlphaVantageApiKey,
            JQuantsApiKey = JQuantsApiKey,
            ApiTimeoutSeconds = ApiTimeoutSeconds,
            UseLastValueOnApiFailure = UseLastValueOnApiFailure,
            SaveLoginCredentials = SaveLoginCredentials,
            EnableApiResponseLog = EnableApiResponseLog,
            ThemeMode = ToStoredThemeMode(ThemeMode),
            IsSidebarCollapsed = _loadSettings().IsSidebarCollapsed,
            StockListDisplayMode = _loadSettings().StockListDisplayMode,
            StockListFilterJson = _loadSettings().StockListFilterJson,
            LastDashboardCompositionMode = _loadSettings().LastDashboardCompositionMode,
            LastOpenedPage = _loadSettings().LastOpenedPage
        };
        _saveSettings(settings);
        _afterSave(settings);
        Message = "設定を保存しました。";
    }

    private void ReloadLogs()
    {
        ApiLogs.Clear();
        foreach (var log in _loadLogs())
        {
            ApiLogs.Add(new ApiFetchLogRowViewModel(log));
        }

        OnPropertyChanged(nameof(ApiLogs));
    }

    private void ReloadBackups()
    {
        Backups.Clear();
        if (_restoreService is null) return;
        foreach (var backup in _restoreService.GetBackups()) Backups.Add(backup);
        var totalBytes = Backups.Sum(backup => backup.SizeBytes);
        BackupStorageSummary = Backups.Count == 0 ? "バックアップはありません。" :
            $"{Backups.Count}世代 / 合計 {totalBytes / 1024.0 / 1024.0:N1} MB。" +
            "自動削除はしません。別媒体への保存と定期的な復元確認を行ってください。";
        OnPropertyChanged(nameof(Backups));
        OnPropertyChanged(nameof(BackupStorageSummary));
    }

    private static string ToDisplayThemeMode(string value) =>
        value.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? "ダーク"
            : value.Equals("System", StringComparison.OrdinalIgnoreCase)
                ? "Windowsの設定に従う"
                : "ライト";

    private static string ToStoredThemeMode(string value) =>
        value.Equals("ダーク", StringComparison.OrdinalIgnoreCase)
            ? "Dark"
            : value.Equals("Windowsの設定に従う", StringComparison.OrdinalIgnoreCase)
                ? "System"
                : "Light";
}
