using System.Collections.ObjectModel;
using System.IO;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.App.ViewModels;

public sealed class CsvReviewRow(CsvReviewRecord record)
{
    public CsvReviewRecord Record { get; } = record;
    public string SourceKey => Record.SourceKey;
    public string Category => Record.Category;
    public string Date => Record.EventDate;
    public string Security => Record.Security;
    public string Broker => Record.Broker;
    public string Account => Record.Account;
    public string Status => Record.Status;
    public string Reason => Record.Reason;
    public string ReviewNote => Record.ReviewNote;
    public string Source => string.IsNullOrWhiteSpace(Record.SourceFile) ? "元ファイル記録なし" :
        Record.SourceRowNumber > 0 ? $"{Record.SourceFile} / {Record.SourceRowNumber}行" : Record.SourceFile;
}

public sealed class CsvReviewViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<CsvReviewRecord>> _loadRecords;
    private readonly Action _openImport;
    private readonly Action<string, string, string>? _saveDisposition;
    private readonly Action? _afterChange;
    private CsvReviewRow? _selected;
    private string _reviewNote = string.Empty;
    private string _message = "保存済み要確認記録を読み込んでください。";

    public CsvReviewViewModel(Func<IReadOnlyList<CsvReviewRecord>> loadRecords, Action openImport,
        Action<string, string, string>? saveDisposition = null, Action? afterChange = null)
    {
        _loadRecords = loadRecords;
        _openImport = openImport;
        _saveDisposition = saveDisposition;
        _afterChange = afterChange;
        RefreshCommand = new RelayCommand(Load);
        OpenImportCommand = new RelayCommand(_openImport);
        HoldCommand = new RelayCommand(() => ChangeStatus("保留"), () => CanReview);
        ConfirmCommand = new RelayCommand(() => ChangeStatus("確認済み"), () => CanReview);
        ReopenCommand = new RelayCommand(() => ChangeStatus("要確認"), () => CanReview);
    }

    public ObservableCollection<CsvReviewRow> Rows { get; } = new();
    public CsvReviewRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                ReviewNote = value?.ReviewNote ?? string.Empty;
                OnPropertyChanged(nameof(SelectedDetail));
                OnPropertyChanged(nameof(CanReview));
                HoldCommand.RaiseCanExecuteChanged();
                ConfirmCommand.RaiseCanExecuteChanged();
                ReopenCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool CanReview => _saveDisposition is not null && Selected is not null &&
        (Selected.SourceKey.StartsWith("holding:", StringComparison.Ordinal) ||
         Selected.SourceKey.StartsWith("dividend:", StringComparison.Ordinal));
    public string ReviewNote { get => _reviewNote; set => SetProperty(ref _reviewNote, value); }
    public string SelectedDetail => Selected is null ? "行を選択すると、元記録と確認理由を表示します。" :
        $"{Selected.Category} / {Selected.Status}\n元記録: {Selected.Source}\n確認理由: {Selected.Reason}" +
        (string.IsNullOrWhiteSpace(Selected.ReviewNote) ? "" : $"\n判断理由: {Selected.ReviewNote}");
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenImportCommand { get; }
    public RelayCommand HoldCommand { get; }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand ReopenCommand { get; }

    public void Load()
    {
        try
        {
            var selectedKey = Selected?.SourceKey;
            Rows.Clear();
            foreach (var record in _loadRecords()) Rows.Add(new CsvReviewRow(record));
            Selected = Rows.FirstOrDefault(x => x.SourceKey == selectedKey);
            Message = $"保存済み記録 {Rows.Count}件。取引の解決はCSV取込画面で行い、保有・配当履歴は理由を記録して確認済み／保留にできます。";
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        { Message = "要確認記録を読み込めませんでした: " + ex.Message; }
    }

    public bool FocusKey(string sourceKey)
    {
        var row = Rows.FirstOrDefault(x => x.SourceKey == sourceKey);
        if (row is null) { Selected = null; return false; }
        Selected = row;
        return true;
    }

    private void ChangeStatus(string status)
    {
        if (!CanReview || Selected is null) return;
        var key = Selected.SourceKey;
        try
        {
            _saveDisposition!(key, status, ReviewNote);
            Load(); FocusKey(key);
            _afterChange?.Invoke();
            Message = $"{Selected?.Security ?? "記録"}を「{status}」に更新しました。";
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException or ArgumentException)
        { Message = "確認状態を保存できませんでした: " + ex.Message; }
    }
}
