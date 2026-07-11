using InvestmentStory.Core.Models;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class SampleDataSeederTests
{
    [Fact]
    public void ResetSampleSessionDatabase_CreatesSeparatedSampleDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var normalPath = Path.Combine(directory, "normal.db");
        var samplePath = Path.Combine(directory, "sample.db");

        try
        {
            var normalRepository = new InvestmentStoryRepository(normalPath);
            normalRepository.Initialize();
            normalRepository.SavePosition(CreateRealOnlyPosition());
            var normalBefore = normalRepository.GetPositions().Count;

            var sampleRepository = SampleDataSeeder.ResetSampleSessionDatabase(samplePath, normalRepository.GetSettings());
            var samplePositions = sampleRepository.GetPositions();
            var sampleSettings = sampleRepository.GetSettings();

            Assert.True(File.Exists(samplePath));
            Assert.Equal(DataDisplayModes.Sample, sampleSettings.DataDisplayMode);
            Assert.Equal("Mock", sampleSettings.MarketDataMode);
            Assert.Equal("Mock", sampleSettings.ExchangeRateProvider);
            Assert.Contains(samplePositions, x => x.Stock.Ticker == "7203");
            Assert.Contains(samplePositions, x => x.Stock.AssetType == AssetTypes.MutualFund);
            Assert.DoesNotContain(samplePositions, x => x.Stock.Ticker == "REALONLY");
            Assert.Equal(normalBefore, normalRepository.GetPositions().Count);
            Assert.Contains(normalRepository.GetPositions(), x => x.Stock.Ticker == "REALONLY");
            Assert.DoesNotContain(normalRepository.GetPositions(), x => x.Stock.Ticker == "7203");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static StockPosition CreateRealOnlyPosition()
    {
        return new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.Stock,
                Ticker = "REALONLY",
                Name = "Real Only Test",
                Country = "Japan",
                Currency = "JPY",
                Broker = "TestBroker",
                AccountType = AccountTypes.Specific,
                CustodyType = AccountTypes.Specific,
                DataSource = "UnitTest"
            },
            Purchase = new Purchase
            {
                Shares = 1m,
                UnitPrice = 100m,
                ExchangeRate = 1m
            },
            Split = new StockSplit
            {
                SplitRatio = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 1m,
                CurrentPrice = 100m,
                CurrentExchangeRate = 1m
            }
        };
    }
}
