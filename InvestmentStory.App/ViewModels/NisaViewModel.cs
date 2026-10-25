using System.Collections.ObjectModel;
using System.Globalization;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed record NisaOption(string Key, string Label)
{
    public override string ToString() => Label;
}

public sealed record NisaTransactionRow(long Id, string Date, string Frame, string Kind,
    string BookCostJpy, string Source, string Evidence);

public sealed class NisaViewModel : ObservableObject
{
    private readonly Func<NisaHistoryRepository> _repository;
    private readonly NisaCapacityService _service = new();
    private IReadOnlyList<StockSnapshot> _snapshots = [];
    private DateTime _asOf = DateTime.Today;
    private NisaCapacityAnalysis? _analysis;
    private NisaOption _frame, _kind;
    private DateTime _entryDate = DateTime.Today;
    private string _bookCost = string.Empty, _source = string.Empty, _evidence = string.Empty;
    private string _historyEvidence = string.Empty, _message = string.Empty;
    private NisaTransactionRow? _selected;

    public NisaViewModel(Func<NisaHistoryRepository> repository)
    {
        _repository = repository;
        _frame = Frames[0]; _kind = Kinds[0];
        SaveCommand = new RelayCommand(Save);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        ConfirmHistoryCommand = new RelayCommand(ConfirmHistory);
    }

    public IReadOnlyList<NisaOption> Frames { get; } =
    [new(AccountTypes.NisaGrowth, "成長投資枠"), new(AccountTypes.NisaAccumulation, "つみたて投資枠")];
    public IReadOnlyList<NisaOption> Kinds { get; } =
    [new(NisaTransactionKinds.Buy, "買付簿価"), new(NisaTransactionKinds.Sell, "売却した取得簿価")];
    public ObservableCollection<NisaTransactionRow> Transactions { get; } = new();
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ConfirmHistoryCommand { get; }
    public NisaOption Frame { get => _frame; set => SetProperty(ref _frame, value); }
    public NisaOption Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public DateTime EntryDate { get => _entryDate; set => SetProperty(ref _entryDate, value); }
    public string BookCost { get => _bookCost; set => SetProperty(ref _bookCost, value); }
    public string Source { get => _source; set => SetProperty(ref _source, value); }
    public string Evidence { get => _evidence; set => SetProperty(ref _evidence, value); }
    public string HistoryEvidence { get => _historyEvidence; set => SetProperty(ref _historyEvidence, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public NisaTransactionRow? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) DeleteCommand.RaiseCanExecuteChanged(); }
    }
    public string AsOfLabel => $"基準日 {_asOf:yyyy/MM/dd} / 制度年度 {_analysis?.RuleYear} / 算式版 {_analysis?.RuleVersion}";
    public string RuleSource => _analysis?.RuleSource ?? NisaCapacityService.RuleSource;
    public string Valuation => _analysis?.TotalValuationJpy is decimal total
        ? $"新NISA保有評価額 {total:N0}円（時価）"
        : $"把握済み評価額 {_analysis?.KnownValuationJpy ?? 0m:N0}円 / 評価額不明 {_analysis?.UnknownValuationCount ?? 0}件";
    public string EvidenceCoverage
    {
        get
        {
            var coverage = PositionEvidenceCoverage.Build(_snapshots.Where(x => x.Position.Stock.AccountType is
                AccountTypes.NisaGrowth or AccountTypes.NisaAccumulation), _asOf);
            var estimatedValue = coverage.EstimatedCostMarketValueJpy is decimal estimated
                ? $"{estimated:N0}円" : "算出不可";
            var excludedValue = coverage.ExcludedMarketValueJpy is decimal excluded
                ? $"{excluded:N0}円" : "算出不可";
            return $"保有銘柄の取得額: 確定 {coverage.ConfirmedCostCount}件 / 概算 {coverage.EstimatedCostCount}件（評価額 {estimatedValue}） / " +
                   $"未確認 {coverage.UnconfirmedCostCount}件（損益集計対象外の評価額 {excludedValue}）。" +
                   $"価格の鮮度: 更新遅れ {coverage.DelayedPriceCount}件 / 時刻不明 {coverage.UnknownPriceTimeCount}件。" +
                   "投資枠の使用量は下の買付・売却履歴の取得簿価から計算します。";
        }
    }
    public string AnnualUse => _analysis?.AnnualGrowthUsedJpy is decimal growth &&
        _analysis.AnnualAccumulationUsedJpy is decimal accumulation
        ? $"今年の買付簿価: 成長 {growth:N0}円 / つみたて {accumulation:N0}円"
        : "今年の枠使用量: 履歴不足のため不明";
    public string Remaining => _analysis?.LifetimeRemainingJpy is decimal lifetime
        ? $"参考残枠: 生涯 {lifetime:N0}円 / 成長枠内数 {_analysis.GrowthLifetimeRemainingJpy:N0}円。" +
          $"今年の未使用枠: 成長 {_analysis.AnnualGrowthRemainingJpy:N0}円 / つみたて {_analysis.AnnualAccumulationRemainingJpy:N0}円"
        : "残枠: 履歴不足または規則未登録のため不明";
    public string Status => _analysis?.Status ?? string.Empty;

    public void Load(IReadOnlyList<StockSnapshot> snapshots, DateTime? asOf = null)
    {
        _snapshots = snapshots;
        _asOf = (asOf ?? DateTime.Today).Date;
        try
        {
            var repo = _repository();
            var transactions = repo.GetTransactions();
            var history = repo.GetHistoryState();
            _analysis = _service.Build(_snapshots, transactions, history, _asOf);
            Transactions.Clear();
            foreach (var item in transactions.OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id))
                Transactions.Add(new NisaTransactionRow(item.Id, item.EffectiveDate.ToString("yyyy/MM/dd"),
                    AccountTypes.DisplayName(item.Frame), item.Kind == NisaTransactionKinds.Buy ? "買付" : "売却",
                    $"{item.BookCostJpy:N0}円", item.Source, item.Evidence));
            Refresh();
        }
        catch (Exception ex) { Message = $"NISA履歴を読み込めません: {ex.Message}"; }
    }

    private void Save()
    {
        if (!decimal.TryParse(BookCost, NumberStyles.Number, CultureInfo.CurrentCulture, out var cost) &&
            !decimal.TryParse(BookCost, NumberStyles.Number, CultureInfo.InvariantCulture, out cost))
        { Message = "取得簿価を円で入力してください。"; return; }
        try
        {
            _repository().Save(new NisaTransaction(0, EntryDate, Frame.Key, Kind.Key, cost,
                Source, Evidence, NisaCapacityService.RuleVersion));
            Load(_snapshots, _asOf);
            Message = "NISA履歴を保存しました。履歴全体の確認状態は解除しました。";
        }
        catch (Exception ex) { Message = $"保存できません: {ex.Message}"; }
    }

    private void Delete()
    {
        if (Selected is null) return;
        try
        {
            _repository().Delete(Selected.Id);
            Load(_snapshots, _asOf);
            Message = "選択した履歴を削除しました。履歴全体の確認状態は解除しました。";
        }
        catch (Exception ex) { Message = $"削除できません: {ex.Message}"; }
    }

    private void ConfirmHistory()
    {
        try
        {
            if (_snapshots.Any(x => x.Position.Stock.AccountType is
                    AccountTypes.NisaGrowth or AccountTypes.NisaAccumulation) &&
                !_repository().GetTransactions().Any(x => x.Kind == NisaTransactionKinds.Buy))
            {
                Message = "新NISAの保有があるため、買付履歴を入力してから全履歴を確認してください。";
                return;
            }
            _repository().ConfirmCompleteThrough(_asOf, HistoryEvidence);
            Load(_snapshots, _asOf);
            Message = "基準日までの全証券会社の新NISA履歴を確認済みとして記録しました。";
        }
        catch (Exception ex) { Message = $"確認状態を保存できません: {ex.Message}"; }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(AsOfLabel)); OnPropertyChanged(nameof(RuleSource));
        OnPropertyChanged(nameof(Valuation)); OnPropertyChanged(nameof(EvidenceCoverage)); OnPropertyChanged(nameof(AnnualUse));
        OnPropertyChanged(nameof(Remaining)); OnPropertyChanged(nameof(Status));
    }
}
