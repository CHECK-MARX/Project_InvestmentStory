using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class MutualFundAssetSimulationServiceTests
{
    private readonly MutualFundAssetSimulationService _service = new();

    [Fact]
    public void Simulate_AllAccounts_SumsLegacyAndNewNisa()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.NisaLegacy, unitsHeld: 10000m, averageCostNav: 9000m, currentNav: 10000m),
            CreateFundPosition(2, AccountTypes.NisaAccumulation, unitsHeld: 20000m, averageCostNav: 9000m, currentNav: 10000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());

        Assert.Equal(30_000m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(27_000m, result.Summary.CurrentCostJpy);
        Assert.Equal(3_000m, result.Summary.UnrealizedGainJpy);
        Assert.Equal(1, result.Summary.FundCount);
        Assert.Equal(2, result.Summary.PositionCount);
    }

    [Fact]
    public void Simulate_AccountScope_UsesOnlySelectedAccount()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.NisaLegacy, unitsHeld: 10000m, averageCostNav: 9000m, currentNav: 10000m),
            CreateFundPosition(2, AccountTypes.NisaAccumulation, unitsHeld: 20000m, averageCostNav: 9000m, currentNav: 10000m),
            CreateFundPosition(3, AccountTypes.Specific, unitsHeld: 30000m, averageCostNav: 9000m, currentNav: 10000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.Account(AccountTypes.NisaAccumulation), CreateInput());

        Assert.Equal(20_000m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(18_000m, result.Summary.CurrentCostJpy);
        Assert.Equal(1, result.Summary.PositionCount);
    }

    [Fact]
    public void Simulate_DeduplicatesSameFundBrokerAndAccount()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.NisaAccumulation, unitsHeld: 411318m, averageCostNav: 29499m, currentNav: 40579m),
            CreateFundPosition(2, AccountTypes.NisaAccumulation, unitsHeld: 411318m, averageCostNav: 29499m, currentNav: 40579m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());

        Assert.Equal(1_669_087m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_213_347m, result.Summary.CurrentCostJpy);
        Assert.Equal(455_740m, result.Summary.UnrealizedGainJpy);
        Assert.Equal(1, result.Summary.PositionCount);
    }

    [Fact]
    public void Simulate_CalculatesMutualFundValueByTenThousandUnits()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.NisaAccumulation, unitsHeld: 411318m, averageCostNav: 29499m, currentNav: 40579m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());

        Assert.Equal(1_213_347m, result.Summary.CurrentCostJpy);
        Assert.Equal(1_669_087m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(455_740m, result.Summary.UnrealizedGainJpy);
        Assert.Equal(37.56m, result.Summary.UnrealizedGainRate, precision: 2);
    }

    [Fact]
    public void Simulate_UsesMonthlyCompoundingWithMonthEndContribution()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 12m,
            targetAmount: 10_000m,
            projectionYears: 1));

        var expectedMonthlyRate = (decimal)(Math.Pow(1.12d, 1d / 12d) - 1d);
        var expectedFirstMonth = Math.Round(1000m * (1m + expectedMonthlyRate) + 100m, 0, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedFirstMonth, result.Projections[0].MarketValueJpy);
    }

    [Fact]
    public void Simulate_AllowsZeroContribution()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 0m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 2_000m,
            projectionYears: 1));

        Assert.All(result.Projections, row => Assert.Equal(1000m, row.MarketValueJpy));
        Assert.Equal(0m, result.ContributionComparisons.Min(x => x.MonthlyContributionJpy));
    }

    [Fact]
    public void Simulate_ZeroAnnualReturnAddsOnlyContribution()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 2_200m,
            projectionYears: 1));

        Assert.Equal(2_200m, result.Projections[^1].MarketValueJpy);
        Assert.Equal(100m, result.RequiredMonthlyContributionJpy);
    }

    [Fact]
    public void Simulate_ReturnsTargetAchievementMonth()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 1_300m,
            projectionYears: 1,
            startYear: 2026,
            startMonth: 7));

        Assert.Equal(new DateTime(2026, 10, 1), result.TargetAchievementMonth);
        Assert.Equal(3, result.MonthsToTarget);
    }

    [Fact]
    public void Simulate_ActualAnnualizedReturnIsUnavailableWithoutCashFlowHistory()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1200m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());

        Assert.Null(result.Summary.ActualAnnualizedReturnRate);
    }

    [Fact]
    public void Simulate_UsesProvidedPositionsOnlyForDatabaseModeSeparation()
    {
        var normalPositions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };
        var samplePositions = new[]
        {
            CreateFundPosition(2, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 2000m, currentNav: 2000m)
        };

        var normalResult = _service.Simulate(normalPositions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());
        var sampleResult = _service.Simulate(samplePositions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());

        Assert.Equal(1000m, normalResult.Summary.CurrentMarketValueJpy);
        Assert.Equal(2000m, sampleResult.Summary.CurrentMarketValueJpy);
    }

    private static MutualFundSimulationInput CreateInput(
        decimal monthlyContribution = 0m,
        decimal expectedAnnualReturnRate = 5m,
        decimal targetAmount = 20_000_000m,
        int projectionYears = 20,
        int targetYears = 1,
        int startYear = 2026,
        int startMonth = 7) =>
        new()
        {
            MonthlyContributionJpy = monthlyContribution,
            ExpectedAnnualReturnRate = expectedAnnualReturnRate,
            TargetAmountJpy = targetAmount,
            ProjectionYears = projectionYears,
            TargetYears = targetYears,
            StartYear = startYear,
            StartMonth = startMonth
        };

    private static StockPosition CreateFundPosition(
        int stockId,
        string accountType,
        decimal unitsHeld,
        decimal averageCostNav,
        decimal currentNav,
        string broker = "SBI証券",
        string fundCode = "FUND1",
        string fundName = "SBI・V・S&P500インデックス・ファンド") =>
        new()
        {
            Stock = new Stock
            {
                Id = stockId,
                AssetType = AssetTypes.MutualFund,
                Name = fundName,
                Ticker = fundCode,
                Country = "日本",
                Currency = "JPY",
                Broker = broker,
                AccountType = accountType,
                CustodyType = accountType
            },
            MutualFund = new MutualFundHolding
            {
                StockId = stockId,
                FundName = fundName,
                FundCode = fundCode,
                UnitsHeld = unitsHeld,
                UnitBase = 10000m,
                AverageCostNav = averageCostNav,
                CurrentNav = currentNav,
                AccountType = accountType,
                NavDate = new DateTime(2026, 7, 10),
                NavSource = "CSV"
            }
        };
}
