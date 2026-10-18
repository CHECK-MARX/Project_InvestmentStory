using InvestmentStory.App.Infrastructure;

namespace InvestmentStory.App.ViewModels;

public sealed class CsvPreviewRowViewModel : ObservableObject
{
    private string _conflictChoice = "保留";
    private string _tradeChoice = "保留";
    private string _manualChoice = "保留";
    public static IReadOnlyList<string> ManualChoices { get; } = new[] { "保留", "この行を反映" };
    public IReadOnlyList<string> AvailableManualChoices => ManualChoices;
    public bool CanChooseManual { get; init; }
    public string ManualCandidateId { get; init; } = string.Empty;
    public string ManualChoice
    {
        get => _manualChoice;
        set
        {
            if (SetProperty(ref _manualChoice, value)) OnPropertyChanged(nameof(After));
        }
    }
    public static IReadOnlyList<string> ConflictChoices { get; } = new[] { "保留", "この行を採用" };
    public static IReadOnlyList<string> TradeChoices { get; } = new[] { "保留", "別取引として追加" };
    public static IReadOnlyList<string> TradeChoicesWithExisting { get; } =
        new[] { "保留", "別取引として追加", "既存と同一" };
    public IReadOnlyList<string> AvailableConflictChoices => ConflictChoices;
    public IReadOnlyList<string> AvailableTradeChoices => CanMatchExistingTrade
        ? TradeChoicesWithExisting : TradeChoices;
    public bool CanChooseConflict { get; init; }
    public string ConflictCandidateId { get; init; } = string.Empty;
    public bool CanChooseTrade { get; init; }
    public bool CanMatchExistingTrade { get; init; }
    public string TradeCandidateId { get; init; } = string.Empty;
    public string TradeChoice
    {
        get => _tradeChoice;
        set
        {
            if (SetProperty(ref _tradeChoice, value)) OnPropertyChanged(nameof(After));
        }
    }
    public string ConflictChoice
    {
        get => _conflictChoice;
        set
        {
            if (SetProperty(ref _conflictChoice, value)) OnPropertyChanged(nameof(After));
        }
    }
    public int RowNumber { get; init; }
    public string Status { get; init; } = "取込時判定";
    public string Before { get; init; } = string.Empty;
    private string _after = string.Empty;
    public string After
    {
        get => CanChooseConflict && ConflictChoice == "この行を採用"
            ? "選択済み（取込時に再判定）" :
            CanChooseManual && ManualChoice == "この行を反映"
                ? "反映を選択（取込時に再判定）" :
            CanChooseTrade && TradeChoice == "別取引として追加"
                ? "追加を選択（取込時に再判定）" :
            CanChooseTrade && TradeChoice == "既存と同一"
                ? "同一と判断（反映なし）" : _after;
        init => _after = value;
    }
    public string Reason { get; init; } = string.Empty;
    public string Values { get; init; } = string.Empty;
}
