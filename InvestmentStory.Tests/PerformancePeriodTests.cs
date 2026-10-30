using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class PerformancePeriodTests : IDisposable
{
    private static readonly DateTime Start = new(2025, 1, 1), End = new(2026, 1, 1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryPerformance_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly CashLedgerRepository _repository;
    private readonly PerformancePeriodService _service = new();
    public PerformancePeriodTests()
    {
        Directory.CreateDirectory(_directory); _path = Path.Combine(_directory, "test.db");
        new DatabaseInitializer().Initialize(_path); _repository = new(_path);
    }
    private static PerformancePeriodInput Input(decimal? opening = 1000m, decimal? closing = 1100m,
        IReadOnlyList<PerformanceFx>? fx = null, IReadOnlyList<PerformanceFlowValuation>? flowValues = null) => new()
    {
        StartDate = Start, EndDate = End, OpeningSecuritiesJpy = opening, ClosingSecuritiesJpy = closing,
        OpeningEvidence = "期首残高明細・全保有", ClosingEvidence = "期末残高明細・全保有",
        SecuritiesConfirmed = true, AllAccountsConfirmed = true, NoInKindTransfersConfirmed = true, Fx = fx ?? [], FlowValuations = flowValues ?? []
    };
    private static CashOperation Operation(CashOperationKind kind, decimal amount, DateTime? date = null, string currency = "JPY", string account = "A") => new()
    {
        Kind = kind, Date = date ?? Start.AddDays(180), Evidence = "明細", Legs = [new(account, currency, amount)]
    };
    private static CashReconciliation Review(IReadOnlyList<CashOperation> operations, decimal? opening = 0m, decimal? closing = 0m,
        string currency = "JPY", string account = "A")
    {
        var period = new CashReconciliation { Account = account, Currency = currency, StartDate = Start.AddDays(1), EndDate = End };
        return new()
        {
            Account = account, Currency = currency, StartDate = period.StartDate, EndDate = period.EndDate,
            OpeningBalance = opening, ClosingBalance = closing, AllEntriesConfirmed = true, ExternalFlowsConfirmed = true,
            Evidence = "証券会社の残高・全明細を確認", LedgerFingerprint = CashLedgerService.Fingerprint(operations, period)
        };
    }
    private static CashLedgerState Ledger(IReadOnlyList<CashOperation>? operations = null, decimal? opening = 0m, decimal? closing = 0m, string currency = "JPY")
    {
        operations ??= []; return new(operations, [Review(operations, opening, closing, currency)]);
    }
    private void Seed(CashLedgerState ledger)
    {
        foreach (var op in ledger.Operations) _repository.Add(op);
        foreach (var period in ledger.Reconciliations) _repository.SaveReconciliation(period);
    }
    private static void Near(double expected, double? actual, double tolerance = 1e-9)
    {
        Assert.NotNull(actual); Assert.InRange(actual.Value, expected - tolerance, expected + tolerance);
    }

    private static PerformancePeriodInput WithBenchmark(PerformanceBenchmark benchmark, IReadOnlyList<PerformanceFx>? fx = null) => new()
    {
        StartDate = Start, EndDate = End, OpeningSecuritiesJpy = 1000m, ClosingSecuritiesJpy = 1100m,
        OpeningEvidence = "期首残高明細", ClosingEvidence = "期末残高明細", SecuritiesConfirmed = true,
        AllAccountsConfirmed = true, NoInKindTransfersConfirmed = true, Benchmark = benchmark, Fx = fx ?? []
    };

    [Fact]
    public void NoFlowsProducesTenPercentWithoutAnnualizingPeriodTwr()
    {
        var result = _service.Calculate(Input(), Ledger());
        Assert.Empty(result.Problems); Assert.Equal(100m, result.ProfitJpy);
        Near(.1, result.TimeWeighted.Value); Near(.1, result.MoneyWeighted.Value); Near(.1, result.ModifiedDietz.Value);
    }

    [Fact]
    public void BenchmarkComparisonRequiresMatchingPeriodNetDividendBasisAndMeasuredEvidence()
    {
        var comparable = new PerformanceBenchmark("税引後配当込み指数", Start, End, "JPY", 100m, 105m,
            true, true, false, "両日の公表値と配当条件を確認");
        var result = _service.Calculate(WithBenchmark(comparable), Ledger());
        Near(.05, result.Benchmark.BenchmarkPeriodReturn);
        Near(.05, result.Benchmark.Difference);

        var mismatch = comparable with { EndDate = End.AddDays(-1) };
        Assert.Null(_service.Calculate(WithBenchmark(mismatch), Ledger()).Benchmark.Difference);
        Assert.Contains("期間", _service.Calculate(WithBenchmark(mismatch), Ledger()).Benchmark.Status);
        Assert.Null(_service.Calculate(WithBenchmark(comparable with { IncludesNetDividends = false }), Ledger()).Benchmark.Difference);
        Assert.Null(_service.Calculate(WithBenchmark(comparable with { IsHistoricalEstimate = true }), Ledger()).Benchmark.Difference);
        Assert.Contains("遡及推計", _service.Calculate(WithBenchmark(comparable with { IsHistoricalEstimate = true }), Ledger()).Benchmark.Status);
        Assert.Null(_service.Calculate(WithBenchmark(comparable with { SourceConfirmed = false }), Ledger()).Benchmark.Difference);
        Assert.Null(PerformancePeriodService.CompareBenchmark(WithBenchmark(comparable), new(null, "評価時点の根拠不足")).Difference);
    }

    [Fact]
    public void ForeignBenchmarkNeedsEndpointFxAndIsComparedInYen()
    {
        var benchmark = new PerformanceBenchmark("外貨の税引後配当込み指数", Start, End, "USD", 100m, 100m,
            true, true, false, "両日の指数公表値");
        var missing = _service.Calculate(WithBenchmark(benchmark), Ledger());
        Assert.Empty(missing.Problems);
        Assert.Null(missing.Benchmark.Difference);
        Assert.Contains("為替根拠", missing.Benchmark.Status);

        PerformanceFx[] fx = [new(Start, "USD", 100m, Start, "期首公表レート"),
            new(End, "USD", 110m, End, "期末公表レート")];
        var result = _service.Calculate(WithBenchmark(benchmark, fx), Ledger());
        Near(.1, result.Benchmark.BenchmarkPeriodReturn);
        Near(0, result.Benchmark.Difference);
    }
    [Theory]
    [InlineData(500, 0, 500)]
    [InlineData(-500, 500, 0)]
    public void DepositOrWithdrawalAloneDoesNotCreateProfit(decimal flow, decimal openingCash, decimal closingCash)
    {
        var kind = flow > 0 ? CashOperationKind.Deposit : CashOperationKind.Withdrawal;
        var result = _service.Calculate(Input(1000m, 1000m), Ledger([Operation(kind, flow)], openingCash, closingCash));
        Assert.Empty(result.Problems); Assert.Equal(0m, result.ProfitJpy);
        Near(0d, result.MoneyWeighted.Value); Near(0d, result.ModifiedDietz.Value);
        Assert.Null(result.TimeWeighted.Value); Assert.Contains("直前・直後", result.TimeWeighted.Status);
    }
    [Fact]
    public void DividendCountsOnceAndFeesReduceTheResult()
    {
        var result = _service.Calculate(Input(1000m, 1000m), Ledger([
            Operation(CashOperationKind.Dividend, 100m), Operation(CashOperationKind.Fee, -10m)], 0m, 90m));
        Assert.Empty(result.Problems); Assert.Equal(90m, result.ProfitJpy);
        Assert.Equal(0m, result.ExternalNetFlowJpy); Near(.09, result.TimeWeighted.Value);
    }
    [Fact]
    public void InternalTransferAndExchangeAreNotExternalFlows()
    {
        var operation = new CashOperation { Date = Start.AddDays(2), Kind = CashOperationKind.Transfer, Evidence = "振替明細",
            Legs = [new("A", "JPY", -100m), new("B", "JPY", 100m)] };
        var ledger = new CashLedgerState([operation], [Review([operation], 100m, 0m), Review([operation], 0m, 100m, account: "B")]);
        var result = _service.Calculate(Input(1000m, 1000m), ledger);
        Assert.Empty(result.Problems); Assert.Empty(result.Flows); Near(0d, result.TimeWeighted.Value);
        var exchange = new CashOperation { Date = Start.AddDays(2), Kind = CashOperationKind.Exchange, Evidence = "両替明細",
            Legs = [new("A", "JPY", -15000m), new("A", "USD", 100m)] };
        ledger = new([exchange], [Review([exchange], 15000m, 0m), Review([exchange], 0m, 100m, "USD")]);
        result = _service.Calculate(Input(0m, 0m, [new(End, "USD", 150m, End, "明細の換算レート")]), ledger);
        Assert.Empty(result.Problems); Assert.Empty(result.Flows); Near(0d, result.TimeWeighted.Value);
    }
    [Fact]
    public void MatchingNetZeroFlowsStillDoNotInventIntradayTwr()
    {
        var result = _service.Calculate(Input(), Ledger([Operation(CashOperationKind.Deposit, 100m), Operation(CashOperationKind.Withdrawal, -100m)]));
        Assert.Equal(0m, result.ExternalNetFlowJpy); Assert.Equal(2, result.Flows.Count);
        Assert.Null(result.TimeWeighted.Value); Near(.1, result.MoneyWeighted.Value);
    }
    [Fact]
    public void MissingOrStaleReconciliationBlocksAllReturnMeasures()
    {
        var unknown = _service.Calculate(Input(), Ledger(opening: null));
        Assert.Contains(unknown.Problems, x => x.Contains("期首残高未確認")); Assert.Null(unknown.MoneyWeighted.Value);
        var stale = Ledger(); stale = new([Operation(CashOperationKind.Deposit, 10m)], stale.Reconciliations);
        var result = _service.Calculate(Input(), stale);
        Assert.Contains(result.Problems, x => x.Contains("再読込")); Assert.Null(result.ModifiedDietz.Value);
        Assert.Null(_service.Calculate(Input(), new([], [])).TimeWeighted.Value);
    }
    [Fact]
    public void ExtraUnreviewedAccountAndIncompleteDeclarationsBlockCalculation()
    {
        var ledger = Ledger();
        ledger = new([Operation(CashOperationKind.Deposit, 1m, Start.AddDays(-1), account: "未登録の口座")], ledger.Reconciliations);
        Assert.Contains(_service.Calculate(Input(), ledger).Problems, x => x.Contains("未登録の口座"));
        var input = new PerformancePeriodInput { StartDate = Start, EndDate = End, OpeningSecuritiesJpy = 100m, ClosingSecuritiesJpy = 200m };
        var result = _service.Calculate(input, Ledger());
        Assert.Equal(3, result.Problems.Count); Assert.Null(result.ProfitJpy);
    }
    [Fact]
    public void ExternalFlowsUseExclusiveStartInclusiveEndAndExcludeCancellations()
    {
        var cancelled = new CashOperation { Date = Start.AddDays(10), Kind = CashOperationKind.Deposit, Evidence = "取消",
            Legs = [new("A", "JPY", 200m)], CancelledAtUtc = DateTime.UtcNow, CancellationReason = "誤登録" };
        var result = _service.Calculate(Input(1000m, 1000m), Ledger([
            Operation(CashOperationKind.Deposit, 50m, Start), Operation(CashOperationKind.Deposit, 100m, End), cancelled], 50m, 150m));
        Assert.Empty(result.Problems); Assert.Single(result.Flows); Assert.Equal(100m, result.ExternalNetFlowJpy);
        Near(0d, result.MoneyWeighted.Value); Near(0d, result.ModifiedDietz.Value);
    }
    [Fact]
    public void ForeignCashNeedsEndpointAndFlowDateRatesAndIncludesFxGain()
    {
        var date = Start.AddDays(180);
        var ledger = Ledger([Operation(CashOperationKind.Deposit, 10m, date, "USD")], 100m, 110m, "USD");
        PerformanceFx[] fx = [new(Start, "USD", 150m, Start, "期首レート"), new(End, "USD", 160m, End, "期末レート")];
        var result = _service.Calculate(Input(0m, 0m, fx), ledger);
        Assert.Contains(result.Problems, x => x.Contains(date.ToString("yyyy/MM/dd")));
        result = _service.Calculate(Input(0m, 0m, [..fx, new(date, "USD", 155m, date, "入金日レート")]), ledger);
        Assert.Empty(result.Problems); Assert.Equal(15000m, result.OpeningTotalJpy); Assert.Equal(17600m, result.ClosingTotalJpy);
        Assert.Equal(1550m, result.ExternalNetFlowJpy); Assert.Equal(1050m, result.ProfitJpy);
    }
    [Fact]
    public void DuplicateFutureAndTooOldFxEvidenceCannotBecomeAValidRate()
    {
        var ledger = Ledger(opening: 100m, closing: 100m, currency: "USD");
        PerformanceFx start = new(Start, "USD", 150m, Start, "出所");
        PerformanceFx end = new(End, "USD", 150m, End, "出所");
        foreach (var quotes in new IReadOnlyList<PerformanceFx>[] {
            [start, start, end], [start with { QuoteDate = Start.AddDays(1) }, end],
            [start with { QuoteDate = Start.AddDays(-8) }, end], [start with { Evidence = "" }, end],
            [start with { JpyPerUnit = 0m }, end] })
            Assert.Null(_service.Calculate(Input(0m, 0m, quotes), ledger).MoneyWeighted.Value);
        Assert.Empty(_service.Calculate(Input(0m, 0m, [start with { QuoteDate = Start.AddDays(-2) }, end]), ledger).Problems);
    }
    [Fact]
    public void ZeroOpeningCanHaveXirrButHasNoTwrAndZeroCapitalDoesNotBecomeZeroPercent()
    {
        var ledger = Ledger([Operation(CashOperationKind.Deposit, 100m)], 0m, 100m);
        var result = _service.Calculate(Input(0m, 0m), ledger);
        Assert.Empty(result.Problems); Assert.Null(result.TimeWeighted.Value); Near(0d, result.MoneyWeighted.Value);
        result = _service.Calculate(Input(0m, 0m), Ledger());
        Assert.Null(result.TimeWeighted.Value); Assert.Null(result.MoneyWeighted.Value); Assert.Null(result.ModifiedDietz.Value);
    }
    [Fact]
    public void ModifiedDietzWeightsFlowsByRemainingDays()
    {
        var ledger = Ledger([Operation(CashOperationKind.Deposit, 1000m, Start.AddDays(100))], 0m, 1000m);
        var result = _service.Calculate(Input(1000m, 1200m), ledger);
        var expected = 200d / (1000d + 1000d * 265d / 365d);
        Near(expected, result.ModifiedDietz.Value); Assert.Equal(200m, result.ProfitJpy);
    }
    [Fact]
    public void XirrMatchesMicrosoftPublishedIrregularDatesExample()
    {
        // https://support.microsoft.com/en-us/excel/functions/xirr-function
        var result = PerformancePeriodService.Xirr([(new(2008, 1, 1), -10000m), (new(2008, 3, 1), 2750m),
            (new(2008, 10, 30), 4250m), (new(2009, 2, 15), 3250m), (new(2009, 4, 1), 2750m)]);
        // Microsoft's iterative example is rounded; its stated precision is 0.000001 percentage points.
        Near(0.373362535d, result.Value, 1e-8);
        var rate = result.Value!.Value;
        var npv = -10000d + 2750d / Math.Pow(1d + rate, 60d / 365d) + 4250d / Math.Pow(1d + rate, 303d / 365d)
            + 3250d / Math.Pow(1d + rate, 411d / 365d) + 2750d / Math.Pow(1d + rate, 456d / 365d);
        Assert.InRange(Math.Abs(npv), 0d, 1e-7);
    }
    [Fact]
    public void TimeWeightedLinksVerifiedBeforeAndAfterFlowValuations()
    {
        var deposit = Operation(CashOperationKind.Deposit, 500m);
        var value = new PerformanceFlowValuation(deposit.Id, 1, 1100m, 1600m, true, "入金直前・直後の全資産評価、同一価格・為替");
        var result = _service.Calculate(Input(1000m, 1260m, flowValues: [value]), Ledger([deposit], 0m, 500m));
        Assert.Empty(result.Problems); Near(.21, result.TimeWeighted.Value);
        Assert.Equal(260m, result.ProfitJpy); Assert.NotEqual(result.TimeWeighted.Value, result.ModifiedDietz.Value);
        foreach (var invalid in new[] { value with { Confirmed = false }, value with { Evidence = "" },
            value with { AfterJpy = 1599m }, value with { Sequence = 0 }, value with { BeforeJpy = null } })
        {
            var incomplete = _service.Calculate(Input(1000m, 1260m, flowValues: [invalid]), Ledger([deposit], 0m, 500m));
            Assert.Null(incomplete.TimeWeighted.Value); Assert.NotNull(incomplete.MoneyWeighted.Value);
        }
    }
    [Fact]
    public void SameDayFlowOrderIsExplicitAndDuplicateOrderOrRemovedFlowIsRejected()
    {
        var deposit = Operation(CashOperationKind.Deposit, 500m); var withdrawal = Operation(CashOperationKind.Withdrawal, -300m);
        var first = new PerformanceFlowValuation(deposit.Id, 1, 1100m, 1600m, true, "同日10時の評価");
        var second = new PerformanceFlowValuation(withdrawal.Id, 2, 1760m, 1460m, true, "同日15時の評価");
        var ledger = Ledger([deposit, withdrawal], 0m, 200m);
        Near(.331, _service.Calculate(Input(1000m, 1406m, flowValues: [second, first]), ledger).TimeWeighted.Value);
        var result = _service.Calculate(Input(1000m, 1406m, flowValues: [first, second with { Sequence = 1 }]), ledger);
        Assert.Null(result.TimeWeighted.Value); Assert.Contains("順序が重複", result.TimeWeighted.Status);
        result = _service.Calculate(Input(flowValues: [first]), Ledger());
        Assert.Null(result.TimeWeighted.Value); Assert.Contains("対象外", result.TimeWeighted.Status);
    }
    [Fact]
    public void FullWithdrawalCanEndTwrButZeroCapitalIntervalCannotBeDivided()
    {
        var withdrawal = Operation(CashOperationKind.Withdrawal, -1100m);
        var value = new PerformanceFlowValuation(withdrawal.Id, 1, 1100m, 0m, true, "全額出金時点の評価");
        var dividend = Operation(CashOperationKind.Dividend, 100m, Start.AddDays(100));
        Near(.1, _service.Calculate(Input(0m, 0m, flowValues: [value]), Ledger([dividend, withdrawal], 1000m, 0m)).TimeWeighted.Value);
        var deposit = Operation(CashOperationKind.Deposit, 100m);
        var result = _service.Calculate(Input(0m, 0m, flowValues: [value, new(deposit.Id, 2, 0m, 100m, true, "再入金")]),
            Ledger([dividend, withdrawal, deposit], 1000m, 100m));
        Assert.Null(result.TimeWeighted.Value); Assert.Contains("残高0", result.TimeWeighted.Status);
    }
    [Fact]
    public void XirrUses365DayYearAndWithholdsAmbiguousRootsOrNoSolution()
    {
        Near(Math.Pow(1.1, 365d / 366d) - 1d,
            PerformancePeriodService.Xirr([(new(2024, 1, 1), -100m), (new(2025, 1, 1), 110m)]).Value);
        Assert.Null(PerformancePeriodService.Xirr([(Start, -100m), (End, 230m), (End.AddYears(1), -132m)]).Value);
        var ambiguous = PerformancePeriodService.Xirr([(Start, -100m), (End, 350m), (End.AddYears(1), -380m), (End.AddYears(2), 132m)]);
        Assert.Null(ambiguous.Value); Assert.Contains("一意性", ambiguous.Status);
        Assert.Null(PerformancePeriodService.Xirr([(Start, -100m), (End, -1m)]).Value);
        Assert.Null(PerformancePeriodService.Xirr([(Start, -1m), (Start.AddDays(1), 1000000m)]).Value);
        Near(-.5, PerformancePeriodService.Xirr([(Start, -100m), (End, 50m)]).Value);
    }
    [Fact]
    public void InvalidDateNegativeAssetsAndDecimalOverflowHaveExplicitProblems()
    {
        Assert.NotEmpty(_service.Calculate(new() { StartDate = End, EndDate = Start }, Ledger()).Problems);
        Assert.NotEmpty(_service.Calculate(Input(-1m), Ledger()).Problems);
        Assert.NotEmpty(_service.Calculate(Input(decimal.MaxValue), Ledger(opening: 1m, closing: 1m)).Problems);
    }
    [Fact]
    public void SavedReportPreservesEvidenceAndRejectsChangedLedger()
    {
        var ledger = Ledger(); Seed(ledger); var input = Input();
        var fingerprint = PerformancePeriodService.Fingerprint(_repository.GetState(), End);
        var saved = _repository.SavePerformanceReport(input, fingerprint);
        Assert.Equal(100m, saved.Result.ProfitJpy);
        Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        _repository.Add(Operation(CashOperationKind.Deposit, 1m));
        Assert.Throws<InvalidOperationException>(() => _repository.SavePerformanceReport(input, fingerprint));
        var reloaded = Assert.Single(new CashLedgerRepository(_path).GetPerformanceReports());
        Assert.Equal(100m, reloaded.Result.ProfitJpy); Assert.Empty(reloaded.LedgerEvidence.Operations);
        Assert.NotEqual(reloaded.LedgerFingerprint, PerformancePeriodService.Fingerprint(_repository.GetState(), End));
        Assert.Throws<InvalidOperationException>(() => _repository.SavePerformanceReport(input, PerformancePeriodService.Fingerprint(_repository.GetState(), End)));
    }
    [Fact]
    public void SavedReportKeepsBenchmarkBasisAndComparison()
    {
        Seed(Ledger());
        var benchmark = new PerformanceBenchmark("税引後配当込み指数", Start, End, "JPY", 100m, 105m,
            true, true, false, "指数の公表資料");
        var input = WithBenchmark(benchmark);
        _repository.SavePerformanceReport(input, PerformancePeriodService.Fingerprint(_repository.GetState(), End));
        var reloaded = Assert.Single(new CashLedgerRepository(_path).GetPerformanceReports());
        Assert.Equal(benchmark, reloaded.Input.Benchmark);
        Near(.05, reloaded.Result.Benchmark.Difference);
    }
    [Fact]
    public void MigrationAndRestoreIncludePerformanceReports()
    {
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE PerformanceReports;"; command.ExecuteNonQuery();
        }
        new DatabaseInitializer().Initialize(_path);
        var before = Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        new DatabaseInitializer().Initialize(_path);
        Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        Seed(Ledger()); _repository.SavePerformanceReport(Input(), PerformancePeriodService.Fingerprint(_repository.GetState(), End));
        Assert.Contains("運用成績の保存記録 1件", new DatabaseRestoreService(_path).GetRestoreImpact(Path.GetFileName(before)));
    }
    [Fact]
    public async Task ViewModelRequiresReconfirmationAfterEditingOrLoadingSavedConditions()
    {
        Seed(Ledger()); var vm = new PerformanceViewModel(() => _repository)
        {
            StartDate = Start, EndDate = End, Opening = "1000", Closing = "1100", OpeningEvidence = "期首明細", ClosingEvidence = "期末明細",
            SecuritiesConfirmed = true, AccountsConfirmed = true, NoInKindConfirmed = true
        };
        await vm.CalculateAsync(); Assert.True(vm.SaveCommand.CanExecute(null)); Near(.1, vm.Result.MoneyWeighted.Value);
        await vm.SaveAsync(); Assert.Single(vm.History); Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Closing = "1200"; Assert.False(vm.SecuritiesConfirmed); Assert.Null(vm.Result.MoneyWeighted.Value);
        vm.SelectedHistory = vm.History[0]; vm.UseHistoryCommand.Execute(null);
        Assert.Equal("1100", vm.Closing); Assert.False(vm.AccountsConfirmed); Assert.False(vm.NoInKindConfirmed);
        await vm.CalculateAsync(); Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SecuritiesConfirmed = vm.AccountsConfirmed = vm.NoInKindConfirmed = true;
        vm.Load(); Assert.False(vm.SecuritiesConfirmed);
        vm.Reset(); Assert.Empty(vm.History); Assert.Empty(vm.Opening); Assert.Empty(vm.FxRows);
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
