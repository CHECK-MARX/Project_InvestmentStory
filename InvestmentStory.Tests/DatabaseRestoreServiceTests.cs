using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace InvestmentStory.Tests;

public sealed class DatabaseRestoreServiceTests
{
    [Fact]
    public void RestoreDrill_VerifiesCopiedTotals_RecordsResult_AndLeavesLiveDatabaseUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var stockId = repository.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "TEST", Name = "Test", Broker = "SBI", Currency = "JPY" },
                Purchase = new Purchase
                    { PurchaseDate = new DateTime(2026, 1, 1), Shares = 10m, UnitPrice = 100m,
                      ExchangeRate = 1m, AcquisitionAmountJpy = 1000m },
                CurrentHolding = new CurrentHolding
                    { CurrentShares = 10m, CurrentPrice = 120m, CurrentExchangeRate = 1m }
            });
            repository.SaveDividendPayment(new DividendPayment
            {
                StockId = stockId, Broker = "SBI", PaymentDate = new DateTime(2026, 9, 1),
                Currency = "JPY", NetAmount = 50m, NetAmountJpy = 50m,
                DividendStatus = DividendConstants.Actual
            });
            repository.SavePosition(new StockPosition
            {
                Stock = new Stock
                    { AssetType = AssetTypes.MutualFund, Ticker = "FUND:TEST", Name = "Test Fund",
                      Broker = "SBI", Currency = "JPY" },
                Purchase = new Purchase { Shares = 10_000m, UnitPrice = 2_000m, ExchangeRate = 1m },
                CurrentHolding = new CurrentHolding
                    { CurrentShares = 10_000m, CurrentPrice = 2_500m, CurrentExchangeRate = 1m },
                MutualFund = new MutualFundHolding
                    { FundName = "Test Fund", UnitsHeld = 10_000m, UnitBase = 10_000m,
                      AverageCostNav = 2_000m, CurrentNav = 2_500m,
                      AcquisitionAmount = 2_000m, MarketValue = 2_500m }
            });
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "later", FileName = "later.csv", Result = "Succeeded" }));
            var service = new DatabaseRestoreService(path);
            var backup = Assert.Single(service.GetBackups());
            Assert.Equal("未実施", backup.LastDrillDisplay);

            var drill = service.RunRestoreDrill(backup.FileName);

            Assert.Equal(2, drill.StockCount);
            Assert.Equal(2, drill.HoldingCount);
            Assert.Equal(1, drill.DividendCount);
            Assert.Equal(10_010m, drill.HoldingShares);
            Assert.Equal(3_000m, drill.AcquisitionAmountJpy);
            Assert.Equal(3_700m, drill.MarketValueJpy);
            Assert.Equal(50m, drill.NetDividendJpy);
            Assert.NotEqual("未実施", Assert.Single(service.GetBackups()).LastDrillDisplay);
            var drillRecordPath = Assert.Single(repository.GetPreImportBackupPaths()) + ".drill.json";
            var drillRecord = JsonNode.Parse(File.ReadAllText(drillRecordPath))!;
            drillRecord["BackupSha256"] = new string('0', 64);
            File.WriteAllText(drillRecordPath, drillRecord.ToJsonString());
            Assert.Equal("未実施", Assert.Single(service.GetBackups()).LastDrillDisplay);
            Assert.Single(repository.GetRecentCsvImportLogs());
            Assert.Equal(2, repository.GetPositions().Count);
            Assert.Single(repository.GetDividendPayments());
            Assert.Null(service.ApplyPendingRestore());
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public void RestoringPreImportBackup_ArchivesCurrentDatabaseAndRestoresOldRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.SaveCsvImportLog(new CsvImportLogEntry { BatchId = "old", FileName = "old.csv", Result = "Succeeded" });
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "new", FileName = "new.csv", Result = "Succeeded" }));

            var service = new DatabaseRestoreService(path);
            var backup = Assert.Single(service.GetBackups());
            Assert.Equal("正常", backup.Status);
            Assert.Equal(1, backup.ImportLogCount);
            Assert.Contains("現在 2 件", service.GetRestoreImpact(backup.FileName));
            Assert.Contains("内容が異なる取込記録: 1件", service.GetRestoreImpact(backup.FileName));
            Assert.Contains("new.csv", service.GetRestoreImpact(backup.FileName));
            service.ScheduleRestore(backup.FileName);
            var archive = service.ApplyPendingRestore();

            Assert.NotNull(archive);
            Assert.True(File.Exists(archive));
            Assert.Single(repository.GetRecentCsvImportLogs());
            Assert.Equal("old.csv", repository.GetRecentCsvImportLogs()[0].FileName);
            Assert.Equal(2, new InvestmentStoryRepository(archive).GetRecentCsvImportLogs().Count);
            Assert.Null(service.ApplyPendingRestore());
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public void CorruptBackup_IsRejectedBeforeSchedulingAndDoesNotChangeCurrentDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "new", FileName = "new.csv", Result = "Succeeded" }));
            var backup = Assert.Single(repository.GetPreImportBackupPaths());
            File.WriteAllText(backup, "not SQLite");

            var service = new DatabaseRestoreService(path);
            Assert.StartsWith("復元不可", Assert.Single(service.GetBackups()).Status);
            Assert.Throws<InvalidDataException>(() => service.ScheduleRestore(Path.GetFileName(backup)));
            Assert.Single(repository.GetRecentCsvImportLogs());
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public void RestoreImpact_ListsChangedManualRowsAndSavedPlansBeforeScheduling()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            Execute(path, """
                INSERT INTO Stocks(Name, Ticker, Country, Currency, Broker)
                VALUES ('Before', 'TEST', 'JP', 'JPY', 'Manual');
                INSERT INTO AppSettings(Key, Value) VALUES ('test-setting', 'before');
                """);
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "import-after-backup", FileName = "new.csv", Result = "Succeeded" }));
            Execute(path, """
                UPDATE Stocks SET Name = 'After', UpdatedAt = '2026-10-18 12:34:56' WHERE Ticker = 'TEST';
                UPDATE AppSettings SET Value = 'after' WHERE Key = 'test-setting';
                INSERT INTO DividendPurchasePlans(Name, TargetYear, PlannedPurchaseDate)
                VALUES ('New plan', 2027, '2027-01-01');
                """);

            var service = new DatabaseRestoreService(path);
            var backup = Assert.Single(service.GetBackups());
            var impact = service.GetRestoreImpact(backup.FileName);
            Assert.Contains("銘柄 1件", impact);
            Assert.Contains("設定 1件", impact);
            Assert.Contains("購入計画 1件", impact);
            Assert.Contains("new.csv", impact);
            Assert.Contains("記録日時:", impact);
            Assert.Contains("対象: TEST", impact);
            Assert.Contains("対象: New plan", impact);
            Assert.Contains("編集日時は特定できません", impact);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public void RestoreImpact_DetectsChangedImportEvenWhenIdIsReused()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "old", FileName = "old.csv", Result = "Succeeded" });
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "temporary", FileName = "temporary.csv", Result = "Succeeded" }));
            Execute(path, """
                DELETE FROM CsvImportLogs WHERE FileName = 'temporary.csv';
                UPDATE CsvImportLogs SET FileName = 'replacement.csv',
                    ImportedAt = '2026-10-18 12:34:56' WHERE Id = 1;
                """);

            var service = new DatabaseRestoreService(path);
            var backup = Assert.Single(service.GetBackups());
            var impact = service.GetRestoreImpact(backup.FileName);
            Assert.Contains("内容が異なる取込記録: 1件", impact);
            Assert.Contains("replacement.csv", impact);
            Assert.Contains("2026/10/18", impact);
            Assert.DoesNotContain("temporary.csv", impact);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public void BackupWithMismatchedRecordedHash_IsRejectedBeforeRestore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "new", FileName = "new.csv", Result = "Succeeded" }));
            var backupPath = Assert.Single(repository.GetPreImportBackupPaths());
            var metadata = JsonNode.Parse(File.ReadAllText(backupPath + ".json"))!;
            metadata["Sha256"] = new string('0', 64);
            File.WriteAllText(backupPath + ".json", metadata.ToJsonString());

            var service = new DatabaseRestoreService(path);
            Assert.Contains("ハッシュ", Assert.Single(service.GetBackups()).Status);
            Assert.Throws<InvalidDataException>(() => service.ScheduleRestore(Path.GetFileName(backupPath)));
            Assert.Single(repository.GetRecentCsvImportLogs());
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [Fact]
    public void ScheduledBackupChangedBeforeRestart_IsNotAppliedAndPendingRequestIsCleared()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
                { BatchId = "new", FileName = "new.csv", Result = "Succeeded" }));
            var backupPath = Assert.Single(repository.GetPreImportBackupPaths());
            var service = new DatabaseRestoreService(path);
            service.ScheduleRestore(Path.GetFileName(backupPath));
            var metadata = JsonNode.Parse(File.ReadAllText(backupPath + ".json"))!;
            metadata["Sha256"] = new string('0', 64);
            File.WriteAllText(backupPath + ".json", metadata.ToJsonString());

            Assert.Throws<InvalidDataException>(() => service.ApplyPendingRestore());
            Assert.Single(repository.GetRecentCsvImportLogs());
            Assert.Null(service.ApplyPendingRestore());
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DeleteTestDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var tempPath = Path.GetFullPath(Path.GetTempPath());
        if (!fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith("InvestmentStoryRestore_", StringComparison.Ordinal))
            throw new InvalidOperationException("テスト用一時フォルダー以外を削除できません。");
        Directory.Delete(fullPath, true);
    }
}
