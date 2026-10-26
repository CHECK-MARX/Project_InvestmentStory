using InvestmentStory.App.Services;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Tests;

public sealed class CsvFolderMonitorTests
{
    [Fact]
    public async Task InboxRequiresExplicitPreviewSelectionAndNeverImportsOnDetection()
    {
        var folder = Path.Combine(Path.GetTempPath(), "InvestmentStory_csv_inbox_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var settings = new AppSettings { CsvWatchFolder = folder };
            string? opened = null;
            var vm = new BrokerIntegrationViewModel(() => settings, value => settings = value,
                () => throw new Xunit.Sdk.XunitException("Detection must not start an import."),
                path => opened = path);
            vm.Update([], settings);
            var csv = Path.Combine(folder, "new.csv");
            await File.WriteAllTextAsync(csv, "date,amount\n2026-10-25,123\n");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (vm.PendingCsvFiles.Count == 0)
            {
                await Task.Delay(100, timeout.Token);
            }
            Assert.Null(opened);
            vm.SelectedInboxFile = Assert.Single(vm.PendingCsvFiles);
            vm.OpenInboxCsvCommand.Execute(null);
            Assert.Equal(csv, opened);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task DetectsOnlyNewStableCsvAndDoesNotTouchExistingFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "InvestmentStory_csv_watch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var existing = Path.Combine(folder, "existing.csv");
            await File.WriteAllTextAsync(existing, "old");
            var detected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new CsvFolderMonitor(folder);
            watcher.NewCsvDetected += path => detected.TrySetResult(path);
            await File.WriteAllTextAsync(Path.Combine(folder, "ignore.txt"), "other");
            await File.WriteAllTextAsync(Path.Combine(folder, "new.csv"), "date,amount\n2026-10-24,123\n");
            var result = await detected.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(Path.Combine(folder, "new.csv"), result);
            Assert.Equal("old", await File.ReadAllTextAsync(existing));
        }
        finally { Directory.Delete(folder, true); }
    }
}
