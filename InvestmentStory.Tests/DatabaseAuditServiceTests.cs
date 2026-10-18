using InvestmentStory.Data;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Tests;

public sealed class DatabaseAuditServiceTests
{
    [Fact]
    public void Audit_ReturnsZeroDuplicateCounts_ForInitializedDatabase()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_audit_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();

            var audit = new DatabaseAuditService().Audit(databasePath);

            Assert.Equal(databasePath, audit.Database);
            Assert.Equal(0, audit.Stocks);
            Assert.Equal(0, audit.DuplicatePositionGroups);
            Assert.Equal(0, audit.DuplicateDividendRows);
            Assert.Equal(0, audit.DuplicateTradeRows);
            Assert.Equal(0, audit.DuplicateTransactionRows);
            Assert.Equal(0, audit.DuplicateMutualFundGroups);
            Assert.Equal(0, audit.SecurityMasters);
            Assert.Equal(0, audit.Positions);
            Assert.True(audit.Transactions >= 0);
            Assert.True(audit.DividendPayments >= 0);
            Assert.True(audit.ZeroPriceInboundEvents >= 0);
            Assert.True(audit.StockSplitCandidates >= 0);
            Assert.True(audit.MissingTransactionHistoryPositions >= 0);
            Assert.True(audit.UnknownCostPositions >= 0);
            Assert.Equal(0, audit.UnknownMarketValuePositions);
            Assert.Equal(0, audit.UnknownGainPositions);
            Assert.Equal(0m, audit.TotalMarketValue);
            Assert.Equal(0m, audit.TotalCostBasis);
            Assert.Equal(0m, audit.UnrealizedProfitLoss);
            Assert.Equal(0, audit.OrphanHoldings);
            Assert.Equal(0, audit.OrphanDividends);
            Assert.Equal(0, audit.OrphanTrades);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void Audit_SeparatesCompleteTotalsFromKnownSubtotalsAndReferenceCost()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_audit_evidence_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            repository.SavePosition(Position("JP", "JPY", 100m, 1m, 120m, 1m));
            repository.SavePosition(Position("UNKNOWN", "USD", 10m, 1m, 12m, 150m));
            var reference = Position("REFERENCE", "USD", 20m, 0m, 25m, 150m);
            reference.Purchase.ReferenceAcquisitionForeignAmount = 200m;
            reference.Purchase.ReferenceAcquisitionAmountJpy = 30_000m;
            reference.Purchase.ReferenceAcquisitionSource = "取得日の公表為替";
            repository.SavePosition(reference);
            var missingMarket = Position("MISSINGFX", "USD", 10m, 0m, 12m, 1m);
            missingMarket.Purchase.AcquisitionAmountJpy = 15_000m;
            missingMarket.Purchase.AcquisitionAmountSource = "証券会社の取引報告書";
            repository.SavePosition(missingMarket);

            var audit = new DatabaseAuditService().Audit(databasePath);

            Assert.Equal(4, audit.Positions);
            Assert.Equal(1, audit.UnknownCostPositions);
            Assert.Equal(1, audit.UnknownMarketValuePositions);
            Assert.Equal(2, audit.UnknownGainPositions);
            Assert.Equal(2, audit.KnownGainPositions);
            Assert.Equal(18_000m, audit.KnownMarketValueExcludedFromGain);
            Assert.Equal(1, audit.ReferenceCostPositions);
            Assert.Equal(30_000m, audit.ReferenceCostSubtotal);
            Assert.Null(audit.TotalMarketValue);
            Assert.Null(audit.TotalCostBasis);
            Assert.Null(audit.UnrealizedProfitLoss);
            Assert.Equal(56_700m, audit.KnownMarketValueSubtotal);
            Assert.Equal(46_000m, audit.KnownCostBasisSubtotal);
            Assert.Equal(7_700m, audit.KnownGainSubtotal);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public void Audit_DoesNotBlockCurrentPortfolioForAClosedPositionWithUnknownCost()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_audit_closed_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            repository.SavePosition(Position("JP", "JPY", 100m, 1m, 120m, 1m));
            var closed = Position("CLOSED", "USD", 10m, 1m, 0m, 1m);
            closed.CurrentHolding.CurrentShares = 0m;
            repository.SavePosition(closed);

            var audit = new DatabaseAuditService().Audit(databasePath);

            Assert.Equal(2, audit.Positions);
            Assert.Equal(0, audit.UnknownCostPositions);
            Assert.Equal(1, audit.HistoricalUnknownCostPositions);
            Assert.Equal(1, audit.KnownGainPositions);
            Assert.Equal(1200m, audit.TotalMarketValue);
            Assert.Equal(1000m, audit.TotalCostBasis);
            Assert.Equal(200m, audit.UnrealizedProfitLoss);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static StockPosition Position(string ticker, string currency, decimal purchasePrice,
        decimal purchaseFx, decimal currentPrice, decimal currentFx) => new()
    {
        Stock = new Stock { Name = ticker, Ticker = ticker, Broker = "テスト証券", Currency = currency },
        Purchase = new Purchase
        {
            PurchaseDate = new DateTime(2023, 9, 12), Shares = 10m, UnitPrice = purchasePrice,
            ExchangeRate = purchaseFx
        },
        CurrentHolding = new CurrentHolding
        {
            CurrentShares = 10m, CurrentPrice = currentPrice, CurrentExchangeRate = currentFx
        }
    };
}
