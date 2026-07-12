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
    public void ReconciliationService_CsvActualReplacesMatchingSchedule()
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
        Assert.Single(decision.ReplaceTargets);
        Assert.Equal(1, decision.ReplaceTargets[0].Id);
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
