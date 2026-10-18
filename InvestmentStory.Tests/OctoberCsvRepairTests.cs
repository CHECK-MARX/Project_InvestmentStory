using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using InvestmentStory.App.ViewModels;
using System.Reflection;

namespace InvestmentStory.Tests;

public sealed class OctoberCsvRepairTests
{
    [Fact]
    public void NomuraTransactionCashCorrectsAnOlderDividendWithoutDiscardingTaxDetail()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_cash_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var stockId = repository.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "T", Name = "AT&T", Broker = "野村証券", Currency = "USD", AccountType = AccountTypes.Specific },
                Purchase = new Purchase { Shares = 50m, UnitPrice = 40m, ExchangeRate = 150m },
                CurrentHolding = new CurrentHolding { CurrentShares = 50m, CurrentPrice = 50m, CurrentExchangeRate = 150m }
            });
            repository.SaveDividendPayment(new DividendPayment
            {
                StockId = stockId, Ticker = "T", Broker = "野村証券", AccountType = AccountTypes.Specific,
                PaymentDate = new DateTime(2026, 5, 11), DividendStatus = DividendConstants.Actual,
                Quantity = 50m, Currency = "USD", ExchangeRate = 150m,
                GrossAmountJpy = 1955m, TotalTaxAmountJpy = 396m,
                NetAmountJpy = 1559m, JpyAmount = 1559m, NetAmount = 1559m / 150m,
                Source = DividendConstants.SourceCsv, SourceFile = "Nomura_配当履歴202607.csv", SourceRowNumber = 51
            });
            var statement = new BrokerStatementImport
            {
                Broker = "野村証券", Dividends = new[]
                {
                    new BrokerDividendRecord
                    {
                        Broker = "野村証券", Account = AccountTypes.Specific, Ticker = "T", Name = "AT&T",
                        PaymentDate = new DateTime(2026, 5, 11), Quantity = 50m, Currency = "USD",
                        NetAmountJpy = 1551m, ExchangeRate = 150m,
                        SourceFile = "Nomura_取引履歴_受け渡し.csv", SourceRowNumber = 51,
                        Source = "野村取引履歴CSV", Product = "外国株式"
                    }
                }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                records => repository.SaveBrokerTrades(records), null, repository.GetBrokerTrades,
                repository.RunImportAtomically);
            var import = typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!;
            import.Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            import.Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            var actual = Assert.Single(repository.GetDividendPayments());
            Assert.Equal(1551m, actual.NetAmountJpy);
            Assert.Equal(396m, actual.TotalTaxAmountJpy);
            Assert.Equal(1955m, actual.GrossAmountJpy);
            Assert.Contains("Nomura_取引履歴_受け渡し.csv:51", actual.Memo);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void IdenticalTradesInOneStatementSurviveImportAndReimport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trade_occurrences_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "8593", Name = "Test", Broker = "SBI証券", Currency = "JPY", AccountType = AccountTypes.NisaGrowth },
                Purchase = new Purchase { Shares = 200m, UnitPrice = 1346m, ExchangeRate = 1m },
                CurrentHolding = new CurrentHolding { CurrentShares = 200m, CurrentPrice = 1320.5m, CurrentExchangeRate = 1m }
            });
            var trades = Enumerable.Range(0, 2).Select(_ => new BrokerTradeRecord
            {
                TradeDate = new DateTime(2026, 6, 15), SettlementDate = new DateTime(2026, 6, 17),
                Broker = "SBI証券", Account = AccountTypes.NisaGrowth, Ticker = "8593",
                TradeType = "Buy", Quantity = 100m, SignedQuantity = 100m,
                UnitPrice = 1345.8m, SettlementAmountJpy = 134580m,
                Currency = "JPY", ExchangeRate = 1m, Source = "SBI国内約定CSV"
            }).ToArray();

            repository.SaveBrokerTrades(trades);
            Assert.Equal(2, repository.GetAllBrokerTrades().Count(x => x.TradeType == "Buy"));
            Assert.Equal(200m, repository.GetAllBrokerTrades().Where(x => x.TradeType == "Buy").Sum(x => x.Quantity));

            repository.SaveBrokerTrades(trades);
            Assert.Equal(2, repository.GetAllBrokerTrades().Count(x => x.TradeType == "Buy"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ExplicitYenCostWinsOverRoundedForeignUnitCostAndSurvivesStorage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"yen_cost_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            repository.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "T", Name = "Test", Broker = "野村證券", Currency = "USD", AccountType = AccountTypes.Specific },
                Purchase = new Purchase { Shares = 20m, UnitPrice = 43.32m, ExchangeRate = 158.43m, AcquisitionAmountJpy = 67450m },
                CurrentHolding = new CurrentHolding { CurrentShares = 20m, CurrentPrice = 56m, CurrentExchangeRate = 158m }
            });
            var position = Assert.Single(repository.GetPositions());
            var snapshot = new InvestmentCalculator().CreateSnapshot(position);
            Assert.Equal(67450m, position.Purchase.AcquisitionAmountJpy);
            Assert.Equal(67450m, snapshot.PurchaseTotalJpy);
            Assert.Equal(snapshot.CurrentMarketValueJpy - 67450m, snapshot.UnrealizedGainJpy);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void NewerMarketQuoteDoesNotMakeAnOlderHoldingSnapshotLookStale()
    {
        var existing = new StockPosition
        {
            Stock = new Stock { Id = 9, Ticker = "T", Broker = "野村証券", Currency = "USD", AccountType = AccountTypes.Specific, CustodyType = "特定", DataSource = "野村保有残高CSV" },
            Purchase = new Purchase
            {
                Shares = 20m, UnitPrice = 43m, ExchangeRate = 160m,
                ExchangeRateAcquiredAt = new DateTime(2026, 9, 24), ExchangeRateInputType = "CSV"
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 20m, CurrentPrice = 57m, CurrentExchangeRate = 158m,
                UpdatedAt = new DateTime(2026, 10, 14),
                CurrentPriceAcquiredAt = new DateTime(2026, 10, 14, 5, 0, 0),
                ExchangeRateAcquiredAt = new DateTime(2026, 10, 14, 8, 0, 0)
            }
        };
        var csv = new BrokerHoldingRecord
        {
            Ticker = "T", Broker = "野村証券", Account = "特定", Currency = "USD",
            Shares = 20m, AverageAcquisitionPrice = 43.32m, AcquisitionAmountJpy = 67450m,
            CurrentPrice = 56m, CurrentExchangeRate = 158.54m, PurchaseExchangeRate = 77.85m,
            SnapshotDate = new DateTime(2026, 10, 12), Source = "野村保有残高CSV"
        };
        var decision = Assert.Single(new BrokerHoldingMergeService().MergeHoldings(
            new[] { existing }, new[] { csv }, new DateTime(2026, 10, 14)).Decisions);
        Assert.Equal(BrokerMergeAction.Overwrite, decision.Action);
        Assert.Equal(67450m, decision.Merged!.Purchase.AcquisitionAmountJpy);
        Assert.Equal(57m, decision.Merged.CurrentHolding.CurrentPrice);
        Assert.Equal(158m, decision.Merged.CurrentHolding.CurrentExchangeRate);
        Assert.Equal(new DateTime(2026, 10, 12), decision.Merged.CurrentHolding.UpdatedAt);
    }

    [Fact]
    public void MissingHistoricFxDoesNotTurnForeignCostIntoYen()
    {
        var snapshot = new InvestmentCalculator().CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Ticker = "CMBT", Currency = "USD" },
            Purchase = new Purchase { Shares = 10m, UnitPrice = 15.81m, ExchangeRate = 0m },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 20m, CurrentExchangeRate = 158m }
        });
        Assert.False(snapshot.HasKnownPurchaseCostJpy);
        Assert.Equal(0m, snapshot.PurchaseTotalJpy);
        Assert.Equal(0m, snapshot.UnrealizedGainJpy);
    }

    [Fact]
    public void ReferenceCostIsShownOnlyWhileThePositionStillMatchesItsBasis()
    {
        var position = new StockPosition
        {
            Stock = new Stock { Ticker = "CMBT", Currency = "USD" },
            Purchase = new Purchase
            {
                Shares = 10m, UnitPrice = 15.81m, ExchangeRate = 0m,
                ReferenceAcquisitionForeignAmount = 158.10m,
                ReferenceAcquisitionAmountJpy = 23_262.267m,
                ReferenceAcquisitionSource = "FRED DEXJPUS / 約定日"
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 10m, CurrentPrice = 20m, CurrentExchangeRate = 158m
            }
        };
        var calculator = new InvestmentCalculator();

        var reference = calculator.CreateSnapshot(position);
        Assert.True(reference.HasKnownPurchaseCostJpy);
        Assert.True(reference.IsReferencePurchaseCostJpy);
        Assert.Equal(23_262.267m, reference.PurchaseTotalJpy);
        Assert.Equal(8_337.733m, reference.UnrealizedGainJpy);
        var summary = calculator.CreateDashboardSummary(new[] { reference },
            Array.Empty<DividendPayment>(), null, new DateTime(2026, 10, 14));
        Assert.Equal(1, summary.ReferencePurchaseCostPositionCount);
        Assert.Equal(0, summary.UnknownPurchaseCostPositionCount);
        var row = new StockRowViewModel(reference);
        Assert.StartsWith("概算 ", row.CostBasisJpy);
        Assert.StartsWith("概算 ", row.GainLossJpy);
        var dashboard = new DashboardViewModel();
        dashboard.Update(summary, new[] { reference }, Array.Empty<PortfolioSnapshot>());
        Assert.Equal("含み損益（概算）", dashboard.UnrealizedGainLabel);

        position.CurrentHolding.CurrentShares = 11m;
        var stale = calculator.CreateSnapshot(position);
        Assert.False(stale.HasKnownPurchaseCostJpy);
        Assert.False(stale.IsReferencePurchaseCostJpy);

        position.CurrentHolding.CurrentShares = 10m;
        position.Purchase.AcquisitionAmountJpy = 24_000m;
        var verified = calculator.CreateSnapshot(position);
        Assert.True(verified.HasKnownPurchaseCostJpy);
        Assert.False(verified.IsReferencePurchaseCostJpy);
        Assert.Equal(24_000m, verified.PurchaseTotalJpy);
    }

    [Fact]
    public void NisaScheduleUsesNisaProfileEvenWhenStoredNameIsNisa()
    {
        var position = new StockPosition
        {
            Stock = new Stock { Id = 4, Ticker = "8593", Currency = "JPY", AccountType = AccountTypes.NisaGrowth },
            Purchase = new Purchase { Shares = 200m, UnitPrice = 1346m },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 200m, CurrentPrice = 1320m, CurrentExchangeRate = 1m,
                AnnualDividendPerShare = 52m, DividendFrequency = "年2回", DividendMonths = "6,12"
            }
        };
        var profiles = new[]
        {
            new TaxProfile { Id = 2, Currency = "JPY", AccountType = AccountTypes.General, TotalDomesticTaxRate = 20.315m },
            new TaxProfile { Id = 3, Currency = "JPY", AccountType = "NISA", IsDomesticTaxExempt = true }
        };
        var schedules = new DividendScheduleService().BuildSchedules(new[] { position },
            Array.Empty<DividendPayment>(), profiles, new DateTime(2026, 1, 1)).Schedules;
        Assert.NotEmpty(schedules);
        Assert.All(schedules, x => { Assert.Equal(3, x.TaxProfileId); Assert.Equal(0m, x.DomesticTaxAmountJpy); });
    }

    [Fact]
    public void TradeReconstructedPurchaseDoesNotGenerateEarlierDividendPlans()
    {
        var position = new StockPosition
        {
            Stock = new Stock { Id = 4, Ticker = "8593", Currency = "JPY", AccountType = AccountTypes.Specific },
            Purchase = new Purchase
            {
                PurchaseDate = new DateTime(2026, 6, 15), Memo = "取引履歴CSVから時系列集計",
                Shares = 200m, UnitPrice = 1346m, ExchangeRate = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 200m, CurrentPrice = 1320m, CurrentExchangeRate = 1m,
                AnnualDividendPerShare = 52m, DividendFrequency = "年2回", DividendMonths = "1,12"
            }
        };
        var result = new DividendScheduleService().BuildSchedules(new[] { position },
            Array.Empty<DividendPayment>(), Array.Empty<TaxProfile>(), new DateTime(2026, 10, 14));
        Assert.Single(result.Schedules);
        Assert.Equal(12, result.Schedules[0].PaymentDate.Month);
    }

    [Fact]
    public void ScheduleRefreshKeepsOldOverduePlansButDoesNotBackfillNewOnes()
    {
        var position = new StockPosition
        {
            Stock = new Stock { Id = 7, Ticker = "PEP", Currency = "USD", AccountType = AccountTypes.Specific },
            Purchase = new Purchase { Shares = 10m, UnitPrice = 100m, ExchangeRate = 150m },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 10m, CurrentPrice = 150m, CurrentExchangeRate = 150m,
                AnnualDividendPerShare = 4m, DividendFrequency = "年2回", DividendMonths = "6,12"
            }
        };
        var oldOverdue = new DividendPayment
        {
            Id = 822, StockId = 7, Ticker = "PEP", Broker = "野村証券",
            PaymentDate = new DateTime(2026, 3, 31), DividendStatus = DividendConstants.PaymentDue,
            Source = DividendConstants.SourceEstimatedFromHistory, NetAmountJpy = 100m
        };
        var result = new DividendScheduleService().BuildSchedules(new[] { position },
            new[] { oldOverdue }, Array.Empty<TaxProfile>(), new DateTime(2026, 10, 14));
        Assert.Empty(result.ObsoleteScheduleIds);
        Assert.DoesNotContain(result.Schedules, x => x.PaymentDate.Month == 6);
        Assert.Contains(result.Schedules, x => x.PaymentDate.Month == 12);
    }

    [Fact]
    public void FutureActualIsExcludedAndForeignStockInYenIsClassifiedAsForeign()
    {
        var position = new StockPosition
        {
            Stock = new Stock { Id = 8, Ticker = "MO", Currency = "USD" },
            Purchase = new Purchase { Shares = 10m, UnitPrice = 10m, ExchangeRate = 150m },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 20m, CurrentExchangeRate = 150m }
        };
        var snapshot = new InvestmentCalculator().CreateSnapshot(position);
        var summary = new InvestmentCalculator().CreateDashboardSummary(new[] { snapshot }, new[]
        {
            new DividendPayment { StockId = 8, PaymentDate = new DateTime(2026, 10, 13), Currency = "JPY", JpyAmount = 100m },
            new DividendPayment { StockId = 8, PaymentDate = new DateTime(2026, 12, 1), Currency = "JPY", JpyAmount = 1000m }
        }, null, new DateTime(2026, 10, 14));
        Assert.Equal(100m, summary.ThisYearPassiveIncomeJpy);
        Assert.Equal(0m, summary.DomesticStockDividendActualJpy);
        Assert.Equal(100m, summary.ForeignStockDividendActualJpy);
    }

    [Fact]
    public void OneMatchingPlanCanLinkWithDifferentSettlementDateCurrencyAndRoundedAmount()
    {
        var plan = new DividendPayment
        {
            Id = 970, StockId = 108, Ticker = "MO", Broker = "野村證券",
            AccountType = AccountTypes.Specific, PaymentDate = new DateTime(2026, 10, 9),
            DividendStatus = DividendConstants.PaymentDue, Quantity = 20m,
            Currency = "USD", NetAmountJpy = 2520.89m,
            Source = DividendConstants.SourceEstimatedFromHistory
        };
        var actual = new DividendPayment
        {
            StockId = 108, Ticker = "MO", Broker = "野村證券",
            AccountType = AccountTypes.Specific, PaymentDate = new DateTime(2026, 10, 13),
            DividendStatus = DividendConstants.Actual, Quantity = 20m,
            Currency = "JPY", NetAmountJpy = 2508.43m
        };
        var decision = new DividendReconciliationService().ReconcileActual(new[] { plan }, actual);
        Assert.Same(plan, Assert.Single(decision.ReplaceTargets));
    }
}
