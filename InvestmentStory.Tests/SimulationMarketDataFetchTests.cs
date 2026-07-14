using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class SimulationMarketDataFetchTests
{
    [Fact]
    public async Task NewDividendStockFetch_NormalizesPgAndAppliesMarketDataToClickedRow()
    {
        var marketData = new RecordingMarketDataService(symbol => MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = symbol,
            Name = "The Procter & Gamble Company",
            Country = "米国",
            Currency = "USD",
            CurrentPrice = 150m,
            AnnualDividendPerShare = 4m,
            UsdJpyRate = 160m,
            Source = "Yahoo Finance",
            DividendInfoSource = "Yahoo Finance"
        }));
        var viewModel = CreateViewModel(marketData);
        var row = AddNewRow(viewModel);
        row.Ticker = "pg";
        row.PlannedAdditionalShares = 10m;

        var changed = new List<string>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        viewModel.FetchNewDividendStockCommand.Execute(row);
        await WaitForFetch(row);

        Assert.Equal("PG", row.Ticker);
        Assert.Equal("The Procter & Gamble Company", row.Name);
        Assert.Equal(150m, row.CurrentPrice);
        Assert.Equal("USD", row.Currency);
        Assert.Equal("United States", row.Country);
        Assert.Equal(4m, row.AnnualDividendPerShare);
        Assert.Equal(MarketDataFetchStatusKinds.Success, row.MarketDataFetchState);
        Assert.StartsWith("取得成功", row.MarketDataFetchStatus);
        Assert.Equal("PG", marketData.RequestedSymbols.Single());
        Assert.Contains(nameof(row.Name), changed);
        Assert.Contains(nameof(row.CurrentPrice), changed);
        Assert.Contains(nameof(row.AnnualDividendPerShare), changed);
        Assert.Contains(nameof(row.PlannedInvestmentPreview), changed);
        Assert.Contains(nameof(row.AdditionalAnnualDividendPreview), changed);
        Assert.Contains("240,000", row.PlannedInvestmentPreview);
        Assert.Contains("6,400", row.AdditionalAnnualDividendPreview);
        Assert.Contains("240,000", viewModel.PassiveIncomeNewPlannedInvestment);
    }

    [Fact]
    public async Task NewDividendStockFetch_UsesTokyoSuffixForJapaneseCodeAndAppliesJpy()
    {
        var marketData = new RecordingMarketDataService(symbol => MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = "5020",
            Name = "ENEOS Holdings, Inc.",
            Country = "日本",
            Currency = "JPY",
            CurrentPrice = 500m,
            AnnualDividendPerShare = 22m,
            Source = "Yahoo Finance",
            DividendInfoSource = "Yahoo Finance"
        }));
        var viewModel = CreateViewModel(marketData);
        var row = AddNewRow(viewModel);
        row.Ticker = "5020";
        row.PlannedAdditionalShares = 100m;

        viewModel.FetchNewDividendStockCommand.Execute(row);
        await WaitForFetch(row);

        Assert.Equal("5020", row.Ticker);
        Assert.Equal("5020.T", marketData.RequestedSymbols.Single());
        Assert.Equal("ENEOS Holdings, Inc.", row.Name);
        Assert.Equal("JPY", row.Currency);
        Assert.Equal("Japan", row.Country);
        Assert.Equal(500m, row.CurrentPrice);
        Assert.Equal(22m, row.AnnualDividendPerShare);
        Assert.Equal(MarketDataFetchStatusKinds.Success, row.MarketDataFetchState);
        Assert.Contains("50,000", row.PlannedInvestmentPreview);
        Assert.Contains("2,200", row.AdditionalAnnualDividendPreview);
    }

    [Fact]
    public async Task NewDividendStockFetch_DoesNotTreatEmptyNameAndZeroPriceAsSuccess()
    {
        var viewModel = CreateViewModel(new RecordingMarketDataService(symbol => MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = symbol,
            Name = string.Empty,
            Country = "米国",
            Currency = "USD",
            CurrentPrice = 0m,
            AnnualDividendPerShare = 0m,
            Source = "Yahoo Finance"
        })));
        var row = AddNewRow(viewModel);
        row.Ticker = "BAD";
        row.PlannedAdditionalShares = 10m;

        viewModel.FetchNewDividendStockCommand.Execute(row);
        await WaitForFetch(row);

        Assert.Equal(MarketDataFetchStatusKinds.Failed, row.MarketDataFetchState);
        Assert.StartsWith("取得失敗", row.MarketDataFetchStatus);
        Assert.Equal(string.Empty, row.Name);
        Assert.Equal(0m, row.CurrentPrice);
        Assert.Contains("0", viewModel.PassiveIncomeNewPlannedInvestment);
    }

    [Fact]
    public async Task NewDividendStockFetch_AcceptsZeroDividendWhenNameAndPriceAreValid()
    {
        var viewModel = CreateViewModel(new RecordingMarketDataService(symbol => MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = symbol,
            Name = "No Dividend Inc.",
            Country = "米国",
            Currency = "USD",
            CurrentPrice = 25m,
            AnnualDividendPerShare = 0m,
            UsdJpyRate = 160m,
            Source = "Yahoo Finance",
            DividendInfoSource = "Yahoo Finance"
        })));
        var row = AddNewRow(viewModel);
        row.Ticker = "NODIV";

        viewModel.FetchNewDividendStockCommand.Execute(row);
        await WaitForFetch(row);

        Assert.Equal(MarketDataFetchStatusKinds.Success, row.MarketDataFetchState);
        Assert.Equal(0m, row.AnnualDividendPerShare);
        Assert.StartsWith("取得成功", row.MarketDataFetchStatus);
    }

    [Fact]
    public async Task NewDividendStockFetch_AcceptsSpcxWhenChartReturnsPriceAndCurrency()
    {
        var viewModel = CreateViewModel(new RecordingMarketDataService(symbol => MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = "SPCX",
            Name = "Space Exploration Technologies Corp.",
            Country = "米国",
            Currency = "USD",
            CurrentPrice = 139m,
            AnnualDividendPerShare = 0m,
            Source = "Yahoo Finance"
        })));
        var row = AddNewRow(viewModel);
        row.Ticker = "SPCX";

        viewModel.FetchNewDividendStockCommand.Execute(row);
        await WaitForFetch(row);

        Assert.Equal("SPCX", row.Ticker);
        Assert.Equal("Space Exploration Technologies Corp.", row.Name);
        Assert.Equal(139m, row.CurrentPrice);
        Assert.Equal("USD", row.Currency);
        Assert.Equal(MarketDataFetchStatusKinds.Success, row.MarketDataFetchState);
        Assert.StartsWith("取得成功", row.MarketDataFetchStatus);
    }

    [Fact]
    public async Task NewDividendStockFetch_DoesNotApplyResultToAnotherRowAndSurvivesRebuild()
    {
        var viewModel = CreateViewModel(new RecordingMarketDataService(symbol => MarketDataResult.Success(new MarketDataQuote
        {
            Symbol = symbol,
            Name = "The Procter & Gamble Company",
            Country = "米国",
            Currency = "USD",
            CurrentPrice = 150m,
            AnnualDividendPerShare = 4m,
            UsdJpyRate = 160m,
            Source = "Yahoo Finance",
            DividendInfoSource = "Yahoo Finance"
        })));
        var first = AddNewRow(viewModel);
        first.Ticker = "PG";
        var second = AddNewRow(viewModel);
        second.Ticker = "KO";

        viewModel.FetchNewDividendStockCommand.Execute(first);
        await WaitForFetch(first);

        Assert.Equal("The Procter & Gamble Company", first.Name);
        Assert.Equal(string.Empty, second.Name);

        viewModel.UpdateDividendPortfolio(Array.Empty<StockPosition>(), Array.Empty<TaxProfile>());
        var restored = viewModel.NewDividendSimulationRows.Single(row => row.Ticker == "PG");
        Assert.Equal("The Procter & Gamble Company", restored.Name);
        Assert.Equal(150m, restored.CurrentPrice);
    }

    [Theory]
    [InlineData("pg", "PG")]
    [InlineData("goog", "GOOG")]
    [InlineData("zm", "ZM")]
    [InlineData("spcx", "SPCX")]
    [InlineData("2914", "2914.T")]
    [InlineData("5020", "5020.T")]
    public void MarketDataSymbolResolver_NormalizesProviderSymbols(string input, string expected)
    {
        Assert.Equal(expected, MarketDataSymbolResolver.ToProviderSymbol(MarketDataSymbolResolver.NormalizeInput(input)));
    }

    private static SimulationViewModel CreateViewModel(IMarketDataService marketDataService) =>
        new(
            new InvestmentCalculator(),
            new MutualFundAssetSimulationService(),
            () => new AppSettings
            {
                MarketDataMode = "Web/API",
                UsMarketDataProvider = "Yahoo Finance",
                JapanMarketDataProvider = "Yahoo Finance"
            },
            _ => { },
            marketDataService,
            new EmptyStockLookupService());

    private static DividendSimulationRowViewModel AddNewRow(SimulationViewModel viewModel)
    {
        viewModel.AddNewDividendStockCommand.Execute(null);
        return viewModel.NewDividendSimulationRows.Last();
    }

    private static async Task WaitForFetch(DividendSimulationRowViewModel row)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (!row.IsMarketDataFetching && !row.MarketDataFetchStatus.Equals("取得中", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Market data fetch did not complete.");
    }

    private sealed class EmptyStockLookupService : IStockLookupService
    {
        public StockLookupResult? Find(string query) => null;
    }

    private sealed class RecordingMarketDataService : IMarketDataService
    {
        private readonly Func<string, MarketDataResult> _getQuote;

        public RecordingMarketDataService(Func<string, MarketDataResult> getQuote)
        {
            _getQuote = getQuote;
        }

        public List<string> RequestedSymbols { get; } = new();

        public MarketDataResult GetQuote(string symbol, AppSettings settings)
        {
            RequestedSymbols.Add(symbol);
            return _getQuote(symbol);
        }
    }
}
