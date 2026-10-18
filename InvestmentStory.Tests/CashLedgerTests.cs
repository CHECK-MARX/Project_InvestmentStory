using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class CashLedgerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryCash_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly CashLedgerRepository _repository;
    private static readonly DateTime Day = new(2026, 1, 15);

    public CashLedgerTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "ledger.db");
        new DatabaseInitializer().Initialize(_path);
        _repository = new CashLedgerRepository(_path);
    }

    private static CashOperation Operation(CashOperationKind kind, params CashLeg[] legs) => new()
    {
        Date = Day, Kind = kind, Evidence = "テスト明細 1行", Legs = legs
    };

    private CashReconciliation Period(decimal? opening = 0m, decimal? closing = 100m,
        bool entries = true, bool flows = true, string account = "SBI / 特定", string currency = "JPY")
    {
        var scope = new CashReconciliation { Account = account, Currency = currency, StartDate = Day, EndDate = Day };
        return new()
        {
            Account = account, Currency = currency, StartDate = Day, EndDate = Day,
            OpeningBalance = opening, ClosingBalance = closing,
            AllEntriesConfirmed = entries, ExternalFlowsConfirmed = flows, Evidence = "日次残高明細",
            LedgerFingerprint = CashLedgerService.Fingerprint(_repository.GetOperations(), scope)
        };
    }

    [Fact]
    public void DecimalAmountsSurviveRoundtripAndSameIdCannotBeInsertedTwice()
    {
        var operation = Operation(CashOperationKind.Deposit, new CashLeg("SBI / 特定", "USD", 0.1234567890123456789012345678m));
        _repository.Add(operation);
        Assert.Throws<SqliteException>(() => _repository.Add(operation));
        var stored = Assert.Single(_repository.GetOperations());
        Assert.Equal(operation.Legs[0].Amount, Assert.Single(stored.Legs).Amount);
        Assert.Equal(operation.CreatedAtUtc, stored.CreatedAtUtc);
    }

    [Fact]
    public void TransferFailureOnSecondLegRollsBackWholeOperation()
    {
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_second_leg BEFORE INSERT ON CashLegs WHEN NEW.LegIndex=1 BEGIN SELECT RAISE(ABORT, 'test failure'); END;";
            command.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => _repository.Add(Operation(CashOperationKind.Transfer,
            new("SBI / 特定", "JPY", -100m), new("野村 / 特定", "JPY", 100m))));
        Assert.Empty(_repository.GetOperations());
        using var verify = new SqliteConnection($"Data Source={_path};Pooling=False");
        verify.Open();
        using var count = verify.CreateCommand();
        count.CommandText = "SELECT (SELECT COUNT(*) FROM CashOperations) + (SELECT COUNT(*) FROM CashLegs);";
        Assert.Equal(0L, count.ExecuteScalar());
    }

    [Fact]
    public void ExchangeIsInternalAndCancellationRemovesBothLegsFromBalances()
    {
        var exchange = Operation(CashOperationKind.Exchange,
            new("SBI / 特定", "JPY", -15000m), new("SBI / 特定", "USD", 100m));
        _repository.Add(exchange);
        var jpy = CashLedgerService.Reconcile(_repository.GetOperations(), Period(15000m, 0m));
        var usd = CashLedgerService.Reconcile(_repository.GetOperations(), Period(0m, 100m, currency: "USD"));
        Assert.True(jpy.IsComplete); Assert.True(usd.IsComplete);
        Assert.Equal(0m, jpy.ExternalNetFlow); Assert.Equal(0m, usd.ExternalNetFlow);
        _repository.Cancel(exchange.Id, "取引日訂正のため");
        var cancelled = Assert.Single(_repository.GetOperations());
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.Equal(2, cancelled.Legs.Count);
        Assert.Equal(0m, CashLedgerService.Reconcile(_repository.GetOperations(), Period(15000m, 15000m)).Movement);
        Assert.Equal(0m, CashLedgerService.Reconcile(_repository.GetOperations(), Period(0m, 0m, currency: "USD")).Movement);
    }

    [Fact]
    public void OnlyExternalDepositAndWithdrawalCountAsExternalFlows()
    {
        foreach (var (kind, amount) in new[]
        {
            (CashOperationKind.Deposit, 1000m), (CashOperationKind.Withdrawal, -100m),
            (CashOperationKind.BuySettlement, -500m), (CashOperationKind.SellSettlement, 300m),
            (CashOperationKind.Dividend, 50m), (CashOperationKind.Tax, -10m), (CashOperationKind.Fee, -5m)
        }) _repository.Add(Operation(kind, new CashLeg("SBI / 特定", "JPY", amount)));
        _repository.Add(Operation(CashOperationKind.Transfer, new("SBI / 特定", "JPY", -100m), new("野村 / 特定", "JPY", 100m)));
        var result = CashLedgerService.Reconcile(_repository.GetOperations(), Period(0m, 635m));
        Assert.Equal(900m, result.ExternalNetFlow);
        Assert.Equal(635m, result.Movement);
        Assert.True(result.IsComplete);
        Assert.Equal(0m, CashLedgerService.Reconcile(_repository.GetOperations(), Period(0m, 100m, account: "野村 / 特定")).ExternalNetFlow);
    }

    [Fact]
    public void MissingOpeningBalanceCannotBecomeZeroOrComplete()
    {
        _repository.Add(Operation(CashOperationKind.Deposit, new CashLeg("SBI / 特定", "JPY", 100m)));
        var result = CashLedgerService.Reconcile(_repository.GetOperations(), Period(null));
        Assert.Null(result.ExpectedClosingBalance); Assert.Null(result.Difference); Assert.False(result.IsComplete);
        Assert.Contains("期首残高未確認", result.Status);
        Assert.True(CashLedgerService.Reconcile(_repository.GetOperations(), Period(0m)).IsComplete);
    }

    [Fact]
    public void MatchingBalanceDoesNotProveCompleteness()
    {
        var result = CashLedgerService.Reconcile(_repository.GetOperations(), Period(0m, 0m, entries: false, flows: false));
        Assert.Equal(0m, result.Difference); Assert.False(result.IsComplete);
        Assert.Contains("全明細未確認", result.Status); Assert.Contains("網羅性未確認", result.Status);
        Assert.False(CashLedgerService.Reconcile(_repository.GetOperations(), Period(0m, 1m)).IsComplete);
    }

    [Fact]
    public void NetZeroNewTransactionsInvalidateSavedReconciliationAndRejectStaleSave()
    {
        var period = Period(0m, 0m);
        _repository.SaveReconciliation(period);
        _repository.Add(Operation(CashOperationKind.Deposit, new CashLeg("SBI / 特定", "JPY", 100m)));
        _repository.Add(Operation(CashOperationKind.Withdrawal, new CashLeg("SBI / 特定", "JPY", -100m)));
        var saved = Assert.Single(_repository.GetReconciliations());
        var result = CashLedgerService.Reconcile(_repository.GetOperations(), saved);
        Assert.Equal(0m, result.Difference); Assert.False(result.IsComplete);
        Assert.Throws<InvalidOperationException>(() => _repository.SaveReconciliation(period));
        _repository.SaveReconciliation(Period(0m, 0m));
        Assert.True(CashLedgerService.Reconcile(_repository.GetOperations(), Assert.Single(_repository.GetReconciliations())).IsComplete);
    }

    [Fact]
    public void ReconciliationOnlyIncludesItsAccountCurrencyAndInclusiveDates()
    {
        _repository.Add(Operation(CashOperationKind.Deposit, new CashLeg("SBI / 特定", "JPY", 100m)));
        var period = Period();
        _repository.Add(Operation(CashOperationKind.Deposit, new CashLeg("別口座", "JPY", 200m)));
        _repository.Add(Operation(CashOperationKind.Deposit, new CashLeg("SBI / 特定", "USD", 300m)));
        _repository.Add(new CashOperation { Date = Day.AddDays(1), Evidence = "翌日", Kind = CashOperationKind.Deposit, Legs = new[] { new CashLeg("SBI / 特定", "JPY", 400m) } });
        _repository.SaveReconciliation(period);
        Assert.True(CashLedgerService.Reconcile(_repository.GetOperations(), period).IsComplete);
        Assert.Equal(100m, CashLedgerService.Reconcile(_repository.GetOperations(), period).Movement);
    }

    [Fact]
    public void InvalidTransferAndExchangeAreRejectedBeforeWriting()
    {
        Assert.Throws<ArgumentException>(() => _repository.Add(Operation(CashOperationKind.Transfer, new CashLeg("A", "JPY", -100m))));
        Assert.Throws<ArgumentException>(() => _repository.Add(Operation(CashOperationKind.Transfer, new("A", "JPY", -100m), new("B", "JPY", 99m))));
        Assert.Throws<ArgumentException>(() => _repository.Add(Operation(CashOperationKind.Transfer, new("A", "JPY", -100m), new("A", "JPY", 100m))));
        Assert.Throws<ArgumentException>(() => _repository.Add(Operation(CashOperationKind.Exchange, new("A", "JPY", -100m), new("B", "JPY", 100m))));
        Assert.Throws<ArgumentException>(() => _repository.Add(Operation(CashOperationKind.Withdrawal, new CashLeg("A", "JPY", 100m))));
        Assert.Empty(_repository.GetOperations());
    }

    [Fact]
    public void ViewModelRejectsBadInputAndResetClearsDraftAndConfirmation()
    {
        var vm = new CashLedgerViewModel(() => _repository);
        vm.Load(); vm.EntryDate = Day; vm.EntryAccount = "SBI / 特定"; vm.EntryAmount = "not a number"; vm.Evidence = "明細";
        vm.SaveCommand.Execute(null);
        Assert.Empty(_repository.GetOperations()); Assert.Contains("保存できません", vm.Message);
        vm.EntryAmount = "100";
        vm.SaveCommand.Execute(null);
        Assert.Single(_repository.GetOperations());
        vm.SaveCommand.Execute(null);
        Assert.Single(_repository.GetOperations());
        vm.AllEntriesConfirmed = true; vm.ExternalFlowsConfirmed = true;
        vm.Reset();
        Assert.Empty(vm.Operations); Assert.Empty(vm.EntryAccount);
        Assert.False(vm.AllEntriesConfirmed); Assert.False(vm.ExternalFlowsConfirmed);
    }

    [Fact]
    public void ExistingDatabaseIsBackedUpBeforeCashSchemaMigration()
    {
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE CashLegs; DROP TABLE CashOperations; DROP TABLE CashReconciliations;";
            command.ExecuteNonQuery();
        }
        new DatabaseInitializer().Initialize(_path);
        var backup = Assert.Single(Directory.GetFiles(_directory, "*_before_cash_ledger.db"));
        using var verify = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        verify.Open(); using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='CashOperations';";
        Assert.Equal(0L, count.ExecuteScalar());
        new DatabaseInitializer().Initialize(_path);
        Assert.Single(Directory.GetFiles(_directory, "*_before_cash_ledger.db"));
    }

    [Fact]
    public void RestoreImpactIncludesNewCashRecordsAndReconciliation()
    {
        var main = new InvestmentStoryRepository(_path);
        main.RunImportAtomically(() => { });
        _repository.Add(Operation(CashOperationKind.Deposit, new CashLeg("SBI / 特定", "JPY", 100m)));
        _repository.SaveReconciliation(Period());
        var restore = new DatabaseRestoreService(_path);
        var impact = restore.GetRestoreImpact(Assert.Single(restore.GetBackups()).FileName);
        Assert.Contains("現金取引 1件", impact);
        Assert.Contains("現金明細 1件", impact);
        Assert.Contains("現金残高照合 1件", impact);
    }

    [Fact]
    public void SaveReconciliationAndCancellationRefreshCashDashboard()
    {
        CashAssetSummary? latest = null;
        var vm = new CashLedgerViewModel(() => _repository,
            () => latest = new CashAssetValuationService().Build(_repository.GetState(), Day));
        vm.Load(); vm.EntryDate = Day; vm.EntryAccount = "SBI / 特定"; vm.EntryAmount = "100"; vm.Evidence = "明細";
        vm.SaveCommand.Execute(null);
        Assert.NotNull(latest); Assert.Null(latest.TotalRegisteredCashJpy);
        vm.ReviewAccount = "SBI / 特定"; vm.StartDate = Day; vm.EndDate = Day;
        vm.OpeningBalance = "0"; vm.ClosingBalance = "100"; vm.AllEntriesConfirmed = true;
        vm.ExternalFlowsConfirmed = true; vm.ReviewEvidence = "期末残高";
        vm.ReconcileCommand.Execute(null);
        Assert.Equal(100m, latest.TotalRegisteredCashJpy);
        vm.SelectedOperation = Assert.Single(vm.Operations); vm.CancellationReason = "入力訂正";
        vm.CancelCommand.Execute(null);
        Assert.Null(latest.TotalRegisteredCashJpy);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
