using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed record CashKindOption(CashOperationKind Kind, string Label)
{
    public override string ToString() => Label;
}
public sealed record CashOperationRow(CashOperation Operation)
{
    public string Date => Operation.Date.ToString("yyyy/MM/dd");
    public string Kind => CashLedgerService.KindLabel(Operation.Kind);
    public string Details => string.Join(" → ", Operation.Legs.Select(x => $"{x.Account}: {x.Amount:+#,0.############################;-#,0.############################;0} {x.Currency}"));
    public string Evidence => Operation.Evidence;
    public string Status => Operation.CancelledAtUtc is null ? "記録済み" : "取消済み: " + Operation.CancellationReason;
}
public sealed record CashPeriodRow(CashReconciliation Period, CashReconciliationResult Result)
{
    public string Scope => $"{Period.Account} / {Period.Currency}";
    public string Dates => $"{Period.StartDate:yyyy/MM/dd} ～ {Period.EndDate:yyyy/MM/dd}";
    public string Expected => Result.ExpectedClosingBalance?.ToString("#,0.############################") ?? "算出不可";
    public string Difference => Result.Difference?.ToString("#,0.############################") ?? "未確認";
    public string Status => Result.Status;
    public string ReviewDetails => $"照合保存日時: {Period.ReviewedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}\n根拠: {Period.Evidence}\n{Result.Status}";
}

public sealed class CashLedgerViewModel : ObservableObject
{
    private readonly Func<CashLedgerRepository> _repository;
    private readonly Action _afterChanged;
    private IReadOnlyList<CashOperation> _operations = Array.Empty<CashOperation>();
    private CashKindOption _selectedKind;
    private CashOperationRow? _selectedOperation;
    private CashPeriodRow? _selectedPeriod;
    private string _message = string.Empty;

    public CashLedgerViewModel(Func<CashLedgerRepository> repository, Action? afterChanged = null)
    {
        _repository = repository;
        _afterChanged = afterChanged ?? (() => { });
        Kinds = Enum.GetValues<CashOperationKind>().Select(x => new CashKindOption(x, CashLedgerService.KindLabel(x))).ToArray();
        _selectedKind = Kinds[0];
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel, () => SelectedOperation?.Operation.CancelledAtUtc is null && SelectedOperation is not null);
        ReloadCommand = new RelayCommand(() => Load());
        ReconcileCommand = new RelayCommand(SaveReconciliation);
    }

    public IReadOnlyList<CashKindOption> Kinds { get; }
    public ObservableCollection<CashOperationRow> Operations { get; } = new();
    public ObservableCollection<CashPeriodRow> Periods { get; } = new();
    public ICommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ReconcileCommand { get; }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public CashKindOption SelectedKind
    {
        get => _selectedKind;
        set { if (SetProperty(ref _selectedKind, value)) { OnPropertyChanged(nameof(IsPaired)); OnPropertyChanged(nameof(IsExchange)); } }
    }
    public bool IsPaired => SelectedKind.Kind is CashOperationKind.Transfer or CashOperationKind.Exchange;
    public bool IsExchange => SelectedKind.Kind == CashOperationKind.Exchange;
    public DateTime? EntryDate { get; set; } = DateTime.Today;
    public string EntryAccount { get; set; } = string.Empty;
    public string EntryCurrency { get; set; } = "JPY";
    public string EntryAmount { get; set; } = string.Empty;
    public string DestinationAccount { get; set; } = string.Empty;
    public string DestinationCurrency { get; set; } = "USD";
    public string DestinationAmount { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string CancellationReason { get; set; } = string.Empty;
    public CashOperationRow? SelectedOperation
    {
        get => _selectedOperation;
        set { if (SetProperty(ref _selectedOperation, value)) CancelCommand.RaiseCanExecuteChanged(); }
    }
    public string ReviewAccount { get; set; } = string.Empty;
    public string ReviewCurrency { get; set; } = "JPY";
    public DateTime? StartDate { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime? EndDate { get; set; } = DateTime.Today;
    public string OpeningBalance { get; set; } = string.Empty;
    public string ClosingBalance { get; set; } = string.Empty;
    public bool AllEntriesConfirmed { get; set; }
    public bool ExternalFlowsConfirmed { get; set; }
    public string ReviewEvidence { get; set; } = string.Empty;
    public CashPeriodRow? SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (!SetProperty(ref _selectedPeriod, value) || value is null) return;
            var p = value.Period;
            ReviewAccount = p.Account; ReviewCurrency = p.Currency;
            StartDate = p.StartDate; EndDate = p.EndDate;
            OpeningBalance = p.OpeningBalance?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            ClosingBalance = p.ClosingBalance?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            // Reconfirmation is intentional: reloading a stale period must not silently approve new entries.
            AllEntriesConfirmed = false; ExternalFlowsConfirmed = false;
            ReviewEvidence = p.Evidence;
            RefreshAllProperties();
        }
    }

    public void Reset()
    {
        _operations = Array.Empty<CashOperation>(); Operations.Clear(); Periods.Clear();
        SelectedOperation = null; SelectedPeriod = null; SelectedKind = Kinds[0];
        EntryDate = DateTime.Today; EntryAccount = string.Empty; EntryCurrency = "JPY"; EntryAmount = string.Empty;
        DestinationAccount = string.Empty; DestinationCurrency = "USD"; DestinationAmount = string.Empty;
        Evidence = string.Empty; CancellationReason = string.Empty;
        ReviewAccount = string.Empty; ReviewCurrency = "JPY"; OpeningBalance = string.Empty; ClosingBalance = string.Empty;
        StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); EndDate = DateTime.Today;
        AllEntriesConfirmed = false; ExternalFlowsConfirmed = false; ReviewEvidence = string.Empty; Message = string.Empty;
        RefreshAllProperties();
    }

    public bool Load()
    {
        try
        {
            var repository = _repository();
            var state = repository.GetState();
            var operations = state.Operations;
            var periods = state.Reconciliations;
            _operations = operations;
            SelectedOperation = null; SelectedPeriod = null;
            Operations.Clear();
            foreach (var operation in operations.Reverse()) Operations.Add(new(operation));
            Periods.Clear();
            foreach (var period in periods) Periods.Add(new(period, CashLedgerService.Reconcile(operations, period)));
            AllEntriesConfirmed = false; ExternalFlowsConfirmed = false;
            RefreshAllProperties();
            return true;
        }
        catch (Exception ex) { Message = "現金台帳の読込に失敗しました: " + ex.Message; return false; }
    }

    private static decimal? ParseAmount(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
            throw new ArgumentException("金額は数値で入力してください。残高不明は空欄にし、0とは区別してください。");
        return amount;
    }

    private void Save()
    {
        try
        {
            var amount = ParseAmount(EntryAmount) ?? 0m;
            if (amount <= 0m) throw new ArgumentException("取引金額は正の数で入力してください。増減方向は取引種類から決まります。");
            var kind = SelectedKind.Kind;
            var incoming = kind is CashOperationKind.Deposit or CashOperationKind.SellSettlement or CashOperationKind.Dividend;
            var legs = new List<CashLeg> { new(EntryAccount.Trim(), EntryCurrency.Trim().ToUpperInvariant(), incoming ? amount : -amount) };
            if (IsPaired)
                legs.Add(new(DestinationAccount.Trim(), IsExchange ? DestinationCurrency.Trim().ToUpperInvariant() : legs[0].Currency,
                    IsExchange ? ParseAmount(DestinationAmount) ?? 0m : amount));
            _repository().Add(new CashOperation { Date = EntryDate ?? DateTime.MinValue, Kind = kind, Evidence = Evidence, Legs = legs });
        }
        catch (Exception ex) { Message = "保存できませんでした: " + ex.Message; return; }
        EntryAmount = string.Empty; DestinationAmount = string.Empty; Evidence = string.Empty;
        if (Load()) Message = "記録しました。対象期間を再照合するとダッシュボードの現金集計に反映されます。";
        else Message = "保存は完了しました。" + Message;
        NotifyChanged();
        RefreshAllProperties();
    }

    private void Cancel()
    {
        if (SelectedOperation is null) return;
        try { _repository().Cancel(SelectedOperation.Operation.Id, CancellationReason); }
        catch (Exception ex) { Message = "取消できませんでした: " + ex.Message; return; }
        CancellationReason = string.Empty;
        if (Load()) Message = "取引を取り消しました。両替・振替は両側を取り消し、履歴を残しています。";
        else Message = "取消は完了しました。" + Message;
        NotifyChanged();
        RefreshAllProperties();
    }

    private void SaveReconciliation()
    {
        try
        {
            var scope = new CashReconciliation
            {
                Account = ReviewAccount.Trim(), Currency = ReviewCurrency.Trim().ToUpperInvariant(),
                StartDate = StartDate ?? DateTime.MinValue, EndDate = EndDate ?? DateTime.MinValue
            };
            var period = new CashReconciliation
            {
                Account = scope.Account, Currency = scope.Currency, StartDate = scope.StartDate, EndDate = scope.EndDate,
                OpeningBalance = ParseAmount(OpeningBalance), ClosingBalance = ParseAmount(ClosingBalance),
                AllEntriesConfirmed = AllEntriesConfirmed, ExternalFlowsConfirmed = ExternalFlowsConfirmed,
                Evidence = ReviewEvidence.Trim(), LedgerFingerprint = CashLedgerService.Fingerprint(_operations, scope)
            };
            _repository().SaveReconciliation(period);
        }
        catch (Exception ex) { Message = "照合を保存できませんでした: " + ex.Message; return; }
        if (Load()) Message = "照合を保存しました。残高差異・不足条件は下の照合結果で確認できます。";
        else Message = "照合の保存は完了しました。" + Message;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        try { _afterChanged(); }
        catch (Exception ex) { Message += " 集計の再表示に失敗しました: " + ex.Message; }
    }
}
