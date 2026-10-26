using InvestmentStory.App.ViewModels;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class CsvReviewDispositionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryCsvReview_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public CsvReviewDispositionTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "test.db");
        new DatabaseInitializer().Initialize(_path);
        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CsvImportReviewHoldings
            (Broker,Account,Ticker,SecurityName,AssetType,Currency,SnapshotDate,Shares,Reason)
            VALUES ('A','特定','ABC','ABC','Stock','JPY','2026-10-01',1,'口座を確認');
            INSERT INTO CsvImportReviewDividends
            (PaymentDate,Broker,SecurityName,Product,Quantity,GrossAmount,TaxAmount,Source,Reason)
            VALUES ('2026-10-02','A','DEF','Stock',1,10,1,'csv','税額を確認');
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void HoldAndConfirmKeepCardCountAlignedWithActiveRows()
    {
        var investment = new InvestmentStoryRepository(_path);
        var disposition = new CsvReviewDispositionRepository(_path);
        var initial = investment.GetCsvReviewRecords();
        Assert.Equal(2, initial.Count);
        var holding = initial.Single(x => x.SourceKey.StartsWith("holding:"));
        var dividend = initial.Single(x => x.SourceKey.StartsWith("dividend:"));
        Assert.Throws<ArgumentException>(() => disposition.Save(holding.SourceKey, "確認済み", ""));
        disposition.Save(holding.SourceKey, "保留", "証券会社の回答待ち");
        Assert.Equal("保留", investment.GetCsvReviewRecords().Single(x => x.SourceKey == holding.SourceKey).Status);
        disposition.Save(dividend.SourceKey, "確認済み", "税額を元CSVで確認");
        var active = investment.GetCsvReviewRecords();
        Assert.Single(active);
        Assert.Equal(1, investment.GetCsvReviewSummary().HoldingHistory);
        Assert.Equal(0, investment.GetCsvReviewSummary().DividendHistory);
        Assert.Equal("確認済み", investment.GetCsvReviewRecords(includeResolved: true)
            .Single(x => x.SourceKey == dividend.SourceKey).Status);
        disposition.Save(dividend.SourceKey, "要確認", "再確認");
        Assert.Equal(2, investment.GetCsvReviewRecords().Count);
    }

    [Fact]
    public void ViewModelWritesReasonAndRevisitsResolvedHistory()
    {
        var repo = new InvestmentStoryRepository(_path);
        var disposition = new CsvReviewDispositionRepository(_path);
        var changed = 0;
        var vm = new CsvReviewViewModel(() => repo.GetCsvReviewRecords(includeResolved: true), () => { },
            disposition.Save, () => changed++);
        vm.Load();
        var holding = vm.Rows.Single(x => x.SourceKey.StartsWith("holding:"));
        vm.Selected = holding;
        vm.ReviewNote = "口座を確認済み";
        vm.ConfirmCommand.Execute(null);
        Assert.Equal(1, changed);
        Assert.Equal("確認済み", vm.Selected?.Status);
        Assert.Equal("口座を確認済み", vm.Selected?.ReviewNote);
    }

    [Fact]
    public void MigrationBacksUpExistingDatabaseBeforeAddingStateTable()
    {
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE CsvReviewDispositions;"; command.ExecuteNonQuery();
        }
        new DatabaseInitializer().Initialize(_path);
        var backups = new InvestmentStoryRepository(_path).GetPreImportBackupPaths();
        var backup = Assert.Single(backups);
        var holding = new InvestmentStoryRepository(_path).GetCsvReviewRecords()
            .Single(x => x.SourceKey.StartsWith("holding:"));
        new CsvReviewDispositionRepository(_path).Save(holding.SourceKey, "保留", "照会中");
        Assert.Contains("CSV要確認の解決・保留状態", new DatabaseRestoreService(_path)
            .GetRestoreImpact(Path.GetFileName(backup)));
        Assert.Equal(2, new InvestmentStoryRepository(_path).GetCsvReviewRecords().Count);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
