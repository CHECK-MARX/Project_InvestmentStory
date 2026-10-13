using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class StockSectorClassifierTests
{
    [Theory]
    [InlineData("8151", "国内株式", "卸売・商社")]
    [InlineData("8593", "株式", "金融")]
    [InlineData("NVDA", "外株", "情報技術")]
    [InlineData("CMBT", "", "海運")]
    [InlineData("TRMD", "", "海運")]
    [InlineData("EURN", "外株", "海運")]
    public void Classify_ReplacesBrokerProductTypesWithBusinessFields(string ticker, string product, string expected)
    {
        var stock = new Stock { Ticker = ticker, Sector = product };

        StockSectorClassifier.FillMissingClassification(stock);

        Assert.Equal(expected, stock.Sector);
        Assert.NotEmpty(stock.Industry);
    }

    [Fact]
    public void Classify_PreservesSpecificUserSectorAndSeparatesFunds()
    {
        var manual = new Stock { Ticker = "NVDA", Sector = "独自分類" };
        StockSectorClassifier.FillMissingClassification(manual);
        Assert.Equal("独自分類", manual.Sector);

        var fund = new Stock { Ticker = "FUND:S&P500", AssetType = AssetTypes.MutualFund,
            Sector = "投資信託" };
        Assert.Equal("投資信託", StockSectorClassifier.Classify(fund).Sector);
        Assert.Equal("未分類", StockSectorClassifier.Classify(new Stock { Ticker = "UNKNOWN", Sector = "外株" }).Sector);
    }

    [Fact]
    public void DashboardComposition_GroupsAllHoldingsByFieldWithoutLosingValue()
    {
        var snapshots = new[]
        {
            Snapshot("8151", "東陽テクニカ", "国内株式", "日本", 600m),
            Snapshot("NVDA", "エヌビディア", "外株", "米国", 250m),
            Snapshot("NVDA", "エヌビディア", "", "米国", 100m),
            Snapshot("FUND:S&P500", "S&P500ファンド", "投資信託", "日本", 50m, AssetTypes.MutualFund)
        };
        var viewModel = new DashboardViewModel();

        viewModel.Update(new DashboardSummary(), snapshots, Array.Empty<PortfolioSnapshot>());

        Assert.Equal("分野別", viewModel.SelectedCompositionMode);
        Assert.Equal(1000m, viewModel.CompositionCategories.Sum(x => x.Amount));
        Assert.InRange(viewModel.CompositionCategories.Sum(x => x.SharePercent), 99.99, 100.01);
        Assert.Equal(3, viewModel.CompositionCategories.Count);
        var technology = Assert.Single(viewModel.CompositionCategories, x => x.Label == "情報技術");
        Assert.Equal(350m, technology.Amount);
        Assert.Single(technology.Holdings);
        Assert.Contains(viewModel.CompositionCategories, x => x.Label == "投資信託");

        viewModel.SelectedCompositionMode = "国別";
        Assert.Equal(2, viewModel.CompositionCategories.Count);
        Assert.Equal(1000m, viewModel.CompositionCategories.Sum(x => x.Amount));
    }

    private static StockSnapshot Snapshot(string ticker, string name, string sector, string country,
        decimal value, string assetType = AssetTypes.Stock) => new()
        {
            Position = new StockPosition { Stock = new Stock
                { Ticker = ticker, Name = name, Sector = sector, Country = country, AssetType = assetType } },
            CurrentMarketValueJpy = value
        };
}
