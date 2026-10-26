using System.Text;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class CashCsvImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryCashCsv_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly CashLedgerRepository _repository;
    private const string Header = "約定日,受渡日,取引区分,発行通貨,決済通貨,受渡金額/決済損益,手数料（税込）,銘柄名";
    private const string Dividend = "2026/01/01,2026/01/05,入金（配当金）,USD,JPY,1201,10,NVDA";
    private const string Purchase = "2026/01/01,2026/01/06,現物買付,USD,JPY,15000,100,CSCO";
    public CashCsvImportTests()
    {
        Directory.CreateDirectory(_directory); _path = Path.Combine(_directory, "ledger.db");
        new DatabaseInitializer().Initialize(_path); _repository = new(_path);
    }
    private string Csv(string text, string name = "cash.csv")
    {
        var path = Path.Combine(_directory, name); File.WriteAllText(path, text, new UTF8Encoding(true)); return path;
    }
    private CashCsvPlan Plan(params string[] rows) => new CashCsvParser().Parse(Csv(Header + "\n" + string.Join("\n", rows)), "野村 / 現金口座");
    private static CashCsvDecision Add(CashCsvCandidate row) => new(row.SourceKey, CashCsvResolutions.Add, "");
    private static CashCsvDecision Distinct(CashCsvCandidate row) => new(row.SourceKey, CashCsvResolutions.Distinct, "別々の取引番号を原本で確認");
    private void Sql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    [Fact]
    public void UsesSettlementDateCurrencyAndNetAmountWithoutDeductingFeesAgain()
    {
        var plan = Plan(Dividend, Purchase);
        var dividend = plan.Rows[0].Operation!; var purchase = plan.Rows[1].Operation!;
        Assert.Equal(new DateTime(2026, 1, 5), dividend.Date);
        Assert.Equal(new CashLeg("野村 / 現金口座", "JPY", 1201m), Assert.Single(dividend.Legs));
        Assert.Equal(-15000m, Assert.Single(purchase.Legs).Amount);
        Assert.Equal(CashOperationKind.BuySettlement, purchase.Kind);
        Assert.Empty(_repository.GetOperations()); // Parsing is read-only.
    }

    [Fact]
    public void MissingCurrencyRequiresExplicitEvidenceAndIsNeverInferredFromIssueCurrency()
    {
        var plan = Plan(Dividend.Replace("USD,JPY", "USD,"));
        Assert.Null(plan.Rows[0].Operation); Assert.Contains("決済通貨", plan.Rows[0].Problem);
        Assert.Throws<ArgumentException>(() => new CashCsvParser().Parse(plan.FilePath, "野村", "JPY"));
        var confirmed = new CashCsvParser().Parse(plan.FilePath, "野村", "JPY", "円預り残高明細の入金欄を確認");
        _repository.ImportCsv(confirmed, [Add(confirmed.Rows[0])]);
        Assert.Contains("円預り残高明細", Assert.Single(_repository.GetOperations()).Evidence);
        Assert.Equal(1201m, Assert.Single(_repository.GetOperations()).Legs[0].Amount);
    }

    [Theory]
    [InlineData("入金（振替）")]
    [InlineData("出金（振替）")]
    [InlineData("出庫（増減資）")]
    [InlineData("入金（権処）")]
    public void TransfersAndCorporateActionsRemainPending(string kind)
    {
        var plan = Plan(Dividend.Replace("入金（配当金）", kind));
        Assert.Null(plan.Rows[0].Operation);
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(plan, [Add(plan.Rows[0])]));
        Assert.Empty(_repository.GetOperations()); Assert.Empty(_repository.GetCsvSources());
    }

    [Fact]
    public void BankTransferRequiresExternalFlowConfirmation()
    {
        var plan = Plan(Dividend.Replace("入金（配当金）", "入金（振込）"));
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(plan, [Add(plan.Rows[0])]));
        Assert.Empty(_repository.GetOperations());
        _repository.ImportCsv(plan, [Add(plan.Rows[0])], externalFlowsConfirmed: true);
        Assert.Contains("管理対象口座外", Assert.Single(_repository.GetCsvSources()).Reason);
    }

    [Fact]
    public void SbiDomesticAndForeignUseTheirSettlementCurrency()
    {
        var domestic = Csv("約定日,受渡日,取引,手数料/諸経費等,受渡金額/決済損益\n2026/01/01,2026/01/05,投信金額買付,10,5000");
        Assert.Equal(new CashLeg("SBI", "JPY", -5000m), new CashCsvParser().Parse(domestic, "SBI").Rows[0].Operation!.Legs[0]);
        var foreign = Csv("国内約定日,国内受渡日,取引,受渡金額/決済損益\n2026/01/01,26/01/05,現買,\"2,859.60USD\"");
        Assert.Equal(new CashLeg("SBI", "USD", -2859.60m), new CashCsvParser().Parse(foreign, "SBI").Rows[0].Operation!.Legs[0]);
        var explicitCurrency = Csv("国内約定日,国内受渡日,取引,通貨,受渡金額\n2026年01月01日,26/01/05,現売,米国ドル,120.75");
        Assert.Equal(120.75m, new CashCsvParser().Parse(explicitCurrency, "SBI").Rows[0].Operation!.Legs[0].Amount);
    }

    [Fact]
    public void InvalidStructureFailsWholePreviewButInvalidValuesStayPending()
    {
        Assert.Throws<InvalidOperationException>(() => Plan(Dividend, "broken,row"));
        Assert.Throws<InvalidOperationException>(() => Plan(Dividend + ",extra"));
        Assert.Throws<InvalidOperationException>(() => Plan(Dividend + ",\"unclosed"));
        Assert.Throws<InvalidOperationException>(() => Plan(Dividend.Replace("1201", "\"12\"01")));
        var plan = Plan(Dividend.Replace("1201", "-100"), Dividend.Replace("2026/01/05", "2099/01/05"));
        Assert.All(plan.Rows, x => Assert.Null(x.Operation));
    }

    [Fact]
    public void ShiftJisQuotedCommaAndMultilineEvidenceArePreserved()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Csv("");
        File.WriteAllText(path, Header + "\n" + Dividend.Replace("NVDA", "\"銘柄,名称\n追加説明\""), Encoding.GetEncoding(932));
        var row = Assert.Single(new CashCsvParser().Parse(path, "野村").Rows);
        Assert.NotNull(row.Operation); Assert.Contains("銘柄,名称\n追加説明", row.RawRecord);
    }

    [Fact]
    public void SameFileAndRenamedCopyAreIdempotentAndCancelledRowsAreNotResurrected()
    {
        var plan = Plan(Dividend); var decision = Add(plan.Rows[0]);
        Assert.Equal(1, _repository.ImportCsv(plan, [decision]).AddedCount);
        var copy = Path.Combine(_directory, "renamed.csv"); File.Copy(plan.FilePath, copy);
        var renamed = new CashCsvParser().Parse(copy, plan.Account);
        Assert.True(Assert.Single(_repository.PreviewCsv(renamed)).AlreadyImported);
        Assert.Equal(0, _repository.ImportCsv(renamed, [decision]).AddedCount);
        _repository.Cancel(Assert.Single(_repository.GetOperations()).Id, "訂正");
        Assert.Equal(0, _repository.ImportCsv(renamed, [decision]).AddedCount);
        Assert.NotNull(Assert.Single(_repository.GetOperations()).CancelledAtUtc);
        Assert.Single(_repository.GetCsvSources());
    }

    [Fact]
    public void SortedExportRequiresReviewAndCanLinkWithoutChangingCashBalance()
    {
        var plan = Plan(Dividend, Purchase);
        _repository.ImportCsv(plan, plan.Rows.Select(Add).ToArray());
        var sorted = Plan(Purchase, Dividend);
        var reviews = _repository.PreviewCsv(sorted);
        Assert.All(reviews, x => { Assert.False(x.AlreadyImported); Assert.True(x.NeedsReview); });
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(sorted, sorted.Rows.Select(Add).ToArray()));
        var decisions = reviews.Select(x => new CashCsvDecision(x.Candidate.SourceKey, CashCsvResolutions.Link,
            "受渡日順と約定日順の同じ明細", Assert.Single(x.Matches).Id)).ToArray();
        var result = _repository.ImportCsv(sorted, decisions);
        Assert.Equal(0, result.AddedCount); Assert.Equal(2, result.LinkedCount);
        Assert.Equal(2, _repository.GetOperations().Count); Assert.Equal(4, _repository.GetCsvSources().Count);
    }

    [Fact]
    public void EqualPaymentsWithinOneFileAreNotCollapsedAndNeedExplicitSeparateEvidence()
    {
        var plan = Plan(Dividend, Dividend.Replace("1201", "1201.00"));
        Assert.All(_repository.PreviewCsv(plan), x => Assert.True(x.SameAmountInFile));
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(plan, plan.Rows.Select(Add).ToArray()));
        Assert.Empty(_repository.GetOperations());
        _repository.ImportCsv(plan, plan.Rows.Select(Distinct).ToArray());
        Assert.Equal(2402m, _repository.GetOperations().Sum(x => x.Legs[0].Amount));
        Assert.Equal(2, _repository.GetCsvSources().Count);
    }

    [Fact]
    public void ManualMatchIsDetectedAndNewMatchAfterPreviewRollsBackEntireBatch()
    {
        var plan = Plan(Purchase, Dividend);
        Assert.All(_repository.PreviewCsv(plan), x => Assert.False(x.NeedsReview));
        _repository.Add(new CashOperation { Date = plan.Rows[1].Operation!.Date, Kind = CashOperationKind.Dividend,
            Legs = plan.Rows[1].Operation!.Legs, Evidence = "手入力の明細" });
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(plan, plan.Rows.Select(Add).ToArray()));
        Assert.Single(_repository.GetOperations()); Assert.Empty(_repository.GetCsvSources());
        Assert.True(_repository.PreviewCsv(plan)[1].NeedsReview);
    }

    [Fact]
    public void LinkingRejectsWrongAmountCancelledTargetAndMissingReason()
    {
        var plan = Plan(Dividend);
        var op = plan.Rows[0].Operation!;
        _repository.Add(op);
        Assert.Throws<ArgumentException>(() => _repository.ImportCsv(plan,
            [new(plan.Rows[0].SourceKey, CashCsvResolutions.Link, "", op.Id)]));
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(plan,
            [new(plan.Rows[0].SourceKey, CashCsvResolutions.Link, "確認", "unknown")]));
        _repository.Cancel(op.Id, "訂正");
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(plan,
            [new(plan.Rows[0].SourceKey, CashCsvResolutions.Link, "確認", op.Id)]));
        Assert.Empty(_repository.GetCsvSources());
    }

    [Fact]
    public void CsvModificationAndBackupFailurePreventAnyWrites()
    {
        var plan = Plan(Dividend); File.AppendAllText(plan.FilePath, "\n" + Purchase);
        Assert.Throws<InvalidOperationException>(() => _repository.ImportCsv(plan, [Add(plan.Rows[0])]));
        Assert.Empty(_repository.GetOperations());
        plan = Plan(Dividend); File.WriteAllText(_path + ".pre_import_backups", "block backup folder");
        Assert.Throws<IOException>(() => _repository.ImportCsv(plan, [Add(plan.Rows[0])]));
        Assert.Empty(_repository.GetOperations()); Assert.Empty(_repository.GetCsvSources());
    }

    [Fact]
    public void SourceInsertFailureRollsBackOperationsLegsAndOtherSources()
    {
        var plan = Plan(Dividend, Purchase);
        Sql("CREATE TRIGGER fail_cash_csv BEFORE INSERT ON CashCsvSources WHEN (SELECT COUNT(*) FROM CashCsvSources)=1 BEGIN SELECT RAISE(ABORT,'test'); END;");
        Assert.Throws<SqliteException>(() => _repository.ImportCsv(plan, plan.Rows.Select(Add).ToArray()));
        Assert.Empty(_repository.GetOperations()); Assert.Empty(_repository.GetCsvSources());
        Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
    }

    [Fact]
    public void MigrationBacksUpExistingLedgerAndRestoreImpactIncludesSources()
    {
        Sql("DROP TABLE CashCsvSources;");
        new DatabaseInitializer().Initialize(_path);
        var backups = new InvestmentStoryRepository(_path).GetPreImportBackupPaths(); Assert.Single(backups);
        new DatabaseInitializer().Initialize(_path);
        Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        var plan = Plan(Dividend); _repository.ImportCsv(plan, [Add(plan.Rows[0])]);
        Assert.Contains("現金CSV照合履歴 1件", new DatabaseRestoreService(_path).GetRestoreImpact(Path.GetFileName(backups[0])));
    }

    [Fact]
    public async Task ViewModelStartsOnHoldInvalidatesChangedSettingsAndRefreshesParentAfterSuccess()
    {
        var plan = Plan(Dividend); var refreshes = 0;
        var parent = new CashLedgerViewModel(() => _repository, () => refreshes++);
        var vm = parent.CsvImport; vm.FilePath = plan.FilePath; vm.Account = plan.Account;
        await vm.PreviewAsync();
        Assert.Equal(CashCsvResolutions.Hold, Assert.Single(vm.Rows).Resolution);
        await vm.ImportAsync(); Assert.Empty(_repository.GetOperations());
        vm.Account = "別口座"; Assert.Empty(vm.Rows); Assert.False(vm.ImportCommand.CanExecute(null));
        await vm.PreviewAsync(); vm.SelectCandidatesCommand.Execute(null); await vm.ImportAsync();
        Assert.Equal(1, refreshes); Assert.Single(parent.Operations); Assert.Single(parent.CsvSources);
        Assert.Equal("別口座", Assert.Single(_repository.GetOperations()).Legs[0].Account);
        Assert.True(Assert.Single(vm.Rows).Review.AlreadyImported); Assert.False(vm.IsBusy);
        parent.Reset(); Assert.Empty(vm.FilePath); Assert.Empty(vm.Rows); Assert.Empty(parent.CsvSources);
    }

    [Fact]
    public void ImportReparsesMonetaryValuesInsteadOfTrustingMutablePreviewObjects()
    {
        var plan = Plan(Dividend);
        var forged = plan with { Rows = [plan.Rows[0] with { Operation = new CashOperation { Date = new(2026, 1, 5),
            Evidence = "forged", Kind = CashOperationKind.Dividend, Legs = [new(plan.Account, "JPY", 999999m)] } }] };
        _repository.ImportCsv(forged, [Add(forged.Rows[0])]);
        Assert.Equal(1201m, Assert.Single(_repository.GetOperations()).Legs[0].Amount);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
