using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class SampleCsvFilesTests
{
    [Fact]
    public void SbiSampleCsvV1_IsReadableByExistingImporter()
    {
        var root = FindRepositoryRoot();
        var seedFile = Path.Combine(root, "SampleData", "Csv", "Sbi", "v1", "SBI_HOLDINGS.csv");

        var result = new SbiStatementCsvParser().ParseFromSeedFile(seedFile);

        Assert.True(result.CanUpdateHoldings);
        Assert.Contains(result.Holdings, x => x.Ticker == "7203" && x.Shares == 300m);
        Assert.Contains(result.Holdings, x => x.AssetType == AssetTypes.MutualFund && x.UnitsHeld == 411_318m);
        Assert.Equal(3, result.Dividends.Count);
    }

    [Fact]
    public void NomuraSampleCsvV1_IsReadableByExistingImporter()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "SampleData", "Csv", "Nomura", "v1", "Nomura_Holdings.csv");

        var result = new NomuraHoldingsCsvParser().Parse(file);

        Assert.True(result.CanUpdateHoldings);
        Assert.Equal(3, result.Holdings.Count);
        Assert.Contains(result.Holdings, x => x.Ticker == "9433" && x.Currency == "JPY");
        Assert.Contains(result.Holdings, x => x.Ticker == "AAPL" && x.Currency == "USD");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "SampleData")) &&
                File.Exists(Path.Combine(directory.FullName, "InvestmentStory.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
