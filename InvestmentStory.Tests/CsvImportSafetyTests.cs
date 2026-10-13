using System.Reflection;
using System.Text;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class CsvImportSafetyTests
{
    [Fact]
    public void JpyDividend_DoesNotChangeUsdSecurityOrHoldingExchangeRate()
    {
        WithRepository(repository =>
        {
            var id = repository.SavePosition(Position("KO", "USD", 10m, 150m));
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = false,
                Dividends = new[]
                {
                    new BrokerDividendRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific,
                        Ticker = "KO", Name = "Coca-Cola", PaymentDate = new DateTime(2026, 8, 1),
                        Currency = "JPY", NetAmount = 1000m, NetAmountJpy = 1000m,
                        Source = "SBI配当CSV"
                    }
                }
            };

            Import(repository, statement);

            var position = repository.GetPositions().Single(x => x.Stock.Id == id);
            Assert.Equal("USD", position.Stock.Currency);
            Assert.Equal(150m, position.CurrentHolding.CurrentExchangeRate);
            Assert.Equal(10m, position.CurrentHolding.CurrentShares);
            var dividend = Assert.Single(repository.GetDividendPayments());
            Assert.Equal("JPY", dividend.Currency);
            Assert.Equal(0m, dividend.GrossAmount);
            Assert.Contains("税引後入金のみ判明", dividend.Memo);
        });
    }

    [Fact]
    public void NetOnlyDividend_DisplaysUnknownGrossAndTaxInsteadOfZero()
    {
        var row = new DividendPaymentRowViewModel(new DividendPayment
        {
            IsNetOnly = true, Currency = "JPY", NetAmountJpy = 1000m
        });
        Assert.Equal("税引後のみ", row.DataQuality);
        Assert.Equal("不明", row.GrossAmount);
        Assert.Equal("不明", row.TaxAmount);
        Assert.Equal("税額不明", row.TaxNote);
    }

    [Fact]
    public void UnknownForeignDividendFx_RemainsUnknownAfterPersistence()
    {
        WithRepository(repository =>
        {
            var stockId = repository.SavePosition(Position("EXMP", "USD", 1m, 150m));
            repository.SaveDividendPayment(new DividendPayment
            {
                StockId = stockId, Broker = "SBI証券", PaymentDate = new DateTime(2026, 8, 1),
                Currency = "EUR", ExchangeRate = 0m, GrossAmount = 1m, NetAmount = 1m,
                DividendStatus = DividendConstants.Actual
            });
            Assert.Equal(0m, Assert.Single(repository.GetDividendPayments()).ExchangeRate);
        });
    }

    [Fact]
    public void YenReceiptWithoutHolding_CreatesReferenceWithUnknownSecurityFx()
    {
        WithRepository(repository =>
        {
            Import(repository, new BrokerStatementImport
            {
                Dividends = new[]
                {
                    new BrokerDividendRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific,
                        Product = "米国株式", Ticker = "EXMP", Name = "Example",
                        PaymentDate = new DateTime(2026, 8, 1), Currency = "JPY",
                        ExchangeRate = 1m, NetAmount = 1000m, NetAmountJpy = 1000m
                    }
                }
            });
            var reference = Assert.Single(repository.GetPositions());
            Assert.Equal("USD", reference.Stock.Currency);
            Assert.Equal(0m, reference.CurrentHolding.CurrentExchangeRate);
            Assert.Equal(0m, reference.CurrentHolding.CurrentShares);
            Assert.True(Assert.Single(repository.GetDividendPayments()).IsForeignStock);
        });
    }

    [Fact]
    public void IncrementalTrades_RebuildPositionAndEveryLedgerRowFromCompleteHistory()
    {
        WithRepository(repository =>
        {
            Import(repository, TradeStatement(new DateTime(2026, 1, 1), 10m));
            Import(repository, TradeStatement(new DateTime(2026, 1, 2), 5m));

            var position = Assert.Single(repository.GetPositions());
            Assert.Equal(15m, position.CurrentHolding.CurrentShares);
            var trades = repository.GetBrokerTrades(position.Stock.Id)
                .Where(x => x.Source != "HoldingsSnapshot").OrderBy(x => x.TradeDate).ToList();
            Assert.Equal(2, trades.Count);
            Assert.Equal(10m, trades[0].AfterTradeQuantity);
            Assert.Equal(15m, trades[1].AfterTradeQuantity);

            Import(repository, TradeStatement(new DateTime(2026, 1, 2), 5m));
            Assert.Equal(2, repository.GetBrokerTrades(position.Stock.Id).Count(x => x.Source != "HoldingsSnapshot"));
            Assert.Equal(15m, repository.GetPositions().Single().CurrentHolding.CurrentShares);
        });
    }

    [Fact]
    public void TradeWithoutSettlementDate_DoesNotDuplicateAfterLedgerRoundTrip()
    {
        var tradeDate = new DateTime(2026, 1, 1);
        var incoming = new BrokerTradeRecord
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "EXMP",
            TradeDate = tradeDate, SettlementDate = DateTime.MinValue,
            TradeType = "現物買付", Quantity = 10m, SignedQuantity = 10m,
            UnitPrice = 100m, Currency = "JPY", SettlementAmountJpy = 1000m
        };
        var saved = Assert.Single(new TradeLedgerService().BuildLedger(1, new[] { incoming }));
        saved.Quantity = 10.00m;
        saved.SignedQuantity = 10.00m;
        saved.UnitPrice = 100.000000001m; // SQLite REAL can lose sub-cent unit-price precision.
        saved.SettlementAmountJpy = 1000.00m;

        Assert.Single(TradeHistoryService.Merge(new[] { saved }, new[] { incoming }, "EXMP"));
    }

    [Fact]
    public void SplitCandidate_DoesNotDuplicateAfterLedgerRoundTrip()
    {
        var buy = new BrokerTradeRecord
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "EXMP",
            TradeDate = new DateTime(2026, 1, 1), TradeType = "現物買付",
            Quantity = 10m, SignedQuantity = 10m, UnitPrice = 100m, Currency = "USD"
        };
        var inbound = new BrokerTradeRecord
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "EXMP",
            TradeDate = new DateTime(2026, 2, 1), TradeType = "入庫（増減資）",
            Quantity = 10m, SignedQuantity = 10m, UnitPrice = 0m, Currency = "USD"
        };
        var ledger = new TradeLedgerService().BuildLedger(1, new[] { buy, inbound });
        Assert.Equal("StockSplit", ledger[1].TradeType);

        Assert.Equal(2, TradeHistoryService.Merge(ledger, new[] { buy, inbound }, "EXMP").Count);
    }

    [Fact]
    public void EquivalentSbiFundCustodyLabels_MergeIntoOnePosition()
    {
        var first = new BrokerHoldingRecord
        {
            AssetType = AssetTypes.MutualFund, Broker = "SBI証券", Account = "NISA (つみたて)預り",
            Ticker = "FUND:TEST", FundCode = "FUND:TEST", Name = "Test Fund", Currency = "JPY",
            Shares = 100m, UnitsHeld = 100m, SnapshotDate = new DateTime(2026, 8, 1)
        };
        var second = new BrokerHoldingRecord
        {
            AssetType = AssetTypes.MutualFund, Broker = "SBI証券", Account = "投資信託（金額/NISA預り（つみたて投資枠））",
            Ticker = "FUND:TEST", FundCode = "FUND:TEST", Name = "Test Fund", Currency = "JPY",
            Shares = 110m, UnitsHeld = 110m, SnapshotDate = new DateTime(2026, 9, 1)
        };
        var decisions = new BrokerHoldingMergeService()
            .MergeHoldings(Array.Empty<StockPosition>(), new[] { first, second }, DateTime.Today).Decisions;

        Assert.Equal(new[] { BrokerMergeAction.Create, BrokerMergeAction.Overwrite }, decisions.Select(x => x.Action));
        Assert.Equal(110m, decisions[1].Merged!.CurrentHolding.CurrentShares);

        WithRepository(repository =>
        {
            Import(repository, new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                Holdings = new[] { first, second }
            });
            var fund = Assert.Single(repository.GetPositions());
            Assert.Equal(110m, fund.MutualFund.UnitsHeld);
            Assert.Equal(AccountTypes.NisaAccumulation, fund.Stock.AccountType);
        });
    }

    [Fact]
    public void OlderSnapshot_IsRejected_AndExplicitZeroClosesMatchingPosition()
    {
        var existing = Position("KO", "USD", 5m, 150m);
        existing.CurrentHolding.UpdatedAt = new DateTime(2026, 9, 10);
        var source = new BrokerHoldingRecord
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "KO", Name = "Coca-Cola",
            Currency = "USD", Shares = 10m, SnapshotDate = new DateTime(2026, 8, 5)
        };
        var service = new BrokerHoldingMergeService();
        Assert.Equal(BrokerMergeAction.NeedsReview,
            Assert.Single(service.MergeHoldings(new[] { existing }, new[] { source }, DateTime.Today).Decisions).Action);

        source.Shares = 0m;
        source.SnapshotDate = new DateTime(2026, 9, 11);
        var closed = Assert.Single(service.MergeHoldings(new[] { existing }, new[] { source }, DateTime.Today).Decisions);
        Assert.Equal(BrokerMergeAction.Overwrite, closed.Action);
        Assert.Equal(0m, closed.Merged!.CurrentHolding.CurrentShares);
    }

    [Fact]
    public void UndatedSnapshot_CannotOverwriteAnExistingPosition()
    {
        var existing = Position("KO", "USD", 5m, 150m);
        var source = new BrokerHoldingRecord
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "KO",
            Currency = "USD", Shares = 10m, SnapshotDate = DateTime.MinValue
        };
        var decision = Assert.Single(new BrokerHoldingMergeService()
            .MergeHoldings(new[] { existing }, new[] { source }, DateTime.Today).Decisions);
        Assert.Equal(BrokerMergeAction.NeedsReview, decision.Action);
        Assert.Null(decision.Merged);
        Assert.Contains("基準日", decision.Reason);
    }

    [Fact]
    public void UndatedOrUnknownForeignRate_CannotCreateAPosition()
    {
        var source = new BrokerHoldingRecord
        {
            Broker = "野村証券", Account = AccountTypes.Specific, Ticker = "ABC",
            Currency = "EUR", Shares = 10m, SnapshotDate = DateTime.MinValue,
            PurchaseExchangeRate = 0m, CurrentExchangeRate = 0m
        };
        var service = new BrokerHoldingMergeService();
        var undated = Assert.Single(service.MergeHoldings(Array.Empty<StockPosition>(),
            new[] { source }, DateTime.Today).Decisions);
        Assert.Equal(BrokerMergeAction.NeedsReview, undated.Action);
        Assert.Contains("基準日", undated.Reason);

        source.SnapshotDate = new DateTime(2026, 7, 1);
        var unknownRate = Assert.Single(service.MergeHoldings(Array.Empty<StockPosition>(),
            new[] { source }, DateTime.Today).Decisions);
        Assert.Equal(BrokerMergeAction.NeedsReview, unknownRate.Action);
        Assert.Contains("為替", unknownRate.Reason);
    }

    [Fact]
    public void StaleHolding_IsPersistedForReviewWithoutChangingCurrentShares()
    {
        WithRepository(repository =>
        {
            var position = Position("KO", "USD", 5m, 150m);
            position.CurrentHolding.UpdatedAt = new DateTime(2026, 9, 10);
            repository.SavePosition(position);
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                Holdings = new[]
                {
                    new BrokerHoldingRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific,
                        Ticker = "KO", Currency = "USD", Shares = 10m,
                        SnapshotDate = new DateTime(2026, 8, 1)
                    }
                }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically, null, repository.SaveCsvImportReviewTrade,
                repository.SaveCsvImportReviewDividend, repository.SaveCsvImportReviewHolding);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            Assert.Equal(5m, repository.GetPositions().Single().CurrentHolding.CurrentShares);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = repository.DatabasePath, Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM CsvImportReviewHoldings;";
            Assert.Equal(1L, (long)command.ExecuteScalar()!);
        });
    }

    [Fact]
    public void DistinctActualDividendsSevenDaysApart_AreNotDuplicates()
    {
        var service = new DividendReconciliationService();
        var first = new DividendPayment
        {
            Id = 1, StockId = 1, Broker = "SBI証券", AccountType = AccountTypes.Specific,
            Currency = "JPY", DividendStatus = DividendConstants.Actual,
            PaymentDate = new DateTime(2026, 1, 1), NetAmountJpy = 1000m
        };
        var second = new DividendPayment
        {
            StockId = 1, Broker = "SBI証券", AccountType = AccountTypes.Specific,
            Currency = "JPY", DividendStatus = DividendConstants.Actual,
            PaymentDate = new DateTime(2026, 1, 8), NetAmountJpy = 1000m
        };
        Assert.Null(service.ReconcileActual(new[] { first }, second).DuplicateActual);
    }

    [Fact]
    public void ImportTransaction_RollsBackPositionAndDividendWhenLaterWriteFails()
    {
        WithRepository(repository =>
        {
            Assert.Throws<InvalidOperationException>(() => repository.RunImportAtomically(() =>
            {
                var id = repository.SavePosition(Position("KO", "USD", 10m, 150m));
                repository.SaveDividendPayment(new DividendPayment
                {
                    StockId = id, Broker = "SBI証券", PaymentDate = new DateTime(2026, 8, 1),
                    NetAmount = 10m, Currency = "USD"
                });
                throw new InvalidOperationException("simulated failure");
            }));

            Assert.Empty(repository.GetPositions());
            Assert.Empty(repository.GetDividendPayments());
            Assert.Empty(repository.GetAllBrokerTrades());
        });
    }

    [Fact]
    public void ChangedFileAfterPreview_RequiresNewPreviewBeforeImport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            var vm = new CsvImportViewModel();
            vm.FilePath = path;
            vm.PreviewCommand.Execute(null);
            File.AppendAllText(path, "2026/08/02,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            vm.ImportCommand.Execute(null);
            Assert.Contains("再プレビュー", vm.Message);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SellWithoutOpeningBalance_StaysInReviewInsteadOfCreatingCertainHolding()
    {
        WithRepository(repository =>
        {
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                Trades = new[]
                {
                    new BrokerTradeRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific,
                        Ticker = "EXMP", Name = "Example", TradeType = "現物売却",
                        TradeDate = new DateTime(2026, 8, 1), SettlementDate = new DateTime(2026, 8, 3),
                        Quantity = 5m, SignedQuantity = -5m, UnitPrice = 100m,
                        Currency = "USD", Source = "test"
                    }
                }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                records => repository.SaveBrokerTrades(records), null, repository.GetBrokerTrades,
                repository.RunImportAtomically, null, repository.SaveCsvImportReviewTrade);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            Assert.Empty(repository.GetPositions());
            Assert.Empty(repository.GetAllBrokerTrades());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = repository.DatabasePath, Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM CsvImportReviewRows;";
            Assert.Equal(1L, (long)command.ExecuteScalar()!);
        });
    }

    [Fact]
    public void ViewModelImport_RollsBackFirstHoldingWhenSecondWriteFails()
    {
        WithRepository(repository =>
        {
            var saves = 0;
            var reloads = 0;
            var vm = new CsvImportViewModel(repository.GetPositions,
                position =>
                {
                    if (++saves == 2) throw new InvalidOperationException("simulated second write failure");
                    return repository.SavePosition(position);
                },
                repository.SaveDividendPayment, repository.GetDividendPayments,
                records => repository.SaveBrokerTrades(records), () => reloads++,
                repository.GetBrokerTrades, repository.RunImportAtomically);
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                Holdings = new[]
                {
                    new BrokerHoldingRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "AAA",
                        Currency = "USD", Shares = 10m, SnapshotDate = new DateTime(2026, 8, 1)
                    },
                    new BrokerHoldingRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "BBB",
                        Currency = "USD", Shares = 10m, SnapshotDate = new DateTime(2026, 8, 1)
                    }
                }
            };
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            Assert.Contains("保存されていません", vm.Message);
            Assert.Equal(0, reloads);
            Assert.Empty(repository.GetPositions());
        });
    }

    [Fact]
    public void MultipleSelectedFiles_RollBackEntireBatchWhenLaterFileFails()
    {
        var first = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        var second = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(first, "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            File.WriteAllText(second, "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n2026/09/01,特定,米国株式,Example Corp EXMP,10,200\n", Encoding.UTF8);
            WithRepository(repository =>
            {
                var saves = 0;
                var reloads = 0;
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    payment =>
                    {
                        if (++saves == 2) throw new InvalidOperationException("simulated later file failure");
                        return repository.SaveDividendPayment(payment);
                    }, repository.GetDividendPayments, null, () => reloads++, null,
                    repository.RunImportAtomically, null, null, null, null,
                    repository.SaveCsvImportLog);
                typeof(CsvImportViewModel).GetMethod("ImportMultipleFiles", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, new object[] { new[] { first, second } });

                Assert.Contains("保存されていません", vm.Message);
                Assert.Equal(0, reloads);
                Assert.Empty(repository.GetPositions());
                Assert.Empty(repository.GetDividendPayments());
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = repository.DatabasePath, Pooling = false
                }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM CsvImportLogs WHERE Result = 'Failed';";
                Assert.Equal(2L, (long)command.ExecuteScalar()!);
            });
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public async Task AsyncImport_CancelRollsBackAppliedRows()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        var dbPath = Path.Combine(Path.GetTempPath(), $"investment_story_async_cancel_{Guid.NewGuid():N}.db");
        using var saved = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            File.WriteAllText(csvPath,
                "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                "2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            var repository = new InvestmentStoryRepository(dbPath);
            repository.Initialize();
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                payment =>
                {
                    var id = repository.SaveDividendPayment(payment);
                    saved.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test import did not resume.");
                    return id;
                }, repository.GetDividendPayments, repository.SaveBrokerTrades, null,
                repository.GetBrokerTrades, repository.RunImportAtomically, null, null, null, null,
                repository.SaveCsvImportLog, useAsyncCommands: true) { FilePath = csvPath };

            vm.ImportCommand.Execute(null);
            Assert.True(await Task.Run(() => saved.Wait(TimeSpan.FromSeconds(10))));
            Assert.True(vm.IsBusy);
            Assert.False(vm.ImportCommand.CanExecute(null));
            vm.CancelCommand.Execute(null);
            release.Set();
            for (var attempt = 0; attempt < 500 && vm.IsBusy; attempt++) await Task.Delay(20);

            Assert.False(vm.IsBusy);
            Assert.Empty(repository.GetDividendPayments());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = dbPath, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM CsvImportLogs WHERE Result = 'Canceled';";
            Assert.Equal(1L, (long)command.ExecuteScalar()!);
        }
        finally
        {
            release.Set();
            if (File.Exists(csvPath)) File.Delete(csvPath);
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void Initialize_AddsSourceRowAuditColumnsToExistingImportLog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_legacy_log_{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE CsvImportLogs (Id INTEGER PRIMARY KEY, FileHash TEXT NOT NULL DEFAULT '', Broker TEXT NOT NULL DEFAULT '', CsvType TEXT NOT NULL DEFAULT '', RowCount INTEGER NOT NULL DEFAULT 0);";
                command.ExecuteNonQuery();
            }

            new InvestmentStoryRepository(path).Initialize();

            using var migrated = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ToString());
            migrated.Open();
            using var columns = migrated.CreateCommand();
            columns.CommandText = "PRAGMA table_info(CsvImportLogs);";
            using var reader = columns.ExecuteReader();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read()) names.Add(reader.GetString(1));
            Assert.Contains("ParsedRowCount", names);
            Assert.Contains("PendingRowCount", names);
            Assert.Contains("ExcludedRowCount", names);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void MultipleSelectedFiles_PersistIndependentSourceRowAudits()
    {
        var first = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        var second = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            const string header = "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n";
            File.WriteAllText(first, header + "2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            File.WriteAllText(second, header +
                "2026/09/01,特定,米国株式,Example Corp EXMP,10,200\n" +
                "2026/10/01,特定,米国株式,Example Corp EXMP,10,300\n", Encoding.UTF8);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically, null, null, null, null,
                    repository.SaveCsvImportLog);
                typeof(CsvImportViewModel).GetMethod("ImportMultipleFiles", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, new object[] { new[] { first, second } });

                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = repository.DatabasePath, Pooling = false
                }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT FileName, RowCount, ParsedRowCount, PendingRowCount, ExcludedRowCount FROM CsvImportLogs ORDER BY FileName;";
                using var reader = command.ExecuteReader();
                var audits = new Dictionary<string, (long Input, long Parsed, long Pending, long Excluded)>();
                while (reader.Read())
                    audits.Add(reader.GetString(0), (reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
                Assert.Equal(2, audits.Count);
                Assert.Equal((1L, 1L, 0L, 0L), audits[Path.GetFileName(first)]);
                Assert.Equal((2L, 2L, 0L, 0L), audits[Path.GetFileName(second)]);
            });
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public void Reimport_WritesSeparatePerFileAuditAttemptsWithStableHash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically, null, null, null, null,
                    repository.SaveCsvImportLog) { FilePath = path };
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);

                Assert.Single(repository.GetDividendPayments());
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = repository.DatabasePath, Pooling = false
                }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*), COUNT(DISTINCT FileHash), COUNT(DISTINCT BatchId), SUM(RowCount), SUM(UpdatedCount), SUM(ParsedRowCount), SUM(PendingRowCount), SUM(ExcludedRowCount) FROM CsvImportLogs WHERE Result = 'Succeeded';";
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(2L, reader.GetInt64(2));
                Assert.Equal(2L, reader.GetInt64(3));
                Assert.Equal(0L, reader.GetInt64(4));
                Assert.Equal(2L, reader.GetInt64(5));
                Assert.Equal(0L, reader.GetInt64(6));
                Assert.Equal(0L, reader.GetInt64(7));
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void EqualDividendRowsInOneCsv_RemainDistinctAndReimportIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                "2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n" +
                "2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically) { FilePath = path };
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);
                var first = repository.GetDividendPayments();
                Assert.Equal(2, first.Count);
                Assert.Equal(new[] { 2, 3 }, first.Select(x => x.SourceRowNumber).OrderBy(x => x));

                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);
                Assert.Equal(2, repository.GetDividendPayments().Count);
                repository.Initialize();
                Assert.Equal(2, repository.GetDividendPayments().Count);
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FailedDividendBatch_DoesNotReuseRolledBackPreparedCommand()
    {
        WithRepository(repository =>
        {
            var stockId = repository.SavePosition(Position("EXMP", "USD", 0m, 150m));
            Assert.Throws<InvalidOperationException>(() => repository.RunImportAtomically(() =>
            {
                repository.SaveDividendPayment(new DividendPayment
                {
                    StockId = stockId, Broker = "SBI証券", PaymentDate = new DateTime(2026, 8, 1),
                    Currency = "JPY", NetAmount = 100m, NetAmountJpy = 100m
                });
                throw new InvalidOperationException("Rollback this batch.");
            }));

            repository.RunImportAtomically(() => repository.SaveDividendPayment(new DividendPayment
            {
                StockId = stockId, Broker = "SBI証券", PaymentDate = new DateTime(2026, 9, 1),
                Currency = "JPY", NetAmount = 200m, NetAmountJpy = 200m
            }));
            Assert.Equal(200m, Assert.Single(repository.GetDividendPayments()).NetAmountJpy);
        });
    }

    [Fact]
    public void ImportedDividend_ReplacesPlannedPaymentNineDaysLater()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path,
                "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                "2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            WithRepository(repository =>
            {
                var stockId = repository.SavePosition(Position("EXMP", "USD", 0m, 150m));
                repository.SaveDividendPayment(new DividendPayment
                {
                    StockId = stockId, Broker = "SBI証券", PaymentDate = new DateTime(2026, 8, 10),
                    AccountType = AccountTypes.Specific, TaxAccountType = AccountTypes.Specific,
                    Currency = "JPY", NetAmount = 100m, NetAmountJpy = 100m,
                    DividendStatus = DividendConstants.Planned, Source = DividendConstants.SourceManual
                });
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically) { FilePath = path };
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);

                var payments = repository.GetDividendPayments();
                var actual = Assert.Single(payments, x => x.DividendStatus == DividendConstants.Actual);
                var planned = Assert.Single(payments, x => x.DividendStatus == DividendConstants.Replaced);
                Assert.Equal(actual.Id, planned.ReplacedByDividendId);
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ChangedDividendSourceRowAndDate_PreservesOriginalAndQueuesRevisionForReview()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                "2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n", Encoding.UTF8);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically, null, null, repository.SaveCsvImportReviewDividend)
                { FilePath = path };
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);
                File.WriteAllText(path, "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                    "2026/09/01,特定,米国株式,Example Corp EXMP,10,200\n", Encoding.UTF8);
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);
                Assert.Equal(100m, Assert.Single(repository.GetDividendPayments()).NetAmountJpy);
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = repository.DatabasePath, Pooling = false }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Reason FROM CsvImportReviewDividends LIMIT 1;";
                Assert.Contains("200", (string)command.ExecuteScalar()!);
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void EqualDividendInSeparateCsvFiles_IsQueuedOnceForReview()
    {
        var first = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        var second = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            var csv = "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                "2026/08/01,特定,米国株式,Example Corp EXMP,10,100\n";
            File.WriteAllText(first, csv, Encoding.UTF8);
            File.WriteAllText(second, csv, Encoding.UTF8);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically, null, null, repository.SaveCsvImportReviewDividend);
                typeof(CsvImportViewModel).GetMethod("ImportMultipleFiles", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, new object[] { new[] { first, second } });
                Assert.Single(repository.GetDividendPayments());
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = repository.DatabasePath, Pooling = false }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM CsvImportReviewDividends;";
                Assert.Equal(1L, (long)command.ExecuteScalar()!);
            });
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public void CompleteSnapshot_ClosesOnlyOlderMissingBrokerCsvHoldingInExactScope()
    {
        var missing = Position("AAA", "JPY", 10m, 1m);
        missing.Stock.DataSource = "SBI日本株保有CSV";
        missing.CurrentHolding.UpdatedAt = new DateTime(2026, 9, 1);
        var present = Position("BBB", "JPY", 4m, 1m);
        present.Stock.DataSource = "SBI日本株保有CSV";
        var otherAccount = Position("CCC", "JPY", 7m, 1m);
        otherAccount.Stock.DataSource = "SBI日本株保有CSV";
        otherAccount.Stock.AccountType = AccountTypes.NisaGrowth;
        otherAccount.Stock.CustodyType = AccountTypes.NisaGrowth;
        var newer = Position("DDD", "JPY", 6m, 1m);
        newer.Stock.DataSource = "SBI日本株保有CSV";
        newer.CurrentHolding.UpdatedAt = new DateTime(2026, 9, 30);
        var manual = Position("EEE", "JPY", 3m, 1m);
        var holdings = new[] { new BrokerHoldingRecord
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "BBB", Name = "BBB",
            Currency = "JPY", Shares = 4m, SnapshotDate = new DateTime(2026, 9, 26)
        } };
        var scope = new CompleteHoldingSnapshotScope
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, AssetType = AssetTypes.Stock,
            Currency = "JPY", SnapshotDate = new DateTime(2026, 9, 26), Source = "SBI保有証券一覧CSV"
        };
        var service = new BrokerHoldingMergeService();
        var positions = new[] { missing, present, otherAccount, newer, manual };

        var partial = service.MergeHoldings(positions, holdings, DateTime.Today);
        Assert.DoesNotContain(partial.Decisions, x => x.Existing == missing);
        var full = service.MergeHoldings(positions, holdings, DateTime.Today, completeScopes: new[] { scope });
        var closure = Assert.Single(full.Decisions, x => x.Existing == missing);
        Assert.Equal(0m, closure.Merged!.CurrentHolding.CurrentShares);
        Assert.Equal(new DateTime(2026, 9, 26), closure.Merged.CurrentHolding.UpdatedAt);
        Assert.Equal(10m, closure.Merged.Purchase.Shares);
        Assert.DoesNotContain(full.Decisions, x => x.Existing == otherAccount || x.Existing == newer || x.Existing == manual);
        Assert.DoesNotContain(service.MergeHoldings(positions, holdings, DateTime.Today,
            "新規銘柄だけ追加", new[] { scope }).Decisions, x => x.Existing == missing);
    }

    [Fact]
    public void CompleteSnapshot_ClosesMissingFundAndClearsCurrentValuation()
    {
        var existing = Position("FUND:TEST", "JPY", 100m, 1m);
        existing.Stock.AssetType = AssetTypes.MutualFund;
        existing.Stock.DataSource = "SBI投信保有CSV";
        existing.CurrentHolding.UpdatedAt = new DateTime(2026, 9, 1);
        existing.MutualFund.FundCode = "FUND:TEST";
        existing.MutualFund.UnitsHeld = 100m;
        existing.MutualFund.AcquisitionAmount = 900m;
        existing.MutualFund.MarketValue = 1000m;
        existing.MutualFund.UnrealizedGainLoss = 100m;
        var scope = new CompleteHoldingSnapshotScope
        {
            Broker = "SBI証券", Account = AccountTypes.Specific, AssetType = AssetTypes.MutualFund,
            SnapshotDate = new DateTime(2026, 9, 26)
        };
        var decision = Assert.Single(new BrokerHoldingMergeService().MergeHoldings(
            new[] { existing }, Array.Empty<BrokerHoldingRecord>(), DateTime.Today,
            completeScopes: new[] { scope }).Decisions);
        Assert.Equal(0m, decision.Merged!.CurrentHolding.CurrentShares);
        Assert.Equal(0m, decision.Merged.MutualFund.UnitsHeld);
        Assert.Equal(0m, decision.Merged.MutualFund.MarketValue);
        Assert.Equal(0m, decision.Merged.MutualFund.AcquisitionAmount);
        Assert.Equal(0m, decision.Merged.MutualFund.UnrealizedGainLoss);
        Assert.Equal(100m, decision.Merged.Purchase.Shares);
    }

    [Fact]
    public void CompleteEmptySnapshot_PersistsClosureAndIsIdempotent()
    {
        WithRepository(repository =>
        {
            var existing = Position("8593", "JPY", 10m, 1m);
            existing.Stock.DataSource = "SBI日本株保有CSV";
            existing.CurrentHolding.UpdatedAt = new DateTime(2026, 9, 1);
            repository.SavePosition(existing);
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                CompleteHoldingScopes = new[] { new CompleteHoldingSnapshotScope
                {
                    Broker = "SBI証券", Account = AccountTypes.Specific, AssetType = AssetTypes.Stock,
                    SnapshotDate = new DateTime(2026, 9, 26), Source = "SBI保有証券一覧CSV"
                } }
            };
            Import(repository, statement);
            var closed = Assert.Single(repository.GetPositions());
            Assert.Equal(0m, closed.CurrentHolding.CurrentShares);
            Assert.Equal(10m, closed.Purchase.Shares);
            Import(repository, statement);
            Assert.Single(repository.GetPositions());
            Assert.Equal(0m, repository.GetPositions()[0].CurrentHolding.CurrentShares);
        });
    }

    private static BrokerStatementImport TradeStatement(DateTime date, decimal quantity) => new()
    {
        CanUpdateHoldings = true,
        Trades = new[]
        {
            new BrokerTradeRecord
            {
                Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "KO", Name = "Coca-Cola",
                Currency = "USD", TradeType = "現物買付", TradeDate = date, SettlementDate = date,
                Quantity = quantity, SignedQuantity = quantity, UnitPrice = 50m,
                ExchangeRate = 150m, SettlementAmountJpy = quantity * 50m * 150m, Source = "test"
            }
        }
    };

    private static StockPosition Position(string ticker, string currency, decimal shares, decimal rate) => new()
    {
        Stock = new Stock
        {
            Ticker = ticker, Name = "Coca-Cola", Broker = "SBI証券", AccountType = AccountTypes.Specific,
            CustodyType = AccountTypes.Specific, Currency = currency
        },
        Purchase = new Purchase { PurchaseDate = new DateTime(2026, 1, 1), Shares = shares, UnitPrice = 50m, ExchangeRate = rate },
        Split = new StockSplit { SplitRatio = 1m },
        CurrentHolding = new CurrentHolding { CurrentShares = shares, CurrentPrice = 100m, CurrentExchangeRate = rate }
    };

    private static void Import(InvestmentStoryRepository repository, BrokerStatementImport statement)
    {
        var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
            repository.SaveDividendPayment, repository.GetDividendPayments,
            records => repository.SaveBrokerTrades(records), null, repository.GetBrokerTrades,
            repository.RunImportAtomically);
        var method = typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
        Assert.DoesNotContain("失敗", vm.Message);
    }

    private static void WithRepository(Action<InvestmentStoryRepository> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_safety_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            action(repository);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
