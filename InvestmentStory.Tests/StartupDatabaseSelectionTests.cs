using InvestmentStory.App;

namespace InvestmentStory.Tests;

public sealed class StartupDatabaseSelectionTests
{
    [Fact]
    public void ResolveIsolatedPath_RequiresExistingNonDefaultDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_isolated_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var normal = Path.Combine(directory, "normal.db");
            var isolated = Path.Combine(directory, "isolated.db");
            File.WriteAllBytes(normal, []);
            File.WriteAllBytes(isolated, []);

            Assert.Null(StartupDatabaseSelection.ResolveIsolatedPath([], normal));
            Assert.Equal(Path.GetFullPath(isolated),
                StartupDatabaseSelection.ResolveIsolatedPath(["--isolated-db", isolated], normal));
            Assert.Throws<ArgumentException>(() =>
                StartupDatabaseSelection.ResolveIsolatedPath(["--isolated-db", normal], normal));
            Assert.Throws<ArgumentException>(() =>
                StartupDatabaseSelection.ResolveIsolatedPath(["--isolated-db"], normal));
            Assert.Throws<FileNotFoundException>(() =>
                StartupDatabaseSelection.ResolveIsolatedPath(["--isolated-db", Path.Combine(directory, "missing.db")], normal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
