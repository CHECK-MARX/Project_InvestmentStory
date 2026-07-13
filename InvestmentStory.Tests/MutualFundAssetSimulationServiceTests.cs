using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class MutualFundAssetSimulationServiceTests
{
    private readonly MutualFundAssetSimulationService _service = new();

    [Fact]
    public void Simulate_AllAccounts_SumsLegacyAndNewNisaCsvValues()
    {
        var positions = CreateSbiSp500Positions();

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(monthlyContribution: 40_000m));

        Assert.Equal(2_670_633m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_680_021m, result.Summary.CurrentCostJpy);
        Assert.Equal(990_612m, result.Summary.UnrealizedGainJpy);
        Assert.Equal(1, result.Summary.FundCount);
        Assert.Equal(2, result.Summary.PositionCount);
        Assert.True(result.Summary.AllowsMonthlyContribution);
        Assert.Equal(40_000m, result.Summary.EffectiveMonthlyContributionJpy);
        Assert.Equal(2, result.AccountBreakdowns.Count);
        Assert.Contains(result.AccountBreakdowns, x => x.AccountType == AccountTypes.NisaLegacy && x.MonthlyContributionJpy == 0m);
        Assert.Contains(result.AccountBreakdowns, x => x.AccountType == AccountTypes.NisaAccumulation && x.MonthlyContributionJpy == 40_000m);
    }

    [Fact]
    public void Simulate_FundScope_SumsLegacyAndNewNisaForSameFund()
    {
        var positions = CreateSbiSp500Positions();
        var fundKey = _service
            .CreateScopeOptions(positions)
            .Single(x => x.Key.StartsWith(MutualFundSimulationScopeKeys.FundPrefix, StringComparison.OrdinalIgnoreCase))
            .Key;

        var result = _service.Simulate(positions, fundKey, CreateInput(monthlyContribution: 40_000m));

        Assert.Equal(2_670_633m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_680_021m, result.Summary.CurrentCostJpy);
        Assert.Equal(990_612m, result.Summary.UnrealizedGainJpy);
        Assert.Equal(1, result.Summary.FundCount);
        Assert.Equal(2, result.Summary.PositionCount);
    }

    [Fact]
    public void Simulate_AccountScope_UsesOnlySelectedAccount()
    {
        var positions = CreateSbiSp500Positions();

        var result = _service.Simulate(
            positions,
            MutualFundSimulationScopeKeys.Account(AccountTypes.NisaAccumulation),
            CreateInput(monthlyContribution: 40_000m));

        Assert.Equal(1_669_087m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_213_346m, result.Summary.CurrentCostJpy);
        Assert.Equal(455_741m, result.Summary.UnrealizedGainJpy);
        Assert.Equal(1, result.Summary.FundCount);
        Assert.Equal(1, result.Summary.PositionCount);
        Assert.True(result.Summary.AllowsMonthlyContribution);
        Assert.Equal(40_000m, result.Summary.EffectiveMonthlyContributionJpy);
    }

    [Fact]
    public void Simulate_LegacyNisaScope_DisablesMonthlyContribution()
    {
        var positions = CreateSbiSp500Positions();

        var result = _service.Simulate(
            positions,
            MutualFundSimulationScopeKeys.Account(AccountTypes.NisaLegacy),
            CreateInput(monthlyContribution: 40_000m, targetAmount: 2_000_000m, projectionYears: 1, targetYears: 10));

        Assert.Equal(1_001_546m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(466_675m, result.Summary.CurrentCostJpy);
        Assert.False(result.Summary.AllowsMonthlyContribution);
        Assert.Equal(0m, result.Summary.EffectiveMonthlyContributionJpy);
        Assert.Equal(0m, result.Projections[0].CumulativeContributionJpy);
        Assert.False(result.IsRequiredMonthlyContributionApplicable);
        Assert.Equal(0m, result.RequiredMonthlyContributionJpy);
        Assert.Single(result.ContributionComparisons);
        Assert.Equal(0m, result.ContributionComparisons[0].MonthlyContributionJpy);
    }

    [Fact]
    public void Simulate_DeduplicatesExactSameCsvPositionOnly()
    {
        var positions = CreateSbiSp500Positions()
            .Concat(CreateSbiSp500Positions())
            .ToArray();

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());

        Assert.Equal(2_670_633m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_680_021m, result.Summary.CurrentCostJpy);
        Assert.Equal(2, result.Summary.PositionCount);
    }

    [Fact]
    public void Simulate_PrefersMutualFundAccountTypeWhenStockCustodyDiffers()
    {
        var positions = new[]
        {
            CreateFundPosition(
                1,
                AccountTypes.NisaLegacy,
                unitsHeld: 246814m,
                averageCostNav: 18908m,
                currentNav: 40579m,
                acquisitionAmount: 466675m,
                marketValue: 1001546m,
                stockAccountType: AccountTypes.Unknown,
                custodyType: AccountTypes.NisaAccumulation),
            CreateFundPosition(
                2,
                AccountTypes.NisaAccumulation,
                unitsHeld: 411318m,
                averageCostNav: 29499m,
                currentNav: 40579m,
                acquisitionAmount: 1213346m,
                marketValue: 1669087m)
        };

        var all = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());
        var legacy = _service.Simulate(positions, MutualFundSimulationScopeKeys.Account(AccountTypes.NisaLegacy), CreateInput());
        var accumulation = _service.Simulate(positions, MutualFundSimulationScopeKeys.Account(AccountTypes.NisaAccumulation), CreateInput());

        Assert.Equal(2_670_633m, all.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_680_021m, all.Summary.CurrentCostJpy);
        Assert.Equal(2, all.Summary.PositionCount);
        Assert.Equal(1_001_546m, legacy.Summary.CurrentMarketValueJpy);
        Assert.Equal(466_675m, legacy.Summary.CurrentCostJpy);
        Assert.Equal(1, legacy.Summary.PositionCount);
        Assert.Equal(1_669_087m, accumulation.Summary.CurrentMarketValueJpy);
        Assert.Equal(1, accumulation.Summary.PositionCount);
    }

    [Fact]
    public void Simulate_UsesLegacyNisaCustodyWhenStoredFundAccountWasMisclassified()
    {
        var positions = new[]
        {
            CreateFundPosition(
                1,
                AccountTypes.NisaAccumulation,
                unitsHeld: 246814m,
                averageCostNav: 18908m,
                currentNav: 40579m,
                acquisitionAmount: 466675m,
                marketValue: 1001546m,
                stockAccountType: AccountTypes.NisaAccumulation,
                custodyType: "投資信託（金額/旧つみたてNISA預り）"),
            CreateFundPosition(
                2,
                AccountTypes.NisaAccumulation,
                unitsHeld: 411318m,
                averageCostNav: 29499m,
                currentNav: 40579m,
                acquisitionAmount: 1213346m,
                marketValue: 1669087m)
        };

        var all = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(monthlyContribution: 40_000m));
        var legacy = _service.Simulate(positions, MutualFundSimulationScopeKeys.Account(AccountTypes.NisaLegacy), CreateInput(monthlyContribution: 40_000m));
        var accumulation = _service.Simulate(positions, MutualFundSimulationScopeKeys.Account(AccountTypes.NisaAccumulation), CreateInput(monthlyContribution: 40_000m));

        Assert.Equal(2_670_633m, all.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_680_021m, all.Summary.CurrentCostJpy);
        Assert.Equal(2, all.Summary.PositionCount);
        Assert.Contains(all.AccountBreakdowns, x => x.AccountType == AccountTypes.NisaLegacy && x.MonthlyContributionJpy == 0m);
        Assert.Contains(all.AccountBreakdowns, x => x.AccountType == AccountTypes.NisaAccumulation && x.MonthlyContributionJpy == 40_000m);
        Assert.Equal(1_001_546m, legacy.Summary.CurrentMarketValueJpy);
        Assert.False(legacy.Summary.AllowsMonthlyContribution);
        Assert.Equal(1_669_087m, accumulation.Summary.CurrentMarketValueJpy);
    }

    [Fact]
    public void Simulate_CalculatesMutualFundValueByTenThousandUnitsWhenCsvValueIsMissing()
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
    public void Simulate_TargetAmountChange_DoesNotChangeProjectionFinalAsset()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var lowerTarget = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 2_000m,
            projectionYears: 1));
        var higherTarget = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 1));

        Assert.Equal(lowerTarget.Projections[^1].MarketValueJpy, higherTarget.Projections[^1].MarketValueJpy);
    }

    [Fact]
    public void Simulate_ProjectionYearsChange_ChangesFinalAssetOnlyWhenTargetYearsIsFixed()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var oneYear = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 1,
            targetYears: 5));
        var twoYears = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 2,
            targetYears: 5));

        Assert.NotEqual(oneYear.Projections[^1].MarketValueJpy, twoYears.Projections[^1].MarketValueJpy);
        Assert.Equal(oneYear.RequiredMonthlyContributionJpy, twoYears.RequiredMonthlyContributionJpy);
    }

    [Fact]
    public void Simulate_RequiredMonthlyContribution_UsesTargetYearsAsDeadline()
    {
        var positions = CreateSbiSp500Positions();

        var tenYears = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 40_000m,
            expectedAnnualReturnRate: 5m,
            targetAmount: 20_000_000m,
            projectionYears: 20,
            targetYears: 10));
        var twentyYears = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 40_000m,
            expectedAnnualReturnRate: 5m,
            targetAmount: 20_000_000m,
            projectionYears: 20,
            targetYears: 20));

        Assert.True(tenYears.RequiredMonthlyContributionJpy > 0m);
        Assert.True(tenYears.RequiredMonthlyContributionJpy > twentyYears.RequiredMonthlyContributionJpy);
        Assert.Equal(tenYears.RequiredMonthlyContributionJpy - 40_000m, tenYears.AdditionalMonthlyContributionNeededJpy);

        var tenYearCheck = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: tenYears.RequiredMonthlyContributionJpy,
            expectedAnnualReturnRate: 5m,
            targetAmount: 20_000_000m,
            projectionYears: 10,
            targetYears: 10));

        Assert.True(tenYearCheck.Projections[^1].MarketValueJpy >= 20_000_000m);
    }

    [Fact]
    public void Simulate_MonthlyContributionChange_ChangesTargetMonthButNotRequiredContribution()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var lower = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 20_000m,
            projectionYears: 10,
            startYear: 2026,
            startMonth: 7));
        var higher = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 200m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 20_000m,
            projectionYears: 10,
            startYear: 2026,
            startMonth: 7));

        Assert.True(lower.MonthsToTarget > higher.MonthsToTarget);
        Assert.Equal(lower.RequiredMonthlyContributionJpy, higher.RequiredMonthlyContributionJpy);
        Assert.True(lower.AdditionalMonthlyContributionNeededJpy > higher.AdditionalMonthlyContributionNeededJpy);
        Assert.Equal(Math.Max(0m, lower.RequiredMonthlyContributionJpy - 100m), lower.AdditionalMonthlyContributionNeededJpy);
        Assert.Equal(Math.Max(0m, higher.RequiredMonthlyContributionJpy - 200m), higher.AdditionalMonthlyContributionNeededJpy);
    }

    [Fact]
    public void Simulate_AdditionalMonthlyContributionNeeded_BecomesZeroWhenCurrentContributionIsEnough()
    {
        var positions = CreateSbiSp500Positions();

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 10_000_000m,
            expectedAnnualReturnRate: 17.3m,
            targetAmount: 20_000_000m,
            projectionYears: 10));

        Assert.True(result.RequiredMonthlyContributionJpy > 0m);
        Assert.Equal(0m, result.AdditionalMonthlyContributionNeededJpy);
        Assert.True(result.MonthlyContributionMarginJpy > 0m);
    }

    [Fact]
    public void Simulate_TargetAmountChange_ChangesTargetMonthAndRequiredContribution()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var lowerTarget = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 3_000m,
            projectionYears: 10));
        var higherTarget = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 10));

        Assert.True(lowerTarget.MonthsToTarget < higherTarget.MonthsToTarget);
        Assert.True(lowerTarget.RequiredMonthlyContributionJpy < higherTarget.RequiredMonthlyContributionJpy);
    }

    [Fact]
    public void Simulate_CurrentValueAlreadyReachesTarget_ReturnsZeroRequiredContribution()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 0m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 900m,
            projectionYears: 1,
            startYear: 2026,
            startMonth: 7));

        Assert.Equal(0m, result.RequiredMonthlyContributionJpy);
        Assert.Equal(new DateTime(2026, 7, 1), result.TargetAchievementMonth);
        Assert.Equal(0, result.MonthsToTarget);
    }

    [Fact]
    public void Simulate_NoContributionFinalAsset_IgnoresMonthlyContribution()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var lower = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 5m,
            targetAmount: 5_000m,
            projectionYears: 3));
        var higher = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 1_000m,
            expectedAnnualReturnRate: 5m,
            targetAmount: 5_000m,
            projectionYears: 3));

        Assert.Equal(lower.Projections[^1].NoContributionMarketValueJpy, higher.Projections[^1].NoContributionMarketValueJpy);
        Assert.NotEqual(lower.Projections[^1].MarketValueJpy, higher.Projections[^1].MarketValueJpy);
    }

    [Fact]
    public void Simulate_TargetAchievementCanExceedProjectionPeriod()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 1,
            startYear: 2026,
            startMonth: 7));

        Assert.Equal(40, result.MonthsToTarget);
        Assert.Equal(new DateTime(2029, 11, 1), result.TargetAchievementMonth);
    }

    [Fact]
    public void Simulate_TargetYearsInput_ChangesRequiredContributionButNotTargetMonth()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var shortTargetYears = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 10,
            targetYears: 1));
        var longTargetYears = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 10,
            targetYears: 50));

        Assert.True(shortTargetYears.RequiredMonthlyContributionJpy > longTargetYears.RequiredMonthlyContributionJpy);
        Assert.Equal(shortTargetYears.TargetAchievementMonth, longTargetYears.TargetAchievementMonth);
        Assert.Equal(shortTargetYears.MonthsToTarget, longTargetYears.MonthsToTarget);
    }

    [Fact]
    public void Simulate_ProjectionYearsControlsFinalGraphMonth()
    {
        var positions = new[]
        {
            CreateFundPosition(1, AccountTypes.Specific, unitsHeld: 10000m, averageCostNav: 1000m, currentNav: 1000m)
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput(
            monthlyContribution: 100m,
            expectedAnnualReturnRate: 0m,
            targetAmount: 5_000m,
            projectionYears: 2,
            startYear: 2026,
            startMonth: 7));

        Assert.Equal(24, result.Projections.Count);
        Assert.Equal(new DateTime(2028, 7, 1), result.Projections[^1].YearMonth);
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
    public void Simulate_CalculatesActualAnnualizedReturnFromPurchaseDate()
    {
        var positions = new[]
        {
            CreateFundPosition(
                1,
                AccountTypes.Specific,
                unitsHeld: 10000m,
                averageCostNav: 1000m,
                currentNav: 1210m,
                purchaseDate: DateTime.Today.AddYears(-2))
        };

        var result = _service.Simulate(positions, MutualFundSimulationScopeKeys.AllAccounts, CreateInput());

        Assert.NotNull(result.Summary.ActualAnnualizedReturnRate);
        Assert.InRange(result.Summary.ActualAnnualizedReturnRate.Value, 9.5m, 10.5m);
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

    private static StockPosition[] CreateSbiSp500Positions() =>
    [
        CreateFundPosition(
            1,
            AccountTypes.NisaAccumulation,
            unitsHeld: 411318m,
            averageCostNav: 29499m,
            currentNav: 40579m,
            acquisitionAmount: 1213346m,
            marketValue: 1669087m,
            fundCode: "SBI-V-SP500",
            fundName: "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド"),
        CreateFundPosition(
            2,
            AccountTypes.NisaLegacy,
            unitsHeld: 246814m,
            averageCostNav: 18908m,
            currentNav: 40579m,
            acquisitionAmount: 466675m,
            marketValue: 1001546m,
            fundCode: "SBI-V-SP500",
            fundName: "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド")
    ];

    private static MutualFundSimulationInput CreateInput(
        decimal monthlyContribution = 0m,
        decimal expectedAnnualReturnRate = 5m,
        decimal targetAmount = 20_000_000m,
        int projectionYears = 20,
        int targetYears = 0,
        int startYear = 2026,
        int startMonth = 7) =>
        new()
        {
            MonthlyContributionJpy = monthlyContribution,
            ExpectedAnnualReturnRate = expectedAnnualReturnRate,
            TargetAmountJpy = targetAmount,
            ProjectionYears = projectionYears,
            TargetYears = targetYears <= 0 ? projectionYears : targetYears,
            StartYear = startYear,
            StartMonth = startMonth
        };

    private static StockPosition CreateFundPosition(
        int stockId,
        string accountType,
        decimal unitsHeld,
        decimal averageCostNav,
        decimal currentNav,
        decimal acquisitionAmount = 0m,
        decimal marketValue = 0m,
        string broker = "SBI証券",
        string fundCode = "FUND1",
        string fundName = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
        string? stockAccountType = null,
        string? custodyType = null,
        DateTime? purchaseDate = null) =>
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
                AccountType = stockAccountType ?? accountType,
                CustodyType = custodyType ?? accountType
            },
            Purchase = new Purchase
            {
                PurchaseDate = purchaseDate ?? DateTime.Today,
                Shares = unitsHeld,
                UnitPrice = averageCostNav,
                ExchangeRate = 1m
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
                AcquisitionAmount = acquisitionAmount,
                MarketValue = marketValue,
                AccountType = accountType,
                NavDate = new DateTime(2026, 7, 10),
                NavSource = "CSV"
            }
        };
}
