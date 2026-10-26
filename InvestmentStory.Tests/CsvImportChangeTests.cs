using InvestmentStory.Core.Models;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class CsvImportChangeTests
{
    [Fact]
    public void LegacyChangeRows_AreBackedUpAndRetainedDuringDetailMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryChanges_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "portfolio.db");
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE CsvImportChangeRows (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, BatchId TEXT NOT NULL,
                        Source TEXT NOT NULL DEFAULT '', Broker TEXT NOT NULL DEFAULT '',
                        Account TEXT NOT NULL DEFAULT '', Ticker TEXT NOT NULL DEFAULT '',
                        Action TEXT NOT NULL DEFAULT '', BeforeQuantity REAL NULL,
                        AfterQuantity REAL NULL, BeforeExactCostJpy REAL NULL,
                        AfterExactCostJpy REAL NULL, BeforeReferenceCostJpy REAL NULL,
                        AfterReferenceCostJpy REAL NULL, Reason TEXT NOT NULL DEFAULT '');
                    INSERT INTO CsvImportChangeRows (BatchId, Ticker, Action, AfterQuantity)
                    VALUES ('legacy', 'KO', '更新', 10);
                    """;
                command.ExecuteNonQuery();
            }

            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();

            var old = Assert.Single(repository.GetCsvImportChanges("legacy"));
            Assert.Equal("保有", old.RecordKind);
            Assert.Equal(10m, old.AfterQuantity);
            Assert.Single(Directory.GetFiles(directory, "*before_csv_change_details*.db"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ChangeRowsAreLinkedToBatch_AndRollBackWithFailedImport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryChanges_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new InvestmentStoryRepository(Path.Combine(directory, "portfolio.db"));
            repository.Initialize();
            repository.RunImportAtomically(() => repository.SaveCsvImportChange(new CsvImportChangeEntry
            {
                BatchId = "kept", Broker = "SBI証券", Ticker = "CMBT", Action = "更新",
                BeforeQuantity = 20m, AfterQuantity = 40m, BeforeExactCostJpy = null,
                AfterReferenceCostJpy = 23262.267m, Reason = "CSV残高"
            }));
            var change = Assert.Single(repository.GetCsvImportChanges("kept"));
            Assert.Equal(20m, change.BeforeQuantity);
            Assert.Equal(40m, change.AfterQuantity);
            Assert.Contains("概算", change.AfterDisplay);

            Assert.Throws<InvalidOperationException>(() => repository.RunImportAtomically(() =>
            {
                repository.SaveCsvImportChange(new CsvImportChangeEntry { BatchId = "rolled-back" });
                throw new InvalidOperationException("fail");
            }));
            Assert.Empty(repository.GetCsvImportChanges("rolled-back"));
            Assert.Single(repository.GetCsvImportChanges("kept"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
