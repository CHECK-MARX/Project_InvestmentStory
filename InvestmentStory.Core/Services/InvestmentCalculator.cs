using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class InvestmentCalculator
{
    public StockSnapshot CreateSnapshot(StockPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var purchase = position.Purchase;
        var current = position.CurrentHolding;
        var stock = position.Stock;
        var purchaseExchangeRate = NormalizeExchangeRate(stock.Currency, purchase.ExchangeRate);
        var currentExchangeRate = NormalizeExchangeRate(stock.Currency, current.CurrentExchangeRate);
        var purchaseTotal = CalculatePurchaseTotal(purchase.Shares, purchase.UnitPrice);
        var currentMarketValue = CalculateCurrentMarketValue(current.CurrentShares, current.CurrentPrice);
        var unrealizedGain = currentMarketValue - purchaseTotal;
        var annualDividend = CalculateAnnualDividendForecast(current.CurrentShares, current.AnnualDividendPerShare);
        var purchaseTotalJpy = CalculateJpyAmount(purchaseTotal, purchaseExchangeRate);
        var currentMarketValueJpy = CalculateJpyAmount(currentMarketValue, currentExchangeRate);
        var unrealizedGainJpy = currentMarketValueJpy - purchaseTotalJpy;
        var annualDividendJpy = CalculateJpyAmount(annualDividend, currentExchangeRate);

        return new StockSnapshot
        {
            Position = position,
            PurchaseTotal = purchaseTotal,
            EffectiveAcquisitionPrice = DivideOrZero(purchaseTotal, current.CurrentShares),
            CurrentMarketValue = currentMarketValue,
            UnrealizedGain = unrealizedGain,
            UnrealizedGainRate = CalculateRate(unrealizedGain, purchaseTotal),
            UnrealizedGainRateJpy = CalculateRate(unrealizedGainJpy, purchaseTotalJpy),
            Multiple = DivideOrZero(currentMarketValue, purchaseTotal),
            AnnualDividendForecast = annualDividend,
            AnnualDividendForecastJpy = annualDividendJpy,
            MonthlyPassiveIncomeForecast = annualDividend / 12m,
            MonthlyPassiveIncomeForecastJpy = annualDividendJpy / 12m,
            YieldOnCost = CalculateRate(annualDividend, purchaseTotal),
            CurrentDividendYield = CalculateRate(current.AnnualDividendPerShare, current.CurrentPrice),
            ShareChangeRatio = DivideOrZero(current.CurrentShares, purchase.Shares),
            PurchaseTotalJpy = purchaseTotalJpy,
            CurrentMarketValueJpy = currentMarketValueJpy,
            UnrealizedGainJpy = unrealizedGainJpy,
            CurrencyImpactJpy = unrealizedGainJpy - CalculateJpyAmount(unrealizedGain, currentExchangeRate)
        };
    }

    public DashboardSummary CreateDashboardSummary(
        IEnumerable<StockSnapshot> snapshots,
        IEnumerable<DividendPayment> dividendPayments,
        IncomeGoal? goal,
        DateTime asOf,
        ExchangeRateQuote? usdJpyQuote = null)
    {
        var snapshotList = snapshots.ToList();
        var dividendList = dividendPayments.ToList();
        var actualDividends = dividendList
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus))
            .ToList();
        var plannedDividends = dividendList
            .Where(x => DividendConstants.IsUnconfirmed(x.DividendStatus))
            .ToList();

        var totalCurrentMarketValueJpy = snapshotList.Sum(x => x.CurrentMarketValueJpy);
        var totalPurchaseAmountJpy = snapshotList.Sum(x => x.PurchaseTotalJpy);
        var totalGainJpy = totalCurrentMarketValueJpy - totalPurchaseAmountJpy;
        var foreignSnapshots = snapshotList
            .Where(x => x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var latestExchangeInfo = foreignSnapshots
            .OrderByDescending(x => x.Position.CurrentHolding.ExchangeRateAcquiredAt)
            .FirstOrDefault();
        var annualForecastJpy = snapshotList.Sum(x => x.AnnualDividendForecastJpy);
        var actualGrossThisYear = actualDividends
            .Where(x => x.PaymentDate.Year == asOf.Year)
            .Sum(x => x.GrossAmountJpy > 0m ? x.GrossAmountJpy : x.GrossAmount * NormalizeExchangeRate(x.Currency, x.ExchangeRate));
        var thisMonthActual = actualDividends
            .Where(x => x.PaymentDate.Year == asOf.Year && x.PaymentDate.Month == asOf.Month)
            .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount);
        var thisYearActual = actualDividends
            .Where(x => x.PaymentDate.Year == asOf.Year)
            .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount);
        var thisMonthPlanned = plannedDividends
            .Where(x => x.PaymentDate.Year == asOf.Year && x.PaymentDate.Month == asOf.Month)
            .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount);
        var thisYearPlanned = plannedDividends
            .Where(x => x.PaymentDate.Year == asOf.Year)
            .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount);

        return new DashboardSummary
        {
            TotalCurrentMarketValueJpy = totalCurrentMarketValueJpy,
            TotalPurchaseAmountJpy = totalPurchaseAmountJpy,
            TotalUnrealizedGainJpy = totalGainJpy,
            TotalUnrealizedGainRate = CalculateRate(totalGainJpy, totalPurchaseAmountJpy),
            ForeignAssetTotalUsd = foreignSnapshots.Sum(x => x.CurrentMarketValueUsd),
            ForeignAssetTotalJpy = foreignSnapshots.Sum(x => x.CurrentMarketValueJpy),
            FxIncludedUnrealizedGainJpy = totalGainJpy,
            CurrentUsdJpyRate = usdJpyQuote?.Rate
                ?? latestExchangeInfo?.Position.CurrentHolding.CurrentExchangeRate
                ?? 0m,
            ExchangeRateAcquiredAt = usdJpyQuote?.AcquiredAt
                ?? latestExchangeInfo?.Position.CurrentHolding.ExchangeRateAcquiredAt
                ?? DateTime.MinValue,
            ExchangeRateSource = usdJpyQuote?.Source
                ?? latestExchangeInfo?.Position.CurrentHolding.ExchangeRateSource
                ?? string.Empty,
            ExchangeRateInputType = usdJpyQuote?.InputType
                ?? latestExchangeInfo?.Position.CurrentHolding.ExchangeRateInputType
                ?? string.Empty,
            ThisMonthPassiveIncomeJpy = thisMonthActual,
            ThisYearPassiveIncomeJpy = thisYearActual,
            ThisMonthPlannedIncomeJpy = thisMonthPlanned,
            ThisYearPlannedIncomeJpy = thisYearPlanned,
            ThisYearForecastIncludingPlannedJpy = thisYearActual + thisYearPlanned,
            AnnualPassiveIncomeForecastJpy = annualForecastJpy,
            AnnualGrossDividendForecastJpy = actualGrossThisYear > 0m ? actualGrossThisYear + thisYearPlanned : annualForecastJpy,
            AnnualNetDividendForecastJpy = thisYearActual + thisYearPlanned,
            MonthlyAveragePassiveIncomeForecastJpy = annualForecastJpy / 12m,
            ForeignTaxActualJpy = actualDividends
                .Where(x => x.PaymentDate.Year == asOf.Year)
                .Sum(x => x.ForeignTaxAmountJpy),
            DomesticTaxActualJpy = actualDividends
                .Where(x => x.PaymentDate.Year == asOf.Year)
                .Sum(x => x.DomesticTaxAmountJpy > 0m ? x.DomesticTaxAmountJpy : x.TaxAmount * NormalizeExchangeRate(x.Currency, x.ExchangeRate)),
            TotalTaxActualJpy = actualDividends
                .Where(x => x.PaymentDate.Year == asOf.Year)
                .Sum(x => x.TotalTaxAmountJpy > 0m ? x.TotalTaxAmountJpy : x.TaxAmount * NormalizeExchangeRate(x.Currency, x.ExchangeRate)),
            NisaDividendActualJpy = actualDividends
                .Where(x => x.PaymentDate.Year == asOf.Year && x.IsNisa)
                .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount),
            TaxableDividendActualJpy = actualDividends
                .Where(x => x.PaymentDate.Year == asOf.Year && !x.IsNisa)
                .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount),
            DomesticStockDividendActualJpy = actualDividends
                .Where(x => x.PaymentDate.Year == asOf.Year && IsJpyCurrency(x.Currency))
                .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount),
            ForeignStockDividendActualJpy = actualDividends
                .Where(x => x.PaymentDate.Year == asOf.Year && !IsJpyCurrency(x.Currency))
                .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount),
            AnnualGoalAchievementRate = goal is null ? 0m : CalculateRate(thisYearActual, goal.AnnualPassiveIncomeGoal),
            MonthlyGoalAchievementRate = goal is null ? 0m : CalculateRate(thisMonthActual, goal.MonthlyPassiveIncomeGoal),
            AnnualGoalGapJpy = goal is null ? 0m : Math.Max(0m, goal.AnnualPassiveIncomeGoal - thisYearActual),
            MonthlyGoalGapJpy = goal is null ? 0m : Math.Max(0m, goal.MonthlyPassiveIncomeGoal - thisMonthActual)
        };
    }

    public IReadOnlyList<DividendAggregate> AggregateMonthlyDividends(
        IEnumerable<DividendPayment> dividendPayments,
        int year)
    {
        var monthly = dividendPayments
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus))
            .Where(x => x.PaymentDate.Year == year)
            .GroupBy(x => x.PaymentDate.Month)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.NetAmountJpy > 0m ? y.NetAmountJpy : y.JpyAmount));

        return Enumerable.Range(1, 12)
            .Select(month => new DividendAggregate
            {
                Label = $"{month}月",
                AmountJpy = monthly.TryGetValue(month, out var amount) ? amount : 0m
            })
            .ToList();
    }

    public IReadOnlyList<DividendAggregate> AggregateYearlyDividends(IEnumerable<DividendPayment> dividendPayments)
    {
        return dividendPayments
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus))
            .GroupBy(x => x.PaymentDate.Year)
            .OrderBy(x => x.Key)
            .Select(x => new DividendAggregate
            {
                Label = x.Key.ToString(),
                AmountJpy = x.Sum(y => y.NetAmountJpy > 0m ? y.NetAmountJpy : y.JpyAmount)
            })
            .ToList();
    }

    public IReadOnlyList<DividendAggregate> AggregateDividendsByStock(IEnumerable<DividendPayment> dividendPayments)
    {
        return dividendPayments
            .Where(x => DividendConstants.IsVisibleActual(x.DividendStatus))
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Ticker) ? x.StockName : x.Ticker)
            .OrderByDescending(x => x.Sum(y => y.NetAmountJpy > 0m ? y.NetAmountJpy : y.JpyAmount))
            .Select(x => new DividendAggregate
            {
                Label = x.Key,
                AmountJpy = x.Sum(y => y.NetAmountJpy > 0m ? y.NetAmountJpy : y.JpyAmount)
            })
            .ToList();
    }

    public PassiveIncomeSimulationResult SimulatePassiveIncome(PassiveIncomeSimulationInput input, int years = 10)
    {
        ArgumentNullException.ThrowIfNull(input);

        var projections = new List<PassiveIncomeProjection>();
        var annualIncome = input.CurrentAnnualPassiveIncome;
        var yearlyNewIncome = input.MonthlyAdditionalInvestment * 12m * (input.AssumedDividendYieldRate / 100m);
        int? achievementYear = null;
        int? yearsToTarget = null;

        for (var i = 1; i <= years; i++)
        {
            var previousAnnualIncome = annualIncome;
            annualIncome = annualIncome * (1m + input.AnnualDividendGrowthRate / 100m) + yearlyNewIncome;
            var year = input.StartYear + i;

            projections.Add(new PassiveIncomeProjection
            {
                Year = year,
                YearsFromNow = i,
                AnnualPassiveIncome = annualIncome,
                YearOverYearIncrease = annualIncome - previousAnnualIncome,
                TargetAchievementRate = CalculateRate(annualIncome, input.TargetAnnualPassiveIncome)
            });

            if (achievementYear is null && annualIncome >= input.TargetAnnualPassiveIncome && input.TargetAnnualPassiveIncome > 0)
            {
                achievementYear = year;
                yearsToTarget = i;
            }
        }

        return new PassiveIncomeSimulationResult
        {
            Projections = projections,
            TargetAchievementYear = achievementYear,
            YearsToTarget = yearsToTarget
        };
    }

    public decimal CalculatePurchaseTotal(decimal shares, decimal unitPrice) => shares * unitPrice;

    public decimal CalculateCurrentMarketValue(decimal currentShares, decimal currentPrice) => currentShares * currentPrice;

    public decimal CalculateAnnualDividendForecast(decimal currentShares, decimal annualDividendPerShare) =>
        currentShares * annualDividendPerShare;

    public decimal CalculateJpyAmount(decimal amount, decimal exchangeRate) => amount * NormalizeExchangeRate(exchangeRate);

    public decimal CalculateRate(decimal numerator, decimal denominator) => DivideOrZero(numerator, denominator) * 100m;

    private static decimal DivideOrZero(decimal numerator, decimal denominator) =>
        denominator == 0m ? 0m : numerator / denominator;

    private static decimal NormalizeExchangeRate(string currency, decimal exchangeRate)
    {
        return IsJpyCurrency(currency) ? 1m : NormalizeExchangeRate(exchangeRate);
    }

    private static decimal NormalizeExchangeRate(decimal exchangeRate) => exchangeRate == 0m ? 1m : exchangeRate;

    private static bool IsJpyCurrency(string currency)
    {
        return currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
            currency.Equals("YEN", StringComparison.OrdinalIgnoreCase) ||
            currency == "円";
    }
}
