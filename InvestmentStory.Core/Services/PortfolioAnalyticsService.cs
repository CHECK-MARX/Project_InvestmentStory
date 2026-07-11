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
        var stockValue = snapshotList
            .Where(x => !x.Position.IsMutualFund)
            .Sum(x => x.CurrentMarketValueJpy);
        var mutualFundValue = snapshotList
            .Where(x => x.Position.IsMutualFund)
            .Sum(x => x.CurrentMarketValueJpy);
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
            UsdJpyRate = usdJpyRate,
            StockValueJpy = stockValue,
            MutualFundValueJpy = mutualFundValue,
            CashValueJpy = 0m
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
        int year,
        IEnumerable<StockSnapshot>? snapshots = null)
    {
        var dividendList = dividends
            .Where(x => !string.Equals(x.DividendStatus, DividendConstants.Replaced, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var snapshotList = snapshots?.ToList() ?? new List<StockSnapshot>();

        if (mode is "現在保有ベース年間配当" or "税引後年間見込み" or "取得額ベース利回り")
        {
            return BuildSnapshotDividendRanking(snapshotList, mode);
        }

        if (mode == "配当成長率")
        {
            return BuildDividendGrowthRanking(dividendList, year);
        }

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

    private static IReadOnlyList<DividendRankingItem> BuildSnapshotDividendRanking(
        IReadOnlyList<StockSnapshot> snapshots,
        string mode)
    {
        var ranked = snapshots
            .Where(x => x.AnnualDividendForecastJpy > 0m || mode == "取得額ベース利回り")
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Position.Stock.Ticker) ? x.Position.Stock.Name : x.Position.Stock.Ticker)
            .Select(x =>
            {
                var amount = x.Sum(y => y.AnnualDividendForecastJpy);
                var cost = x.Sum(y => y.PurchaseTotalJpy);
                var rate = cost <= 0m ? 0m : amount / cost * 100m;
                var afterTax = x.Sum(y => EstimateAfterTaxDividend(y));
                return new
                {
                    Ticker = x.Key,
                    Name = x.First().Position.Stock.Name,
                    Amount = mode == "税引後年間見込み" ? afterTax : amount,
                    Rate = rate
                };
            })
            .OrderByDescending(x => mode == "取得額ベース利回り" ? x.Rate : x.Amount)
            .Take(10)
            .ToList();
        var total = ranked.Sum(x => x.Amount);
        var rank = 1;
        return ranked.Select(x => new DividendRankingItem
        {
            Rank = rank++,
            Ticker = x.Ticker,
            Name = x.Name,
            AmountJpy = x.Amount,
            Rate = x.Rate,
            ShareOfTotal = total <= 0m ? 0m : x.Amount / total * 100m
        }).ToList();
    }

    private static IReadOnlyList<DividendRankingItem> BuildDividendGrowthRanking(
        IReadOnlyList<DividendPayment> dividends,
        int year)
    {
        var current = dividends
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus) && x.PaymentDate.Year == year)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Ticker) ? x.StockName : x.Ticker)
            .ToDictionary(x => x.Key, x => (Name: x.First().StockName, Amount: x.Sum(NetDividendJpy)), StringComparer.OrdinalIgnoreCase);
        var previous = dividends
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus) && x.PaymentDate.Year == year - 1)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Ticker) ? x.StockName : x.Ticker)
            .ToDictionary(x => x.Key, x => x.Sum(NetDividendJpy), StringComparer.OrdinalIgnoreCase);

        var rank = 1;
        return current
            .Select(x =>
            {
                previous.TryGetValue(x.Key, out var previousAmount);
                var growthRate = previousAmount <= 0m ? 0m : (x.Value.Amount - previousAmount) / previousAmount * 100m;
                return new DividendRankingItem
                {
                    Rank = 0,
                    Ticker = x.Key,
                    Name = x.Value.Name,
                    AmountJpy = x.Value.Amount,
                    Rate = growthRate,
                    PreviousYearDifferenceJpy = x.Value.Amount - previousAmount
                };
            })
            .OrderByDescending(x => x.Rate)
            .Take(10)
            .Select(x => new DividendRankingItem
            {
                Rank = rank++,
                Ticker = x.Ticker,
                Name = x.Name,
                AmountJpy = x.AmountJpy,
                Rate = x.Rate,
                ShareOfTotal = x.Rate,
                PreviousYearDifferenceJpy = x.PreviousYearDifferenceJpy
            })
            .ToList();
    }

    private static decimal EstimateAfterTaxDividend(StockSnapshot snapshot)
    {
        if (DividendConstants.IsNisaAccount(snapshot.Position.Stock.AccountType))
        {
            return snapshot.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
                ? snapshot.AnnualDividendForecastJpy
                : snapshot.AnnualDividendForecastJpy * 0.9m;
        }

        return snapshot.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? snapshot.AnnualDividendForecastJpy * 0.79685m
            : snapshot.AnnualDividendForecastJpy * 0.717165m;
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

    public IReadOnlyList<FxSensitivityPoint> BuildFxSensitivity(
        IEnumerable<StockSnapshot> snapshots,
        decimal currentUsdJpyRate)
    {
        var snapshotList = snapshots.ToList();
        if (currentUsdJpyRate <= 0m)
        {
            currentUsdJpyRate = snapshotList
                .Where(x => x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Position.CurrentHolding.CurrentExchangeRate)
                .FirstOrDefault(x => x > 0m);
        }

        if (currentUsdJpyRate <= 0m)
        {
            return Array.Empty<FxSensitivityPoint>();
        }

        var totalMarketValueJpy = snapshotList.Sum(x => x.CurrentMarketValueJpy);
        var foreignMarketValueUsd = snapshotList
            .Where(x => x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.CurrentMarketValue);
        if (foreignMarketValueUsd <= 0m)
        {
            return Array.Empty<FxSensitivityPoint>();
        }

        var currentForeignMarketValueJpy = foreignMarketValueUsd * currentUsdJpyRate;
        var nonForeignMarketValueJpy = totalMarketValueJpy - currentForeignMarketValueJpy;
        var deltas = new[] { -10m, -5m, -1m, 1m, 5m, 10m };

        return deltas
            .Select(delta =>
            {
                var rate = Math.Max(0m, currentUsdJpyRate + delta);
                var marketValue = nonForeignMarketValueJpy + foreignMarketValueUsd * rate;
                return new FxSensitivityPoint
                {
                    RateDelta = delta,
                    UsdJpyRate = rate,
                    TotalMarketValueJpy = marketValue,
                    ChangeFromCurrentJpy = marketValue - totalMarketValueJpy
                };
            })
            .ToList();
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
