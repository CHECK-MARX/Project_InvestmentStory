using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MutualFundAssetSimulationService
{
    private const decimal DefaultUnitBase = 10000m;
    private static readonly string[] AccountScopeOrder =
    [
        AccountTypes.NisaLegacy,
        AccountTypes.NisaAccumulation,
        AccountTypes.NisaGrowth,
        AccountTypes.Specific,
        AccountTypes.General,
        AccountTypes.Unknown
    ];

    public IReadOnlyList<MutualFundSimulationScopeOption> CreateScopeOptions(IEnumerable<StockPosition> positions)
    {
        var currentPositions = GetCurrentDeduplicatedPositions(positions).ToList();
        var options = new List<MutualFundSimulationScopeOption>
        {
            new()
            {
                Key = MutualFundSimulationScopeKeys.AllAccounts,
                DisplayName = "すべての投資信託",
                FundCount = CountFunds(currentPositions),
                PositionCount = currentPositions.Count
            }
        };

        var accountGroups = currentPositions
            .GroupBy(GetNormalizedAccountType)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var accountType in AccountScopeOrder)
        {
            if (!accountGroups.TryGetValue(accountType, out var groupPositions))
            {
                continue;
            }

            options.Add(new MutualFundSimulationScopeOption
            {
                Key = MutualFundSimulationScopeKeys.Account(accountType),
                DisplayName = GetAccountDisplayName(accountType),
                FundCount = CountFunds(groupPositions),
                PositionCount = groupPositions.Count
            });
        }

        var fundGroups = currentPositions
            .GroupBy(GetFundScopeKey)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => GetFundDisplayName(group.First()), StringComparer.CurrentCulture);

        foreach (var group in fundGroups)
        {
            var groupPositions = group.ToList();
            options.Add(new MutualFundSimulationScopeOption
            {
                Key = MutualFundSimulationScopeKeys.Fund(group.Key),
                LegacyKey = MutualFundSimulationScopeKeys.Fund(GetLegacyFundScopeKey(group.First())),
                DisplayName = $"ファンド別：{GetFundDisplayName(group.First())}",
                FundCount = 1,
                PositionCount = groupPositions.Count
            });
        }

        return options;
    }

    public MutualFundAssetSimulationResult Simulate(
        IEnumerable<StockPosition> positions,
        string? scopeKey,
        MutualFundSimulationInput input,
        IEnumerable<BrokerTrade>? brokerTrades = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(input);

        var selectedPositions = SelectPositions(positions, scopeKey).ToList();
        var requestedContribution = Math.Max(0m, input.MonthlyContributionJpy);
        var contributionAccountType = ResolveContributionAccountType(selectedPositions, scopeKey);
        var allowsContribution = contributionAccountType is not null;
        var effectiveMonthlyContribution = allowsContribution ? requestedContribution : 0m;
        var summary = BuildSummary(selectedPositions, allowsContribution, effectiveMonthlyContribution, brokerTrades);
        var accountBreakdowns = BuildAccountBreakdowns(selectedPositions, contributionAccountType, effectiveMonthlyContribution);
        var projectionYears = Math.Clamp(input.ProjectionYears, 1, 100);
        var months = projectionYears * 12;
        var targetYears = Math.Clamp(input.TargetYears <= 0 ? projectionYears : input.TargetYears, 1, 100);
        var targetMonths = targetYears * 12;
        var start = new DateTime(
            input.StartYear <= 0 ? DateTime.Today.Year : input.StartYear,
            Math.Clamp(input.StartMonth, 1, 12),
            1);
        var annualRate = Math.Max(input.ExpectedAnnualReturnRate, -99.99m);
        var monthlyRate = ToMonthlyRate(annualRate);
        var targetAmount = Math.Max(0m, input.TargetAmountJpy);

        var projections = BuildProjections(
            summary,
            effectiveMonthlyContribution,
            monthlyRate,
            targetAmount,
            start,
            months);

        var targetAchievement = CalculateTargetAchievement(
            summary.CurrentMarketValueJpy,
            effectiveMonthlyContribution,
            monthlyRate,
            targetAmount,
            start,
            maxMonths: 100 * 12);
        var requiredMonthlyContribution = allowsContribution
            ? CalculateRequiredMonthlyContribution(summary.CurrentMarketValueJpy, targetAmount, monthlyRate, targetMonths)
            : 0m;
        var monthlyContributionMargin = allowsContribution
            ? effectiveMonthlyContribution - requiredMonthlyContribution
            : 0m;

        return new MutualFundAssetSimulationResult
        {
            Summary = summary,
            AccountBreakdowns = accountBreakdowns,
            Projections = projections,
            ContributionComparisons = BuildContributionComparisons(
                summary.CurrentMarketValueJpy,
                effectiveMonthlyContribution,
                monthlyRate,
                months,
                allowsContribution),
            TargetAchievementMonth = targetAchievement.Month,
            MonthsToTarget = targetAchievement.Months,
            RequiredMonthlyContributionJpy = requiredMonthlyContribution,
            AdditionalMonthlyContributionNeededJpy = allowsContribution
                ? Math.Max(0m, requiredMonthlyContribution - effectiveMonthlyContribution)
                : 0m,
            MonthlyContributionMarginJpy = monthlyContributionMargin,
            IsRequiredMonthlyContributionApplicable = allowsContribution
        };
    }

    public MutualFundScenarioComparisonResult SimulateScenarios(
        IEnumerable<StockPosition> positions,
        string? scopeKey,
        MutualFundSimulationInput input,
        IEnumerable<MutualFundScenarioInput> scenarios,
        IEnumerable<BrokerTrade>? brokerTrades = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(scenarios);

        var selectedPositions = SelectPositions(positions, scopeKey).ToList();
        var requestedContribution = Math.Max(0m, input.MonthlyContributionJpy);
        var contributionAccountType = ResolveContributionAccountType(selectedPositions, scopeKey);
        var allowsContribution = contributionAccountType is not null;
        var effectiveMonthlyContribution = allowsContribution ? requestedContribution : 0m;
        var summary = BuildSummary(selectedPositions, allowsContribution, effectiveMonthlyContribution, brokerTrades);
        var accountBreakdowns = BuildAccountBreakdowns(selectedPositions, contributionAccountType, effectiveMonthlyContribution);
        var projectionYears = Math.Clamp(input.ProjectionYears, 1, 100);
        var months = projectionYears * 12;
        var start = new DateTime(
            input.StartYear <= 0 ? DateTime.Today.Year : input.StartYear,
            Math.Clamp(input.StartMonth, 1, 12),
            1);
        var targetAmount = Math.Max(0m, input.TargetAmountJpy);
        var scenarioInputs = scenarios.ToList();
        var baseResults = scenarioInputs
            .Select(scenario => SimulateScenario(summary, effectiveMonthlyContribution, targetAmount, start, months, scenario))
            .ToList();
        var conservative = baseResults.FirstOrDefault(result =>
            result.IsAvailable &&
            string.Equals(result.Key, "Conservative", StringComparison.OrdinalIgnoreCase));
        var usesConservativeTargetHorizon = conservative?.MonthsToTarget is > 0;
        var chartMonths = usesConservativeTargetHorizon
            ? Math.Clamp(conservative!.MonthsToTarget!.Value, 1, 100 * 12)
            : months;
        var results = baseResults
            .Select(result => AddChartProjection(
                result,
                summary,
                effectiveMonthlyContribution,
                targetAmount,
                start,
                chartMonths))
            .ToList();
        var comparisons = BuildMonthlyComparisons(results, months, useChartProjections: false);
        var chartComparisons = BuildMonthlyComparisons(results, chartMonths, useChartProjections: true);

        return new MutualFundScenarioComparisonResult
        {
            Summary = summary,
            AccountBreakdowns = accountBreakdowns,
            Scenarios = results,
            MonthlyComparisons = comparisons,
            ChartMonthlyComparisons = chartComparisons,
            ChartHorizonMonths = chartMonths,
            ChartEndMonth = chartMonths <= 0 ? start : start.AddMonths(chartMonths),
            ConservativeTargetMonth = conservative?.TargetAchievementMonth,
            UsesConservativeTargetHorizon = usesConservativeTargetHorizon
        };
    }

    public IReadOnlyList<StockPosition> SelectPositions(IEnumerable<StockPosition> positions, string? scopeKey)
    {
        var currentPositions = GetCurrentDeduplicatedPositions(positions).ToList();
        if (string.IsNullOrWhiteSpace(scopeKey) ||
            string.Equals(scopeKey, MutualFundSimulationScopeKeys.AllAccounts, StringComparison.OrdinalIgnoreCase))
        {
            return currentPositions;
        }

        if (scopeKey.StartsWith(MutualFundSimulationScopeKeys.AccountPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var accountType = scopeKey[MutualFundSimulationScopeKeys.AccountPrefix.Length..];
            return currentPositions
                .Where(position => string.Equals(GetNormalizedAccountType(position), accountType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (scopeKey.StartsWith(MutualFundSimulationScopeKeys.FundPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var fundKey = scopeKey[MutualFundSimulationScopeKeys.FundPrefix.Length..];
            return currentPositions
                .Where(position => IsFundScopeMatch(position, fundKey))
                .ToList();
        }

        return currentPositions;
    }

    private static MutualFundPortfolioSummary BuildSummary(
        IReadOnlyList<StockPosition> positions,
        bool allowsContribution,
        decimal effectiveMonthlyContribution,
        IEnumerable<BrokerTrade>? brokerTrades)
    {
        var values = positions.Select(CreateValuation).ToList();
        var marketValue = values.Sum(x => x.MarketValueJpy);
        var cost = values.Sum(x => x.CostJpy);
        var gain = marketValue - cost;

        var actualEstimate = CalculateActualAnnualizedReturnEstimate(positions, marketValue, cost, brokerTrades);
        return new MutualFundPortfolioSummary
        {
            CurrentMarketValueJpy = marketValue,
            CurrentCostJpy = cost,
            UnrealizedGainJpy = gain,
            UnrealizedGainRate = cost == 0m ? 0m : gain / cost * 100m,
            ActualAnnualizedReturnRate = actualEstimate?.AnnualizedReturnRate,
            ActualAnnualizedReturnEstimate = actualEstimate,
            FundCount = CountFunds(positions),
            PositionCount = positions.Count,
            AllowsMonthlyContribution = allowsContribution,
            EffectiveMonthlyContributionJpy = effectiveMonthlyContribution
        };
    }

    private static IReadOnlyList<MutualFundSimulationAccountBreakdown> BuildAccountBreakdowns(
        IReadOnlyList<StockPosition> positions,
        string? contributionAccountType,
        decimal effectiveMonthlyContribution)
    {
        return positions
            .GroupBy(GetNormalizedAccountType)
            .OrderBy(group => GetAccountSortIndex(group.Key))
            .ThenBy(group => GetAccountDisplayName(group.Key), StringComparer.CurrentCulture)
            .Select(group =>
            {
                var groupPositions = group.ToList();
                var values = groupPositions.Select(CreateValuation).ToList();
                var marketValue = values.Sum(x => x.MarketValueJpy);
                var cost = values.Sum(x => x.CostJpy);
                var allowsContribution = string.Equals(group.Key, contributionAccountType, StringComparison.OrdinalIgnoreCase);
                return new MutualFundSimulationAccountBreakdown
                {
                    AccountType = group.Key,
                    AccountDisplayName = GetAccountDisplayName(group.Key),
                    CurrentMarketValueJpy = marketValue,
                    CurrentCostJpy = cost,
                    UnrealizedGainJpy = marketValue - cost,
                    FundCount = CountFunds(groupPositions),
                    PositionCount = groupPositions.Count,
                    AllowsContribution = allowsContribution,
                    MonthlyContributionJpy = allowsContribution ? effectiveMonthlyContribution : 0m
                };
            })
            .ToList();
    }

    private static IReadOnlyList<MutualFundMonthlyProjection> BuildProjections(
        MutualFundPortfolioSummary summary,
        decimal monthlyContribution,
        decimal monthlyRate,
        decimal targetAmount,
        DateTime start,
        int months)
    {
        var rows = new List<MutualFundMonthlyProjection>(months);
        var marketValue = summary.CurrentMarketValueJpy;
        var noContributionMarketValue = summary.CurrentMarketValueJpy;
        var cost = summary.CurrentCostJpy;
        var cumulativeContribution = 0m;

        for (var month = 1; month <= months; month++)
        {
            marketValue = marketValue * (1m + monthlyRate) + monthlyContribution;
            noContributionMarketValue *= 1m + monthlyRate;
            cost += monthlyContribution;
            cumulativeContribution += monthlyContribution;
            var yearMonth = start.AddMonths(month);
            rows.Add(new MutualFundMonthlyProjection
            {
                YearMonth = yearMonth,
                MonthsFromNow = month,
                MarketValueJpy = RoundYen(marketValue),
                NoContributionMarketValueJpy = RoundYen(noContributionMarketValue),
                CostJpy = RoundYen(cost),
                CumulativeContributionJpy = RoundYen(cumulativeContribution),
                UnrealizedGainJpy = RoundYen(marketValue - cost),
                TargetAchievementRate = targetAmount <= 0m ? 0m : marketValue / targetAmount * 100m
            });
        }

        return rows;
    }

    private static MutualFundScenarioResult SimulateScenario(
        MutualFundPortfolioSummary summary,
        decimal monthlyContribution,
        decimal targetAmount,
        DateTime start,
        int months,
        MutualFundScenarioInput scenario)
    {
        if (!scenario.IsEnabled || scenario.AnnualReturnRate is null)
        {
            return new MutualFundScenarioResult
            {
                Key = scenario.Key,
                Name = scenario.Name,
                AnnualReturnRate = scenario.AnnualReturnRate,
                IsEnabled = scenario.IsEnabled,
                IsAvailable = false,
                Basis = scenario.Basis,
                UnavailableReason = string.IsNullOrWhiteSpace(scenario.UnavailableReason)
                    ? "実績年利を算出できません。"
                    : scenario.UnavailableReason
            };
        }

        var annualRate = Math.Max(scenario.AnnualReturnRate.Value, -99.99m);
        var monthlyRate = ToMonthlyRate(annualRate);
        var projections = BuildScenarioProjections(summary, monthlyContribution, monthlyRate, targetAmount, start, months);
        var targetProjection = summary.CurrentMarketValueJpy >= targetAmount && targetAmount > 0m
            ? new MutualFundScenarioMonthlyProjection
            {
                YearMonth = start,
                MonthsFromNow = 0,
                MarketValueJpy = summary.CurrentMarketValueJpy,
                NoContributionMarketValueJpy = summary.CurrentMarketValueJpy,
                CumulativeContributionJpy = 0m,
                TotalCostJpy = summary.CurrentCostJpy,
                UnrealizedGainJpy = summary.CurrentMarketValueJpy - summary.CurrentCostJpy,
                TargetAchievementRate = 100m
            }
            : projections.FirstOrDefault(row => targetAmount > 0m && row.MarketValueJpy >= targetAmount);
        var longTermTarget = targetProjection is null
            ? CalculateTargetAchievement(summary.CurrentMarketValueJpy, monthlyContribution, monthlyRate, targetAmount, start, maxMonths: 100 * 12)
            : (targetProjection.YearMonth, (int?)targetProjection.MonthsFromNow);
        var targetMonth = targetProjection?.YearMonth ?? longTermTarget.Month;
        var monthsToTarget = targetProjection?.MonthsFromNow ?? longTermTarget.Months;
        var contributionAtTarget = targetProjection is not null
            ? targetProjection.CumulativeContributionJpy
            : monthsToTarget is { } targetMonths ? monthlyContribution * targetMonths : 0m;
        var marketValueAtTarget = targetProjection?.MarketValueJpy ?? targetAmount;
        var gainAtTarget = targetMonth is null
            ? 0m
            : RoundYen(marketValueAtTarget - summary.CurrentCostJpy - contributionAtTarget);

        return new MutualFundScenarioResult
        {
            Key = scenario.Key,
            Name = scenario.Name,
            AnnualReturnRate = annualRate,
            IsEnabled = true,
            IsAvailable = true,
            Basis = scenario.Basis,
            FinalMarketValueJpy = projections.Count == 0 ? summary.CurrentMarketValueJpy : projections[^1].MarketValueJpy,
            FiveYearMarketValueJpy = ValueAtMonth(projections, 60),
            TenYearMarketValueJpy = ValueAtMonth(projections, 120),
            TargetAchievementMonth = targetMonth,
            MonthsToTarget = monthsToTarget,
            ReachesTargetWithinProjection = targetProjection is not null,
            CumulativeContributionAtTargetJpy = RoundYen(contributionAtTarget),
            InvestmentGainAtTargetJpy = gainAtTarget,
            NoContributionFinalMarketValueJpy = projections.Count == 0 ? summary.CurrentMarketValueJpy : projections[^1].NoContributionMarketValueJpy,
            Projections = projections,
            ChartFinalMarketValueJpy = projections.Count == 0 ? summary.CurrentMarketValueJpy : projections[^1].MarketValueJpy,
            ChartProjections = projections
        };
    }

    private static MutualFundScenarioResult AddChartProjection(
        MutualFundScenarioResult scenario,
        MutualFundPortfolioSummary summary,
        decimal monthlyContribution,
        decimal targetAmount,
        DateTime start,
        int chartMonths)
    {
        if (!scenario.IsAvailable || scenario.AnnualReturnRate is null)
        {
            return scenario;
        }

        var chartProjections = BuildScenarioProjections(
            summary,
            monthlyContribution,
            ToMonthlyRate(Math.Max(scenario.AnnualReturnRate.Value, -99.99m)),
            targetAmount,
            start,
            chartMonths);

        return new MutualFundScenarioResult
        {
            Key = scenario.Key,
            Name = scenario.Name,
            AnnualReturnRate = scenario.AnnualReturnRate,
            IsEnabled = scenario.IsEnabled,
            IsAvailable = scenario.IsAvailable,
            Basis = scenario.Basis,
            UnavailableReason = scenario.UnavailableReason,
            FinalMarketValueJpy = scenario.FinalMarketValueJpy,
            FiveYearMarketValueJpy = scenario.FiveYearMarketValueJpy,
            TenYearMarketValueJpy = scenario.TenYearMarketValueJpy,
            TargetAchievementMonth = scenario.TargetAchievementMonth,
            MonthsToTarget = scenario.MonthsToTarget,
            ReachesTargetWithinProjection = scenario.ReachesTargetWithinProjection,
            CumulativeContributionAtTargetJpy = scenario.CumulativeContributionAtTargetJpy,
            InvestmentGainAtTargetJpy = scenario.InvestmentGainAtTargetJpy,
            NoContributionFinalMarketValueJpy = scenario.NoContributionFinalMarketValueJpy,
            Projections = scenario.Projections,
            ChartFinalMarketValueJpy = chartProjections.Count == 0
                ? summary.CurrentMarketValueJpy
                : chartProjections[^1].MarketValueJpy,
            ChartProjections = chartProjections
        };
    }

    private static IReadOnlyList<MutualFundScenarioMonthlyProjection> BuildScenarioProjections(
        MutualFundPortfolioSummary summary,
        decimal monthlyContribution,
        decimal monthlyRate,
        decimal targetAmount,
        DateTime start,
        int months)
    {
        var rows = new List<MutualFundScenarioMonthlyProjection>(months);
        var marketValue = summary.CurrentMarketValueJpy;
        var noContributionMarketValue = summary.CurrentMarketValueJpy;
        var cumulativeContribution = 0m;

        for (var month = 1; month <= months; month++)
        {
            marketValue = marketValue * (1m + monthlyRate) + monthlyContribution;
            noContributionMarketValue *= 1m + monthlyRate;
            cumulativeContribution += monthlyContribution;
            var totalCost = summary.CurrentCostJpy + cumulativeContribution;
            rows.Add(new MutualFundScenarioMonthlyProjection
            {
                YearMonth = start.AddMonths(month),
                MonthsFromNow = month,
                MarketValueJpy = RoundYen(marketValue),
                NoContributionMarketValueJpy = RoundYen(noContributionMarketValue),
                CumulativeContributionJpy = RoundYen(cumulativeContribution),
                TotalCostJpy = RoundYen(totalCost),
                UnrealizedGainJpy = RoundYen(marketValue - totalCost),
                TargetAchievementRate = targetAmount <= 0m ? 0m : marketValue / targetAmount * 100m
            });
        }

        return rows;
    }

    private static IReadOnlyList<MutualFundScenarioMonthlyComparison> BuildMonthlyComparisons(
        IReadOnlyList<MutualFundScenarioResult> scenarios,
        int months,
        bool useChartProjections)
    {
        var available = scenarios.Where(x => x.IsAvailable).ToList();
        if (available.Count == 0)
        {
            return Array.Empty<MutualFundScenarioMonthlyComparison>();
        }

        var rows = new List<MutualFundScenarioMonthlyComparison>(months);
        for (var monthIndex = 0; monthIndex < months; monthIndex++)
        {
            var values = available
                .Where(scenario => (useChartProjections ? scenario.ChartProjections : scenario.Projections).Count > monthIndex)
                .ToDictionary(
                    scenario => scenario.Key,
                    scenario => (useChartProjections ? scenario.ChartProjections : scenario.Projections)[monthIndex],
                    StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0)
            {
                continue;
            }

            var first = values.Values.First();
            rows.Add(new MutualFundScenarioMonthlyComparison
            {
                YearMonth = first.YearMonth,
                MonthsFromNow = first.MonthsFromNow,
                CumulativeContributionJpy = first.CumulativeContributionJpy,
                ScenarioValues = values
            });
        }

        return rows;
    }

    private static decimal ValueAtMonth(IReadOnlyList<MutualFundScenarioMonthlyProjection> projections, int month) =>
        projections.Count >= month ? projections[month - 1].MarketValueJpy : 0m;

    private static (DateTime? Month, int? Months) CalculateTargetAchievement(
        decimal currentMarketValue,
        decimal monthlyContribution,
        decimal monthlyRate,
        decimal targetAmount,
        DateTime start,
        int maxMonths)
    {
        if (targetAmount <= 0m)
        {
            return (null, null);
        }

        if (currentMarketValue >= targetAmount)
        {
            return (start, 0);
        }

        var value = currentMarketValue;
        var contribution = Math.Max(0m, monthlyContribution);
        for (var month = 1; month <= maxMonths; month++)
        {
            value = value * (1m + monthlyRate) + contribution;
            if (value >= targetAmount)
            {
                return (start.AddMonths(month), month);
            }
        }

        return (null, null);
    }

    private static IReadOnlyList<MutualFundContributionComparison> BuildContributionComparisons(
        decimal currentMarketValue,
        decimal monthlyContribution,
        decimal monthlyRate,
        int months,
        bool allowsContribution)
    {
        if (!allowsContribution)
        {
            return
            [
                CreateComparison("追加積立なし", 0m)
            ];
        }

        var lower = Math.Max(0m, monthlyContribution - 50_000m);
        var upper = monthlyContribution + 50_000m;
        return
        [
            CreateComparison("現在設定額 - 50,000円", lower),
            CreateComparison("現在設定額", monthlyContribution),
            CreateComparison("現在設定額 + 50,000円", upper)
        ];

        MutualFundContributionComparison CreateComparison(string label, decimal contribution)
        {
            var value = currentMarketValue;
            for (var i = 0; i < months; i++)
            {
                value = value * (1m + monthlyRate) + contribution;
            }

            return new MutualFundContributionComparison
            {
                Label = label,
                MonthlyContributionJpy = contribution,
                FinalMarketValueJpy = RoundYen(value)
            };
        }
    }

    private static decimal CalculateRequiredMonthlyContribution(
        decimal currentMarketValue,
        decimal targetAmount,
        decimal monthlyRate,
        int months)
    {
        if (targetAmount <= 0m || currentMarketValue >= targetAmount || months <= 0)
        {
            return 0m;
        }

        if (ProjectFinalValue(currentMarketValue, 0m, monthlyRate, months) >= targetAmount)
        {
            return 0m;
        }

        if (monthlyRate == 0m)
        {
            return Math.Ceiling((targetAmount - currentMarketValue) / months);
        }

        var high = 10_000m;
        while (ProjectFinalValue(currentMarketValue, high, monthlyRate, months) < targetAmount && high < 100_000_000m)
        {
            high *= 2m;
        }

        var low = 0m;
        for (var i = 0; i < 80; i++)
        {
            var mid = (low + high) / 2m;
            if (ProjectFinalValue(currentMarketValue, mid, monthlyRate, months) >= targetAmount)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return Math.Ceiling(high);
    }

    private static decimal ProjectFinalValue(decimal currentMarketValue, decimal contribution, decimal monthlyRate, int months)
    {
        var value = currentMarketValue;
        for (var i = 0; i < months; i++)
        {
            value = value * (1m + monthlyRate) + contribution;
        }

        return value;
    }

    private static decimal ToMonthlyRate(decimal annualRatePercent)
    {
        var annualRate = (double)(annualRatePercent / 100m);
        return (decimal)(Math.Pow(1d + annualRate, 1d / 12d) - 1d);
    }

    private static IReadOnlyList<StockPosition> GetCurrentDeduplicatedPositions(IEnumerable<StockPosition> positions)
    {
        return positions
            .Where(IsCurrentMutualFundPosition)
            .GroupBy(BuildStrictDeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(GetDataCompletenessScore)
                .ThenByDescending(position => position.MutualFund.NavDate)
                .ThenByDescending(position => position.Stock.Id)
                .First())
            .ToList();
    }

    private static bool IsCurrentMutualFundPosition(StockPosition position)
    {
        if (!position.IsMutualFund || position.MutualFund.UnitsHeld <= 0m)
        {
            return false;
        }

        var valuation = CreateValuation(position);
        return valuation.MarketValueJpy > 0m;
    }

    private static FundValuation CreateValuation(StockPosition position)
    {
        var fund = position.MutualFund;
        var unitBase = fund.UnitBase <= 0m ? DefaultUnitBase : fund.UnitBase;
        var marketValue = fund.MarketValue > 0m
            ? RoundYen(fund.MarketValue)
            : fund.CurrentNav > 0m
                ? RoundYen(fund.UnitsHeld / unitBase * fund.CurrentNav)
                : 0m;
        var cost = fund.AcquisitionAmount > 0m
            ? RoundYen(fund.AcquisitionAmount)
            : fund.AverageCostNav > 0m
                ? RoundYen(fund.UnitsHeld / unitBase * fund.AverageCostNav)
                : 0m;

        return new FundValuation(marketValue, cost);
    }

    private static int GetDataCompletenessScore(StockPosition position)
    {
        var fund = position.MutualFund;
        var score = 0;
        if (fund.MarketValue > 0m)
        {
            score += 8;
        }

        if (fund.AcquisitionAmount > 0m)
        {
            score += 8;
        }

        if (fund.CurrentNav > 0m)
        {
            score += 4;
        }

        if (fund.AverageCostNav > 0m)
        {
            score += 4;
        }

        if (fund.NavDate != DateTime.MinValue)
        {
            score += 1;
        }

        return score;
    }

    private static string BuildStrictDeduplicationKey(StockPosition position)
    {
        var valuation = CreateValuation(position);
        return string.Join(
            "|",
            PositionIdentityService.NormalizeBroker(position.Stock.Broker),
            GetFundScopeKey(position),
            GetNormalizedAccountType(position),
            NormalizeDecimal(position.MutualFund.UnitsHeld),
            NormalizeDecimal(position.MutualFund.AverageCostNav),
            NormalizeDecimal(position.MutualFund.CurrentNav),
            NormalizeDecimal(valuation.CostJpy),
            NormalizeDecimal(valuation.MarketValueJpy),
            position.MutualFund.DistributionMethod?.Trim() ?? string.Empty);
    }

    private static string GetFundScopeKey(StockPosition position)
    {
        var canonicalKey = SecurityIdentityService.BuildCanonicalKey(position);
        return string.IsNullOrWhiteSpace(canonicalKey)
            ? GetLegacyFundScopeKey(position)
            : canonicalKey;
    }

    private static bool IsFundScopeMatch(StockPosition position, string fundKey)
    {
        return string.Equals(GetFundScopeKey(position), fundKey, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(GetLegacyFundScopeKey(position), fundKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLegacyFundScopeKey(StockPosition position)
    {
        var securityId = PositionIdentityService.ResolveSecurityId(position);
        return PositionIdentityService.NormalizeSecurityId(securityId, AssetTypes.MutualFund);
    }

    private static string GetFundDisplayName(StockPosition position) =>
        FirstNonBlank(position.MutualFund.FundName, position.Stock.Name, position.Stock.Ticker, "投資信託");

    private static string GetNormalizedAccountType(StockPosition position)
    {
        var accountCandidates = new[]
        {
            position.MutualFund.AccountType,
            position.Stock.AccountType,
            position.Stock.CustodyType
        };

        if (accountCandidates.Any(value =>
                string.Equals(AccountTypeNormalizer.Normalize(value), AccountTypes.NisaLegacy, StringComparison.OrdinalIgnoreCase)))
        {
            return AccountTypes.NisaLegacy;
        }

        return FirstKnownAccountType(accountCandidates);
    }

    private static string FirstKnownAccountType(params string[] values)
    {
        foreach (var value in values)
        {
            var normalized = AccountTypeNormalizer.Normalize(value);
            if (normalized != AccountTypes.Unknown)
            {
                return normalized;
            }
        }

        return AccountTypes.Unknown;
    }

    private static string? ResolveContributionAccountType(IReadOnlyList<StockPosition> positions, string? scopeKey)
    {
        if (positions.Count == 0)
        {
            return null;
        }

        if (scopeKey?.StartsWith(MutualFundSimulationScopeKeys.AccountPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            var accountType = scopeKey[MutualFundSimulationScopeKeys.AccountPrefix.Length..];
            return AllowsAdditionalContribution(accountType) ? accountType : null;
        }

        return positions
            .Select(GetNormalizedAccountType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(AllowsAdditionalContribution)
            .OrderBy(GetContributionAccountPriority)
            .FirstOrDefault();
    }

    private static bool AllowsAdditionalContribution(string accountType) =>
        !string.Equals(accountType, AccountTypes.NisaLegacy, StringComparison.OrdinalIgnoreCase);

    private static int GetContributionAccountPriority(string accountType) =>
        accountType switch
        {
            AccountTypes.NisaAccumulation => 0,
            AccountTypes.NisaGrowth => 1,
            AccountTypes.Specific => 2,
            AccountTypes.General => 3,
            _ => 9
        };

    private static int GetAccountSortIndex(string accountType)
    {
        for (var i = 0; i < AccountScopeOrder.Length; i++)
        {
            if (string.Equals(AccountScopeOrder[i], accountType, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static string GetAccountDisplayName(string accountType) =>
        accountType switch
        {
            AccountTypes.NisaLegacy => "旧NISA",
            AccountTypes.NisaAccumulation => "NISAつみたて投資枠",
            AccountTypes.NisaGrowth => "NISA成長投資枠",
            AccountTypes.Specific => "特定口座",
            AccountTypes.General => "一般口座",
            _ => "未分類"
        };

    private static int CountFunds(IReadOnlyList<StockPosition> positions) =>
        positions
            .Select(GetFundScopeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static MutualFundActualAnnualizedReturnEstimate? CalculateActualAnnualizedReturnEstimate(
        IReadOnlyList<StockPosition> positions,
        decimal currentMarketValue,
        decimal currentCost,
        IEnumerable<BrokerTrade>? brokerTrades)
    {
        var today = DateTime.Today;
        var selectedStockIds = positions.Select(position => position.Stock.Id).Where(id => id > 0).ToHashSet();
        var selectedTrades = (brokerTrades ?? Array.Empty<BrokerTrade>())
            .Where(trade => selectedStockIds.Contains(trade.StockId))
            .Where(trade => trade.TradeDate != DateTime.MinValue && trade.TradeDate <= today)
            .OrderBy(trade => trade.TradeDate)
            .ToList();

        var xirrEstimate = CalculateXirrEstimate(selectedTrades, currentMarketValue, today);
        if (xirrEstimate is not null)
        {
            return xirrEstimate;
        }

        var oldestTradeDate = selectedTrades
            .Where(IsCapitalFlowTrade)
            .Select(trade => trade.TradeDate.Date)
            .Where(date => date != DateTime.MinValue && date < today)
            .DefaultIfEmpty(DateTime.MinValue)
            .Min();
        var oldestPurchaseDate = positions
            .Select(position => position.Purchase.PurchaseDate.Date)
            .Where(date => date != DateTime.MinValue && date < today)
            .DefaultIfEmpty(DateTime.MinValue)
            .Min();
        var periodStart = oldestTradeDate != DateTime.MinValue ? oldestTradeDate : oldestPurchaseDate;
        if (periodStart == DateTime.MinValue || currentMarketValue <= 0m || currentCost <= 0m)
        {
            return null;
        }

        var holdingYears = (today - periodStart).TotalDays / 365.2425d;
        if (holdingYears <= 0d)
        {
            return null;
        }

        var multiple = (double)(currentMarketValue / currentCost);
        if (multiple <= 0d)
        {
            return null;
        }

        var annualized = ToDecimalPercent(Math.Pow(multiple, 1d / holdingYears) - 1d);
        if (annualized is null)
        {
            return null;
        }

        return new MutualFundActualAnnualizedReturnEstimate
        {
            AnnualizedReturnRate = annualized.Value,
            DisplayName = "実績参考年利",
            Method = "簡易年率換算",
            PeriodStart = periodStart,
            PeriodEnd = today,
            Precision = "参考値",
            Note = "積立途中の入金時期を厳密には反映していません"
        };
    }

    private static MutualFundActualAnnualizedReturnEstimate? CalculateXirrEstimate(
        IReadOnlyList<BrokerTrade> trades,
        decimal currentMarketValue,
        DateTime today)
    {
        if (trades.Count == 0 || currentMarketValue <= 0m)
        {
            return null;
        }

        var cashFlows = trades
            .Select(ToCashFlow)
            .Where(flow => flow.Amount != 0m && flow.Date != DateTime.MinValue && flow.Date <= today)
            .ToList();
        if (cashFlows.Count == 0)
        {
            return null;
        }

        cashFlows.Add(new CashFlow(today, currentMarketValue));
        if (!cashFlows.Any(flow => flow.Amount < 0m) || !cashFlows.Any(flow => flow.Amount > 0m))
        {
            return null;
        }

        var rate = SolveXirr(cashFlows);
        if (rate is null)
        {
            return null;
        }

        var annualized = ToDecimalPercent(rate.Value);
        if (annualized is null)
        {
            return null;
        }

        return new MutualFundActualAnnualizedReturnEstimate
        {
            AnnualizedReturnRate = annualized.Value,
            DisplayName = "実績参考年利",
            Method = "XIRR",
            PeriodStart = cashFlows.Min(flow => flow.Date),
            PeriodEnd = today,
            Precision = "取引履歴",
            Note = "入出金日を反映した年率換算です"
        };
    }

    private static CashFlow ToCashFlow(BrokerTrade trade)
    {
        var amount = Math.Abs(trade.SettlementAmountJpy);
        if (amount <= 0m)
        {
            var exchangeRate = trade.ExchangeRate <= 0m ? 1m : trade.ExchangeRate;
            amount = Math.Abs(trade.SignedQuantity * trade.UnitPrice * exchangeRate);
        }

        if (amount <= 0m)
        {
            return new CashFlow(trade.TradeDate.Date, 0m);
        }

        return trade.SignedQuantity < 0m || IsSellTrade(trade.TradeType)
            ? new CashFlow(trade.TradeDate.Date, amount)
            : new CashFlow(trade.TradeDate.Date, -amount);
    }

    private static bool IsCapitalFlowTrade(BrokerTrade trade) =>
        trade.SignedQuantity != 0m || trade.SettlementAmountJpy != 0m;

    private static bool IsSellTrade(string tradeType) =>
        tradeType.Contains("Sell", StringComparison.OrdinalIgnoreCase) ||
        tradeType.Contains("Sale", StringComparison.OrdinalIgnoreCase) ||
        tradeType.Contains("売", StringComparison.Ordinal);

    private static double? SolveXirr(IReadOnlyList<CashFlow> cashFlows)
    {
        var low = -0.999999d;
        var high = 1000d;
        var lowValue = Xnpv(cashFlows, low);
        var highValue = Xnpv(cashFlows, high);
        if (!double.IsFinite(lowValue) || !double.IsFinite(highValue) || Math.Sign(lowValue) == Math.Sign(highValue))
        {
            return null;
        }

        for (var i = 0; i < 200; i++)
        {
            var mid = (low + high) / 2d;
            var value = Xnpv(cashFlows, mid);
            if (!double.IsFinite(value))
            {
                return null;
            }

            if (Math.Abs(value) < 0.0001d)
            {
                return mid;
            }

            if (Math.Sign(value) == Math.Sign(lowValue))
            {
                low = mid;
                lowValue = value;
            }
            else
            {
                high = mid;
            }
        }

        return (low + high) / 2d;
    }

    private static double Xnpv(IReadOnlyList<CashFlow> cashFlows, double rate)
    {
        var start = cashFlows.Min(flow => flow.Date);
        var total = 0d;
        foreach (var flow in cashFlows)
        {
            var years = (flow.Date - start).TotalDays / 365.2425d;
            total += (double)flow.Amount / Math.Pow(1d + rate, years);
        }

        return total;
    }

    private static decimal? ToDecimalPercent(double rate)
    {
        if (!double.IsFinite(rate))
        {
            return null;
        }

        var percent = rate * 100d;
        if (Math.Abs(percent) > 1_000_000d)
        {
            percent = Math.Sign(percent) * 1_000_000d;
        }

        return (decimal)percent;
    }

    private static decimal RoundYen(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string NormalizeDecimal(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####");

    private static string FirstNonBlank(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private readonly record struct FundValuation(decimal MarketValueJpy, decimal CostJpy);

    private readonly record struct CashFlow(DateTime Date, decimal Amount);
}
