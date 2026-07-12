using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MutualFundAssetSimulationService
{
    private const decimal UnitBase = 10000m;
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
                DisplayName = "全口座",
                PositionCount = currentPositions.Count
            }
        };

        var accountGroups = currentPositions
            .GroupBy(GetNormalizedAccountType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var accountType in AccountScopeOrder)
        {
            if (!accountGroups.TryGetValue(accountType, out var count))
            {
                continue;
            }

            options.Add(new MutualFundSimulationScopeOption
            {
                Key = MutualFundSimulationScopeKeys.Account(accountType),
                DisplayName = GetAccountDisplayName(accountType),
                PositionCount = count
            });
        }

        var fundGroups = currentPositions
            .GroupBy(GetFundScopeKey)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => GetFundDisplayName(group.First()), StringComparer.CurrentCulture);

        foreach (var group in fundGroups)
        {
            options.Add(new MutualFundSimulationScopeOption
            {
                Key = MutualFundSimulationScopeKeys.Fund(group.Key),
                DisplayName = $"ファンド別: {GetFundDisplayName(group.First())}",
                PositionCount = group.Count()
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
        var summary = BuildSummary(selectedPositions);
        var projectionYears = Math.Clamp(input.ProjectionYears, 1, 80);
        var targetYears = Math.Clamp(input.TargetYears, 1, 80);
        var months = projectionYears * 12;
        var targetMonths = targetYears * 12;
        var start = new DateTime(
            input.StartYear <= 0 ? DateTime.Today.Year : input.StartYear,
            Math.Clamp(input.StartMonth, 1, 12),
            1);
        var annualRate = Math.Max(input.ExpectedAnnualReturnRate, -99.99m);
        var monthlyRate = ToMonthlyRate(annualRate);
        var monthlyContribution = Math.Max(0m, input.MonthlyContributionJpy);
        var targetAmount = Math.Max(0m, input.TargetAmountJpy);

        var projections = BuildProjections(
            summary,
            monthlyContribution,
            monthlyRate,
            targetAmount,
            start,
            months);

        var targetProjection = projections.FirstOrDefault(x => x.MarketValueJpy >= targetAmount && targetAmount > 0m);
        DateTime? targetAchievementMonth;
        int? monthsToTarget;
        if (targetAmount > 0m && summary.CurrentMarketValueJpy >= targetAmount)
        {
            targetAchievementMonth = start;
            monthsToTarget = 0;
        }
        else
        {
            targetAchievementMonth = targetProjection?.YearMonth;
            monthsToTarget = targetProjection?.MonthsFromNow;
        }

        return new MutualFundAssetSimulationResult
        {
            Summary = summary,
            Projections = projections,
            ContributionComparisons = BuildContributionComparisons(
                summary.CurrentMarketValueJpy,
                monthlyContribution,
                monthlyRate,
                months),
            TargetAchievementMonth = targetAchievementMonth,
            MonthsToTarget = monthsToTarget,
            RequiredMonthlyContributionJpy = CalculateRequiredMonthlyContribution(
                summary.CurrentMarketValueJpy,
                targetAmount,
                monthlyRate,
                targetMonths)
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

    private static MutualFundPortfolioSummary BuildSummary(IReadOnlyList<StockPosition> positions)
    {
        var values = positions.Select(CreateValuation).ToList();
        var marketValue = values.Sum(x => x.MarketValueJpy);
        var cost = values.Sum(x => x.CostJpy);
        var gain = marketValue - cost;
        var fundCount = positions
            .Select(GetFundScopeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new MutualFundPortfolioSummary
        {
            CurrentMarketValueJpy = marketValue,
            CurrentCostJpy = cost,
            UnrealizedGainJpy = gain,
            UnrealizedGainRate = cost == 0m ? 0m : gain / cost * 100m,
            ActualAnnualizedReturnRate = CalculateActualAnnualizedReturnRate(positions),
            FundCount = fundCount,
            PositionCount = positions.Count
        };
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

    private static IReadOnlyList<MutualFundContributionComparison> BuildContributionComparisons(
        decimal currentMarketValue,
        decimal monthlyContribution,
        decimal monthlyRate,
        int months)
    {
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
            .GroupBy(BuildDeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(GetDataCompletenessScore)
                .ThenByDescending(position => position.MutualFund.UnitsHeld)
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
        var unitBase = fund.UnitBase <= 0m ? UnitBase : fund.UnitBase;
        var marketValue = fund.CurrentNav > 0m
            ? RoundYen(fund.UnitsHeld / unitBase * fund.CurrentNav)
            : RoundYen(fund.MarketValue);
        var cost = fund.AverageCostNav > 0m
            ? RoundYen(fund.UnitsHeld / unitBase * fund.AverageCostNav)
            : RoundYen(fund.AcquisitionAmount);

        return new FundValuation(marketValue, cost);
    }

    private static int GetDataCompletenessScore(StockPosition position)
    {
        var fund = position.MutualFund;
        var score = 0;
        if (fund.CurrentNav > 0m)
        {
            score += 4;
        }

        if (fund.AverageCostNav > 0m)
        {
            score += 4;
        }

        if (fund.MarketValue > 0m)
        {
            score += 2;
        }

        if (fund.AcquisitionAmount > 0m)
        {
            score += 2;
        }

        if (fund.NavDate != DateTime.MinValue)
        {
            score += 1;
        }

        return score;
    }

    private static string BuildDeduplicationKey(StockPosition position) =>
        string.Join(
            "|",
            PositionIdentityService.NormalizeBroker(position.Stock.Broker),
            GetFundScopeKey(position),
            GetNormalizedAccountType(position));

    private static string GetFundScopeKey(StockPosition position)
    {
        var securityId = PositionIdentityService.ResolveSecurityId(position);
        return PositionIdentityService.NormalizeSecurityId(securityId, AssetTypes.MutualFund);
    }

    private static string GetFundDisplayName(StockPosition position) =>
        FirstNonBlank(position.MutualFund.FundName, position.Stock.Name, position.Stock.Ticker, "投資信託");

    private static string GetNormalizedAccountType(StockPosition position) =>
        AccountTypeNormalizer.NormalizeForMutualFund(
            FirstNonBlank(position.MutualFund.AccountType, position.Stock.AccountType),
            position.Stock.CustodyType);

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

    private static decimal? CalculateActualAnnualizedReturnRate(IReadOnlyList<StockPosition> positions)
    {
        // 現在保有CSVだけでは正確な入出金タイミングが足りないため、ここでは架空の年利を作らない。
        // 将来、投信の全入出金履歴を渡せるようになった時点でIRR計算を追加する。
        return null;
    }

    private static decimal RoundYen(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

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
