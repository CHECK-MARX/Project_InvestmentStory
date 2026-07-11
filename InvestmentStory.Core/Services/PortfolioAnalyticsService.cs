using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class PortfolioAnalyticsService
{
    public PortfolioSnapshot CreatePortfolioSnapshot(
        IEnumerable<StockSnapshot> snapshots,
        IEnumerable<DividendPayment> dividends,
        decimal realizedGainLossJpy,
        decimal usdJpyRate,
        DateTime snapshotDate)
    {
        var snapshotList = snapshots.ToList();
        var actualDividends = dividends
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus))
            .ToList();
        var cumulativeDividend = actualDividends.Sum(NetDividendJpy);
        var totalMarketValue = snapshotList.Sum(x => x.CurrentMarketValueJpy);
        var totalCost = snapshotList.Sum(x => x.PurchaseTotalJpy);
        var unrealized = totalMarketValue - totalCost;

        return new PortfolioSnapshot
        {
            SnapshotDate = snapshotDate.Date,
            TotalMarketValueJpy = totalMarketValue,
            TotalCostBasisJpy = totalCost,
            UnrealizedGainLossJpy = unrealized,
            CumulativeDividendJpy = cumulativeDividend,
            RealizedGainLossJpy = realizedGainLossJpy,
            TotalReturnJpy = unrealized + realizedGainLossJpy + cumulativeDividend,
            UsdJpyRate = usdJpyRate
        };
    }

    public IReadOnlyList<MonthlyDividendBreakdown> BuildMonthlyDividendBreakdown(
        IEnumerable<DividendPayment> dividends,
        int year,
        decimal monthlyGoalJpy)
    {
        var dividendList = dividends
            .Where(x => !string.Equals(x.DividendStatus, DividendConstants.Replaced, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var actual = dividendList
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus) && x.PaymentDate.Year == year)
            .GroupBy(x => x.PaymentDate.Month)
            .ToDictionary(x => x.Key, x => x.Sum(NetDividendJpy));
        var planned = dividendList
            .Where(x => DividendConstants.IsUnconfirmed(x.DividendStatus) && x.PaymentDate.Year == year)
            .Where(x => !HasActualReplacement(dividendList, x))
            .GroupBy(x => x.PaymentDate.Month)
            .ToDictionary(x => x.Key, x => x.Sum(NetDividendJpy));
        var previous = dividendList
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus) && x.PaymentDate.Year == year - 1)
            .GroupBy(x => x.PaymentDate.Month)
            .ToDictionary(x => x.Key, x => x.Sum(NetDividendJpy));

        return Enumerable.Range(1, 12)
            .Select(month => new MonthlyDividendBreakdown
            {
                Year = year,
                Month = month,
                ActualJpy = actual.TryGetValue(month, out var actualAmount) ? actualAmount : 0m,
                PlannedJpy = planned.TryGetValue(month, out var plannedAmount) ? plannedAmount : 0m,
                PreviousYearActualJpy = previous.TryGetValue(month, out var previousAmount) ? previousAmount : 0m,
                MonthlyGoalJpy = monthlyGoalJpy
            })
            .ToList();
    }

    public IReadOnlyList<DividendRankingItem> BuildDividendRanking(
        IEnumerable<DividendPayment> dividends,
        string mode,
        int year)
    {
        var dividendList = dividends
            .Where(x => !string.Equals(x.DividendStatus, DividendConstants.Replaced, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var current = mode switch
        {
            "今年着地見込み" => dividendList.Where(x => x.PaymentDate.Year == year && (DividendConstants.IsVisibleActual(x.DividendStatus) || DividendConstants.IsUnconfirmed(x.DividendStatus))),
            "累計受取配当" => dividendList.Where(x => DividendConstants.IsVisibleActual(x.DividendStatus)),
            _ => dividendList.Where(x => DividendConstants.IsVisibleActual(x.DividendStatus) && x.PaymentDate.Year == year)
        };

        var previousByTicker = dividendList
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus) && x.PaymentDate.Year == year - 1)
            .GroupBy(x => x.Ticker)
            .ToDictionary(x => x.Key, x => x.Sum(NetDividendJpy), StringComparer.OrdinalIgnoreCase);
        var grouped = current
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Ticker) ? x.StockName : x.Ticker)
            .Select(x => new
            {
                Ticker = x.Key,
                Name = x.OrderByDescending(y => y.PaymentDate).FirstOrDefault()?.StockName ?? x.Key,
                Amount = x.Sum(NetDividendJpy)
            })
            .OrderByDescending(x => x.Amount)
            .Take(10)
            .ToList();
        var total = grouped.Sum(x => x.Amount);
        var rank = 1;

        return grouped.Select(x =>
        {
            previousByTicker.TryGetValue(x.Ticker, out var previous);
            return new DividendRankingItem
            {
                Rank = rank++,
                Ticker = x.Ticker,
                Name = x.Name,
                AmountJpy = x.Amount,
                ShareOfTotal = total <= 0m ? 0m : x.Amount / total * 100m,
                PreviousYearDifferenceJpy = x.Amount - previous
            };
        }).ToList();
    }

    public PortfolioReturnSummary BuildReturnSummary(
        IEnumerable<StockSnapshot> snapshots,
        IEnumerable<DividendPayment> dividends,
        decimal realizedGainLossJpy)
    {
        var snapshotList = snapshots.ToList();
        var totalCost = snapshotList.Sum(x => x.PurchaseTotalJpy);
        var totalMarket = snapshotList.Sum(x => x.CurrentMarketValueJpy);
        var unrealized = totalMarket - totalCost;
        var cumulativeDividend = dividends
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus))
            .Sum(NetDividendJpy);
        var totalReturn = unrealized + realizedGainLossJpy + cumulativeDividend;
        var weights = snapshotList
            .Where(x => x.CurrentMarketValueJpy > 0m && totalMarket > 0m)
            .Select(x => x.CurrentMarketValueJpy / totalMarket * 100m)
            .OrderByDescending(x => x)
            .ToList();

        return new PortfolioReturnSummary
        {
            UnrealizedGainLossJpy = unrealized,
            RealizedGainLossJpy = realizedGainLossJpy,
            CumulativeDividendJpy = cumulativeDividend,
            TotalReturnJpy = totalReturn,
            TotalReturnRate = totalCost <= 0m ? 0m : totalReturn / totalCost * 100m,
            CapitalRecoveryRate = totalCost <= 0m ? 0m : cumulativeDividend / totalCost * 100m,
            Top1ConcentrationRate = weights.Take(1).Sum(),
            Top3ConcentrationRate = weights.Take(3).Sum(),
            Top5ConcentrationRate = weights.Take(5).Sum(),
            Top10ConcentrationRate = weights.Take(10).Sum(),
            Hhi = weights.Sum(x => x * x)
        };
    }

    public SnapshotComparison CompareSnapshots(IEnumerable<PortfolioSnapshot> snapshots, DateTime asOf)
    {
        var ordered = snapshots.OrderBy(x => x.SnapshotDate).ToList();
        var current = ordered.LastOrDefault(x => x.SnapshotDate.Date <= asOf.Date);
        if (current is null)
        {
            return new SnapshotComparison();
        }

        var previousDay = ordered.LastOrDefault(x => x.SnapshotDate.Date < current.SnapshotDate.Date);
        var previousMonth = ordered.LastOrDefault(x => x.SnapshotDate.Date <= new DateTime(asOf.Year, asOf.Month, 1).AddDays(-1));
        return new SnapshotComparison
        {
            TotalAssetDayChangeJpy = previousDay is null ? null : current.TotalMarketValueJpy - previousDay.TotalMarketValueJpy,
            TotalAssetMonthChangeJpy = previousMonth is null ? null : current.TotalMarketValueJpy - previousMonth.TotalMarketValueJpy,
            UnrealizedDayChangeJpy = previousDay is null ? null : current.UnrealizedGainLossJpy - previousDay.UnrealizedGainLossJpy,
            UnrealizedMonthChangeJpy = previousMonth is null ? null : current.UnrealizedGainLossJpy - previousMonth.UnrealizedGainLossJpy
        };
    }

    private static bool HasActualReplacement(IReadOnlyList<DividendPayment> allPayments, DividendPayment planned)
    {
        if (planned.ReplacedByDividendId is not null || planned.MatchedActualDividendId is not null)
        {
            return true;
        }

        return allPayments.Any(x =>
            DividendConstants.IsVisibleActual(x.DividendStatus) &&
            x.StockId == planned.StockId &&
            x.PaymentDate.Date == planned.PaymentDate.Date &&
            Math.Abs(NetDividendJpy(x) - NetDividendJpy(planned)) < 1m);
    }

    private static decimal NetDividendJpy(DividendPayment payment) =>
        payment.NetAmountJpy > 0m ? payment.NetAmountJpy : payment.JpyAmount;
}
