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
    public void EqualTradesFromSeparateRowsOfSameCsv_RemainDistinctAcrossImports()
    {
        WithRepository(repository =>
        {
            var first = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            first.SourceFile = "same.csv";
            first.SourceRowNumber = 2;
            Import(repository, new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { first } });
            var second = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            second.SourceFile = "same.csv";
            second.SourceRowNumber = 3;
            Import(repository, new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { second } });

            var position = Assert.Single(repository.GetPositions());
            Assert.Equal(20m, position.CurrentHolding.CurrentShares);
            Assert.Equal(new[] { 2, 3 }, repository.GetBrokerTrades(position.Stock.Id)
                .Select(x => x.SourceRowNumber).OrderBy(x => x));

            Import(repository, new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { first } });
            Assert.Equal(20m, repository.GetPositions().Single().CurrentHolding.CurrentShares);
            Assert.Equal(2, repository.GetBrokerTrades(position.Stock.Id).Count);
        });
    }

    [Fact]
    public void EqualTradeFromDifferentCsv_IsHeldWithoutChangingLedger()
    {
        WithRepository(repository =>
        {
            var first = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            first.SourceFile = "first.csv";
            first.SourceRowNumber = 2;
            Import(repository, new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { first } });
            var second = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            second.SourceFile = "second.csv";
            second.SourceRowNumber = 2;
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveReviewTrade: repository.SaveCsvImportReviewTrade,
                saveImportChange: repository.SaveCsvImportChange);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() =>
                    new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { second } }), "test" });

            var position = Assert.Single(repository.GetPositions());
            Assert.Equal(10m, position.CurrentHolding.CurrentShares);
            Assert.Single(repository.GetBrokerTrades(position.Stock.Id));
            Assert.Contains("元CSV行を照合できません", second.ReviewReason);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = repository.DatabasePath, Pooling = false }.ToString());
            connection.Open();
            using var review = connection.CreateCommand();
            review.CommandText = "SELECT COUNT(*) FROM CsvImportReviewRows;";
            Assert.Equal(1L, review.ExecuteScalar());
        });
    }

    [Fact]
    public void EqualTradesFromTwoCsvFiles_KeepBothSourceRowsInReview()
    {
        WithRepository(repository =>
        {
            var first = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            first.SourceFile = "first.csv";
            first.SourceRowNumber = 2;
            var second = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            second.SourceFile = "second.csv";
            second.SourceRowNumber = 3;
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveReviewTrade: repository.SaveCsvImportReviewTrade);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => new BrokerStatementImport
                    { CanUpdateHoldings = true, Trades = new[] { first, second } }), "test" });

            Assert.Empty(repository.GetPositions());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = repository.DatabasePath, Pooling = false }.ToString());
            connection.Open();
            using var review = connection.CreateCommand();
            review.CommandText = "SELECT SourceFile, SourceRowNumber FROM CsvImportReviewRows ORDER BY SourceFile;";
            using var reader = review.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("first.csv", reader.GetString(0));
            Assert.Equal(2, reader.GetInt32(1));
            Assert.True(reader.Read());
            Assert.Equal("second.csv", reader.GetString(0));
            Assert.Equal(3, reader.GetInt32(1));
            Assert.False(reader.Read());
        });
    }

    [Fact]
    public void EqualTradesFromTwoCsvFiles_PreviewShowsBothAsPending()
    {
        WithRepository(repository =>
        {
            var first = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            first.SourceFile = "first.csv";
            first.SourceRowNumber = 2;
            var second = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            second.SourceFile = "second.csv";
            second.SourceRowNumber = 3;
            var statement = new BrokerStatementImport
                { CanUpdateHoldings = true, Trades = new[] { first, second } };
            var vm = new CsvImportViewModel(repository.GetPositions,
                getBrokerTrades: repository.GetBrokerTrades);
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });

            Assert.Equal(new[] { "要確認", "要確認" }, vm.PreviewRows.Select(x => x.Status));
            Assert.All(vm.PreviewRows, row => Assert.Contains("別CSV", row.Reason));
        });
    }

    [Fact]
    public void ExplicitlyApprovedDifferentCsvTrade_IsAddedOnceAndAudited()
    {
        WithRepository(repository =>
        {
            var first = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            first.SourceFile = "first.csv";
            first.SourceRowNumber = 2;
            Import(repository, new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { first } });
            var second = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            second.SourceFile = "second.csv";
            second.SourceRowNumber = 2;
            var statement = new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { second } };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveReviewTrade: repository.SaveCsvImportReviewTrade,
                saveImportChange: repository.SaveCsvImportChange,
                resolveReviewTrade: repository.ResolveCsvImportReviewTrade,
                getTradeResolution: repository.GetCsvImportReviewTradeResolution);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            var row = Assert.Single(vm.PreviewRows);
            Assert.True(row.CanChooseTrade);
            row.TradeChoice = "別取引として追加";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            var position = Assert.Single(repository.GetPositions());
            Assert.Equal(20m, position.CurrentHolding.CurrentShares);
            Assert.Equal(new[] { "first.csv", "second.csv" }, repository.GetBrokerTrades(position.Stock.Id)
                .Select(x => x.SourceFile).OrderBy(x => x));
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = repository.DatabasePath, Pooling = false }.ToString()))
            {
                connection.Open();
                using var batch = connection.CreateCommand();
                batch.CommandText = "SELECT BatchId FROM CsvImportChangeRows WHERE Source = 'second.csv' AND Action = '新規' LIMIT 1;";
                var changes = repository.GetCsvImportChanges(Assert.IsType<string>(batch.ExecuteScalar()));
                Assert.Contains(changes, x => x.Source == "second.csv" && x.Action == "新規" &&
                    x.Reason.Contains("別取引として採用"));
                using var review = connection.CreateCommand();
                review.CommandText = "SELECT Resolution FROM CsvImportReviewRows WHERE SourceFile = 'second.csv';";
                Assert.Equal("別取引として追加", review.ExecuteScalar());
            }

            Import(repository, new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { second } });
            Assert.Equal(20m, repository.GetPositions().Single().CurrentHolding.CurrentShares);
            Assert.Equal(2, repository.GetBrokerTrades(position.Stock.Id).Count);
        });
    }

    [Fact]
    public void DifferentCsvTrade_CanBeResolvedAsExistingWithoutAddingLedgerRow()
    {
        WithRepository(repository =>
        {
            var first = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            first.SourceFile = "first.csv";
            first.SourceRowNumber = 2;
            Import(repository, new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { first } });
            var second = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            second.SourceFile = "second.csv";
            second.SourceRowNumber = 2;
            var statement = new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { second } };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveReviewTrade: repository.SaveCsvImportReviewTrade,
                saveImportChange: repository.SaveCsvImportChange,
                resolveReviewTrade: repository.ResolveCsvImportReviewTrade,
                getTradeResolution: repository.GetCsvImportReviewTradeResolution);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            var row = Assert.Single(vm.PreviewRows);
            Assert.True(row.CanMatchExistingTrade);
            row.TradeChoice = "既存と同一";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            var position = Assert.Single(repository.GetPositions());
            Assert.Equal(10m, position.CurrentHolding.CurrentShares);
            Assert.Single(repository.GetBrokerTrades(position.Stock.Id));
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = repository.DatabasePath, Pooling = false }.ToString());
            connection.Open();
            using var review = connection.CreateCommand();
            review.CommandText = "SELECT Resolution, ResolutionBatchId FROM CsvImportReviewRows WHERE SourceFile = 'second.csv';";
            using var reader = review.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("既存と同一", reader.GetString(0));
            var batchId = reader.GetString(1);
            Assert.NotEmpty(batchId);
            reader.Close();
            Assert.Contains(repository.GetCsvImportChanges(batchId), change =>
                change.RecordKind == "取引" && change.Action == "同一" &&
                change.Reason.Contains("プレビューで既存取引と同一"));
            vm.PreviewRows.Clear();
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            Assert.Equal("同一", Assert.Single(vm.PreviewRows).Status);

            var changed = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            changed.SourceFile = "second.csv";
            changed.SourceRowNumber = 2;
            changed.SettlementAmountJpy += 100m;
            vm.PreviewRows.Clear();
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new BrokerStatementImport { CanUpdateHoldings = true, Trades = new[] { changed } }, 1 });
            var changedPreview = Assert.Single(vm.PreviewRows);
            Assert.Equal("要確認", changedPreview.Status);
            Assert.Contains("取引内容が変わりました", changedPreview.Reason);
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => new BrokerStatementImport
                    { CanUpdateHoldings = true, Trades = new[] { changed } }), "test" });
            Assert.Equal(10m, repository.GetPositions().Single().CurrentHolding.CurrentShares);
            Assert.Single(repository.GetBrokerTrades(position.Stock.Id));

            var feeChanged = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            feeChanged.SourceFile = "second.csv";
            feeChanged.SourceRowNumber = 2;
            feeChanged.FeeJpy = 1m;
            Assert.Equal("内容変更", repository.GetCsvImportReviewTradeResolution(feeChanged));

            vm.PreviewRows.Clear();
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            var priorDecision = Assert.Single(vm.PreviewRows);
            Assert.True(priorDecision.CanChooseTrade);
            Assert.Equal("既存と同一", priorDecision.TradeChoice);
            priorDecision.TradeChoice = "別取引として追加";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            Assert.Equal(20m, repository.GetPositions().Single().CurrentHolding.CurrentShares);
            Assert.Equal("別取引として追加", repository.GetCsvImportReviewTradeResolution(second));
            using var changedReview = connection.CreateCommand();
            changedReview.CommandText = "SELECT COUNT(*) FROM CsvImportReviewRows WHERE SourceFile = 'second.csv' AND Resolution = '';";
            Assert.Equal(1L, changedReview.ExecuteScalar());
        });
    }

    [Fact]
    public void TwoCsvTradeConflict_OnlySelectedRowIsImportedThenOtherCanBeAddedLater()
    {
        WithRepository(repository =>
        {
            var first = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            first.SourceFile = "first.csv";
            first.SourceRowNumber = 2;
            var second = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            second.SourceFile = "second.csv";
            second.SourceRowNumber = 3;
            var statement = new BrokerStatementImport
                { CanUpdateHoldings = true, Trades = new[] { first, second } };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveReviewTrade: repository.SaveCsvImportReviewTrade);
            var preview = typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var capture = typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var import = typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            preview.Invoke(vm, new object[] { statement, 1 });
            Assert.All(vm.PreviewRows, row => Assert.True(row.CanChooseTrade));
            Assert.All(vm.PreviewRows, row => Assert.False(row.CanMatchExistingTrade));
            Assert.DoesNotContain("既存と同一", vm.PreviewRows[0].AvailableTradeChoices);
            vm.PreviewRows.Single(row => row.Values.Contains("first.csv")).TradeChoice = "別取引として追加";
            capture.Invoke(vm, null);
            import.Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            Assert.Equal(10m, repository.GetPositions().Single().CurrentHolding.CurrentShares);

            vm.PreviewRows.Clear();
            preview.Invoke(vm, new object[] { statement, 1 });
            vm.PreviewRows.Single(row => row.Values.Contains("second.csv")).TradeChoice = "別取引として追加";
            capture.Invoke(vm, null);
            import.Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            var position = Assert.Single(repository.GetPositions());
            Assert.Equal(20m, position.CurrentHolding.CurrentShares);
            Assert.Equal(2, repository.GetBrokerTrades(position.Stock.Id).Count);
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
    public void ChangedTradeAtSameSourceRow_IsHeldForReview()
    {
        var original = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
        original.SourceFile = "same.csv";
        original.SourceRowNumber = 2;
        var existing = new TradeLedgerService().BuildLedger(1, new[] { original });
        var corrected = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
        corrected.SourceFile = "same.csv";
        corrected.SourceRowNumber = 2;
        corrected.UnitPrice = 51m;
        corrected.SettlementAmountJpy = 76500m;

        var result = TradeHistoryService.Reconcile(existing, new[] { corrected }, "KO");
        Assert.Empty(result.Accepted);
        Assert.Same(corrected, Assert.Single(result.NeedsReview));
        Assert.Contains("同じCSV行", corrected.ReviewReason);
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
    public void SameForeignDividendInUsdAndRoundedJpy_IsOneActualPayment()
    {
        var service = new DividendReconciliationService();
        var usd = new DividendPayment
        {
            Id = 1, StockId = 10, Ticker = "NVDA", Broker = "野村証券",
            AccountType = AccountTypes.Unknown, Currency = "USD", Source = DividendConstants.SourceCsv,
            DividendStatus = DividendConstants.Actual, PaymentDate = new DateTime(2026, 1, 5),
            Quantity = 50m, GrossAmount = .50m, NetAmount = .372368m, NetAmountJpy = 58m
        };
        var jpy = new DividendPayment
        {
            StockId = 10, Ticker = "NVDA", Broker = "野村証券",
            AccountType = AccountTypes.Unknown, Currency = "JPY", Source = DividendConstants.SourceCsv,
            DividendStatus = DividendConstants.Actual, PaymentDate = usd.PaymentDate,
            Quantity = 50m, GrossAmount = 70m, NetAmount = 57m, NetAmountJpy = 57m
        };

        jpy.SourceRowNumber = 39;

        Assert.Equal(usd.Id, service.ReconcileActual(new[] { usd }, jpy).DuplicateActual?.Id);
    }

    [Fact]
    public void SameCsvDividendUnderDifferentStockRows_IsOneActualPayment()
    {
        var service = new DividendReconciliationService();
        var first = new DividendPayment
        {
            Id = 1, StockId = 10, Ticker = "CMBT", Broker = "SBI証券",
            AccountType = AccountTypes.Specific, Currency = "JPY", Source = DividendConstants.SourceCsv,
            DividendStatus = DividendConstants.Actual, PaymentDate = new DateTime(2026, 1, 19),
            Quantity = 40m, NetAmountJpy = 179.64m
        };

        var second = new DividendPayment
        {
            StockId = 11, Ticker = "CMBT", Broker = "SBI証券",
            AccountType = AccountTypes.Specific, Currency = "JPY", Source = DividendConstants.SourceCsv,
            DividendStatus = DividendConstants.Actual, PaymentDate = first.PaymentDate,
            Quantity = 40m, NetAmountJpy = 179.64m
        };
        second.SourceRowNumber = 21;

        Assert.Equal(first.Id, service.ReconcileActual(new[] { first }, second).DuplicateActual?.Id);
        second.AccountType = AccountTypes.NisaGrowth;
        Assert.Null(service.ReconcileActual(new[] { first }, second).DuplicateActual);
    }

    [Fact]
    public void SeparateRowsInOneBrokerCsv_AreNotCollapsedByEqualAmounts()
    {
        var first = new DividendPayment
        {
            Id = 1, StockId = 1, Ticker = "MO", Broker = "SBI証券",
            AccountType = AccountTypes.Specific, Source = DividendConstants.SourceCsv,
            SourceFile = "statement.csv", SourceRowNumber = 20,
            DividendStatus = DividendConstants.Actual, Currency = "JPY",
            PaymentDate = new DateTime(2026, 1, 13), Quantity = 20m, NetAmountJpy = 1_000m
        };
        var second = new DividendPayment
        {
            StockId = first.StockId, Ticker = first.Ticker, Broker = first.Broker,
            AccountType = first.AccountType, Source = first.Source,
            SourceFile = first.SourceFile, SourceRowNumber = 21,
            DividendStatus = first.DividendStatus, Currency = first.Currency,
            PaymentDate = first.PaymentDate, Quantity = first.Quantity, NetAmountJpy = first.NetAmountJpy
        };

        Assert.Null(new DividendReconciliationService().ReconcileActual(new[] { first }, second).DuplicateActual);
    }

    [Fact]
    public void ImportedCsv_ReplacesMatchingManualPaymentWithBrokerAlias()
    {
        var service = new DividendReconciliationService();
        var manual = new DividendPayment
        {
            Id = 1, StockId = 10, Ticker = "CSCO", Broker = "野村證券",
            AccountType = AccountTypes.Unknown, Currency = "USD", Source = DividendConstants.SourceManual,
            DividendStatus = DividendConstants.Actual, PaymentDate = new DateTime(2026, 1, 26),
            GrossAmount = 8.2m, NetAmount = 5.883845m, NetAmountJpy = 928m
        };
        var csv = new DividendPayment
        {
            StockId = 11, Ticker = "CSCO", Broker = "野村証券",
            AccountType = AccountTypes.Unknown, Currency = "USD", Source = DividendConstants.SourceCsv,
            DividendStatus = DividendConstants.Actual, PaymentDate = manual.PaymentDate,
            Quantity = 20m, GrossAmount = manual.GrossAmount,
            NetAmount = manual.NetAmount, NetAmountJpy = manual.NetAmountJpy
        };

        var decision = service.ReconcileActual(new[] { manual }, csv);

        Assert.Null(decision.DuplicateActual);
        Assert.Equal(manual.Id, Assert.Single(decision.ReplaceTargets).Id);
    }

    [Fact]
    public void ImportDividend_ReusesLegacyPaymentEvenWhenItsStockRowDiffers()
    {
        WithRepository(repository =>
        {
            var oldPosition = Position("CMBT", "USD", 40m, 150m);
            oldPosition.Stock.AccountType = AccountTypes.NisaGrowth;
            var oldStockId = repository.SavePosition(oldPosition);
            var correctStockId = repository.SavePosition(Position("CMBT", "USD", 40m, 150m));
            var oldPaymentId = repository.SaveDividendPayment(new DividendPayment
            {
                StockId = oldStockId, Broker = "SBI証券", AccountType = AccountTypes.Specific,
                PaymentDate = new DateTime(2026, 1, 19), DividendStatus = DividendConstants.Actual,
                Source = DividendConstants.SourceCsv, SourceFile = "legacy.csv",
                Currency = "JPY", ExchangeRate = 1m, Quantity = 40m,
                NetAmount = 180m, NetAmountJpy = 180m, JpyAmount = 180m
            });
            Import(repository, new BrokerStatementImport
            {
                Dividends = new[]
                {
                    new BrokerDividendRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific,
                        Ticker = "CMBT", Name = "CMB Tech", PaymentDate = new DateTime(2026, 1, 19),
                        Currency = "JPY", Quantity = 40m, NetAmount = 180m, NetAmountJpy = 180m,
                        Source = "SBI配当CSV", SourceFile = "new.csv", SourceRowNumber = 21
                    }
                }
            });

            var payment = Assert.Single(repository.GetDividendPayments());
            Assert.Equal(oldPaymentId, payment.Id);
            Assert.Equal(correctStockId, payment.StockId);
            Assert.Equal(21, payment.SourceRowNumber);

            Import(repository, new BrokerStatementImport
            {
                Dividends = new[]
                {
                    new BrokerDividendRecord
                    {
                        Broker = payment.Broker, Account = AccountTypes.Specific,
                        Ticker = "CMBT", Name = "CMB Tech", PaymentDate = new DateTime(2026, 1, 19),
                        Currency = "JPY", Quantity = 40m, NetAmount = 180m, NetAmountJpy = 180m,
                        Source = payment.Source, SourceFile = "legacy.csv", SourceRowNumber = 0
                    }
                }
            });

            payment = Assert.Single(repository.GetDividendPayments());
            Assert.Equal(oldPaymentId, payment.Id);
            Assert.Equal(correctStockId, payment.StockId);
            Assert.Equal("new.csv", payment.SourceFile);
            Assert.Equal(21, payment.SourceRowNumber);
        });
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
    public void OversizedCsv_IsRejectedBeforeMaterializingRecords()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            using (var stream = File.Create(path)) stream.SetLength(32L * 1024 * 1024 + 1);
            var error = Assert.Throws<InvalidOperationException>(
                () => new SbiDistributionCsvParser().ParseStatement(path));
            Assert.Contains("32 MiB", error.Message);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SavePosition_ReusesLegacyStockWithDifferentCanonicalKey()
    {
        WithRepository(repository =>
        {
            var id = repository.SavePosition(Position("CMBT", "USD", 10m, 150m));
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = repository.DatabasePath, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Stocks SET CanonicalSecurityKey = 'LEGACY' WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
            }

            var newId = repository.SavePosition(Position("CMBT", "USD", 11m, 150m));
            Assert.Equal(id, newId);
            Assert.Single(repository.GetPositions());
            Assert.Equal(11m, repository.GetPositions().Single().CurrentHolding.CurrentShares);
        });
    }

    [Fact]
    public void HistoricalTradeFromAnotherAccount_DoesNotChangeNisaPositionAccount()
    {
        var position = Position("CMBT", "USD", 30m, 150m);
        position.Stock.Id = 104;
        position.Stock.AccountType = AccountTypes.NisaGrowth;
        position.Stock.CustodyType = "NISA";
        StockPosition? saved = null;
        var oldSpecificTrade = new BrokerTrade
        {
            Id = 1, StockId = 104, Broker = "SBI証券", AccountType = AccountTypes.Specific,
            CustodyType = "特定", TradeDate = new DateTime(2023, 9, 12),
            SettlementDate = new DateTime(2023, 9, 14), TradeType = "買付",
            Quantity = 6m, SignedQuantity = 6m, UnitPrice = 10m, Currency = "USD",
            Source = "SBI米国株約定CSV"
        };
        var statement = new BrokerStatementImport
        {
            CanUpdateHoldings = true,
            Trades = new[]
            {
                new BrokerTradeRecord
                {
                    Broker = "SBI証券", Ticker = "CMBT", Name = "CMBT", Account = "NISA",
                    Product = "米国株式", TradeDate = new DateTime(2024, 1, 30),
                    SettlementDate = new DateTime(2024, 2, 1), TradeType = "買付",
                    Quantity = 30m, SignedQuantity = 30m, UnitPrice = 10m,
                    Currency = "USD", ExchangeRate = 150m, Source = "SBI米国株約定CSV"
                }
            }
        };
        var vm = new CsvImportViewModel(
            () => new[] { position }, value => { saved = value; return 104; },
            _ => 1, () => Array.Empty<DividendPayment>(), _ => { }, null,
            _ => new[] { oldSpecificTrade });

        typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

        Assert.NotNull(saved);
        Assert.Equal(AccountTypes.NisaGrowth, saved.Stock.AccountType);
        Assert.Equal("NISA", saved.Stock.CustodyType);
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
    public void ConflictingHoldingRows_AreHeldAndRecordedWithoutChangingPositions()
    {
        WithRepository(repository =>
        {
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                UnresolvedHoldings = new[]
                {
                    new BrokerHoldingRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific,
                        Ticker = "8593", Name = "Example", Shares = 10m,
                        SnapshotDate = new DateTime(2026, 9, 26),
                        SourceFile = "first.csv", SourceRowNumber = 4,
                        ReviewReason = "同一日時の保有残高が食い違います。"
                    },
                    new BrokerHoldingRecord
                    {
                        Broker = "SBI証券", Account = AccountTypes.Specific,
                        Ticker = "8593", Name = "Example", Shares = 20m,
                        SnapshotDate = new DateTime(2026, 9, 26),
                        SourceFile = "second.csv", SourceRowNumber = 4,
                        ReviewReason = "同一日時の保有残高が食い違います。"
                    }
                }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveReviewHolding: repository.SaveCsvImportReviewHolding,
                saveImportChange: repository.SaveCsvImportChange);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            Assert.Empty(repository.GetPositions());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = repository.DatabasePath, Pooling = false }.ToString());
            connection.Open();
            using var review = connection.CreateCommand();
            review.CommandText = "SELECT COUNT(*) FROM CsvImportReviewHoldings WHERE Reason LIKE '%食い違います%';";
            Assert.Equal(2L, review.ExecuteScalar());
            using var batch = connection.CreateCommand();
            batch.CommandText = "SELECT BatchId FROM CsvImportChangeRows LIMIT 1;";
            var changes = repository.GetCsvImportChanges(Assert.IsType<string>(batch.ExecuteScalar()));
            Assert.Equal(2, changes.Count);
            Assert.All(changes, x =>
            {
                Assert.Equal("要確認", x.Action);
                Assert.Equal(4, x.SourceRowNumber);
                Assert.Contains("食い違います", x.Reason);
            });
        });
    }

    [Fact]
    public void ConflictingHoldingRows_OnlyExplicitlyChosenRowIsApplied()
    {
        WithRepository(repository =>
        {
            var first = ConflictHolding("first.csv", 10m);
            var second = ConflictHolding("second.csv", 20m);
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                UnresolvedHoldings = new[] { first, second }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveReviewHolding: repository.SaveCsvImportReviewHolding,
                saveImportChange: repository.SaveCsvImportChange);
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            Assert.All(vm.PreviewRows, row => Assert.True(row.CanChooseConflict));
            vm.PreviewRows[1].ConflictChoice = "この行を採用";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            Assert.Equal(20m, Assert.Single(repository.GetPositions()).CurrentHolding.CurrentShares);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = repository.DatabasePath, Pooling = false }.ToString());
            connection.Open();
            using var review = connection.CreateCommand();
            review.CommandText = "SELECT COUNT(*) FROM CsvImportReviewHoldings;";
            Assert.Equal(1L, review.ExecuteScalar());
            using var batch = connection.CreateCommand();
            batch.CommandText = "SELECT BatchId FROM CsvImportChangeRows LIMIT 1;";
            var changes = repository.GetCsvImportChanges(Assert.IsType<string>(batch.ExecuteScalar()));
            Assert.Contains(changes, row => row.Action == "新規" &&
                row.SourceDisplay.Contains("second.csv", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(changes, row => row.Action == "要確認" &&
                row.SourceDisplay.Contains("first.csv", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void ConflictingHoldingRows_MultipleChoicesRollBackEntireImport()
    {
        WithRepository(repository =>
        {
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                UnresolvedHoldings = new[]
                {
                    ConflictHolding("first.csv", 10m), ConflictHolding("second.csv", 20m)
                }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically);
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            foreach (var row in vm.PreviewRows) row.ConflictChoice = "この行を採用";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            Assert.Empty(repository.GetPositions());
            Assert.Contains("複数選択", vm.Message);
        });
    }

    [Fact]
    public void ConflictingHoldingRows_CanChooseUnderManualMergePolicy()
    {
        var statement = new BrokerStatementImport
        {
            UnresolvedHoldings = new[]
            {
                ConflictHolding("first.csv", 10m), ConflictHolding("second.csv", 20m)
            }
        };
        var vm = new CsvImportViewModel(getMergePolicy: () => "すべてプレビューして手動選択");
        typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });

        Assert.All(vm.PreviewRows, row => Assert.True(row.CanChooseConflict));
    }

    [Fact]
    public void ManualMergePolicy_AppliesOnlyChosenHoldingAndDividend_WithoutClosingOtherHoldings()
    {
        WithRepository(repository =>
        {
            repository.SavePosition(Position("KO", "USD", 10m, 150m));
            var chosen = ConflictHolding("chosen.csv", 5m);
            chosen.ReviewReason = string.Empty;
            var skipped = ConflictHolding("skipped.csv", 8m);
            skipped.Ticker = "7203";
            skipped.ReviewReason = string.Empty;
            var dividend = new BrokerDividendRecord
            {
                Broker = "SBI証券", Account = AccountTypes.Specific, Ticker = "KO", Name = "Coca-Cola",
                PaymentDate = new DateTime(2026, 9, 1), Currency = "JPY", NetAmount = 100m,
                NetAmountJpy = 100m, SourceFile = Path.GetFullPath("dividend.csv"), SourceRowNumber = 2
            };
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                Holdings = new[] { chosen, skipped }, Dividends = new[] { dividend },
                CompleteHoldingScopes = new[] { new CompleteHoldingSnapshotScope
                {
                    Broker = "SBI証券", Account = AccountTypes.Specific,
                    SnapshotDate = new DateTime(2026, 9, 26)
                } }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically, () => "すべてプレビューして手動選択");
            var preview = typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var capture = typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var import = typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            preview.Invoke(vm, new object[] { statement, 1 });
            Assert.Equal(3, vm.PreviewRows.Count(row => row.CanChooseManual));
            capture.Invoke(vm, null);
            import.Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            Assert.Contains("選択されていません", vm.Message);
            Assert.Single(repository.GetPositions());
            Assert.Empty(repository.GetDividendPayments());

            vm.PreviewRows.Single(row => row.Values.Contains("chosen.csv")).ManualChoice = "この行を反映";
            vm.PreviewRows.Single(row => row.Values.Contains("dividend.csv")).ManualChoice = "この行を反映";
            capture.Invoke(vm, null);
            import.Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            var positions = repository.GetPositions();
            Assert.Equal(2, positions.Count);
            Assert.Equal(10m, positions.Single(x => x.Stock.Ticker == "KO").CurrentHolding.CurrentShares);
            Assert.Equal(5m, positions.Single(x => x.Stock.Ticker == "8593").CurrentHolding.CurrentShares);
            Assert.DoesNotContain(positions, x => x.Stock.Ticker == "7203");
            Assert.Single(repository.GetDividendPayments());
        });
    }

    [Fact]
    public void ManualMergePolicy_AppliesOnlyChosenTrade()
    {
        WithRepository(repository =>
        {
            var chosen = TradeStatement(new DateTime(2026, 1, 1), 10m).Trades.Single();
            chosen.SourceFile = Path.GetFullPath("chosen-trade.csv");
            chosen.SourceRowNumber = 2;
            var skipped = TradeStatement(new DateTime(2026, 1, 2), 5m).Trades.Single();
            skipped.SourceFile = Path.GetFullPath("skipped-trade.csv");
            skipped.SourceRowNumber = 3;
            var statement = new BrokerStatementImport
                { CanUpdateHoldings = true, Trades = new[] { chosen, skipped } };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically, () => "すべてプレビューして手動選択");
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            Assert.Equal(2, vm.PreviewRows.Count(row => row.CanChooseManual));
            vm.PreviewRows.Single(row => row.Values.Contains("chosen-trade.csv")).ManualChoice = "この行を反映";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            var position = Assert.Single(repository.GetPositions());
            Assert.Equal(10m, position.CurrentHolding.CurrentShares);
            Assert.Single(repository.GetBrokerTrades(position.Stock.Id));
        });
    }

    [Fact]
    public void ManualMergePolicy_ChosenConflictDoesNotPersistUnchosenConflict()
    {
        WithRepository(repository =>
        {
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true,
                UnresolvedHoldings = new[]
                    { ConflictHolding("first.csv", 10m), ConflictHolding("second.csv", 20m) }
            };
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically, () => "すべてプレビューして手動選択",
                saveReviewHolding: repository.SaveCsvImportReviewHolding);
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });
            vm.PreviewRows.Single(row => row.Values.Contains("second.csv")).ConflictChoice = "この行を採用";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });

            Assert.Equal(20m, Assert.Single(repository.GetPositions()).CurrentHolding.CurrentShares);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = repository.DatabasePath, Pooling = false }.ToString());
            connection.Open();
            using var review = connection.CreateCommand();
            review.CommandText = "SELECT COUNT(*) FROM CsvImportReviewHoldings;";
            Assert.Equal(0L, review.ExecuteScalar());
        });
    }

    [Fact]
    public void ManualMergePolicy_DividendOnlyCsvAppliesSelectedRow()
    {
        WithRepository(repository =>
        {
            repository.SavePosition(Position("KO", "USD", 10m, 150m));
            var records = new[] { 1, 2 }.Select(day => new BrokerDividendRecord
            {
                Broker = "SBI証券", Account = AccountTypes.Specific,
                Ticker = "KO", Name = "Coca-Cola", PaymentDate = new DateTime(2026, 9, day),
                Currency = "JPY", NetAmount = day * 100m, NetAmountJpy = day * 100m,
                SourceFile = Path.GetFullPath("dividend-only.csv"), SourceRowNumber = day + 1
            }).ToArray();
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                getMergePolicy: () => "すべてプレビューして手動選択",
                runImportAtomically: repository.RunImportAtomically);
            typeof(CsvImportViewModel).GetMethod("AddDividendPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { records, 1 });
            vm.PreviewRows.Single(row => row.Values.Contains("2026/09/02")).ManualChoice = "この行を反映";
            typeof(CsvImportViewModel).GetMethod("CaptureHoldingConflictChoices",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            typeof(CsvImportViewModel).GetMethod("ImportDividendRecords",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                new object[] { new Func<IReadOnlyList<BrokerDividendRecord>>(() => records), "test" });

            Assert.Equal(new DateTime(2026, 9, 2), Assert.Single(repository.GetDividendPayments()).PaymentDate);
        });
    }

    private static BrokerHoldingRecord ConflictHolding(string fileName, decimal shares) => new()
    {
        Broker = "SBI証券", Account = AccountTypes.Specific,
        Ticker = "8593", Name = "Example", Shares = shares,
        Currency = "JPY", CurrentPrice = 100m, MarketValue = shares * 100m,
        SnapshotDate = new DateTime(2026, 9, 26),
        SourceFile = Path.GetFullPath(fileName), SourceRowNumber = 4,
        ReviewReason = "同一日時の保有残高が食い違います。"
    };

    [Fact]
    public void StatementPreview_ShowsAuditedInputRowsAndOriginalIssueLines()
    {
        var statement = new BrokerStatementImport
        {
            SourceRowAudits = new Dictionary<string, CsvSourceRowAudit>
            {
                ["statement.csv"] = new()
                {
                    InputRows = 2, PendingRows = 1, ExcludedRows = 1,
                    Issues = new[]
                    {
                        new CsvRowIssue(3, "要確認", "銘柄コードなし"),
                        new CsvRowIssue(5, "対象外", "投資対象外の明細")
                    }
                }
            }
        };
        var vm = new CsvImportViewModel();
        typeof(CsvImportViewModel).GetMethod("PreviewBrokerStatement",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
            new object[] { new Func<BrokerStatementImport>(() => statement), "test", "statement.csv" });

        var audit = Assert.Single(vm.PreviewAudits);
        Assert.Equal("一致", audit.Balance);
        Assert.Equal(2, audit.InputRows);
        Assert.True(vm.HasPreviewAudits);
        Assert.Equal(new[] { "要確認", "対象外" }, vm.PreviewRows.Select(x => x.Status));
        Assert.Contains("statement.csv:3", vm.PreviewRows[0].Values);
        Assert.Contains("statement.csv:5", vm.PreviewRows[1].Values);
    }

    [Fact]
    public void StatementPreview_RejectsUnbalancedAuditWithoutLeavingPartialRows()
    {
        var statement = new BrokerStatementImport
        {
            SourceRowAudits = new Dictionary<string, CsvSourceRowAudit>
            {
                ["statement.csv"] = new() { InputRows = 2, ParsedRows = 1 }
            }
        };
        var vm = new CsvImportViewModel();
        typeof(CsvImportViewModel).GetMethod("PreviewBrokerStatement",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
            new object[] { new Func<BrokerStatementImport>(() => statement), "test", "statement.csv" });

        Assert.Contains("入力行数と分類件数が一致しません", vm.Message);
        Assert.Empty(vm.PreviewAudits);
        Assert.Empty(vm.PreviewRows);
        Assert.False(vm.HasPreviewAudits);
    }

    [Fact]
    public void TradePreview_PreservesIdenticalTransactionMultiplicity()
    {
        WithRepository(repository =>
        {
            static BrokerTradeRecord Trade() => new()
            {
                Broker = "SBI証券", Account = AccountTypes.Specific,
                Ticker = "KO", Name = "Coca-Cola", TradeType = "現物買付",
                TradeDate = new DateTime(2026, 8, 1), SettlementDate = new DateTime(2026, 8, 3),
                Quantity = 10m, SignedQuantity = 10m, UnitPrice = 50m,
                SettlementAmountJpy = 75_000m, ExchangeRate = 150m,
                Currency = "USD", Source = "test"
            };
            var stockId = repository.SavePosition(Position("KO", "USD", 10m, 150m));
            repository.SaveBrokerTrades(new[] { Trade() });
            var persisted = Assert.Single(repository.GetBrokerTrades(stockId));
            Assert.Equal(AccountTypes.Specific, persisted.CustodyType);
            var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically);
            var statement = new BrokerStatementImport
            {
                CanUpdateHoldings = true, Trades = new[] { Trade(), Trade() }
            };
            typeof(CsvImportViewModel).GetMethod("AddStatementPreviewRows",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { statement, 1 });

            Assert.Equal(new[] { "同一", "新規" }, vm.PreviewRows.Select(x => x.Status));
            Assert.Contains("75,000", vm.PreviewRows[1].After);
            var stock = Assert.Single(repository.GetPositions());
            Assert.Single(repository.GetBrokerTrades(stock.Stock.Id));

            var importVm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                repository.SaveDividendPayment, repository.GetDividendPayments,
                repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                repository.RunImportAtomically,
                saveImportLog: repository.SaveCsvImportLog,
                saveImportChange: repository.SaveCsvImportChange);
            typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(importVm,
                new object[] { new Func<BrokerStatementImport>(() => statement), "test" });
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = repository.DatabasePath, Pooling = false }.ToString());
            connection.Open();
            using var batch = connection.CreateCommand();
            batch.CommandText = "SELECT BatchId FROM CsvImportChangeRows LIMIT 1;";
            var batchId = Assert.IsType<string>(batch.ExecuteScalar());
            var tradeChanges = repository.GetCsvImportChanges(batchId);
            Assert.Equal(new[] { "同一", "新規" }, tradeChanges.Select(x => x.Action));
            Assert.All(tradeChanges, x => Assert.Equal("取引", x.RecordKind));
            Assert.Equal(75_000m, tradeChanges[1].AfterAmountJpy);
        });
    }

    [Fact]
    public void DividendPreview_ShowsEveryRowAndDoesNotWriteToDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            var rows = Enumerable.Range(1, 25)
                .Select(day => $"2026/08/{day:00},特定,米国株式,Example Corp EXMP,10,{100 + day}");
            File.WriteAllText(path,
                "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                string.Join("\n", rows) + "\n", Encoding.UTF8);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically) { FilePath = path };

                vm.PreviewCommand.Execute(null);

                Assert.Equal(25, vm.PreviewRows.Count);
                var audit = Assert.Single(vm.PreviewAudits);
                Assert.Equal(25, audit.InputRows);
                Assert.Equal(25, audit.ParsedRows);
                Assert.Equal("一致", audit.Balance);
                Assert.All(vm.PreviewRows, row => Assert.Equal("新規", row.Status));
                Assert.Equal(25, vm.PreviewRows.Last().RowNumber);
                Assert.Empty(repository.GetDividendPayments());
                Assert.Empty(repository.GetPositions());
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DividendPreview_ReimportAndChangedSourceRowHaveDifferentOutcomes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        try
        {
            static string Csv(int amount) =>
                "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)\n" +
                $"2026/08/01,特定,米国株式,Example Corp EXMP,10,{amount}\n";
            File.WriteAllText(path, Csv(100), Encoding.UTF8);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically,
                    saveImportLog: repository.SaveCsvImportLog,
                    saveImportChange: repository.SaveCsvImportChange) { FilePath = path };
                vm.PreviewCommand.Execute(null);
                Assert.Equal("新規", Assert.Single(vm.PreviewRows).Status);
                vm.ImportCommand.Execute(null);
                Assert.Equal(100m, Assert.Single(repository.GetDividendPayments()).NetAmountJpy);
                var batchId = repository.GetRecentCsvImportLogs().First().BatchId;
                var change = Assert.Single(repository.GetCsvImportChanges(batchId));
                Assert.Equal("配当", change.RecordKind);
                Assert.Equal("新規", change.Action);
                Assert.Equal(100m, change.AfterAmountJpy);
                Assert.True(change.SourceRowNumber > 0);

                vm.PreviewCommand.Execute(null);
                Assert.Equal("同一", Assert.Single(vm.PreviewRows).Status);

                File.WriteAllText(path, Csv(150), Encoding.UTF8);
                vm.PreviewCommand.Execute(null);
                var changed = Assert.Single(vm.PreviewRows);
                Assert.Equal("要確認", changed.Status);
                Assert.Contains("100", changed.Before);
                Assert.Equal("反映保留", changed.After);
                Assert.Equal(100m, Assert.Single(repository.GetDividendPayments()).NetAmountJpy);
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ImportedDividend_NineDayScheduleCandidateRemainsForReview()
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
                    repository.RunImportAtomically, null, null, repository.SaveCsvImportReviewDividend)
                { FilePath = path };
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);

                var payments = repository.GetDividendPayments();
                Assert.Single(payments, x => x.DividendStatus == DividendConstants.Actual);
                var planned = Assert.Single(payments, x => x.DividendStatus == DividendConstants.Planned);
                Assert.Null(planned.ReplacedByDividendId);
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = repository.DatabasePath, Pooling = false }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Reason FROM CsvImportReviewDividends LIMIT 1;";
                Assert.Contains("近似一致", (string)command.ExecuteScalar()!);
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ImportedDividend_DoesNotReplaceMultipleScheduleCandidates()
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
                foreach (var date in new[] { new DateTime(2026, 8, 1), new DateTime(2026, 8, 2) })
                    repository.SaveDividendPayment(new DividendPayment
                    {
                        StockId = stockId, Broker = "SBI証券", PaymentDate = date,
                        AccountType = AccountTypes.Specific, TaxAccountType = AccountTypes.Specific,
                        Currency = "JPY", NetAmount = 100m, NetAmountJpy = 100m,
                        DividendStatus = DividendConstants.Planned, Source = DividendConstants.SourceManual
                    });

                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    repository.SaveBrokerTrades, null, repository.GetBrokerTrades,
                    repository.RunImportAtomically, null, null, repository.SaveCsvImportReviewDividend)
                { FilePath = path };
                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);
                Assert.Equal(2, repository.GetDividendPayments().Count(x => x.DividendStatus == DividendConstants.Planned));
                Assert.Single(repository.GetDividendPayments(), x => x.DividendStatus == DividendConstants.Actual);
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = repository.DatabasePath, Pooling = false }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM CsvImportReviewDividends;";
                Assert.Equal(1L, (long)command.ExecuteScalar()!);

                vm.PreviewCommand.Execute(null);
                vm.ImportCommand.Execute(null);
                Assert.Equal(3, repository.GetDividendPayments().Count);
                Assert.Equal(1L, (long)command.ExecuteScalar()!);
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
