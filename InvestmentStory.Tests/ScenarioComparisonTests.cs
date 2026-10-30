using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class ScenarioComparisonTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryScenarios_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ScenarioComparisonTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "test.db");
        new DatabaseInitializer().Initialize(_path);
    }

    private static StockSnapshot Holding(string ticker, string currency, decimal value, decimal rate, decimal dividendNative,
        decimal dividendJpy, bool known = true) => new()
    {
        Position = new StockPosition
        {
            Stock = new Stock { Ticker = ticker, Name = ticker, Currency = currency },
            CurrentHolding = new CurrentHolding { CurrentShares = 1m, CurrentExchangeRate = rate }
        },
        CurrentMarketValueJpy = value,
        HasKnownCurrentMarketValueJpy = known,
        AnnualDividendForecast = dividendNative,
        AnnualDividendForecastJpy = dividendJpy
    };

    [Fact]
    public void ContributionDividendAndFxLeversChangeOnlyScenarioResults()
    {
        var holdings = new[] { Holding("USD", "USD", 15000m, 150m, 10m, 1500m),
            Holding("JPY", "JPY", 5000m, 1m, 500m, 500m) };
        var service = new ScenarioComparisonService();
        var baseline = service.CaptureBaseline(holdings, new DateTime(2026, 10, 29));
        var inputs = new ScenarioInputs("ストレス", 100m, 0m, 50m, -10m, 1);
        var result = service.Calculate(baseline, inputs, holdings);
        Assert.Equal(19000m, result.FxAdjustedStartingValueJpy);
        Assert.Equal(20200m, result.ProjectedPortfolioValueJpy);
        Assert.Equal(950m, result.GrossAnnualDividendForecastJpy);
        Assert.Equal(20000m, result.ReferenceProjectedValueJpy);
        Assert.Equal(200m, result.DifferenceFromReferenceJpy);
        Assert.Equal(20000m, holdings.Sum(x => x.CurrentMarketValueJpy));
        Assert.Equal(2000m, baseline.GrossAnnualDividendForecastJpy);
    }

    [Fact]
    public void UnknownValuationAndMissingFxBlockUnsupportedProjection()
    {
        var service = new ScenarioComparisonService();
        var unknown = new[] { Holding("X", "JPY", 0m, 1m, 0m, 0m, known: false) };
        Assert.Throws<InvalidOperationException>(() => service.Calculate(service.CaptureBaseline(unknown, DateTime.Today),
            new ScenarioInputs("A", 0m, 0m, 0m, 0m, 1), unknown));
        var missingFx = new[] { Holding("X", "USD", 100m, 0m, 0m, 0m) };
        Assert.Throws<InvalidOperationException>(() => service.Calculate(service.CaptureBaseline(missingFx, DateTime.Today),
            new ScenarioInputs("A", 0m, 0m, 0m, 1m, 1), missingFx));
    }

    [Fact]
    public void SavesImmutableScenariosWithoutChangingPositionsAndComparesEqualBaselines()
    {
        var repo = new InvestmentStoryRepository(_path);
        var scenarioRepo = new ScenarioComparisonRepository(_path);
        var snapshots = new[] { Holding("X", "JPY", 1000m, 1m, 100m, 100m) };
        var before = repo.GetPositions().Count;
        var vm = new ScenarioComparisonViewModel(() => scenarioRepo);
        vm.Load(snapshots);
        vm.MonthlyContribution = "100"; vm.Years = "1";
        vm.CalculateCommand.Execute(null);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        vm.Name = "配当減"; vm.MonthlyContribution = "0"; vm.DividendReduction = "50";
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.CalculateCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.Equal(2, scenarioRepo.GetHistory().Count);
        Assert.Equal(before, repo.GetPositions().Count);
        vm.SelectedA = vm.History[0]; vm.SelectedB = vm.History[1];
        Assert.Contains("A − B", vm.Comparison);
        Assert.Equal(50m, scenarioRepo.GetHistory()[0].Outcome.GrossAnnualDividendForecastJpy);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
