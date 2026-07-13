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
        MutualFundSimulationInput input)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(input);

        var selectedPositions = SelectPositions(positions, scopeKey).ToList();
        var requestedContribution = Math.Max(0m, input.MonthlyContributionJpy);
        var contributionAccountType = ResolveContributionAccountType(selectedPositions, scopeKey);
        var allowsContribution = contributionAccountType is not null;
        var effectiveMonthlyContribution = allowsContribution ? requestedContribution : 0m;
        var summary = BuildSummary(selectedPositions, allowsContribution, effectiveMonthlyContribution);
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
                .Where(position => string.Equals(GetFundScopeKey(position), fundKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return currentPositions;
    }

    private static MutualFundPortfolioSummary BuildSummary(
        IReadOnlyList<StockPosition> positions,
        bool allowsContribution,
        decimal effectiveMonthlyContribution)
    {
        var values = positions.Select(CreateValuation).ToList();
        var marketValue = values.Sum(x => x.MarketValueJpy);
        var cost = values.Sum(x => x.CostJpy);
        var gain = marketValue - cost;

        return new MutualFundPortfolioSummary
        {
            CurrentMarketValueJpy = marketValue,
            CurrentCostJpy = cost,
            UnrealizedGainJpy = gain,
            UnrealizedGainRate = cost == 0m ? 0m : gain / cost * 100m,
            ActualAnnualizedReturnRate = CalculateActualAnnualizedReturnRateFromPurchaseDates(positions),
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

    private static decimal? CalculateActualAnnualizedReturnRateFromPurchaseDates(IReadOnlyList<StockPosition> positions)
    {
        var today = DateTime.Today;
        var weightedReturns = new List<(decimal Rate, decimal Weight)>();
        foreach (var position in positions)
        {
            var valuation = CreateValuation(position);
            if (valuation.CostJpy <= 0m || valuation.MarketValueJpy <= 0m)
            {
                continue;
            }

            var purchaseDate = position.Purchase.PurchaseDate.Date;
            if (purchaseDate == DateTime.MinValue || purchaseDate >= today)
            {
                continue;
            }

            var years = (today - purchaseDate).TotalDays / 365.25d;
            if (years < 0.25d)
            {
                continue;
            }

            var multiple = (double)(valuation.MarketValueJpy / valuation.CostJpy);
            if (multiple <= 0d)
            {
                continue;
            }

            var annualized = (decimal)((Math.Pow(multiple, 1d / years) - 1d) * 100d);
            weightedReturns.Add((annualized, valuation.CostJpy));
        }

        var totalWeight = weightedReturns.Sum(x => x.Weight);
        if (totalWeight <= 0m)
        {
            return null;
        }

        return weightedReturns.Sum(x => x.Rate * x.Weight) / totalWeight;
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
}
