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

        var targetYear = DividendPurchasePlanDatePolicy.NormalizeYear(input.TargetYear, DateTime.Today);
        var purchaseDate = DividendPurchasePlanDatePolicy.NormalizePurchaseDate(
            input.PlannedPurchaseDate == default ? new DateTime(targetYear, 1, 1) : input.PlannedPurchaseDate,
            targetYear,
            DateTime.Today);
        var target = Math.Max(0m, input.TargetAnnualNetDividendJpy);
        var allEvents = new List<DividendPurchasePlanEvent>();
        var holdings = new List<DividendPurchasePlanHolding>();

        foreach (var item in input.PlanItems)
        {
            var result = BuildHolding(item, targetYear, purchaseDate, target, input.DividendPayments, taxProfiles);
            holdings.Add(result.Holding);
            allEvents.AddRange(result.Events);
        }

        var hasPurchasePlan = input.PlanItems.Any(x => x.PlannedAdditionalShares > 0m);
        var nextYearTotal = holdings.Sum(x => x.PostAddNextYearNetDividendJpy);

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
        if (!hasPurchasePlan)
        {
            // With no purchase plan the three headline values must use the same
            // target-year event basis. This avoids unexplained rounding/schedule
            // differences between "before", "after", and "next year".
            plannedTargetYear = currentTargetYear;
            nextYearTotal = currentTargetYear;
        }

        holdings = holdings
            .Select(x => CopyWithComposition(x, nextYearTotal))
            .OrderByDescending(x => x.TargetYearAdditionalNetDividendJpy)
            .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            var annualDividendPerShare = ResolveAnnualDividendPerShare(component);
            var tax = CalculateTax(component, component.CurrentShares, annualDividendPerShare, false, taxProfiles);
            currentAnnualNet += tax.NetAmountJpy;
            if (schedule.Events.Count == 0)
            {
                continue;
            }

            foreach (var scheduleEvent in schedule.Events)
            {
                var perShare = scheduleEvent.AmountPerShare > 0m
                    ? scheduleEvent.AmountPerShare
                    : annualDividendPerShare / schedule.Events.Count;
                var eventTax = CalculateTax(component, component.CurrentShares, perShare, false, taxProfiles);
                var currentNet = scheduleEvent.IsPaid && scheduleEvent.ActualNetDividendJpy is > 0m
                    ? scheduleEvent.ActualNetDividendJpy.Value
                    : eventTax.NetAmountJpy;
                currentEvents.Add(ToEvent(
                    item,
                    scheduleEvent,
                    currentNet,
                    0m,
                    0m,
                    true));
            }
        }

        var plannedShares = Math.Max(0m, item.PlannedAdditionalShares);
        var plannedSchedule = ResolveSchedule(item, targetYear, payments);
        var plannedEvents = new List<DividendPurchasePlanEvent>();
        var plannedAnnualDividendPerShare = ResolveAnnualDividendPerShare(item);
        var fullPlannedTax = CalculateTax(item, plannedShares, plannedAnnualDividendPerShare, true, taxProfiles);
        var eligibleAdded = 0m;
        var missedAdded = 0m;
        if (plannedShares > 0m && plannedSchedule.Events.Count > 0 && plannedAnnualDividendPerShare > 0m)
        {
            foreach (var scheduleEvent in plannedSchedule.Events)
            {
                var perShare = scheduleEvent.AmountPerShare > 0m
                    ? scheduleEvent.AmountPerShare
                    : plannedAnnualDividendPerShare / plannedSchedule.Events.Count;
                var eventTax = CalculateTax(item, plannedShares, perShare, true, taxProfiles);
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
                    eligible));
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
                AnnualDividendPerShare = plannedAnnualDividendPerShare,
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
        var events = new List<ScheduleEvent>();

        // Broker CSV payments are the source of truth for paid dividends. Keep the
        // actual amount and payment date instead of redistributing an annual value.
        foreach (var payment in history
                     .Where(x => x.PaymentDate.Year == targetYear && DividendConstants.IsVisibleActual(x.DividendStatus))
                     .OrderBy(x => x.PaymentDate))
        {
            var exDate = ValidDate(payment.ExDividendDate);
            var recordDate = ValidDate(payment.RecordDate);
            DateTime? lastRightsDate = exDate is not null ? PreviousBusinessDay(exDate.Value) : null;
            events.Add(new ScheduleEvent(
                payment.PaymentDate.Date,
                ValidDate(payment.DeclaredDate),
                lastRightsDate,
                exDate,
                recordDate,
                Math.Max(0m, payment.DividendPerShare),
                DividendPlanDataQuality.Acquired,
                string.IsNullOrWhiteSpace(payment.Source) ? "証券会社CSV入金実績" : payment.Source,
                true,
                ResolveActualNetDividendJpy(payment)));
        }

        // Public calendar data is exact only when the provider published the date.
        // A missing payment date is estimated, while the published ex/record date is retained.
        foreach (var calendar in item.DividendEvents
                     .Where(x => EventYear(x) == targetYear)
                     .OrderBy(x => x.PaymentDate ?? x.ExDividendDate ?? x.RecordDate ?? x.DeclarationDate))
        {
            var exDate = calendar.ExDividendDate?.Date;
            var recordDate = calendar.RecordDate?.Date;
            var paymentDate = calendar.PaymentDate?.Date;
            var eventQuality = calendar.IsConfirmed && paymentDate is not null
                ? DividendPlanDataQuality.Acquired
                : DividendPlanDataQuality.Estimated;
            if (paymentDate is null)
            {
                var anchor = recordDate ?? exDate ?? calendar.DeclarationDate?.Date;
                if (anchor is null)
                {
                    continue;
                }
                paymentDate = NextBusinessDay(anchor.Value.AddDays(IsJpy(item.Currency) ? 60 : 14));
            }

            var lastRightsDate = exDate is not null
                ? PreviousBusinessDay(exDate.Value)
                : EstimateLastRightsDate(paymentDate.Value, item.Currency);
            events.Add(new ScheduleEvent(
                paymentDate.Value,
                calendar.DeclarationDate?.Date,
                lastRightsDate,
                exDate,
                recordDate,
                Math.Max(0m, calendar.AmountPerShare),
                eventQuality,
                string.IsNullOrWhiteSpace(calendar.Source) ? "公開配当カレンダー" : calendar.Source,
                false,
                null));
        }

        var configuredMonths = ParseMonths(item.DividendMonths);
        var historyMonths = history.Select(x => x.PaymentDate.Month).Distinct().Order().ToList();
        var calendarMonths = item.DividendEvents
            .Select(x => x.PaymentDate ?? x.ExDividendDate ?? x.RecordDate)
            .Where(x => x is not null)
            .Select(x => x!.Value.Month)
            .Distinct()
            .Order()
            .ToList();
        var months = configuredMonths.Count > 0
            ? configuredMonths
            : historyMonths.Count > 0
                ? historyMonths
                : calendarMonths.Count > 0
                    ? calendarMonths
                : FrequencyToMonths(item.DividendFrequency);

        if (months.Count == 0 && item.DividendPaymentDate is not null)
        {
            months = new[] { item.DividendPaymentDate.Value.Month };
        }

        if (months.Count == 0 && events.Count == 0)
        {
            return new ScheduleResolution(Array.Empty<ScheduleEvent>(), DividendPlanDataQuality.Missing, "配当情報未取得");
        }

        // Fill only months for which no actual or published target-year event exists.
        // These rows remain estimates and are never presented as confirmed dates.
        foreach (var month in months)
        {
            if (events.Any(x => x.PaymentDate.Month == month))
            {
                continue;
            }
            var historical = history.FirstOrDefault(x => x.PaymentDate.Month == month);
            var paymentDate = historical is not null
                ? SafeDate(targetYear, month, historical.PaymentDate.Day)
                : item.DividendPaymentDate is not null && item.DividendPaymentDate.Value.Month == month
                    ? SafeDate(targetYear, month, item.DividendPaymentDate.Value.Day)
                    : SafeDate(targetYear, month, 20);
            DateTime? declarationDate = null;
            DateTime? recordDate = null;
            DateTime? exDividendDate = null;
            if (historical is not null)
            {
                declarationDate = ProjectDate(historical.DeclaredDate, targetYear);
                recordDate = ProjectDate(historical.RecordDate, targetYear);
                exDividendDate = ProjectDate(historical.ExDividendDate, targetYear);
            }
            else if (item.ExDividendDate is not null && item.ExDividendDate.Value.Month == month)
            {
                exDividendDate = SafeDate(targetYear, month, item.ExDividendDate.Value.Day);
            }
            else if (item.DividendRecordDate is not null && item.DividendRecordDate.Value.Month == month)
            {
                recordDate = SafeDate(targetYear, month, item.DividendRecordDate.Value.Day);
            }

            var lastRightsDate = exDividendDate is not null
                ? PreviousBusinessDay(exDividendDate.Value)
                : EstimateLastRightsDate(paymentDate, item.Currency);
            events.Add(new ScheduleEvent(
                paymentDate,
                declarationDate,
                lastRightsDate,
                exDividendDate,
                recordDate,
                Math.Max(0m, historical?.DividendPerShare ?? 0m),
                DividendPlanDataQuality.Estimated,
                historical is not null ? "過去の配当実績から推定" : "配当月・頻度から推定",
                false,
                null));
        }

        var merged = MergeScheduleEvents(events);
        var quality = merged.Any(x => x.DataQuality == DividendPlanDataQuality.Acquired)
            ? DividendPlanDataQuality.Acquired
            : merged.Count > 0
                ? DividendPlanDataQuality.Estimated
                : DividendPlanDataQuality.Missing;
        var sources = merged.Select(x => x.Source).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        return new ScheduleResolution(
            merged,
            quality,
            sources.Count == 0 ? "配当情報未取得" : string.Join(" / ", sources));
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
        bool eligible) =>
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
            DeclarationDate = schedule.DeclarationDate,
            LastRightsDate = schedule.LastRightsDate,
            ExDividendDate = schedule.ExDividendDate,
            RecordDate = schedule.RecordDate,
            CurrentNetDividendJpy = RoundYen(current),
            AdditionalNetDividendJpy = RoundYen(added),
            MissedNetDividendJpy = RoundYen(missed),
            IsPaid = schedule.IsPaid,
            IsOverdueUnmatched = !schedule.IsPaid &&
                                 schedule.DataQuality == DividendPlanDataQuality.Acquired &&
                                 schedule.PaymentDate.Date < DateTime.Today,
            IsNewStock = item.IsNewStock,
            IsEligible = eligible,
            EligibilityStatus = string.Equals(schedule.DataQuality, DividendPlanDataQuality.Missing, StringComparison.Ordinal)
                ? DividendPlanEligibility.Missing
                : eligible
                    ? string.Equals(schedule.DataQuality, DividendPlanDataQuality.Acquired, StringComparison.Ordinal)
                        ? DividendPlanEligibility.Eligible
                        : DividendPlanEligibility.Estimated
                    : DividendPlanEligibility.Ineligible,
            DataQuality = schedule.DataQuality,
            Source = schedule.Source
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
                    DeclarationDate = group.Select(x => x.DeclarationDate).FirstOrDefault(x => x is not null),
                    LastRightsDate = group.Select(x => x.LastRightsDate).FirstOrDefault(x => x is not null),
                    ExDividendDate = group.Select(x => x.ExDividendDate).FirstOrDefault(x => x is not null),
                    RecordDate = group.Select(x => x.RecordDate).FirstOrDefault(x => x is not null),
                    CurrentNetDividendJpy = RoundYen(group.Sum(x => x.CurrentNetDividendJpy)),
                    AdditionalNetDividendJpy = RoundYen(group.Sum(x => x.AdditionalNetDividendJpy)),
                    MissedNetDividendJpy = RoundYen(group.Sum(x => x.MissedNetDividendJpy)),
                    IsPaid = group.Any(x => x.IsPaid),
                    IsOverdueUnmatched = group.Any(x => x.IsOverdueUnmatched),
                    IsNewStock = first.IsNewStock,
                    IsEligible = group.Any(x => x.IsEligible),
                    EligibilityStatus = group.Any(x => x.EligibilityStatus == DividendPlanEligibility.Eligible)
                        ? DividendPlanEligibility.Eligible
                        : first.EligibilityStatus,
                    DataQuality = group.Any(x => x.DataQuality == DividendPlanDataQuality.Acquired)
                        ? DividendPlanDataQuality.Acquired
                        : group.Any(x => x.DataQuality == DividendPlanDataQuality.Estimated)
                            ? DividendPlanDataQuality.Estimated
                            : DividendPlanDataQuality.Missing,
                    Source = string.Join(" / ", group.Select(x => x.Source).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
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

    private static decimal ResolveAnnualDividendPerShare(DividendGrowthPlanItem item)
    {
        var configured = Math.Max(0m, item.AnnualDividendPerShare);
        var datedEvents = item.DividendEvents
            .Where(x => x.AmountPerShare > 0m)
            .Select(x => new
            {
                Event = x,
                Date = x.PaymentDate ?? x.ExDividendDate ?? x.RecordDate ?? x.DeclarationDate
            })
            .Where(x => x.Date is not null)
            .ToList();
        if (datedEvents.Count < 2)
        {
            return configured;
        }

        var latest = datedEvents.Max(x => x.Date!.Value.Date);
        var trailing = datedEvents
            .Where(x => x.Date!.Value.Date > latest.AddDays(-370) && x.Date.Value.Date <= latest)
            .GroupBy(x => x.Event.EventKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.Event.AcquiredAt).First().Event.AmountPerShare)
            .Sum();
        if (trailing <= 0m)
        {
            return configured;
        }

        // Prefer a complete trailing calendar when the quote's annual value is
        // clearly inconsistent (for example, a price was stored as a dividend).
        return configured <= 0m || configured > trailing * 1.5m || configured < trailing * 0.5m
            ? trailing
            : configured;
    }

    private static IReadOnlyList<ScheduleEvent> MergeScheduleEvents(IEnumerable<ScheduleEvent> source)
    {
        var merged = new List<ScheduleEvent>();
        foreach (var candidate in source
                     .OrderByDescending(ScheduleRank)
                     .ThenBy(x => x.PaymentDate))
        {
            var index = merged.FindIndex(existing => SameScheduleEvent(existing, candidate));
            if (index < 0)
            {
                merged.Add(candidate);
                continue;
            }

            var preferred = ScheduleRank(candidate) > ScheduleRank(merged[index]) ? candidate : merged[index];
            var fallback = ReferenceEquals(preferred, candidate) ? merged[index] : candidate;
            merged[index] = preferred with
            {
                DeclarationDate = preferred.DeclarationDate ?? fallback.DeclarationDate,
                LastRightsDate = preferred.LastRightsDate ?? fallback.LastRightsDate,
                ExDividendDate = preferred.ExDividendDate ?? fallback.ExDividendDate,
                RecordDate = preferred.RecordDate ?? fallback.RecordDate,
                AmountPerShare = preferred.AmountPerShare > 0m ? preferred.AmountPerShare : fallback.AmountPerShare,
                ActualNetDividendJpy = preferred.ActualNetDividendJpy ?? fallback.ActualNetDividendJpy,
                Source = string.Join(" / ", new[] { preferred.Source, fallback.Source }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct())
            };
        }

        return merged.OrderBy(x => x.PaymentDate).ToList();
    }

    private static bool SameScheduleEvent(ScheduleEvent left, ScheduleEvent right)
    {
        if (left.ExDividendDate is not null && right.ExDividendDate is not null &&
            left.ExDividendDate.Value.Date == right.ExDividendDate.Value.Date)
        {
            return true;
        }
        if (left.PaymentDate.Date == right.PaymentDate.Date)
        {
            return true;
        }
        return left.IsPaid != right.IsPaid &&
               left.PaymentDate.Year == right.PaymentDate.Year &&
               left.PaymentDate.Month == right.PaymentDate.Month &&
               Math.Abs((left.PaymentDate - right.PaymentDate).TotalDays) <= 7;
    }

    private static int ScheduleRank(ScheduleEvent item) => item.IsPaid
        ? 3
        : item.DataQuality == DividendPlanDataQuality.Acquired
            ? 2
            : item.DataQuality == DividendPlanDataQuality.Estimated
                ? 1
                : 0;

    private static int? EventYear(DividendCalendarEvent item) =>
        (item.PaymentDate ?? item.ExDividendDate ?? item.RecordDate ?? item.DeclarationDate)?.Year;

    private static DateTime? ValidDate(DateTime value) => value == default || value == DateTime.MinValue ? null : value.Date;

    private static DateTime? ProjectDate(DateTime value, int year) =>
        ValidDate(value) is { } date ? SafeDate(year, date.Month, date.Day) : null;

    private static decimal ResolveActualNetDividendJpy(DividendPayment payment)
    {
        if (payment.NetAmountJpy > 0m) return payment.NetAmountJpy;
        if (payment.JpyAmount > 0m) return payment.JpyAmount;
        if (payment.NetAmount <= 0m) return 0m;
        return payment.NetAmount * (IsJpy(payment.Currency) ? 1m : Math.Max(1m, payment.ExchangeRate));
    }

    private static DateTime EstimateLastRightsDate(DateTime paymentDate, string currency) =>
        PreviousBusinessDay(paymentDate.AddDays(IsJpy(currency) ? -75 : -21));

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

    private static DateTime NextBusinessDay(DateTime date)
    {
        var value = date;
        while (value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            value = value.AddDays(1);
        }
        return value;
    }

    private static string NormalizeCurrency(string value) =>
        string.IsNullOrWhiteSpace(value) ? "JPY" : value.Trim().ToUpperInvariant() is "YEN" ? "JPY" : value.Trim().ToUpperInvariant();
    private static bool IsJpy(string value) => string.Equals(NormalizeCurrency(value), "JPY", StringComparison.OrdinalIgnoreCase);
    private static decimal ExchangeRate(string currency, decimal value) => IsJpy(currency) ? 1m : value <= 0m ? 1m : value;
    private static decimal RoundYen(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private sealed record ScheduleEvent(
        DateTime PaymentDate,
        DateTime? DeclarationDate,
        DateTime? LastRightsDate,
        DateTime? ExDividendDate,
        DateTime? RecordDate,
        decimal AmountPerShare,
        string DataQuality,
        string Source,
        bool IsPaid,
        decimal? ActualNetDividendJpy);
    private sealed record ScheduleResolution(IReadOnlyList<ScheduleEvent> Events, string DataQuality, string Source);
    private sealed record HoldingBuildResult(DividendPurchasePlanHolding Holding, IReadOnlyList<DividendPurchasePlanEvent> Events);
}
