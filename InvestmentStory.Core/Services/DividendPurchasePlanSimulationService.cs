using System.Globalization;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DividendPurchasePlanSimulationService
{
    private const decimal DomesticTaxRate = 20.315m;
    private const decimal UsForeignTaxRate = 10m;
    private readonly DividendTaxCalculator _taxCalculator = new();

    public DividendPurchasePlanResult Simulate(
        DividendPurchasePlanInput input,
        IReadOnlyList<TaxProfile>? taxProfiles = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        taxProfiles ??= Array.Empty<TaxProfile>();

        var targetYear = input.TargetYear is >= 2000 and <= 2200 ? input.TargetYear : DateTime.Today.Year;
        var purchaseDate = input.PlannedPurchaseDate == default
            ? new DateTime(targetYear, 1, 1)
            : input.PlannedPurchaseDate.Date;
        var target = Math.Max(0m, input.TargetAnnualNetDividendJpy);
        var allEvents = new List<DividendPurchasePlanEvent>();
        var holdings = new List<DividendPurchasePlanHolding>();

        foreach (var item in input.PlanItems)
        {
            var result = BuildHolding(item, targetYear, purchaseDate, target, input.DividendPayments, taxProfiles);
            holdings.Add(result.Holding);
            allEvents.AddRange(result.Events);
        }

        var nextYearTotal = holdings.Sum(x => x.PostAddNextYearNetDividendJpy);
        holdings = holdings
            .Select(x => CopyWithComposition(x, nextYearTotal))
            .OrderByDescending(x => x.TargetYearAdditionalNetDividendJpy)
            .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var monthlyTarget = target / 12m;
        var currentCumulative = 0m;
        var cumulative = 0m;
        var months = Enumerable.Range(1, 12).Select(month =>
        {
            var events = allEvents.Where(x => x.Month == month).ToList();
            var current = events.Sum(x => x.CurrentNetDividendJpy);
            var existingAdded = events.Where(x => !x.IsNewStock).Sum(x => x.AdditionalNetDividendJpy);
            var newAdded = events.Where(x => x.IsNewStock).Sum(x => x.AdditionalNetDividendJpy);
            currentCumulative += current;
            cumulative += current + existingAdded + newAdded;
            return new DividendPurchasePlanMonthlyResult
            {
                Year = targetYear,
                Month = month,
                CurrentNetDividendJpy = RoundYen(current),
                ExistingAdditionalNetDividendJpy = RoundYen(existingAdded),
                NewPurchaseNetDividendJpy = RoundYen(newAdded),
                MissedNetDividendJpy = RoundYen(events.Sum(x => x.MissedNetDividendJpy)),
                TargetNetDividendJpy = RoundYen(monthlyTarget),
                CurrentCumulativeNetDividendJpy = RoundYen(currentCumulative),
                CumulativeNetDividendJpy = RoundYen(cumulative),
                Events = events
            };
        }).ToList();

        var currentTargetYear = months.Sum(x => x.CurrentNetDividendJpy);
        var addedTargetYear = months.Sum(x => x.AdditionalNetDividendJpy);
        var plannedTargetYear = currentTargetYear + addedTargetYear;
        var missed = months.Sum(x => x.MissedNetDividendJpy);
        var investment = holdings.Sum(x => x.PlannedPurchaseAmountJpy);
        var currentMarketValue = input.PlanItems.Where(x => !x.IsNewStock).Sum(CurrentMarketValueJpy);
        var currentCost = input.PlanItems.Where(x => !x.IsNewStock).Sum(CurrentCostJpy);
        var currentFullNet = holdings.Where(x => !x.IsNewStock).Sum(x => x.CurrentAnnualNetDividendJpy);
        var fullAddedNet = holdings.Sum(x => x.NextYearAdditionalNetDividendJpy);
        var taxTotals = CalculateFullYearTaxTotals(input.PlanItems, taxProfiles);
        var currentCompositionTotal = holdings.Sum(x => x.CurrentAnnualNetDividendJpy);
        var composition = holdings
            .Where(x => x.PostAddNextYearNetDividendJpy > 0m)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Ticker) ? x.Name : x.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DividendPurchasePlanComposition
            {
                Ticker = group.Key,
                Name = group.First().Name,
                AnnualNetDividendJpy = RoundYen(group.Sum(x => x.PostAddNextYearNetDividendJpy)),
                CompositionRate = nextYearTotal <= 0m ? 0m : group.Sum(x => x.PostAddNextYearNetDividendJpy) / nextYearTotal * 100m,
                CurrentAnnualNetDividendJpy = RoundYen(group.Sum(x => x.CurrentAnnualNetDividendJpy)),
                CurrentCompositionRate = currentCompositionTotal <= 0m
                    ? 0m
                    : group.Sum(x => x.CurrentAnnualNetDividendJpy) / currentCompositionTotal * 100m
            })
            .OrderByDescending(x => x.AnnualNetDividendJpy)
            .ToList();

        return new DividendPurchasePlanResult
        {
            Summary = new DividendPurchasePlanSummary
            {
                CurrentTargetYearNetDividendJpy = RoundYen(currentTargetYear),
                PlannedTargetYearNetDividendJpy = RoundYen(plannedTargetYear),
                TargetYearDividendIncreaseJpy = RoundYen(addedTargetYear),
                MissedTargetYearNetDividendJpy = RoundYen(missed),
                NextYearAnnualNetDividendJpy = RoundYen(nextYearTotal),
                TargetAchievementRate = target <= 0m ? 0m : nextYearTotal / target * 100m,
                PlannedInvestmentJpy = RoundYen(investment),
                AdditionalInvestmentYieldRate = investment <= 0m ? 0m : fullAddedNet / investment * 100m,
                AdditionalInvestmentPaybackYears = fullAddedNet <= 0m ? 0m : investment / fullAddedNet,
                CurrentYieldRate = currentMarketValue <= 0m ? 0m : currentFullNet / currentMarketValue * 100m,
                YieldOnCostRate = currentCost <= 0m ? 0m : currentFullNet / currentCost * 100m,
                PostAddPortfolioYieldRate = currentMarketValue + investment <= 0m
                    ? 0m
                    : nextYearTotal / (currentMarketValue + investment) * 100m,
                CurrentMarketValueJpy = RoundYen(currentMarketValue),
                CurrentCostJpy = RoundYen(currentCost),
                ForeignTaxJpy = RoundYen(taxTotals.ForeignTaxAmountJpy),
                DomesticTaxJpy = RoundYen(taxTotals.DomesticTaxAmountJpy),
                TotalTaxJpy = RoundYen(taxTotals.TotalTaxAmountJpy)
            },
            Holdings = holdings,
            Months = months,
            Composition = composition
        };
    }

    private HoldingBuildResult BuildHolding(
        DividendGrowthPlanItem item,
        int targetYear,
        DateTime purchaseDate,
        decimal target,
        IReadOnlyList<DividendPayment> payments,
        IReadOnlyList<TaxProfile> taxProfiles)
    {
        var sourceItems = item.Components.Count > 0 ? item.Components : new[] { item };
        var currentEvents = new List<DividendPurchasePlanEvent>();
        decimal currentAnnualNet = 0m;

        foreach (var component in sourceItems)
        {
            if (component.IsNewStock || component.CurrentShares <= 0m)
            {
                continue;
            }

            var schedule = ResolveSchedule(component, targetYear, payments);
            var tax = CalculateTax(component, component.CurrentShares, component.AnnualDividendPerShare, false, taxProfiles);
            currentAnnualNet += tax.NetAmountJpy;
            if (schedule.Events.Count == 0)
            {
                continue;
            }

            foreach (var scheduleEvent in schedule.Events)
            {
                var eventTax = CalculateTax(
                    component,
                    component.CurrentShares,
                    component.AnnualDividendPerShare / schedule.Events.Count,
                    false,
                    taxProfiles);
                currentEvents.Add(ToEvent(
                    item,
                    scheduleEvent,
                    eventTax.NetAmountJpy,
                    0m,
                    0m,
                    true,
                    schedule.DataQuality,
                    schedule.Source));
            }
        }

        var plannedShares = Math.Max(0m, item.PlannedAdditionalShares);
        var plannedSchedule = ResolveSchedule(item, targetYear, payments);
        var plannedEvents = new List<DividendPurchasePlanEvent>();
        var fullPlannedTax = CalculateTax(item, plannedShares, item.AnnualDividendPerShare, true, taxProfiles);
        var eligibleAdded = 0m;
        var missedAdded = 0m;
        if (plannedShares > 0m && plannedSchedule.Events.Count > 0 && item.AnnualDividendPerShare > 0m)
        {
            foreach (var scheduleEvent in plannedSchedule.Events)
            {
                var eventTax = CalculateTax(
                    item,
                    plannedShares,
                    item.AnnualDividendPerShare / plannedSchedule.Events.Count,
                    true,
                    taxProfiles);
                var eligible = scheduleEvent.LastRightsDate is not null && purchaseDate <= scheduleEvent.LastRightsDate.Value.Date;
                var receive = eligible ? eventTax.NetAmountJpy : 0m;
                var missed = eligible ? 0m : eventTax.NetAmountJpy;
                eligibleAdded += receive;
                missedAdded += missed;
                plannedEvents.Add(ToEvent(
                    item,
                    scheduleEvent,
                    0m,
                    receive,
                    missed,
                    eligible,
                    plannedSchedule.DataQuality,
                    plannedSchedule.Source));
            }
        }

        var allEvents = MergeEvents(currentEvents.Concat(plannedEvents));
        var currentTargetYearNet = allEvents.Sum(x => x.CurrentNetDividendJpy);
        var plannedInvestment = Math.Max(0m, item.CurrentPrice) * plannedShares * ExchangeRate(item.Currency, item.ExchangeRate);
        var currentMarket = CurrentMarketValueJpy(item);
        var currentCost = CurrentCostJpy(item);
        var currentYield = currentMarket <= 0m ? 0m : currentAnnualNet / currentMarket * 100m;
        var yieldOnCost = currentCost <= 0m ? 0m : currentAnnualNet / currentCost * 100m;
        var additionalYield = plannedInvestment <= 0m ? 0m : fullPlannedTax.NetAmountJpy / plannedInvestment * 100m;
        var nextPayment = plannedSchedule.Events
            .Where(x => x.PaymentDate >= purchaseDate)
            .OrderBy(x => x.PaymentDate)
            .FirstOrDefault() ?? plannedSchedule.Events.OrderBy(x => x.PaymentDate).FirstOrDefault();

        return new HoldingBuildResult(
            new DividendPurchasePlanHolding
            {
                PlanKey = item.PlanKey,
                Ticker = item.Ticker,
                Name = item.Name,
                Broker = item.Broker,
                AccountType = item.AccountType,
                Currency = NormalizeCurrency(item.Currency),
                CurrentShares = Math.Max(0m, item.CurrentShares),
                PlannedAdditionalShares = plannedShares,
                PostAddShares = Math.Max(0m, item.CurrentShares) + plannedShares,
                CurrentPrice = Math.Max(0m, item.CurrentPrice),
                PlannedPurchaseAmountJpy = RoundYen(plannedInvestment),
                AnnualDividendPerShare = Math.Max(0m, item.AnnualDividendPerShare),
                CurrentAnnualNetDividendJpy = RoundYen(currentAnnualNet),
                TargetYearCurrentNetDividendJpy = RoundYen(currentTargetYearNet),
                TargetYearAdditionalNetDividendJpy = RoundYen(eligibleAdded),
                NextYearAdditionalNetDividendJpy = RoundYen(fullPlannedTax.NetAmountJpy),
                PostAddNextYearNetDividendJpy = RoundYen(currentAnnualNet + fullPlannedTax.NetAmountJpy),
                MissedNetDividendJpy = RoundYen(missedAdded),
                CurrentYieldRate = currentYield,
                YieldOnCostRate = yieldOnCost,
                AdditionalInvestmentYieldRate = additionalYield,
                DividendPaybackYears = fullPlannedTax.NetAmountJpy <= 0m ? 0m : plannedInvestment / fullPlannedTax.NetAmountJpy,
                TargetContributionJpy = Math.Min(target, eligibleAdded),
                DividendMonths = plannedSchedule.Events.Count == 0
                    ? string.Empty
                    : string.Join(",", plannedSchedule.Events.Select(x => x.PaymentDate.Month)),
                NextLastRightsDate = nextPayment?.LastRightsDate,
                NextPaymentDate = nextPayment?.PaymentDate,
                EligibilityStatus = ResolveHoldingEligibility(plannedShares, plannedSchedule, plannedEvents),
                DataQuality = plannedSchedule.DataQuality,
                DataSource = plannedSchedule.Source,
                IsNewStock = item.IsNewStock
            },
            allEvents);
    }

    private ScheduleResolution ResolveSchedule(
        DividendGrowthPlanItem item,
        int targetYear,
        IReadOnlyList<DividendPayment> payments)
    {
        var history = payments
            .Where(x => Matches(item, x))
            .Where(x => x.PaymentDate != default)
            .OrderByDescending(x => x.PaymentDate)
            .ToList();
        var configuredMonths = ParseMonths(item.DividendMonths);
        var historyMonths = history.Select(x => x.PaymentDate.Month).Distinct().Order().ToList();
        var months = configuredMonths.Count > 0
            ? configuredMonths
            : historyMonths.Count > 0
                ? historyMonths
                : FrequencyToMonths(item.DividendFrequency);

        if (months.Count == 0 && item.DividendPaymentDate is not null)
        {
            months = new[] { item.DividendPaymentDate.Value.Month };
        }

        if (months.Count == 0)
        {
            return new ScheduleResolution(Array.Empty<ScheduleEvent>(), DividendPlanDataQuality.Missing, "配当情報未取得");
        }

        var events = new List<ScheduleEvent>();
        var authoritativeDateCount = 0;
        foreach (var month in months)
        {
            var historical = history.FirstOrDefault(x => x.PaymentDate.Month == month);
            var paymentDate = historical is not null
                ? SafeDate(targetYear, month, historical.PaymentDate.Day)
                : item.DividendPaymentDate is not null && item.DividendPaymentDate.Value.Month == month
                    ? SafeDate(targetYear, month, item.DividendPaymentDate.Value.Day)
                    : SafeDate(targetYear, month, 20);
            DateTime? exDividendDate = null;
            DateTime? lastRightsDate = null;
            if (historical?.ExDividendDate is not null && historical.ExDividendDate != DateTime.MinValue)
            {
                exDividendDate = SafeDate(targetYear, historical.ExDividendDate.Month, historical.ExDividendDate.Day);
                lastRightsDate = PreviousBusinessDay(exDividendDate.Value);
                authoritativeDateCount++;
            }
            else if (historical?.RecordDate is not null && historical.RecordDate != DateTime.MinValue)
            {
                lastRightsDate = SafeDate(targetYear, historical.RecordDate.Month, historical.RecordDate.Day);
                authoritativeDateCount++;
            }
            else if (item.ExDividendDate is not null && item.ExDividendDate.Value.Month == month)
            {
                exDividendDate = SafeDate(targetYear, month, item.ExDividendDate.Value.Day);
                lastRightsDate = PreviousBusinessDay(exDividendDate.Value);
                authoritativeDateCount++;
            }
            else if (item.DividendRecordDate is not null && item.DividendRecordDate.Value.Month == month)
            {
                lastRightsDate = SafeDate(targetYear, month, item.DividendRecordDate.Value.Day);
                authoritativeDateCount++;
            }
            else
            {
                lastRightsDate = PreviousBusinessDay(paymentDate.AddDays(IsJpy(item.Currency) ? -75 : -21));
            }

            events.Add(new ScheduleEvent(paymentDate, lastRightsDate, exDividendDate));
        }

        var allDatesAuthoritative = events.Count > 0 && authoritativeDateCount == events.Count;
        var source = allDatesAuthoritative
            ? "取得済み配当日程"
            : history.Count > 0
                ? "過去の配当実績から推定"
                : "配当月・頻度から推定";
        return new ScheduleResolution(
            events.OrderBy(x => x.PaymentDate).ToList(),
            allDatesAuthoritative ? DividendPlanDataQuality.Acquired : DividendPlanDataQuality.Estimated,
            source);
    }

    private DividendTaxCalculation CalculateTax(
        DividendGrowthPlanItem item,
        decimal quantity,
        decimal dividendPerShare,
        bool planned,
        IReadOnlyList<TaxProfile> profiles)
    {
        var currency = NormalizeCurrency(item.Currency);
        var account = AccountTypeNormalizer.Normalize(planned ? item.PlannedAccountType : item.AccountType);
        var profile = profiles.FirstOrDefault(x =>
            string.Equals(AccountTypeNormalizer.Normalize(x.AccountType), account, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeCurrency(x.Currency), currency, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(x.AssetType) || string.Equals(x.AssetType, AssetTypes.Stock, StringComparison.OrdinalIgnoreCase)))
            ?? BuildDefaultTaxProfile(account, currency);
        return _taxCalculator.Calculate(new DividendTaxInput
        {
            Quantity = Math.Max(0m, quantity),
            DividendPerShare = Math.Max(0m, dividendPerShare),
            Currency = currency,
            ExchangeRate = ExchangeRate(currency, item.ExchangeRate),
            TaxProfile = profile
        });
    }

    private DividendTaxCalculation CalculateFullYearTaxTotals(
        IReadOnlyList<DividendGrowthPlanItem> items,
        IReadOnlyList<TaxProfile> profiles)
    {
        var total = new DividendTaxCalculation();
        foreach (var item in items)
        {
            foreach (var component in item.Components.Count > 0 ? item.Components : new[] { item })
            {
                if (!component.IsNewStock && component.CurrentShares > 0m)
                {
                    total = Add(total, CalculateTax(component, component.CurrentShares, component.AnnualDividendPerShare, false, profiles));
                }
            }
            if (item.PlannedAdditionalShares > 0m)
            {
                total = Add(total, CalculateTax(item, item.PlannedAdditionalShares, item.AnnualDividendPerShare, true, profiles));
            }
        }
        return total;
    }

    private static DividendPurchasePlanEvent ToEvent(
        DividendGrowthPlanItem item,
        ScheduleEvent schedule,
        decimal current,
        decimal added,
        decimal missed,
        bool eligible,
        string dataQuality,
        string source) =>
        new()
        {
            StockId = item.StockId,
            PlanKey = item.PlanKey,
            Ticker = item.Ticker,
            Name = item.Name,
            Broker = item.Broker,
            AccountType = item.AccountType,
            Month = schedule.PaymentDate.Month,
            PaymentDate = schedule.PaymentDate,
            LastRightsDate = schedule.LastRightsDate,
            ExDividendDate = schedule.ExDividendDate,
            CurrentNetDividendJpy = RoundYen(current),
            AdditionalNetDividendJpy = RoundYen(added),
            MissedNetDividendJpy = RoundYen(missed),
            IsNewStock = item.IsNewStock,
            IsEligible = eligible,
            EligibilityStatus = string.Equals(dataQuality, DividendPlanDataQuality.Missing, StringComparison.Ordinal)
                ? DividendPlanEligibility.Missing
                : eligible
                    ? string.Equals(dataQuality, DividendPlanDataQuality.Acquired, StringComparison.Ordinal)
                        ? DividendPlanEligibility.Eligible
                        : DividendPlanEligibility.Estimated
                    : DividendPlanEligibility.Ineligible,
            DataQuality = dataQuality,
            Source = source
        };

    private static IReadOnlyList<DividendPurchasePlanEvent> MergeEvents(IEnumerable<DividendPurchasePlanEvent> events) =>
        events
            .GroupBy(x => new { x.PlanKey, x.Month, x.PaymentDate, x.IsNewStock })
            .Select(group =>
            {
                var first = group.First();
                return new DividendPurchasePlanEvent
                {
                    StockId = first.StockId,
                    PlanKey = first.PlanKey,
                    Ticker = first.Ticker,
                    Name = first.Name,
                    Broker = first.Broker,
                    AccountType = first.AccountType,
                    Month = first.Month,
                    PaymentDate = first.PaymentDate,
                    LastRightsDate = first.LastRightsDate,
                    ExDividendDate = first.ExDividendDate,
                    CurrentNetDividendJpy = RoundYen(group.Sum(x => x.CurrentNetDividendJpy)),
                    AdditionalNetDividendJpy = RoundYen(group.Sum(x => x.AdditionalNetDividendJpy)),
                    MissedNetDividendJpy = RoundYen(group.Sum(x => x.MissedNetDividendJpy)),
                    IsNewStock = first.IsNewStock,
                    IsEligible = group.Any(x => x.IsEligible),
                    EligibilityStatus = group.Any(x => x.EligibilityStatus == DividendPlanEligibility.Eligible)
                        ? DividendPlanEligibility.Eligible
                        : first.EligibilityStatus,
                    DataQuality = first.DataQuality,
                    Source = first.Source
                };
            })
            .ToList();

    private static DividendPurchasePlanHolding CopyWithComposition(DividendPurchasePlanHolding x, decimal total) =>
        new()
        {
            PlanKey = x.PlanKey, Ticker = x.Ticker, Name = x.Name, Broker = x.Broker,
            AccountType = x.AccountType, Currency = x.Currency, CurrentShares = x.CurrentShares,
            PlannedAdditionalShares = x.PlannedAdditionalShares, PostAddShares = x.PostAddShares,
            CurrentPrice = x.CurrentPrice, PlannedPurchaseAmountJpy = x.PlannedPurchaseAmountJpy,
            AnnualDividendPerShare = x.AnnualDividendPerShare,
            CurrentAnnualNetDividendJpy = x.CurrentAnnualNetDividendJpy,
            TargetYearCurrentNetDividendJpy = x.TargetYearCurrentNetDividendJpy,
            TargetYearAdditionalNetDividendJpy = x.TargetYearAdditionalNetDividendJpy,
            NextYearAdditionalNetDividendJpy = x.NextYearAdditionalNetDividendJpy,
            PostAddNextYearNetDividendJpy = x.PostAddNextYearNetDividendJpy,
            MissedNetDividendJpy = x.MissedNetDividendJpy, CurrentYieldRate = x.CurrentYieldRate,
            YieldOnCostRate = x.YieldOnCostRate, AdditionalInvestmentYieldRate = x.AdditionalInvestmentYieldRate,
            DividendCompositionRate = total <= 0m ? 0m : x.PostAddNextYearNetDividendJpy / total * 100m,
            DividendPaybackYears = x.DividendPaybackYears, TargetContributionJpy = x.TargetContributionJpy,
            DividendMonths = x.DividendMonths, NextLastRightsDate = x.NextLastRightsDate,
            NextPaymentDate = x.NextPaymentDate, EligibilityStatus = x.EligibilityStatus,
            DataQuality = x.DataQuality, DataSource = x.DataSource, IsNewStock = x.IsNewStock
        };

    private static string ResolveHoldingEligibility(
        decimal plannedShares,
        ScheduleResolution schedule,
        IReadOnlyList<DividendPurchasePlanEvent> events)
    {
        if (schedule.DataQuality == DividendPlanDataQuality.Missing)
        {
            return DividendPlanEligibility.Missing;
        }
        if (plannedShares <= 0m)
        {
            return schedule.DataQuality == DividendPlanDataQuality.Acquired
                ? DividendPlanEligibility.Eligible
                : DividendPlanEligibility.Estimated;
        }
        if (events.Any(x => x.IsEligible))
        {
            return schedule.DataQuality == DividendPlanDataQuality.Acquired
                ? DividendPlanEligibility.Eligible
                : DividendPlanEligibility.Estimated;
        }
        return DividendPlanEligibility.Ineligible;
    }

    private static bool Matches(DividendGrowthPlanItem item, DividendPayment payment)
    {
        if (item.StockId > 0 && payment.StockId == item.StockId)
        {
            return true;
        }
        if (!string.Equals(SecuritySymbolNormalizer.NormalizeTicker(item.Ticker), SecuritySymbolNormalizer.NormalizeTicker(payment.Ticker), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return string.IsNullOrWhiteSpace(item.Broker) || item.Broker == "複数" ||
               string.Equals(item.Broker, payment.Broker, StringComparison.CurrentCultureIgnoreCase);
    }

    private static decimal CurrentMarketValueJpy(DividendGrowthPlanItem item) =>
        item.CurrentMarketValueJpy > 0m
            ? item.CurrentMarketValueJpy
            : Math.Max(0m, item.CurrentShares) * Math.Max(0m, item.CurrentPrice) * ExchangeRate(item.Currency, item.ExchangeRate);

    private static decimal CurrentCostJpy(DividendGrowthPlanItem item) =>
        item.CurrentCostJpy > 0m ? item.CurrentCostJpy : 0m;

    private static TaxProfile BuildDefaultTaxProfile(string account, string currency)
    {
        var isNisa = AccountTypes.IsNisa(account);
        var isJpy = IsJpy(currency);
        return new TaxProfile
        {
            AccountType = account,
            Currency = currency,
            AssetType = AssetTypes.Stock,
            ForeignWithholdingTaxRate = isJpy ? 0m : UsForeignTaxRate,
            TotalDomesticTaxRate = isNisa ? 0m : DomesticTaxRate,
            IsDomesticTaxExempt = isNisa,
            IsForeignTaxExempt = isJpy
        };
    }

    private static DividendTaxCalculation Add(DividendTaxCalculation left, DividendTaxCalculation right) => new()
    {
        GrossAmount = left.GrossAmount + right.GrossAmount,
        ForeignTaxAmount = left.ForeignTaxAmount + right.ForeignTaxAmount,
        DomesticTaxAmount = left.DomesticTaxAmount + right.DomesticTaxAmount,
        TotalTaxAmount = left.TotalTaxAmount + right.TotalTaxAmount,
        NetAmount = left.NetAmount + right.NetAmount,
        GrossAmountJpy = left.GrossAmountJpy + right.GrossAmountJpy,
        ForeignTaxAmountJpy = left.ForeignTaxAmountJpy + right.ForeignTaxAmountJpy,
        DomesticTaxAmountJpy = left.DomesticTaxAmountJpy + right.DomesticTaxAmountJpy,
        TotalTaxAmountJpy = left.TotalTaxAmountJpy + right.TotalTaxAmountJpy,
        NetAmountJpy = left.NetAmountJpy + right.NetAmountJpy
    };

    private static IReadOnlyList<int> ParseMonths(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<int>()
            : value.Split(new[] { ',', '/', '、', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim().Replace("月", string.Empty, StringComparison.Ordinal), out var month) ? month : 0)
                .Where(x => x is >= 1 and <= 12)
                .Distinct()
                .Order()
                .ToList();

    private static IReadOnlyList<int> FrequencyToMonths(string frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency)) return Array.Empty<int>();
        if (frequency.Contains("12", StringComparison.Ordinal)) return Enumerable.Range(1, 12).ToList();
        if (frequency.Contains("4", StringComparison.Ordinal)) return new[] { 3, 6, 9, 12 };
        if (frequency.Contains("2", StringComparison.Ordinal)) return new[] { 6, 12 };
        if (frequency.Contains("1", StringComparison.Ordinal)) return new[] { 12 };
        return Array.Empty<int>();
    }

    private static DateTime SafeDate(int year, int month, int day) =>
        new(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));

    private static DateTime PreviousBusinessDay(DateTime date)
    {
        var value = date.AddDays(-1);
        while (value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            value = value.AddDays(-1);
        }
        return value;
    }

    private static string NormalizeCurrency(string value) =>
        string.IsNullOrWhiteSpace(value) ? "JPY" : value.Trim().ToUpperInvariant() is "YEN" ? "JPY" : value.Trim().ToUpperInvariant();
    private static bool IsJpy(string value) => string.Equals(NormalizeCurrency(value), "JPY", StringComparison.OrdinalIgnoreCase);
    private static decimal ExchangeRate(string currency, decimal value) => IsJpy(currency) ? 1m : value <= 0m ? 1m : value;
    private static decimal RoundYen(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private sealed record ScheduleEvent(DateTime PaymentDate, DateTime? LastRightsDate, DateTime? ExDividendDate);
    private sealed record ScheduleResolution(IReadOnlyList<ScheduleEvent> Events, string DataQuality, string Source);
    private sealed record HoldingBuildResult(DividendPurchasePlanHolding Holding, IReadOnlyList<DividendPurchasePlanEvent> Events);
}
