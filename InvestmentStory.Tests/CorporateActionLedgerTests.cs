using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class CorporateActionLedgerTests
{
    [Fact]
    public void ConfirmedTransferAndSplit_PreserveEconomicCostAndReconstructPastAccounts()
    {
        var start = new DateTime(2026, 1, 1);
        var events = new[]
        {
            Opening(start, 1, 100m, 250_000m),
            new CorporateAction
            {
                Id = 2, StockId = 1, EventDate = start.AddMonths(1), Kind = "Transfer",
                SourceBroker = "SBI", SourceAccount = AccountTypes.Specific,
                DestinationBroker = "Nomura", DestinationAccount = AccountTypes.General,
                Quantity = 40m, CarriedCostJpy = 100_000m,
                Status = "Confirmed", Evidence = "移管報告書"
            },
            new CorporateAction
            {
                Id = 3, StockId = 1, EventDate = start.AddMonths(2), Kind = "StockSplit",
                SourceBroker = "Nomura", SourceAccount = AccountTypes.General,
                Quantity = 40m, SplitRatio = 2m,
                Status = "Confirmed", Evidence = "分割通知"
            }
        };

        var before = CorporateActionLedgerService.Reconstruct(events, start.AddDays(15));
        var transferred = CorporateActionLedgerService.Reconstruct(events, start.AddMonths(1));
        var split = CorporateActionLedgerService.Reconstruct(events, start.AddMonths(2));

        Assert.True(before.IsComplete);
        Assert.Equal(100m, before.TotalQuantity);
        Assert.Equal(250_000m, before.TotalCostJpy);
        Assert.Equal(100m, transferred.TotalQuantity);
        Assert.Equal(250_000m, transferred.TotalCostJpy);
        Assert.Equal(60m, transferred.Accounts.Single(x => x.Broker == "SBI").Quantity);
        Assert.Equal(40m, transferred.Accounts.Single(x => x.Broker == "Nomura").Quantity);
        Assert.Equal(AccountTypes.General, transferred.Accounts.Single(x => x.Broker == "Nomura").Account);
        Assert.Equal(140m, split.TotalQuantity);
        Assert.Equal(250_000m, split.TotalCostJpy);
        Assert.Equal(80m, split.Accounts.Single(x => x.Broker == "Nomura").Quantity);
    }

    [Fact]
    public void MissingEvidenceOrAccount_IsNeverTreatedAsConfirmedBalance()
    {
        var start = new DateTime(2026, 1, 1);
        var pending = Opening(start, 1, 10m, null) with { Status = "Pending", Evidence = "" };
        Assert.False(CorporateActionLedgerService.Reconstruct([pending], start).IsComplete);
        Assert.Null(CorporateActionLedgerService.Reconstruct([], start).TotalQuantity);

        var unknownAccount = Opening(start, 1, 10m, null) with
        { DestinationAccount = AccountTypes.Unknown };
        Assert.Throws<ArgumentException>(() => CorporateActionLedgerService.Validate(unknownAccount));

        var badCost = new CorporateAction
        {
            Id = 2, StockId = 1, EventDate = start.AddDays(1), Kind = "Transfer",
            SourceBroker = "SBI", SourceAccount = AccountTypes.Specific,
            DestinationBroker = "Nomura", DestinationAccount = AccountTypes.General,
            Quantity = 4m, CarriedCostJpy = 99m,
            Status = "Confirmed", Evidence = "移管報告書"
        };
        var result = CorporateActionLedgerService.Reconstruct([Opening(start, 1, 10m, 100m), badCost],
            start.AddDays(1));
        Assert.False(result.IsComplete);
        Assert.Contains("引継原価", result.Reason);
        Assert.Null(result.TotalCostJpy);
    }

    [Fact]
    public void Repository_RoundTripsEvidenceAndExactDecimal_AndBacksUpSchemaMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_actions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portfolio.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var stockId = repository.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "EX", Name = "Example", Currency = "JPY" },
                Purchase = new Purchase { Shares = 1m, UnitPrice = 1m },
                CurrentHolding = new CurrentHolding { CurrentShares = 1m, CurrentPrice = 1m }
            });

            using (var connection = new SqliteConnection(
                       new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE CorporateActions;";
                command.ExecuteNonQuery();
            }
            repository.Initialize();
            Assert.Single(Directory.GetFiles(directory, "*_before_corporate_action_ledger.db"));

            var actions = new CorporateActionRepository(path);
            var amount = 123456.123456789123456789m;
            var id = actions.Append(Opening(new DateTime(2026, 1, 1), stockId, 3m, amount));
            var row = Assert.Single(actions.GetForStock(stockId));
            Assert.Equal(id, row.Id);
            Assert.Equal(amount, row.CarriedCostJpy);
            Assert.Equal("残高証明", row.Evidence);
            Assert.Equal(amount, actions.GetBalance(stockId, new DateTime(2026, 1, 2)).TotalCostJpy);
            Assert.Null(actions.GetBalance(stockId, new DateTime(2025, 12, 31)).TotalQuantity);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DetailEditor_RequiresEvidenceAndExplicitAccountsForConfirmedTransfer()
    {
        CorporateAction? saved = null;
        var position = new StockPosition
        {
            Stock = new Stock
            {
                Id = 7, Ticker = "EX", Name = "Example", Currency = "JPY",
                Broker = "SBI", AccountType = AccountTypes.Specific
            },
            Purchase = new Purchase { Shares = 10m, UnitPrice = 100m },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 120m }
        };
        var detail = new StockDetailViewModel(action => saved = action);
        detail.Update(new InvestmentCalculator().CreateSnapshot(position), "Example");
        detail.ActionStatus = "Confirmed";
        detail.ActionKind = "Transfer";
        detail.ActionQuantity = 5m;
        detail.ActionDestinationBroker = "Nomura";
        detail.ActionDestinationAccount = AccountTypes.General;
        detail.SaveCorporateActionCommand.Execute(null);
        Assert.Null(saved);
        Assert.Contains("根拠", detail.CorporateActionMessage);

        detail.ActionEvidence = "移管報告書";
        detail.SaveCorporateActionCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal("SBI", saved.SourceBroker);
        Assert.Equal(AccountTypes.Specific, saved.SourceAccount);
        Assert.Equal("Nomura", saved.DestinationBroker);
        Assert.Equal(AccountTypes.General, saved.DestinationAccount);
        Assert.Equal("移管報告書", saved.Evidence);
    }

    [Fact]
    public void Correction_PreservesOldEvidenceAndReplaysOnlyLatestEvent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_action_correction_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portfolio.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var stockId = repository.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "EX", Name = "Example", Currency = "JPY" },
                Purchase = new Purchase { Shares = 10m, UnitPrice = 100m },
                CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 120m }
            });
            var actions = new CorporateActionRepository(path);
            var openingId = actions.Append(Opening(new DateTime(2026, 1, 1), stockId, 10m, 1_000m));
            var originalId = actions.Append(new CorporateAction
            {
                StockId = stockId, EventDate = new DateTime(2026, 2, 1), Kind = "Transfer",
                SourceBroker = "SBI", SourceAccount = AccountTypes.Specific,
                DestinationBroker = "Nomura", DestinationAccount = AccountTypes.General,
                Quantity = 4m, CarriedCostJpy = 400m, Status = "Pending",
                Evidence = "確認前の資料"
            });
            var corrected = new CorporateAction
            {
                StockId = stockId, EventDate = new DateTime(2026, 2, 1), Kind = "Transfer",
                SourceBroker = "SBI", SourceAccount = AccountTypes.Specific,
                DestinationBroker = "Nomura", DestinationAccount = AccountTypes.General,
                Quantity = 5m, CarriedCostJpy = 500m, Status = "Confirmed",
                Evidence = "移管報告書", Reason = "報告書で数量を訂正", SupersedesId = originalId
            };
            actions.Append(corrected);

            var history = actions.GetForStock(stockId);
            Assert.Equal(3, history.Count);
            Assert.Contains(history, x => x.Id == openingId);
            Assert.Contains(history, x => x.Id == originalId && x.Status == "Pending");
            var balance = actions.GetBalance(stockId, new DateTime(2026, 2, 2));
            Assert.True(balance.IsComplete);
            Assert.Equal(10m, balance.TotalQuantity);
            Assert.Equal(1_000m, balance.TotalCostJpy);
            Assert.Equal(5m, balance.Accounts.Single(x => x.Broker == "Nomura").Quantity);
            Assert.Throws<ArgumentException>(() => actions.Append(corrected));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void TradeImport_RegistersOnePendingSplitCandidateWithCsvEvidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_import_action_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portfolio.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var stockId = repository.SavePosition(new StockPosition
            {
                Stock = new Stock
                {
                    Ticker = "EX", Name = "Example", Broker = "SBI",
                    AccountType = AccountTypes.Specific, Currency = "USD"
                },
                Purchase = new Purchase { Shares = 10m, UnitPrice = 100m },
                CurrentHolding = new CurrentHolding { CurrentShares = 20m, CurrentPrice = 120m }
            });
            var trades = new[]
            {
                new BrokerTradeRecord
                {
                    Ticker = "EX", Broker = "SBI", Account = AccountTypes.Specific,
                    TradeDate = new DateTime(2026, 1, 1), SettlementDate = new DateTime(2026, 1, 3),
                    TradeType = "買付", Quantity = 10m, SignedQuantity = 10m,
                    UnitPrice = 100m, Currency = "USD", ExchangeRate = 150m,
                    Source = "SBI取引履歴CSV", SourceFile = "trades.csv", SourceRowNumber = 2
                },
                new BrokerTradeRecord
                {
                    Ticker = "EX", Broker = "SBI", Account = AccountTypes.Specific,
                    TradeDate = new DateTime(2026, 2, 1), SettlementDate = new DateTime(2026, 2, 1),
                    TradeType = "入庫", Quantity = 10m, SignedQuantity = 10m,
                    UnitPrice = 0m, Currency = "USD", ExchangeRate = 150m,
                    Source = "SBI取引履歴CSV", SourceFile = "trades.csv", SourceRowNumber = 3
                }
            };
            repository.SaveBrokerTrades(trades);
            repository.SaveBrokerTrades(trades);
            var action = Assert.Single(new CorporateActionRepository(path).GetForStock(stockId));
            Assert.Equal("StockSplit", action.Kind);
            Assert.Equal("Pending", action.Status);
            Assert.Equal("trades.csv", action.SourceFile);
            Assert.Equal(3, action.SourceRowNumber);
            Assert.Contains("分割候補", action.Reason);
            Assert.Equal("StockSplitCandidate", repository.GetBrokerTrades(stockId)
                .Single(x => x.SourceRowNumber == 3).TradeType);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void HistoricalBalance_UsesSettledBuysAndSellsAfterOpeningEvidence()
    {
        var opening = Opening(new DateTime(2026, 1, 1), 1, 10m, 1_000m);
        var tradeRows = new[]
        {
            new BrokerTrade
            {
                Id = 1, StockId = 1, Broker = "SBI", AccountType = AccountTypes.Specific,
                TradeDate = new DateTime(2026, 2, 1), SettlementDate = new DateTime(2026, 2, 3),
                TradeType = "買付", SignedQuantity = 5m, Quantity = 5m,
                SettlementAmountJpy = 600m
            },
            new BrokerTrade
            {
                Id = 2, StockId = 1, Broker = "SBI", AccountType = AccountTypes.Specific,
                TradeDate = new DateTime(2026, 3, 1), SettlementDate = new DateTime(2026, 3, 3),
                TradeType = "売却", SignedQuantity = -3m, Quantity = 3m,
                SettlementAmountJpy = 450m
            }
        };
        var beforeSettlement = CorporateActionLedgerService.Reconstruct(
            [opening], tradeRows, new DateTime(2026, 2, 2));
        var afterBuy = CorporateActionLedgerService.Reconstruct(
            [opening], tradeRows, new DateTime(2026, 2, 3));
        var afterSell = CorporateActionLedgerService.Reconstruct(
            [opening], tradeRows, new DateTime(2026, 3, 3));

        Assert.Equal(10m, beforeSettlement.TotalQuantity);
        Assert.Equal(1_000m, beforeSettlement.TotalCostJpy);
        Assert.Equal(15m, afterBuy.TotalQuantity);
        Assert.Equal(1_600m, afterBuy.TotalCostJpy);
        Assert.Equal(12m, afterSell.TotalQuantity);
        Assert.Equal(1_280m, afterSell.TotalCostJpy);
    }

    [Fact]
    public void HistoricalBalance_JoinsAccountsOfSameSecurityAcrossPositionRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_linked_accounts_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portfolio.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var source = new StockPosition
            {
                Stock = new Stock
                {
                    Ticker = "EX", Name = "Example", Broker = "SBI",
                    AccountType = AccountTypes.Specific, Currency = "JPY"
                },
                Purchase = new Purchase { Shares = 10m, UnitPrice = 100m },
                CurrentHolding = new CurrentHolding { CurrentShares = 6m, CurrentPrice = 120m }
            };
            var destination = new StockPosition
            {
                Stock = new Stock
                {
                    Ticker = "EX", Name = "Example", Broker = "Nomura",
                    AccountType = AccountTypes.General, Currency = "JPY"
                },
                Purchase = new Purchase { Shares = 4m, UnitPrice = 100m },
                CurrentHolding = new CurrentHolding { CurrentShares = 4m, CurrentPrice = 120m }
            };
            var sourceId = repository.SavePosition(source);
            var destinationId = repository.SavePosition(destination);
            Assert.NotEqual(sourceId, destinationId);
            var actions = new CorporateActionRepository(path);
            actions.Append(Opening(new DateTime(2026, 1, 1), sourceId, 10m, 1_000m));
            actions.Append(new CorporateAction
            {
                StockId = sourceId, EventDate = new DateTime(2026, 2, 1), Kind = "Transfer",
                SourceBroker = "SBI", SourceAccount = AccountTypes.Specific,
                DestinationBroker = "Nomura", DestinationAccount = AccountTypes.General,
                Quantity = 4m, CarriedCostJpy = 400m,
                Status = "Confirmed", Evidence = "移管報告書"
            });
            var balance = actions.GetBalance(destinationId, new DateTime(2026, 2, 2));
            Assert.True(balance.IsComplete);
            Assert.Equal(10m, balance.TotalQuantity);
            Assert.Equal(1_000m, balance.TotalCostJpy);
            Assert.Equal(6m, balance.Accounts.Single(x => x.Broker == "SBI").Quantity);
            Assert.Equal(4m, balance.Accounts.Single(x => x.Broker == "Nomura").Quantity);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void HistoricalBalance_FollowsConfirmedTickerChangeAcrossOldAndNewRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_ticker_change_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portfolio.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            int Add(string ticker, string broker, string account, decimal shares) =>
                repository.SavePosition(new StockPosition
                {
                    Stock = new Stock
                    {
                        Ticker = ticker, Name = ticker, Broker = broker,
                        AccountType = account, Country = "米国", Currency = "USD"
                    },
                    Purchase = new Purchase { Shares = shares, UnitPrice = 100m },
                    CurrentHolding = new CurrentHolding
                    { CurrentShares = shares, CurrentPrice = 120m, CurrentExchangeRate = 150m }
                });
            var oldId = Add("OLD", "SBI", AccountTypes.Specific, 10m);
            var newId = Add("NEW", "Nomura", AccountTypes.General, 4m);
            var actions = new CorporateActionRepository(path);
            actions.Append(Opening(new DateTime(2026, 1, 1), oldId, 10m, 10_000m));
            actions.Append(new CorporateAction
            {
                StockId = newId, EventDate = new DateTime(2026, 2, 1), Kind = "TickerChange",
                OldTicker = "OLD", NewTicker = "NEW",
                Status = "Confirmed", Evidence = "銘柄変更通知"
            });
            actions.Append(new CorporateAction
            {
                StockId = newId, EventDate = new DateTime(2026, 2, 2), Kind = "Transfer",
                SourceBroker = "SBI", SourceAccount = AccountTypes.Specific,
                DestinationBroker = "Nomura", DestinationAccount = AccountTypes.General,
                Quantity = 4m, CarriedCostJpy = 4_000m,
                Status = "Confirmed", Evidence = "移管報告書"
            });
            var balance = actions.GetBalance(oldId, new DateTime(2026, 2, 3));
            Assert.True(balance.IsComplete);
            Assert.Equal(10m, balance.TotalQuantity);
            Assert.Equal(10_000m, balance.TotalCostJpy);
            Assert.Equal(4m, balance.Accounts.Single(x => x.Broker == "Nomura").Quantity);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Initialize_BackfillsLegacySplitCandidateOnceAfterBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_legacy_action_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portfolio.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var stockId = repository.SavePosition(new StockPosition
            {
                Stock = new Stock { Ticker = "EX", Name = "Example", Broker = "SBI", Currency = "JPY" },
                Purchase = new Purchase { Shares = 10m, UnitPrice = 100m },
                CurrentHolding = new CurrentHolding { CurrentShares = 20m, CurrentPrice = 120m }
            });
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO BrokerTrades
                        (StockId, TradeDate, SettlementDate, Broker, AccountType, CustodyType,
                         TradeType, Quantity, SignedQuantity, UnitPrice, Currency, ExchangeRate,
                         SettlementAmountJpy, AfterTradeQuantity, AfterTradeAverageCost,
                         Source, SourceFile, SourceRowNumber)
                    VALUES ($stockId, '2026-02-01', '2026-02-01', 'SBI', 'Specific', 'Specific',
                            'StockSplit', 10, 10, 0, 'JPY', 1, 0, 20, 50,
                            '取引履歴CSV', 'legacy.csv', 8);
                    """;
                command.Parameters.AddWithValue("$stockId", stockId);
                command.ExecuteNonQuery();
            }
            repository.Initialize();
            repository.Initialize();
            var action = Assert.Single(new CorporateActionRepository(path).GetForStock(stockId));
            Assert.Equal("Pending", action.Status);
            Assert.Equal(8, action.SourceRowNumber);
            Assert.Equal("StockSplitCandidate", repository.GetBrokerTrades(stockId)
                .Single(x => x.SourceRowNumber == 8).TradeType);
            Assert.Single(Directory.GetFiles(directory, "*_before_corporate_action_candidates.db"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static CorporateAction Opening(DateTime date, int stockId, decimal quantity, decimal? cost) =>
        new()
        {
            Id = 1, StockId = stockId, EventDate = date, Kind = "OpeningBalance",
            DestinationBroker = "SBI", DestinationAccount = AccountTypes.Specific,
            Quantity = quantity, CarriedCostJpy = cost,
            Status = "Confirmed", Evidence = "残高証明"
        };

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
