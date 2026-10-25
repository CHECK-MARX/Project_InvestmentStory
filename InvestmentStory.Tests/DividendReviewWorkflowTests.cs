using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using InvestmentStory.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class DividendReviewWorkflowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "InvestmentStoryDividendReview_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly InvestmentStoryRepository _repository;
    private readonly DividendReviewRepository _reviews;
    private readonly int _stockId;
    private static readonly DateTime Today = new(2026, 10, 25);

    public DividendReviewWorkflowTests()
    {
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "test.db");
        _repository = new InvestmentStoryRepository(_path);
        _repository.Initialize();
        _reviews = new DividendReviewRepository(_path);
        _stockId = _repository.SavePosition(new StockPosition
        {
            Stock = new Stock { Ticker = "ABC", Name = "Alpha", Broker = "SBI証券",
                AccountType = AccountTypes.Specific, Currency = "JPY" },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m }
        });
    }

    [Fact]
    public void HoldThenLinkKeepsEvidenceAndUpdatesTodayReviewCount()
    {
        var planId = Save(DividendConstants.Planned, new DateTime(2026, 10, 10), 200m);
        var actualId = Save(DividendConstants.Actual, new DateTime(2026, 10, 12), 250m);
        var candidates = new DividendReconciliationService().FindActualCandidates(
            _repository.GetDividendPayments().Single(x => x.Id == planId), _repository.GetDividendPayments());
        Assert.Equal(actualId, Assert.Single(candidates).Id);
        Assert.Equal(1, TodayCard().Count);

        _reviews.Apply(planId, "Held", null, "入金明細の口座を確認中");
        var held = TodayCard();
        Assert.Equal(1, held.Count);
        Assert.Contains("保留 1件", held.Detail);
        Assert.Contains("入金明細の口座を確認中", Assert.Single(held.Items).Context);
        Assert.Equal(DividendConstants.Planned,
            _repository.GetDividendPayments().Single(x => x.Id == planId).DividendStatus);

        _reviews.Apply(planId, "Linked", actualId, "SBIの入金明細で一致");
        var linked = _repository.GetDividendPayments().Single(x => x.Id == planId);
        Assert.Equal(DividendConstants.Replaced, linked.DividendStatus);
        Assert.Equal(actualId, linked.MatchedActualDividendId);
        Assert.Equal(actualId, linked.ReplacedByDividendId);
        Assert.Equal(0, TodayCard().Count);
        Assert.Equal(250m, new DividendDashboardService()
            .Build(_repository.GetDividendPayments(), 2026, 0m, Today).YearEndForecastJpy);
        var history = _reviews.GetHistory(planId);
        Assert.Equal(new[] { "Linked", "Held" }, history.Select(x => x.Action));
        Assert.Contains("SBIの入金明細", history[0].Reason);
    }

    [Fact]
    public void ExclusionRequiresReasonAndDoesNotDeleteSourceOrActual()
    {
        var planId = Save(DividendConstants.Planned, new DateTime(2026, 10, 8), 100m);
        Assert.Throws<ArgumentException>(() => _reviews.Apply(planId, "Excluded", null, " "));
        Assert.Empty(_reviews.GetHistory(planId));
        _reviews.Apply(planId, "Excluded", null, "配当ではなく別の入金と確認");
        Assert.Equal(DividendConstants.Excluded,
            _repository.GetDividendPayments().Single(x => x.Id == planId).DividendStatus);
        Assert.Equal(0, TodayCard().Count);
        Assert.Single(_reviews.GetHistory(planId));
        Assert.Throws<InvalidOperationException>(() =>
            _reviews.Apply(planId, "Excluded", null, "重複実行"));
    }

    [Fact]
    public void ExactUnlinkedActualRemainsVisibleForReviewWithoutInflatingForecast()
    {
        var planId = Save(DividendConstants.Planned, new DateTime(2026, 10, 10), 100m);
        var actualId = Save(DividendConstants.Actual, new DateTime(2026, 10, 10), 100m);
        var analysis = new DividendDashboardService().Build(_repository.GetDividendPayments(),
            2026, 0m, Today);
        Assert.Equal(1, TodayCard().Count);
        Assert.Equal(100m, analysis.YearEndForecastJpy);
        Assert.Equal(DividendDashboardService.StatusNeedsReview,
            analysis.Entries.Single(x => x.Payment.Id == planId).DisplayStatus);
        Assert.Equal(1, new ReminderService().Build(_repository.GetDividendPayments(), [], Today)
            .Count(x => x.Kind == ReminderKind.DividendOverdue));
        _reviews.Apply(planId, "Linked", actualId, "一致する入金明細を確認");
        Assert.Equal(0, TodayCard().Count);
        Assert.Empty(new ReminderService().Build(_repository.GetDividendPayments(), [], Today));
    }

    [Fact]
    public void KnownDifferentAccountCannotBeLinkedEvenIfSecurityAndDateMatch()
    {
        var planId = Save(DividendConstants.Planned, new DateTime(2026, 10, 8), 100m);
        var nisaActualId = Save(DividendConstants.Actual, new DateTime(2026, 10, 9), 100m,
            AccountTypes.NisaGrowth);
        var plan = _repository.GetDividendPayments().Single(x => x.Id == planId);
        Assert.Empty(new DividendReconciliationService().FindActualCandidates(plan,
            _repository.GetDividendPayments()));
        Assert.Throws<ArgumentException>(() => _reviews.Apply(planId, "Linked", nisaActualId, "別口座"));
        Assert.Empty(_reviews.GetHistory(planId));
        Assert.Equal(DividendConstants.Planned,
            _repository.GetDividendPayments().Single(x => x.Id == planId).DividendStatus);
    }

    [Fact]
    public void DifferentKnownTaxAccountIsNotAnActualCandidate()
    {
        var planId = Save(DividendConstants.Planned, new DateTime(2026, 10, 8), 100m);
        var actualId = Save(DividendConstants.Actual, new DateTime(2026, 10, 9), 100m);
        var actual = _repository.GetDividendPayments().Single(x => x.Id == actualId);
        actual.TaxAccountType = AccountTypes.NisaGrowth;
        _repository.SaveDividendPayment(actual);
        Assert.Empty(new DividendReconciliationService().FindActualCandidates(
            _repository.GetDividendPayments().Single(x => x.Id == planId), _repository.GetDividendPayments()));
        Assert.Throws<ArgumentException>(() => _reviews.Apply(planId, "Linked", actualId,
            "異なる税区分は紐付け不可"));
    }

    [Fact]
    public void AddingReviewHistoryToExistingDatabaseCreatesOneMigrationBackup()
    {
        var planId = Save(DividendConstants.Planned, new DateTime(2026, 10, 8), 100m);
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE DividendReviewHistory;";
            command.ExecuteNonQuery();
        }
        var before = _repository.GetPreImportBackupPaths().Count;
        new DatabaseInitializer().Initialize(_path);
        Assert.Equal(before + 1, _repository.GetPreImportBackupPaths().Count);
        Assert.Equal(planId, _repository.GetDividendPayments().Single(x => x.Id == planId).Id);
        Assert.Empty(_reviews.GetHistory(planId));
        new DatabaseInitializer().Initialize(_path);
        Assert.Equal(before + 1, _repository.GetPreImportBackupPaths().Count);
    }

    [Fact]
    public void SelectingCandidateInDividendScreenUpdatesUnmatchedCountAfterLink()
    {
        var planId = Save(DividendConstants.Planned, new DateTime(2026, 10, 10), 200m);
        var actualId = Save(DividendConstants.Actual, new DateTime(2026, 10, 12), 250m);
        DividendsViewModel? vm = null;
        vm = new DividendsViewModel(_ => { }, _ => { },
            loadReviewHistory: _reviews.GetHistory,
            applyReview: _reviews.Apply,
            afterReview: () => vm!.Update([], _repository.GetDividendPayments(), asOf: Today));
        vm.Update([], _repository.GetDividendPayments(), asOf: Today);
        Assert.True(vm.FocusPayment(planId));
        vm.SelectedActualCandidate = Assert.Single(vm.ActualCandidates);
        Assert.Equal(actualId, vm.SelectedActualCandidate.Id);
        Assert.Equal("1件", vm.UnmatchedCount);
        vm.ReviewReason = "入金明細で照合";
        vm.LinkActualCommand.Execute(null);
        Assert.Equal("0件", vm.UnmatchedCount);
        Assert.Equal("Linked", Assert.Single(_reviews.GetHistory(planId)).Action);
    }

    private int Save(string status, DateTime date, decimal net, string account = AccountTypes.Specific) =>
        _repository.SaveDividendPayment(new DividendPayment
        {
            StockId = _stockId, Ticker = "ABC", StockName = "Alpha", Broker = "SBI証券",
            AccountType = account, TaxAccountType = account, PaymentDate = date,
            DividendStatus = status, Currency = "JPY", Quantity = 10m,
            NetAmount = net, NetAmountJpy = net, JpyAmount = net,
            Source = status == DividendConstants.Actual ? DividendConstants.SourceCsv : DividendConstants.SourceManual
        });

    private TodayReviewCard TodayCard() => new TodayReviewService().Build([], _repository.GetDividendPayments(),
        [], new CsvReviewSummary(0, 0, 0), Today, dividendReviews: _reviews.GetLatestDecisions())
        .Cards.Single(x => x.Kind == TodayReviewKind.Dividend);

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
