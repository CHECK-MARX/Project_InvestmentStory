using System.Collections.ObjectModel;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Win32;

namespace InvestmentStory.App.ViewModels;

public sealed record CashCsvMatchOption(CashOperation Operation)
{
    public override string ToString() => $"{Operation.Date:yyyy/MM/dd} / {string.Join("・", Operation.Legs.Select(x => $"{x.Account} {x.Amount:#,0.############################} {x.Currency}"))} / {Operation.Evidence}" +
        (Operation.CancelledAtUtc is null ? "" : "（取消済み・照合不可）");
}

public sealed class CashCsvRowViewModel(CashCsvReview review) : ObservableObject
{
    private string _resolution = CashCsvResolutions.Hold;
    public CashCsvReview Review { get; } = review;
    public int Number => Review.Candidate.RecordNumber;
    public string Date => Review.Candidate.Operation?.Date.ToString("yyyy/MM/dd") ?? "—";
    public string Kind => Review.Candidate.Operation is { } op ? CashLedgerService.KindLabel(op.Kind) : "要確認";
    public string Amount => Review.Candidate.Operation is { } op
        ? string.Join(" / ", op.Legs.Select(x => $"{x.Amount:+#,0.############################;-#,0.############################;0} {x.Currency}")) : "—";
    public string RawRecord => Review.Candidate.RawRecord;
    public string Status => Review.AlreadyImported ? "取込・照合済み（再追加しません）" : Review.Candidate.Operation is null
        ? Review.Candidate.Problem : Review.NeedsReview ? "重複候補：同一か別取引か確認" : "取込候補";
    public bool CanChoose => !Review.AlreadyImported && Review.Candidate.Operation is not null;
    public IReadOnlyList<string> Options => !CanChoose ? [CashCsvResolutions.Hold] : Review.NeedsReview
        ? [CashCsvResolutions.Hold, CashCsvResolutions.Link, CashCsvResolutions.Distinct]
        : [CashCsvResolutions.Hold, CashCsvResolutions.Add];
    public string Resolution { get => _resolution; set => SetProperty(ref _resolution, value); }
    public string Reason { get; set; } = string.Empty;
    public IReadOnlyList<CashCsvMatchOption> Matches { get; } = review.Matches.Select(x => new CashCsvMatchOption(x)).ToArray();
    public CashCsvMatchOption? SelectedMatch { get; set; }
    public CashCsvDecision Decision() => new(Review.Candidate.SourceKey, Resolution, Reason, SelectedMatch?.Operation.Id);
}

public sealed class CashCsvImportViewModel : ObservableObject
{
    private readonly Func<CashLedgerRepository> _repository;
    private readonly Action _afterChanged;
    private CashLedgerRepository? _previewRepository;
    private CashCsvPlan? _plan;
    private string _filePath = "", _account = "", _emptyCurrency = "", _mappingReason = "", _message = "CSVを選択し、入出金先の現金口座を指定してください。";
    private bool _isBusy;
    private CashCsvRowViewModel? _selectedRow;

    public CashCsvImportViewModel(Func<CashLedgerRepository> repository, Action afterChanged)
    {
        _repository = repository; _afterChanged = afterChanged;
        BrowseCommand = new RelayCommand(Browse, () => IsIdle);
        PreviewCommand = new RelayCommand(() => _ = PreviewAsync(), () => IsIdle);
        ImportCommand = new RelayCommand(() => _ = ImportAsync(), () => IsIdle && _plan is not null);
        SelectCandidatesCommand = new RelayCommand(() =>
        {
            foreach (var row in Rows.Where(x => x.CanChoose && !x.Review.NeedsReview)) row.Resolution = CashCsvResolutions.Add;
            Message = "重複候補のない行を追加対象にしました。対象口座・金額・振込の分類を確認してから取り込んでください。";
        }, () => IsIdle && _plan is not null);
    }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand SelectCandidatesCommand { get; }
    public ObservableCollection<CashCsvRowViewModel> Rows { get; } = new();
    public IReadOnlyList<string> EmptyCurrencies { get; } = ["", "JPY", "USD"];
    public string FilePath { get => _filePath; set { if (SetProperty(ref _filePath, value)) Invalidate(); } }
    public string Account { get => _account; set { if (SetProperty(ref _account, value)) Invalidate(); } }
    public string EmptyCurrency { get => _emptyCurrency; set { if (SetProperty(ref _emptyCurrency, value ?? "")) Invalidate(); } }
    public string MappingReason { get => _mappingReason; set { if (SetProperty(ref _mappingReason, value)) Invalidate(); } }
    public bool ExternalFlowsConfirmed { get; set; }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public CashCsvRowViewModel? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }
    public bool IsIdle => !IsBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(IsIdle)); RefreshCommands(); }
    }
    private void RefreshCommands()
    {
        BrowseCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged();
        ImportCommand.RaiseCanExecuteChanged(); SelectCandidatesCommand.RaiseCanExecuteChanged();
    }
    private void Invalidate()
    {
        _plan = null; _previewRepository = null; SelectedRow = null; Rows.Clear();
        ExternalFlowsConfirmed = false; OnPropertyChanged(nameof(ExternalFlowsConfirmed));
        Message = "設定変更後はプレビューを実行してください。"; RefreshCommands();
    }
    public void Reset()
    {
        if (IsBusy) throw new InvalidOperationException("現金CSVの処理中です。");
        _filePath = _account = _emptyCurrency = _mappingReason = "";
        Invalidate(); RefreshAllProperties();
    }
    private void Browse()
    {
        var dialog = new OpenFileDialog { Filter = "CSVファイル (*.csv)|*.csv", CheckFileExists = true };
        if (dialog.ShowDialog() == true) FilePath = dialog.FileName;
    }
    public async Task PreviewAsync()
    {
        if (IsBusy) return;
        Invalidate(); IsBusy = true; Message = "CSVと現金台帳を照合しています…";
        var path = FilePath; var account = Account; var currency = EmptyCurrency; var reason = MappingReason;
        try
        {
            var repository = _repository();
            var result = await Task.Run(() =>
            {
                var plan = new CashCsvParser().Parse(path, account, currency, reason);
                return (Plan: plan, Reviews: repository.PreviewCsv(plan));
            });
            _plan = result.Plan; _previewRepository = repository;
            SetRows(result.Reviews);
            Message = $"{Rows.Count}件：取込候補 {Rows.Count(x => x.CanChoose && !x.Review.NeedsReview)} / 重複候補 {Rows.Count(x => x.CanChoose && x.Review.NeedsReview)} / 取込済み {Rows.Count(x => x.Review.AlreadyImported)} / 要確認 {Rows.Count(x => !x.Review.AlreadyImported && x.Review.Candidate.Operation is null)}。まだ保存していません。";
        }
        catch (Exception ex) { Message = "プレビューできませんでした: " + ex.Message; }
        finally { IsBusy = false; }
    }
    public async Task ImportAsync()
    {
        if (IsBusy || _plan is null || _previewRepository is null) return;
        var plan = _plan; var repository = _previewRepository;
        var decisions = Rows.Select(x => x.Decision()).ToArray();
        if (decisions.All(x => x.Resolution == CashCsvResolutions.Hold)) { Message = "取込方法を指定した行がありません。"; return; }
        var external = ExternalFlowsConfirmed;
        IsBusy = true; Message = "バックアップを作成し、選択した明細を取り込んでいます…";
        CashCsvImportResult result;
        try { result = await Task.Run(() => repository.ImportCsv(plan, decisions, external)); }
        catch (Exception ex) { Message = "取り込めませんでした。今回の変更は保存していません: " + ex.Message; IsBusy = false; return; }
        Message = $"追加 {result.AddedCount}件 / 既存と照合 {result.LinkedCount}件 / 未追加 {result.SkippedCount}件。残高照合をやり直してください。";
        try
        {
            SetRows(await Task.Run(() => repository.PreviewCsv(plan)));
            _afterChanged();
        }
        catch (Exception ex) { Invalidate(); Message = $"取込は完了しました（追加 {result.AddedCount}件・照合 {result.LinkedCount}件）。再表示に失敗したため再読込してください: " + ex.Message; }
        finally { IsBusy = false; }
    }
    private void SetRows(IReadOnlyList<CashCsvReview> reviews)
    {
        Rows.Clear(); foreach (var review in reviews) Rows.Add(new(review));
        SelectedRow = Rows.FirstOrDefault();
    }
}
