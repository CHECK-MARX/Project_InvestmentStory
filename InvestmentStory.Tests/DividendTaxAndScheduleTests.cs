using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class DividendTaxAndScheduleTests
{
    [Fact]
    public void TaxCalculator_JapanSpecific_AppliesDomesticTaxOnly()
    {
        var result = new DividendTaxCalculator().Calculate(new DividendTaxInput
        {
            Quantity = 100m,
            DividendPerShare = 50m,
            Currency = "JPY",
            ExchangeRate = 1m,
            TaxProfile = new TaxProfile
            {
                Currency = "JPY",
                AccountType = DividendConstants.AccountSpecific,
                TotalDomesticTaxRate = 20.315m,
                IsForeignTaxExempt = true
            }
        });

        Assert.Equal(5000m, result.GrossAmount);
        Assert.Equal(0m, result.ForeignTaxAmount);
        Assert.Equal(1015.75m, result.DomesticTaxAmount);
        Assert.Equal(3984.25m, result.NetAmount);
    }

    [Fact]
    public void TaxCalculator_JapanNisa_IsTaxExempt()
    {
        var result = new DividendTaxCalculator().Calculate(new DividendTaxInput
        {
            Quantity = 100m,
            DividendPerShare = 50m,
            Currency = "JPY",
            ExchangeRate = 1m,
            TaxProfile = new TaxProfile
            {
                Currency = "JPY",
                AccountType = DividendConstants.AccountNisa,
                IsDomesticTaxExempt = true,
                IsForeignTaxExempt = true
            }
        });

        Assert.Equal(5000m, result.GrossAmount);
        Assert.Equal(0m, result.TotalTaxAmount);
        Assert.Equal(5000m, result.NetAmount);
    }

    [Fact]
    public void TaxCalculator_UsSpecific_AppliesForeignThenDomesticTax()
    {
        var result = new DividendTaxCalculator().Calculate(new DividendTaxInput
        {
            Quantity = 10m,
            DividendPerShare = 1m,
            Currency = "USD",
            ExchangeRate = 160m,
            TaxProfile = new TaxProfile
            {
                Currency = "USD",
                AccountType = DividendConstants.AccountSpecific,
                ForeignWithholdingTaxRate = 10m,
                TotalDomesticTaxRate = 20.315m
            }
        });

        Assert.Equal(10m, result.GrossAmount);
        Assert.Equal(1m, result.ForeignTaxAmount);
        Assert.Equal(1.82835m, result.DomesticTaxAmount);
        Assert.Equal(7.17165m, result.NetAmount);
        Assert.Equal(1147.464m, result.NetAmountJpy);
    }

    [Fact]
    public void TaxCalculator_UsNisa_KeepsForeignTaxOnly()
    {
        var result = new DividendTaxCalculator().Calculate(new DividendTaxInput
        {
            Quantity = 10m,
            DividendPerShare = 1m,
            Currency = "USD",
            ExchangeRate = 160m,
            TaxProfile = new TaxProfile
            {
                Currency = "USD",
                AccountType = DividendConstants.AccountNisa,
                ForeignWithholdingTaxRate = 10m,
                IsDomesticTaxExempt = true
            }
        });

        Assert.Equal(1m, result.ForeignTaxAmount);
        Assert.Equal(0m, result.DomesticTaxAmount);
        Assert.Equal(9m, result.NetAmount);
        Assert.Equal(1440m, result.NetAmountJpy);
    }

    [Fact]
    public void ScheduleService_UsesDividendHistoryMonthsAndDoesNotTouchActuals()
    {
        var service = new DividendScheduleService();
        var position = CreatePosition(currentShares: 100m, annualDividendPerShare: 2m);
        var existing = new[]
        {
            new DividendPayment
            {
                Id = 10,
                StockId = 1,
                PaymentDate = new DateTime(2026, 6, 20),
                DividendStatus = DividendConstants.Estimated,
                Source = DividendConstants.SourceEstimatedFromAnnualDividend,
                Quantity = 50m,
                Currency = "USD",
                ExchangeRate = 160m,
                NetAmountJpy = 3000m
            },
            new DividendPayment
            {
                Id = 20,
                StockId = 1,
                PaymentDate = new DateTime(2025, 3, 20),
                DividendStatus = DividendConstants.Actual,
                Quantity = 50m,
                DividendPerShare = 0.5m,
                Currency = "USD",
                ExchangeRate = 160m,
                NetAmountJpy = 3000m
            }
        };

        var result = service.BuildSchedules(
            new[] { position },
            existing,
            UsProfiles(),
            new DateTime(2026, 1, 1));

        Assert.Contains(result.Schedules, x => x.PaymentDate.Month == 3);
        var updatedJune = Assert.Single(result.Schedules, x => x.Id == 10);
        Assert.Equal(100m, updatedJune.Quantity);
        Assert.Equal(DividendConstants.SourceEstimatedFromHistory, updatedJune.Source);
        Assert.DoesNotContain(result.Schedules, x => x.Id == 20);
    }

    [Theory]
    [InlineData(DividendConstants.Excluded)]
    [InlineData(DividendConstants.Replaced)]
    public void ScheduleService_DoesNotRecreateReviewedMonthButKeepsOtherAccount(string reviewedStatus)
    {
        var position = CreatePosition(currentShares: 20m, annualDividendPerShare: 4m);
        var reviewed = new DividendPayment
        {
            Id = 91, StockId = position.Stock.Id,
            PaymentDate = new DateTime(2026, 6, 20),
            AccountType = DividendConstants.AccountSpecific,
            DividendStatus = reviewedStatus,
            Source = DividendConstants.SourceEstimatedFromAnnualDividend
        };
        var calendar = new DividendCalendarEvent
        {
            StockId = position.Stock.Id,
            PaymentDate = reviewed.PaymentDate,
            IsConfirmed = true,
            AmountPerShare = 1m,
            Source = "Issuer"
        };
        var service = new DividendScheduleService();
        var result = service.BuildSchedules([position], [reviewed], UsProfiles(),
            new DateTime(2026, 5, 1), [calendar]);
        Assert.DoesNotContain(result.Schedules, x => x.PaymentDate.Month == 6);

        position.Stock.AccountType = DividendConstants.AccountNisa;
        var otherAccount = service.BuildSchedules([position], [reviewed], UsProfiles(),
            new DateTime(2026, 5, 1), [calendar]);
        Assert.Contains(otherAccount.Schedules, x => x.PaymentDate.Month == 6 &&
            x.AccountType == DividendConstants.AccountNisa);
    }

    [Fact]
    public void ScheduleService_IgnoresImplausibleHistoricalPerShare()
    {
        var service = new DividendScheduleService();
        var position = CreatePosition(currentShares: 20m, annualDividendPerShare: 5.24m, currentPrice: 267m, stockId: 2);
        var existing = new[]
        {
            new DividendPayment
            {
                Id = 30,
                StockId = 2,
                PaymentDate = new DateTime(2026, 6, 10),
                DividendStatus = DividendConstants.Actual,
                Quantity = 20m,
                DividendPerShare = 192.1395m,
                GrossAmount = 3842.79m,
                Currency = "USD",
                ExchangeRate = 161.672m
            }
        };

        var result = service.BuildSchedules(
            new[] { position },
            existing,
            UsProfiles(),
            new DateTime(2026, 7, 12));

        Assert.NotEmpty(result.Schedules);
        Assert.DoesNotContain(result.Schedules, x => x.DividendPerShare > 50m);
        Assert.All(result.Schedules, x => Assert.Equal(1.31m, x.DividendPerShare));
    }

    [Fact]
    public void ScheduleService_UsesHistoricalPaymentDayWhenAvailable()
    {
        var service = new DividendScheduleService();
        var position = CreatePosition(currentShares: 20m, annualDividendPerShare: 5.24m, currentPrice: 267m, stockId: 3);
        var existing = new[]
        {
            new DividendPayment
            {
                Id = 40,
                StockId = 3,
                PaymentDate = new DateTime(2025, 9, 11),
                DividendStatus = DividendConstants.Actual,
                Quantity = 20m,
                DividendPerShare = 1.31m,
                GrossAmount = 26.20m,
                Currency = "USD",
                ExchangeRate = 160m
            }
        };

        var result = service.BuildSchedules(
            new[] { position },
            existing,
            UsProfiles(),
            new DateTime(2026, 1, 1));

        Assert.Contains(result.Schedules, x => x.PaymentDate == new DateTime(2026, 9, 11));
    }

    [Fact]
    public void ScheduleService_UsesFourActualPaymentMonthsAcrossLegacyStockIds()
    {
        var position = CreatePosition(currentShares: 20m, annualDividendPerShare: 4m, stockId: 81);
        var existing = new List<DividendPayment>();
        foreach (var month in new[] { 2, 5, 8, 11 })
            existing.Add(new DividendPayment
            {
                StockId = 99, Ticker = "PEP", Broker = "SBI",
                AccountType = DividendConstants.AccountSpecific,
                PaymentDate = new DateTime(2025, month, 5),
                DividendStatus = DividendConstants.Actual,
                DividendPerShare = 1m, Currency = "USD"
            });
        existing.Add(new DividendPayment
        {
            StockId = 99, Ticker = "PEP", Broker = "SBI",
            AccountType = DividendConstants.AccountSpecific,
            PaymentDate = new DateTime(2026, 2, 5),
            DividendStatus = DividendConstants.Actual,
            DividendPerShare = 1m, Currency = "USD"
        });
        existing.Add(new DividendPayment
        {
            Id = 70, StockId = 81, Ticker = "PEP", Broker = "SBI",
            AccountType = DividendConstants.AccountSpecific,
            PaymentDate = new DateTime(2026, 3, 20),
            DividendStatus = DividendConstants.Estimated,
            Source = DividendConstants.SourceEstimatedFromAnnualDividend
        });

        var result = new DividendScheduleService().BuildSchedules(new[] { position }, existing,
            UsProfiles(), new DateTime(2026, 10, 1));
        Assert.Equal(new[] { 5, 8, 11 }, result.Schedules.Select(x => x.PaymentDate.Month).Order());
        Assert.Contains(70, result.ObsoleteScheduleIds);
    }

    [Fact]
    public void ScheduleService_FillsMissingQuarterWithoutAddingHistoricalDriftMonth()
    {
        var position = CreatePosition(currentShares: 20m, annualDividendPerShare: 4m, stockId: 82);
        var history = new[] { 4, 6, 9, 12 }.Select(month => new DividendPayment
        {
            StockId = 82, Ticker = "PEP", Broker = "SBI",
            AccountType = DividendConstants.AccountSpecific,
            PaymentDate = new DateTime(2025, month, 20),
            DividendStatus = DividendConstants.Actual,
            DividendPerShare = 1m, Currency = "USD"
        }).ToArray();
        var published = new[] { 3, 6, 9 }.Select(month => new DividendCalendarEvent
        {
            StockId = 82, PaymentDate = new DateTime(2026, month, 20),
            IsConfirmed = true, AmountPerShare = 1m, Source = "Issuer"
        }).ToArray();

        var result = new DividendScheduleService().BuildSchedules(new[] { position }, history,
            UsProfiles(), new DateTime(2026, 10, 1), published);
        Assert.Equal(new[] { 3, 6, 9, 12 }, result.Schedules.Select(x => x.PaymentDate.Month).Order());
    }

    [Fact]
    public void ScheduleService_PrefersConfirmedPublicCalendarDateAndAmount()
    {
        var service = new DividendScheduleService();
        var position = CreatePosition(currentShares: 20m, annualDividendPerShare: 5.24m, currentPrice: 267m, stockId: 4);
        var calendar = new[]
        {
            new DividendCalendarEvent
            {
                StockId = 4,
                DeclarationDate = new DateTime(2026, 4, 15),
                ExDividendDate = new DateTime(2026, 5, 22),
                RecordDate = new DateTime(2026, 5, 23),
                PaymentDate = new DateTime(2026, 6, 10),
                AmountPerShare = 1.45m,
                Currency = "USD",
                Source = "Nasdaq",
                DataQuality = DividendPlanDataQuality.Acquired,
                IsConfirmed = true,
                AcquiredAt = new DateTime(2026, 4, 16)
            }
        };

        var result = service.BuildSchedules(
            new[] { position },
            Array.Empty<DividendPayment>(),
            UsProfiles(),
            new DateTime(2026, 5, 1),
            calendar);

        var june = Assert.Single(result.Schedules, x => x.PaymentDate.Month == 6);
        Assert.Equal(new DateTime(2026, 6, 10), june.PaymentDate);
        Assert.Equal(1.45m, june.DividendPerShare);
        Assert.Equal(DividendConstants.Confirmed, june.DividendStatus);
        Assert.StartsWith(DividendConstants.SourcePublicCalendar, june.Source);
        Assert.Contains("権利落ち日 2026/05/22", june.Memo);
        Assert.Contains("支払日 2026/06/10", june.Memo);
    }

    [Fact]
    public void ReconciliationService_NearbyScheduleIsReviewCandidate()
    {
        var existing = new[]
        {
            new DividendPayment
            {
                Id = 1,
                StockId = 1,
                AccountType = DividendConstants.AccountSpecific,
                Broker = "SBI",
                PaymentDate = new DateTime(2026, 6, 20),
                DividendStatus = DividendConstants.Estimated,
                Currency = "USD",
                NetAmount = 10m,
                NetAmountJpy = 1600m
            }
        };

        var actual = new DividendPayment
        {
            StockId = 1,
            AccountType = DividendConstants.AccountSpecific,
            Broker = "SBI",
            PaymentDate = new DateTime(2026, 6, 22),
            DividendStatus = DividendConstants.Actual,
            Currency = "USD",
            NetAmount = 10m,
            NetAmountJpy = 1600m,
            Source = DividendConstants.SourceCsv
        };

        var decision = new DividendReconciliationService().ReconcileActual(existing, actual);

        Assert.Null(decision.DuplicateActual);
        Assert.Empty(decision.ReplaceTargets);
        Assert.Equal(1, Assert.Single(decision.ReviewCandidates).Id);
    }

    [Fact]
    public void ReconciliationService_ExactSingleScheduleCanBeReplaced()
    {
        var planned = new DividendPayment
        {
            Id = 1, StockId = 1, Broker = "SBI", AccountType = DividendConstants.AccountSpecific,
            PaymentDate = new DateTime(2026, 6, 22), DividendStatus = DividendConstants.Planned,
            Currency = "JPY", Quantity = 10m, NetAmount = 100m, NetAmountJpy = 100m
        };
        var actual = new DividendPayment
        {
            StockId = 1, Broker = "SBI", AccountType = DividendConstants.AccountSpecific,
            PaymentDate = planned.PaymentDate, DividendStatus = DividendConstants.Actual,
            Currency = "JPY", Quantity = 10m, NetAmount = 100m, NetAmountJpy = 100m,
            Source = DividendConstants.SourceCsv
        };

        var decision = new DividendReconciliationService().ReconcileActual(new[] { planned }, actual);

        Assert.Equal(planned.Id, Assert.Single(decision.ReplaceTargets).Id);
        Assert.Empty(decision.ReviewCandidates);
    }

    [Fact]
    public void ReconciliationService_UnknownAccountRequiresReviewEvenWithExactDateAndAmount()
    {
        var planned = new DividendPayment
        {
            Id = 21, StockId = 1, Broker = "SBI", AccountType = DividendConstants.AccountUnknown,
            PaymentDate = new DateTime(2026, 6, 22), DividendStatus = DividendConstants.Planned,
            Currency = "JPY", Quantity = 10m, NetAmountJpy = 100m
        };
        var actual = new DividendPayment
        {
            Id = 22, StockId = 1, Broker = "SBI", AccountType = DividendConstants.AccountSpecific,
            PaymentDate = planned.PaymentDate, DividendStatus = DividendConstants.Actual,
            Currency = "JPY", Quantity = 10m, NetAmountJpy = 100m
        };
        var decision = new DividendReconciliationService().ReconcileActual([planned], actual);
        Assert.Empty(decision.ReplaceTargets);
        Assert.Same(planned, Assert.Single(decision.ReviewCandidates));
        actual.AccountType = DividendConstants.AccountUnknown;
        decision = new DividendReconciliationService().ReconcileActual([planned], actual);
        Assert.Empty(decision.ReplaceTargets);
        Assert.Same(planned, Assert.Single(decision.ReviewCandidates));
    }

    private static StockPosition CreatePosition(
        decimal currentShares,
        decimal annualDividendPerShare,
        decimal currentPrice = 100m,
        int stockId = 1)
    {
        return new StockPosition
        {
            Stock = new Stock
            {
                Id = stockId,
                Name = "PepsiCo",
                Ticker = "PEP",
                Country = "US",
                Currency = "USD",
                Broker = "SBI",
                AccountType = DividendConstants.AccountSpecific
            },
            CurrentHolding = new CurrentHolding
            {
                StockId = stockId,
                CurrentShares = currentShares,
                CurrentPrice = currentPrice,
                CurrentExchangeRate = 160m,
                ExchangeRateAcquiredAt = new DateTime(2026, 1, 1),
                ExchangeRateSource = "Yahoo Finance",
                ExchangeRateInputType = "API",
                AnnualDividendPerShare = annualDividendPerShare,
                DividendStatus = "配当あり",
                DividendFrequency = "年4回"
            }
        };
    }

    private static IReadOnlyList<TaxProfile> UsProfiles()
    {
        return new[]
        {
            new TaxProfile
            {
                Id = 1,
                Currency = "USD",
                AccountType = DividendConstants.AccountSpecific,
                ForeignWithholdingTaxRate = 10m,
                TotalDomesticTaxRate = 20.315m
            }
        };
    }
}
