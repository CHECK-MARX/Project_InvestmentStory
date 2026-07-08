using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class BrokerIntegrationViewModel : ObservableObject
{
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly Action _openCsvImport;
    private string _currentMode = "CSV取込";
    private string _selectedMode = "CSV取込";
    private string _selectedMergePolicy = "証券会社データを正として上書き";
    private string _existingSummary = "登録済み銘柄を読み込み中です。";
    private string _message = string.Empty;

    public BrokerIntegrationViewModel(
        Func<AppSettings> getSettings,
        Action<AppSettings> saveSettings,
        Action openCsvImport)
    {
        _getSettings = getSettings;
        _saveSettings = saveSettings;
        _openCsvImport = openCsvImport;
        SaveModeCommand = new RelayCommand(SaveMode);
        OpenCsvImportCommand = new RelayCommand(_openCsvImport);
    }

    public ICommand SaveModeCommand { get; }
    public ICommand OpenCsvImportCommand { get; }

    public string[] BrokerDataModes { get; } =
    {
        "CSV取込",
        "手入力",
        "公式API（市場データのみ）"
    };

    public string[] MergePolicies { get; } =
    {
        "証券会社データを正として上書き",
        "手入力を保護して不足分だけ補完",
        "現在株数・現在株価だけ証券会社データで更新",
        "新規銘柄だけ追加",
        "すべてプレビューして手動選択"
    };

    public IReadOnlyList<string> Rules { get; } = new[]
    {
        "証券会社 + ティッカーが一致した既存データは、CSVで取得した口座データを正として上書きします。",
        "上書き対象は、CSVから判定できる保有株数、取得単価、証券会社名、通貨、配当入金実績です。",
        "手入力メモ、分割メモ、独自メモなど証券会社から取得できない情報は残します。",
        "証券会社が異なる同一ティッカー、口座区分違い、複数候補は自動上書きせず確認対象にします。",
        "注文、売買、取消などの発注機能は作りません。読み取りと取込だけに限定します。"
    };

    public IReadOnlyList<string> SupportedConnectors { get; } = new[]
    {
        "現在対応: CSV取込。SBI証券、野村證券などの対応済みCSVは自動判定して一括取込します。",
        "現在対応: 市場データAPI。株価、為替、会社名、予想配当などを更新します。",
        "未対応: 証券会社ログイン自動取得。公式API契約や認証仕様がないため、このローカルアプリでは標準機能にしません。",
        "未対応: 画面取得/ブラウザ連携。規約・二要素認証・画面変更の問題があるため、現時点では選択肢から外します。"
    };

    public ObservableCollection<BrokerIntegrationExistingRowViewModel> ExistingRows { get; } = new();

    public string CurrentMode
    {
        get => _currentMode;
        private set => SetProperty(ref _currentMode, value);
    }

    public string SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    public string SelectedMergePolicy
    {
        get => _selectedMergePolicy;
        set => SetProperty(ref _selectedMergePolicy, value);
    }

    public string ExistingSummary
    {
        get => _existingSummary;
        private set => SetProperty(ref _existingSummary, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public void Update(IReadOnlyList<StockPosition> positions, AppSettings settings)
    {
        var mode = string.IsNullOrWhiteSpace(settings.BrokerDataMode) ? "CSV取込" : settings.BrokerDataMode;
        if (!BrokerDataModes.Contains(mode))
        {
            mode = "CSV取込";
            settings.BrokerDataMode = mode;
            _saveSettings(settings);
        }

        CurrentMode = mode;
        SelectedMode = mode;

        ExistingRows.Clear();
        foreach (var position in positions.OrderBy(x => NormalizeTicker(x.Stock.Ticker)).ThenBy(x => x.Stock.Broker))
        {
            ExistingRows.Add(new BrokerIntegrationExistingRowViewModel
            {
                Ticker = position.Stock.Ticker,
                Name = position.Stock.Name,
                Broker = string.IsNullOrWhiteSpace(position.Stock.Broker) ? "未入力" : position.Stock.Broker,
                Currency = position.Stock.Currency,
                CurrentShares = position.CurrentHolding.CurrentShares.ToString("N2"),
                Source = string.IsNullOrWhiteSpace(position.Stock.DataSource) ? "手入力" : position.Stock.DataSource,
                ProtectedFields = "メモ・分割メモを保持",
                PlannedAction = BuildPlannedAction(position)
            });
        }

        var total = positions.Count;
        var brokerBlank = positions.Count(x => string.IsNullOrWhiteSpace(x.Stock.Broker));
        var tickerDuplicates = positions
            .GroupBy(x => NormalizeTicker(x.Stock.Ticker))
            .Count(x => !string.IsNullOrWhiteSpace(x.Key) && x.Count() > 1);
        ExistingSummary = total == 0
            ? "登録済み銘柄はありません。CSV取込時はすべて新規候補になります。"
            : $"登録済み {total:N0}件。証券会社未入力 {brokerBlank:N0}件、同一ティッカー重複候補 {tickerDuplicates:N0}件。";
    }

    private void SaveMode()
    {
        var settings = _getSettings();
        settings.BrokerDataMode = SelectedMode;
        _saveSettings(settings);
        CurrentMode = SelectedMode;
        Message = $"連携方式を保存しました。既存手入力データの扱い: {SelectedMergePolicy}";
    }

    private static string BuildPlannedAction(StockPosition position)
    {
        var hasBroker = !string.IsNullOrWhiteSpace(position.Stock.Broker);
        var hasManualPurchase = position.Purchase.Shares > 0m || position.Purchase.UnitPrice > 0m;
        if (!hasBroker)
        {
            return hasManualPurchase
                ? "証券会社未入力のため確認後に証券会社データで上書き"
                : "証券会社未入力のため確認が必要";
        }

        return hasManualPurchase
            ? "同一証券会社・同一ティッカーなら証券会社データで上書き"
            : "同一証券会社・同一ティッカーなら証券会社データを反映";
    }

    private static string NormalizeTicker(string ticker)
    {
        return ticker.Trim().ToUpperInvariant().Replace(".T", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class BrokerIntegrationExistingRowViewModel
{
    public string Ticker { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string CurrentShares { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string ProtectedFields { get; init; } = string.Empty;
    public string PlannedAction { get; init; } = string.Empty;
}
