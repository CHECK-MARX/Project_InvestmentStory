using InvestmentStory.Core.Models;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class CsvImportHistoryBackupTests
{
    [Fact]
    public void ImportCreatesConsistentPreImportBackup_AndHistoryShowsCommittedAttempt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_import_backup_{Guid.NewGuid():N}.db");
        var backupDirectory = path + ".pre_import_backups";
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.SaveCsvImportLog(new CsvImportLogEntry
            {
                BatchId = "before", FileName = "old.csv", Result = "Succeeded"
            });

            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
            {
                BatchId = "new", FileName = "new.csv", FileHash = "abc123",
                CsvType = "SBI配当CSV", RowCount = 1, ParsedRowCount = 1,
                InsertedCount = 1, Result = "Succeeded", FormatVersion = "SbiDistribution-v1"
            }));

            var backupPath = Assert.Single(repository.GetPreImportBackupPaths());
            Assert.Equal(1L, CountImportLogs(backupPath));
            Assert.Equal(2L, CountImportLogs(path));
            var recent = repository.GetRecentCsvImportLogs();
            Assert.Equal(2, recent.Count);
            Assert.Equal("new.csv", recent[0].FileName);
            Assert.Equal("new", recent[0].BatchId);
            Assert.Equal("abc123", recent[0].FileHash);
            Assert.Equal(1, recent[0].ParsedRowCount);
            Assert.Equal("SbiDistribution-v1", recent[0].FormatVersion);
            Assert.NotEqual(default, recent[0].ImportedAtUtc);
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                foreach (var file in Directory.GetFiles(backupDirectory)) File.Delete(file);
                Directory.Delete(backupDirectory);
            }
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FailedImportKeepsOriginalDataAndAValidPreImportBackup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_failed_backup_{Guid.NewGuid():N}.db");
        var backupDirectory = path + ".pre_import_backups";
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            Assert.Throws<InvalidOperationException>(() => repository.RunImportAtomically(() =>
            {
                repository.SaveCsvImportLog(new CsvImportLogEntry
                {
                    BatchId = "rolled-back", FileName = "failure.csv", Result = "Succeeded"
                });
                throw new InvalidOperationException("simulated failure");
            }));
            Assert.Equal(0L, CountImportLogs(path));
            Assert.Equal(0L, CountImportLogs(Assert.Single(repository.GetPreImportBackupPaths())));
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                foreach (var file in Directory.GetFiles(backupDirectory)) File.Delete(file);
                Directory.Delete(backupDirectory);
            }
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static long CountImportLogs(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM CsvImportLogs;";
        return (long)command.ExecuteScalar()!;
    }
}
