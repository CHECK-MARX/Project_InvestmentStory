using System.Collections.ObjectModel;
using System.Globalization;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed class PerformanceFxRow : ObservableObject
{
    private DateTime? _date = DateTime.Today, _quoteDate = DateTime.Today;
    private string _currency = "USD", _rate = "", _evidence = "";
    public DateTime? Date { get => _date; set => SetProperty(ref _date, value); }
    public DateTime? QuoteDate { get => _quoteDate; set => SetProperty(ref _quoteDate, value); }
    public string Currency { get => _currency; set => SetProperty(ref _currency, value); }
    public string Rate { get => _rate; set => SetProperty(ref _rate, value); }
    public string Evidence { get => _evidence; set => SetProperty(ref _evidence, value); }
}

public sealed record PerformanceHistoryRow(PerformanceReport Report, bool MatchesCurrentLedger)
{
    public string Period => $"{Report.Input.StartDate:yyyy/MM/dd} ～ {Report.Input.EndDate:yyyy/MM/dd}";
    public string SavedAt => Report.CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    public string Xirr => PerformanceViewModel.Rate(Report.Result.MoneyWeighted.Value);
    public string Profit => PerformanceViewModel.Money(Report.Result.ProfitJpy);
    public string Status => MatchesCurrentLedger ? "保存時の台帳と一致" : "台帳変更あり・過去の記録";
    public string Details => $"保存時点の値（現在値ではありません） / 算式 {Report.Result.FormulaVersion}\n" +
        $"期首 {PerformanceViewModel.Money(Report.Result.OpeningTotalJpy)} / 期末 {PerformanceViewModel.Money(Report.Result.ClosingTotalJpy)} / 外部入出金 {PerformanceViewModel.Money(Report.Result.ExternalNetFlowJpy)}\n" +
        $"期首根拠: {Report.Input.OpeningEvidence}\n期末根拠: {Report.Input.ClosingEvidence}\n" +
        $"TWR: {PerformanceViewModel.Rate(Report.Result.TimeWeighted.Value)} / {Report.Result.TimeWeighted.Status}\nXIRR: {Xirr} / {Report.Result.MoneyWeighted.Status}\n" +
        $"修正ディーツ法: {PerformanceViewModel.Rate(Report.Result.ModifiedDietz.Value)} / {Report.Result.ModifiedDietz.Status}\n" +
        $"比較指数: {Report.Input.Benchmark?.Name ?? "未指定"} / 差 {PerformanceViewModel.PercentagePoints(Report.Result.Benchmark.Difference)} / {Report.Result.Benchmark.Status}\n" +
        string.Join("\n", Report.Input.Fx.Select(x => $"{x.Date:yyyy/MM/dd} {x.Currency}: {x.JpyPerUnit}円 / 公表 {x.QuoteDate:yyyy/MM/dd} / {x.Evidence}")) + "\n" +
        string.Join("\n", Report.Input.FlowValuations.Select(x => $"入出金時点: 直前 {x.BeforeJpy}円 → 直後 {x.AfterJpy}円 / 順序{x.Sequence} / {x.Evidence}"));
}

public sealed class PerformanceFlowValuationRow : ObservableObject
{
    public string OperationId { get; init; } = "";
    public DateTime Date { get; init; }
    public string Details { get; init; } = "";
    private string _sequence = "", _before = "", _after = "", _evidence = "";
    private bool _confirmed;
    public string Sequence { get => _sequence; set => SetProperty(ref _sequence, value); }
    public string Before { get => _before; set => SetProperty(ref _before, value); }
    public string After { get => _after; set => SetProperty(ref _after, value); }
    public string Evidence { get => _evidence; set => SetProperty(ref _evidence, value); }
    public bool Confirmed { get => _confirmed; set => SetProperty(ref _confirmed, value); }
}

public sealed class PerformanceViewModel : ObservableObject
{
    private readonly Func<CashLedgerRepository> _repository;
    private PerformancePeriodInput? _calculatedInput;
    private string _fingerprint = "";
    private int _revision;
    private PerformancePeriodResult _result = new();
    private DateTime? _startDate = new DateTime(DateTime.Today.Year, 1, 1).AddDays(-1), _endDate = DateTime.Today.AddDays(-1);
    private string _opening = "", _closing = "", _openingEvidence = "", _closingEvidence = "", _message = "期間と評価根拠を入力し、現金台帳の残高照合を確認してください。";
    private string _benchmarkName = "", _benchmarkCurrency = "JPY", _benchmarkOpening = "", _benchmarkClosing = "", _benchmarkEvidence = "";
    private bool _benchmarkIncludesNetDividends, _benchmarkSourceConfirmed, _benchmarkIsHistoricalEstimate;
    private bool _securitiesConfirmed, _accountsConfirmed, _noInKindConfirmed, _isBusy;
    private PerformanceHistoryRow? _selectedHistory;
    private PerformanceFxRow? _selectedFx;

    public PerformanceViewModel(Func<CashLedgerRepository> repository)
    {
        _repository = repository;
        CalculateCommand = new RelayCommand(() => _ = CalculateAsync(), () => IsIdle);
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => IsIdle && _calculatedInput is not null && _result.Problems.Count == 0);
        AddFxCommand = new RelayCommand(() => AddFx(new() { Date = StartDate, QuoteDate = StartDate }), () => IsIdle);
        RemoveFxCommand = new RelayCommand(() => { if (SelectedFx is not null) FxRows.Remove(SelectedFx); }, () => IsIdle);
        PrepareFxCommand = new RelayCommand(PrepareFx, () => IsIdle);
        PrepareFlowsCommand = new RelayCommand(PrepareFlows, () => IsIdle);
        UseHistoryCommand = new RelayCommand(UseHistory, () => IsIdle && SelectedHistory is not null);
        FxRows.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (PerformanceFxRow row in e.NewItems) row.PropertyChanged += FxChanged;
            if (e.OldItems is not null) foreach (PerformanceFxRow row in e.OldItems) row.PropertyChanged -= FxChanged;
            RequireReconfirmation();
        };
        FlowValuations.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (PerformanceFlowValuationRow row in e.NewItems) row.PropertyChanged += FlowChanged;
            if (e.OldItems is not null) foreach (PerformanceFlowValuationRow row in e.OldItems) row.PropertyChanged -= FlowChanged;
            RequireReconfirmation();
        };
    }
    public RelayCommand CalculateCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand AddFxCommand { get; }
    public RelayCommand RemoveFxCommand { get; }
    public RelayCommand PrepareFxCommand { get; }
    public RelayCommand PrepareFlowsCommand { get; }
    public RelayCommand UseHistoryCommand { get; }
    public ObservableCollection<PerformanceFxRow> FxRows { get; } = new();
    public ObservableCollection<PerformanceFlowValuationRow> FlowValuations { get; } = new();
    public ObservableCollection<PerformanceHistoryRow> History { get; } = new();
    public DateTime? StartDate { get => _startDate; set { if (SetProperty(ref _startDate, value)) RequireReconfirmation(); } }
    public DateTime? EndDate { get => _endDate; set { if (SetProperty(ref _endDate, value)) RequireReconfirmation(); } }
    public string Opening { get => _opening; set { if (SetProperty(ref _opening, value)) RequireReconfirmation(); } }
    public string Closing { get => _closing; set { if (SetProperty(ref _closing, value)) RequireReconfirmation(); } }
    public string OpeningEvidence { get => _openingEvidence; set { if (SetProperty(ref _openingEvidence, value)) RequireReconfirmation(); } }
    public string ClosingEvidence { get => _closingEvidence; set { if (SetProperty(ref _closingEvidence, value)) RequireReconfirmation(); } }
    public string BenchmarkName { get => _benchmarkName; set { if (SetProperty(ref _benchmarkName, value)) Invalidate(); } }
    public string BenchmarkCurrency { get => _benchmarkCurrency; set { if (SetProperty(ref _benchmarkCurrency, value)) Invalidate(); } }
    public string BenchmarkOpening { get => _benchmarkOpening; set { if (SetProperty(ref _benchmarkOpening, value)) Invalidate(); } }
    public string BenchmarkClosing { get => _benchmarkClosing; set { if (SetProperty(ref _benchmarkClosing, value)) Invalidate(); } }
    public string BenchmarkEvidence { get => _benchmarkEvidence; set { if (SetProperty(ref _benchmarkEvidence, value)) Invalidate(); } }
    public bool BenchmarkIncludesNetDividends { get => _benchmarkIncludesNetDividends; set { if (SetProperty(ref _benchmarkIncludesNetDividends, value)) Invalidate(); } }
    public bool BenchmarkSourceConfirmed { get => _benchmarkSourceConfirmed; set { if (SetProperty(ref _benchmarkSourceConfirmed, value)) Invalidate(); } }
    public bool BenchmarkIsHistoricalEstimate { get => _benchmarkIsHistoricalEstimate; set { if (SetProperty(ref _benchmarkIsHistoricalEstimate, value)) Invalidate(); } }
    public bool SecuritiesConfirmed { get => _securitiesConfirmed; set { if (SetProperty(ref _securitiesConfirmed, value)) Invalidate(); } }
    public bool AccountsConfirmed { get => _accountsConfirmed; set { if (SetProperty(ref _accountsConfirmed, value)) Invalidate(); } }
    public bool NoInKindConfirmed { get => _noInKindConfirmed; set { if (SetProperty(ref _noInKindConfirmed, value)) Invalidate(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public PerformancePeriodResult Result => _result;
    public string OpeningTotal => Money(Result.OpeningTotalJpy);
    public string ClosingTotal => Money(Result.ClosingTotalJpy);
    public string ExternalTotal => Money(Result.ExternalNetFlowJpy);
    public string Profit => Money(Result.ProfitJpy);
    public string Twr => Rate(Result.TimeWeighted.Value);
    public string Xirr => Rate(Result.MoneyWeighted.Value);
    public string Dietz => Rate(Result.ModifiedDietz.Value);
    public string BenchmarkPeriodReturn => Rate(Result.Benchmark.BenchmarkPeriodReturn);
    public string BenchmarkDifference => PercentagePoints(Result.Benchmark.Difference);
    public string RequiredCashPeriod => StartDate is DateTime start && EndDate is DateTime end && start < end
        ? $"現金台帳では、各口座・通貨について {start.AddDays(1):yyyy/MM/dd} ～ {end:yyyy/MM/dd} を照合してください。期首は開始日の前日末、期末は終了日末です。"
        : "開始日末より後の終了日末を指定してください。";
    public bool IsIdle => !IsBusy;
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(IsIdle)); RefreshCommands(); } }
    public PerformanceFxRow? SelectedFx { get => _selectedFx; set => SetProperty(ref _selectedFx, value); }
    public PerformanceHistoryRow? SelectedHistory { get => _selectedHistory; set { SetProperty(ref _selectedHistory, value); UseHistoryCommand.RaiseCanExecuteChanged(); } }
    public static string Rate(double? value) => value is double rate ? rate.ToString("P2", CultureInfo.CurrentCulture) : "算出不可";
    public static string PercentagePoints(double? value) => value is double difference
        ? (difference * 100d).ToString("+#,0.00;-#,0.00;0.00", CultureInfo.CurrentCulture) + " pt" : "算出不可";
    public static string Money(decimal? value) => value is decimal amount ? amount.ToString("#,0.##", CultureInfo.CurrentCulture) + "円" : "未確認";
    private void RefreshCommands()
    {
        CalculateCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); AddFxCommand.RaiseCanExecuteChanged();
        RemoveFxCommand.RaiseCanExecuteChanged(); PrepareFxCommand.RaiseCanExecuteChanged(); UseHistoryCommand.RaiseCanExecuteChanged();
        PrepareFlowsCommand.RaiseCanExecuteChanged();
    }
    public void Invalidate()
    {
        _revision++;
        _calculatedInput = null; _fingerprint = ""; _result = new();
        Message = "条件を確認して再計算してください。以前の計算結果は表示を解除しました。";
        RefreshAllProperties(); SaveCommand.RaiseCanExecuteChanged();
    }
    public void RequireReconfirmation()
    {
        _securitiesConfirmed = _accountsConfirmed = _noInKindConfirmed = false;
        foreach (var row in FlowValuations) row.Confirmed = false;
        Invalidate();
    }
    private void FxChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RequireReconfirmation();
    private void FlowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is PerformanceFlowValuationRow row && e.PropertyName != nameof(PerformanceFlowValuationRow.Confirmed)) row.Confirmed = false;
        Invalidate();
    }
    private void AddFx(PerformanceFxRow row) => FxRows.Add(row);
    public void Load()
    {
        RequireReconfirmation();
        try
        {
            var repository = _repository(); var ledger = repository.GetState();
            History.Clear(); SelectedHistory = null;
            foreach (var report in repository.GetPerformanceReports())
                History.Add(new(report, report.LedgerFingerprint == PerformancePeriodService.Fingerprint(ledger, report.Input.EndDate)));
        }
        catch (Exception ex) { Message = "保存記録を読み込めませんでした: " + ex.Message; }
    }
    public void Reset()
    {
        if (IsBusy) throw new InvalidOperationException("運用成績の処理中です。");
        foreach (var row in FxRows) row.PropertyChanged -= FxChanged;
        FxRows.Clear(); History.Clear(); SelectedHistory = null; SelectedFx = null;
        ClearFlowValuations();
        _opening = _closing = _openingEvidence = _closingEvidence = "";
        _benchmarkName = _benchmarkOpening = _benchmarkClosing = _benchmarkEvidence = "";
        _benchmarkCurrency = "JPY";
        _benchmarkIncludesNetDividends = _benchmarkSourceConfirmed = _benchmarkIsHistoricalEstimate = false;
        _securitiesConfirmed = _accountsConfirmed = _noInKindConfirmed = false;
        _startDate = new DateTime(DateTime.Today.Year, 1, 1).AddDays(-1); _endDate = DateTime.Today.AddDays(-1);
        Invalidate(); Message = "期間と評価根拠を入力してください。";
    }
    private static decimal? ParseAmount(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) return amount;
        throw new ArgumentException("評価額・為替は数値で入力してください。不明は空欄にし、0と区別してください。");
    }
    private PerformancePeriodInput Input() => new()
    {
        StartDate = StartDate ?? DateTime.MinValue, EndDate = EndDate ?? DateTime.MinValue,
        OpeningSecuritiesJpy = ParseAmount(Opening), ClosingSecuritiesJpy = ParseAmount(Closing),
        OpeningEvidence = OpeningEvidence.Trim(), ClosingEvidence = ClosingEvidence.Trim(),
        SecuritiesConfirmed = SecuritiesConfirmed, AllAccountsConfirmed = AccountsConfirmed, NoInKindTransfersConfirmed = NoInKindConfirmed,
        Benchmark = string.IsNullOrWhiteSpace(BenchmarkName) ? null : new PerformanceBenchmark(BenchmarkName.Trim(),
            StartDate ?? DateTime.MinValue, EndDate ?? DateTime.MinValue, BenchmarkCurrency.Trim().ToUpperInvariant(),
            ParseAmount(BenchmarkOpening), ParseAmount(BenchmarkClosing), BenchmarkIncludesNetDividends,
            BenchmarkSourceConfirmed, BenchmarkIsHistoricalEstimate, BenchmarkEvidence.Trim()),
        Fx = FxRows.Select(x => new PerformanceFx(x.Date ?? DateTime.MinValue, x.Currency.Trim().ToUpperInvariant(),
            ParseAmount(x.Rate) ?? 0m, x.QuoteDate ?? DateTime.MinValue, x.Evidence.Trim())).ToArray(),
        FlowValuations = FlowValuations.Select(x => new PerformanceFlowValuation(x.OperationId,
            string.IsNullOrWhiteSpace(x.Sequence) ? 0 : int.TryParse(x.Sequence, out var sequence) ? sequence : throw new ArgumentException("入出金の順序は正の整数で入力してください。"),
            ParseAmount(x.Before), ParseAmount(x.After), x.Confirmed, x.Evidence.Trim())).ToArray()
    };
    public async Task CalculateAsync()
    {
        if (IsBusy) return;
        Invalidate(); IsBusy = true; Message = "残高照合と計算条件を確認しています…";
        var revision = _revision;
        try
        {
            var input = Input(); var repository = _repository();
            var calculated = await Task.Run(() =>
            {
                var state = repository.GetState();
                return (Result: new PerformancePeriodService().Calculate(input, state), Fingerprint: PerformancePeriodService.Fingerprint(state, input.EndDate));
            });
            if (revision != _revision) { Message = "計算中に条件が変わりました。再確認して計算してください。"; return; }
            _result = calculated.Result; _fingerprint = calculated.Fingerprint; _calculatedInput = input;
            Message = _result.Problems.Count == 0 ? "計算しました。率ごとの条件・算出不可の理由を確認してください。まだ保存していません。" : "不足条件があります。下の一覧を確認してください。";
            RefreshAllProperties();
        }
        catch (Exception ex) { Message = "計算できませんでした: " + ex.Message; }
        finally { IsBusy = false; }
    }
    public async Task SaveAsync()
    {
        if (IsBusy || _calculatedInput is null || _result.Problems.Count > 0) return;
        var input = _calculatedInput; var fingerprint = _fingerprint; IsBusy = true;
        try
        {
            var repository = _repository();
            var report = await Task.Run(() => repository.SavePerformanceReport(input, fingerprint));
            History.Insert(0, new(report, true)); SelectedHistory = History[0]; _calculatedInput = null;
            Message = "計算条件・照合根拠・保存時点の値を記録しました。後日の台帳変更で保存値を書き換えることはありません。";
        }
        catch (Exception ex) { Invalidate(); Message = "保存できませんでした: " + ex.Message; }
        finally { IsBusy = false; }
    }
    private void PrepareFx()
    {
        try
        {
            if (StartDate is not DateTime start || EndDate is not DateTime end || start >= end) throw new ArgumentException("期間を指定してください。");
            var ledger = PerformancePeriodService.RelevantLedger(_repository().GetState(), end);
            var currencies = ledger.Operations.SelectMany(x => x.Legs).Select(x => x.Currency)
                .Concat(ledger.Reconciliations.Select(x => x.Currency))
                .Concat(string.IsNullOrWhiteSpace(BenchmarkName) ? [] : [BenchmarkCurrency.Trim().ToUpperInvariant()])
                .Where(x => x != "JPY" && CashLedgerService.IsCurrency(x)).Distinct();
            var required = currencies.SelectMany(currency => new[] { (Date: start.Date, Currency: currency), (Date: end.Date, Currency: currency) })
                .Concat(ledger.Operations.Where(x => x.CancelledAtUtc is null && x.Date.Date > start.Date &&
                    x.Kind is CashOperationKind.Deposit or CashOperationKind.Withdrawal)
                    .SelectMany(op => op.Legs.Where(x => x.Currency != "JPY").Select(x => (op.Date.Date, x.Currency)))).Distinct();
            foreach (var (date, currency) in required.OrderBy(x => x.Date).ThenBy(x => x.Currency))
                if (!FxRows.Any(x => x.Date?.Date == date && x.Currency == currency))
                    AddFx(new() { Date = date, QuoteDate = date, Currency = currency });
            Message = "必要な日付の為替欄を作成しました。実際の公表日・レート・根拠を入力してください。残高0の通貨は換算不要です。";
        }
        catch (Exception ex) { Message = "為替欄を作成できませんでした: " + ex.Message; }
    }
    private void UseHistory()
    {
        if (SelectedHistory is null) return;
        var input = SelectedHistory.Report.Input;
        StartDate = input.StartDate; EndDate = input.EndDate;
        Opening = input.OpeningSecuritiesJpy?.ToString(CultureInfo.CurrentCulture) ?? "";
        Closing = input.ClosingSecuritiesJpy?.ToString(CultureInfo.CurrentCulture) ?? "";
        OpeningEvidence = input.OpeningEvidence; ClosingEvidence = input.ClosingEvidence;
        BenchmarkName = input.Benchmark?.Name ?? "";
        BenchmarkCurrency = input.Benchmark?.Currency ?? "JPY";
        BenchmarkOpening = input.Benchmark?.OpeningLevel?.ToString(CultureInfo.CurrentCulture) ?? "";
        BenchmarkClosing = input.Benchmark?.ClosingLevel?.ToString(CultureInfo.CurrentCulture) ?? "";
        BenchmarkEvidence = input.Benchmark?.Evidence ?? "";
        BenchmarkIncludesNetDividends = input.Benchmark?.IncludesNetDividends ?? false;
        BenchmarkSourceConfirmed = input.Benchmark?.SourceConfirmed ?? false;
        BenchmarkIsHistoricalEstimate = input.Benchmark?.IsHistoricalEstimate ?? false;
        foreach (var row in FxRows) row.PropertyChanged -= FxChanged;
        FxRows.Clear();
        foreach (var fx in input.Fx) AddFx(new() { Date = fx.Date, QuoteDate = fx.QuoteDate, Currency = fx.Currency,
            Rate = fx.JpyPerUnit.ToString(CultureInfo.CurrentCulture), Evidence = fx.Evidence });
        var flows = SelectedHistory.Report.Result.Flows;
        ClearFlowValuations();
        foreach (var value in input.FlowValuations)
        {
            var flow = flows.FirstOrDefault(x => x.OperationId == value.OperationId);
            FlowValuations.Add(new() { OperationId = value.OperationId, Date = flow?.Date ?? DateTime.MinValue,
                Details = flow is null ? "保存元明細を確認" : $"{flow.Account} / {flow.Amount} {flow.Currency}",
                Before = value.BeforeJpy?.ToString(CultureInfo.CurrentCulture) ?? "", After = value.AfterJpy?.ToString(CultureInfo.CurrentCulture) ?? "",
                Sequence = value.Sequence.ToString(), Evidence = value.Evidence, Confirmed = false });
        }
        SecuritiesConfirmed = AccountsConfirmed = NoInKindConfirmed = false;
        Invalidate(); Message = "保存記録の条件を読み込みました。現在の台帳と評価根拠を再確認してから計算してください。";
    }
    private void ClearFlowValuations()
    {
        foreach (var row in FlowValuations) row.PropertyChanged -= FlowChanged;
        FlowValuations.Clear();
    }
    private void PrepareFlows()
    {
        try
        {
            if (StartDate is not DateTime start || EndDate is not DateTime end || start >= end) throw new ArgumentException("期間を指定してください。");
            var operations = _repository().GetOperations().Where(x => x.CancelledAtUtc is null && x.Date.Date > start.Date && x.Date.Date <= end.Date &&
                x.Kind is CashOperationKind.Deposit or CashOperationKind.Withdrawal).OrderBy(x => x.Date).ThenBy(x => x.Id).ToArray();
            var existing = FlowValuations.ToDictionary(x => x.OperationId);
            ClearFlowValuations();
            foreach (var op in operations)
            {
                existing.TryGetValue(op.Id, out var old);
                FlowValuations.Add(new() { OperationId = op.Id, Date = op.Date.Date,
                    Details = string.Join(" / ", op.Legs.Select(x => $"{x.Account}: {x.Amount:+#,0.########;-#,0.########} {x.Currency}")),
                    Sequence = old?.Sequence ?? (operations.Count(x => x.Date.Date == op.Date.Date) == 1 ? "1" : ""),
                    Before = old?.Before ?? "", After = old?.After ?? "", Evidence = old?.Evidence ?? "", Confirmed = false });
            }
            Message = $"外部入出金 {operations.Length}件の評価欄を作成しました。直前・直後の全資産額と発生順、根拠を確認してください。未確認のままでもXIRR・概算の条件確認はできます。";
        }
        catch (Exception ex) { Message = "入出金時点の欄を作成できませんでした: " + ex.Message; }
    }
}
