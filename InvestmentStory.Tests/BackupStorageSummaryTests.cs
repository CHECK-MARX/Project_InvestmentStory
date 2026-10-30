using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class BackupStorageSummaryTests
{
    [Fact]
    public async Task SummaryShowsUnverifiedGenerations_AndRefreshesAfterRestoreDrill()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryBackupSummary_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var databasePath = Path.Combine(directory, "portfolio.db");
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            repository.RunImportAtomically(() => repository.SaveCsvImportLog(new CsvImportLogEntry
            {
                BatchId = "summary-test", FileName = "test.csv", Result = "Succeeded"
            }));

            var restoreService = new DatabaseRestoreService(databasePath);
            var viewModel = new SettingsViewModel(
                () => new AppSettings(), _ => { }, () => [], restoreService: restoreService);

            Assert.Contains("1世代", viewModel.BackupStorageSummary);
            Assert.Contains("復元演習未実施 1世代", viewModel.BackupStorageSummary);
            Assert.Contains("自動削除はしません", viewModel.BackupStorageSummary);
            Assert.Contains("暗号化と別媒体への複製はアプリでは保証しません", viewModel.BackupStorageSummary);

            viewModel.SelectedBackup = Assert.Single(viewModel.Backups);
            await viewModel.RunSelectedBackupDrillAsync();

            Assert.Contains("復元演習未実施 0世代", viewModel.BackupStorageSummary);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                File.Delete(file);
            foreach (var child in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
                Directory.Delete(child, recursive: false);
            Directory.Delete(directory);
        }
    }
}
