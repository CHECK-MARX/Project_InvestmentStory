using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace InvestmentStory.App.ViewModels;

public sealed class ReminderRow(ReminderEntry entry)
{
    public ReminderEntry Entry { get; } = entry;
    public string Key => Entry.Event.Key;
    public ReminderKind Kind => Entry.Event.Kind;
    public string Date => Entry.Event.EventDate.ToString("yyyy/MM/dd");
    public string Title => Entry.Event.Title;
    public string Detail => Entry.Event.Detail;
    public string Source => Entry.Event.SourceLabel;
    public string Status => Entry.IsSnoozed
        ? $"延期中（{Entry.State!.SnoozedUntilUtc!.Value.ToLocalTime():yyyy/MM/dd}まで）"
        : Entry.IsUnread ? "未読" : "既読";
}

public sealed record ReminderModeOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class ReminderViewModel : ObservableObject
{
    private readonly Func<ReminderRepository> _repository;
    private readonly Func<IReadOnlyList<DividendPayment>> _payments;
    private readonly Func<IReadOnlyList<ApiFetchLogEntry>> _apiLogs;
    private readonly Action<ReminderEvent> _openSource;
    private readonly ReminderService _service = new();
    private IReadOnlyList<ReminderEvent> _events = [];
    private ReminderRow? _selected;
    private ReminderModeOption _selectedMode;
    private string _message = "通知を読み込んでください。", _newNotice = "";

    public ReminderViewModel(Func<ReminderRepository> repository,
        Func<IReadOnlyList<DividendPayment>> payments,
        Func<IReadOnlyList<ApiFetchLogEntry>> apiLogs,
        Action<ReminderEvent>? openSource = null)
    {
        _repository = repository; _payments = payments; _apiLogs = apiLogs;
        _openSource = openSource ?? (_ => { });
        _selectedMode = ModeOptions[0];
        RefreshCommand = new RelayCommand(() => Load(presentNew: false));
        MarkReadCommand = new RelayCommand(() => ChangeSelected(key => _repository().MarkRead(key, DateTime.UtcNow)));
        MarkUnreadCommand = new RelayCommand(() => ChangeSelected(key => _repository().MarkUnread(key)));
        SnoozeTomorrowCommand = new RelayCommand(() => Snooze(1));
        SnoozeWeekCommand = new RelayCommand(() => Snooze(7));
        ClearSnoozeCommand = new RelayCommand(() => ChangeSelected(key => _repository().ClearSnooze(key)));
        OpenSourceCommand = new RelayCommand(() => { if (Selected is not null) _openSource(Selected.Entry.Event); });
        ExportIcsCommand = new RelayCommand(ExportIcs);
    }

    public ObservableCollection<ReminderRow> Rows { get; } = new();
    public IReadOnlyList<ReminderModeOption> ModeOptions { get; } =
    [new("InApp", "アプリ内"), new("Windows", "Windows通知")];
    public ReminderModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is null || value.Value == _selectedMode.Value) return;
            try
            {
                _repository().SaveMode(value.Value);
                SetProperty(ref _selectedMode, value);
                Message = value.Value == "Windows"
                    ? "Windows通知はアプリ起動中に表示します。Windows側の通知設定によっては表示されません。"
                    : "新着は通知画面を開いたときに表示します。";
                NotificationModeChanged?.Invoke();
            }
            catch (Exception ex) when (ex is SqliteException or IOException or ArgumentException)
            { Message = "通知方法を保存できませんでした: " + ex.Message; }
        }
    }
    public event Action? NotificationModeChanged;
    public ReminderRow? Selected { get => _selected; set => SetProperty(ref _selected, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string NewNotice { get => _newNotice; private set => SetProperty(ref _newNotice, value); }
    public string UnreadSummary => $"未読 {Rows.Count(x => x.Entry.IsUnread)}件 / 延期中 {Rows.Count(x => x.Entry.IsSnoozed)}件 / 全{Rows.Count}件";
    public RelayCommand RefreshCommand { get; }
    public RelayCommand MarkReadCommand { get; }
    public RelayCommand MarkUnreadCommand { get; }
    public RelayCommand SnoozeTomorrowCommand { get; }
    public RelayCommand SnoozeWeekCommand { get; }
    public RelayCommand ClearSnoozeCommand { get; }
    public RelayCommand OpenSourceCommand { get; }
    public RelayCommand ExportIcsCommand { get; }

    public void Load(bool presentNew = true)
    {
        try
        {
            _events = _service.Build(_payments(), _apiLogs(), DateTime.Today);
            var repo = _repository();
            SetProperty(ref _selectedMode, ModeOptions.First(x => x.Value == repo.GetMode()), nameof(SelectedMode));
            var now = DateTime.UtcNow;
            var entries = _service.ApplyStates(_events, repo.GetStates(), now);
            var notice = presentNew && SelectedMode.Value == "InApp"
                ? _service.SelectToNotify(entries, now) : [];
            if (notice.Count > 0)
            {
                repo.MarkNotified(notice.Select(x => x.Event.Key).ToArray(), now);
                NewNotice = "今回の通知: " + string.Join(" / ", notice.Select(x => x.Event.Title));
                entries = _service.ApplyStates(_events, repo.GetStates(), now);
            }
            else NewNotice = SelectedMode.Value == "Windows"
                ? "新着はWindows通知でお知らせします。対象は下の一覧でも確認できます。"
                : "新しい通知はありません。対象は下の一覧で確認できます。";
            var selectedKey = Selected?.Key;
            Rows.Clear(); foreach (var entry in entries) Rows.Add(new ReminderRow(entry));
            Selected = Rows.FirstOrDefault(x => x.Key == selectedKey);
            OnPropertyChanged(nameof(UnreadSummary));
            Message = "一覧には今後1年の予定と期限経過の未照合、対象ごとの最新更新失敗を表示。新着通知は予定日の7日前から、同じ件の再提示は7日以上空けます。";
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            Message = "通知の読込に失敗しました: " + ex.Message;
        }
    }

    public void Reset()
    {
        _events = []; Rows.Clear(); Selected = null;
        SetProperty(ref _selectedMode, ModeOptions[0], nameof(SelectedMode));
        NewNotice = ""; Message = "通知を読み込んでください。";
        OnPropertyChanged(nameof(UnreadSummary));
    }

    public IReadOnlyList<ReminderEntry> GetPendingWindowsNotifications()
    {
        var repo = _repository();
        if (repo.GetMode() != "Windows") return [];
        var now = DateTime.UtcNow;
        var events = _service.Build(_payments(), _apiLogs(), DateTime.Today);
        return _service.SelectToNotify(_service.ApplyStates(events, repo.GetStates(), now), now);
    }

    public void MarkWindowsNotified(IReadOnlyList<ReminderEntry> entries)
    {
        if (entries.Count == 0) return;
        _repository().MarkNotified(entries.Select(x => x.Event.Key).ToArray(), DateTime.UtcNow);
        if (Rows.Count > 0) Load(presentNew: false);
    }

    public void ReportWindowsError(string detail) => Message = "Windows通知を表示できませんでした: " + detail;

    private void ChangeSelected(Action<string> change)
    {
        if (Selected is null) return;
        try { change(Selected.Key); Load(presentNew: false); }
        catch (Exception ex) when (ex is SqliteException or IOException or ArgumentException)
        { Message = ex.Message; }
    }

    private void Snooze(int days)
    {
        if (Selected is null) return;
        var until = DateTime.Today.AddDays(days + 1).ToUniversalTime();
        ChangeSelected(key => _repository().SnoozeUntil(key, until, DateTime.UtcNow));
    }

    private void ExportIcs()
    {
        var future = _events.Count(x => x.Kind == ReminderKind.DividendUpcoming && x.IsCalendarEligible);
        if (future == 0) { Message = "出力できる今後1年の入金予定がありません。"; return; }
        var dialog = new SaveFileDialog
        {
            Filter = "iCalendar (*.ics)|*.ics",
            DefaultExt = ".ics", FileName = $"InvestmentStory_配当予定_{DateTime.Today:yyyyMMdd}.ics",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var text = _service.ExportUpcomingIcs(_events, DateTime.Today, DateTime.UtcNow);
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false));
            Message = $"入金予定{future}件をICSに出力しました。実績入金後はアプリの記録を確認してください。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Message = "ICS出力に失敗しました: " + ex.Message; }
    }
}
