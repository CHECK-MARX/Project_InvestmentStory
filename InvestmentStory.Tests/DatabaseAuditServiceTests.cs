using InvestmentStory.Data;

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
            Assert.True(audit.Stocks > 0);
            Assert.Equal(0, audit.DuplicatePositionGroups);
            Assert.Equal(0, audit.DuplicateDividendRows);
            Assert.Equal(0, audit.DuplicateTradeRows);
            Assert.Equal(0, audit.DuplicateTransactionRows);
            Assert.Equal(0, audit.DuplicateMutualFundGroups);
            Assert.True(audit.SecurityMasters > 0);
            Assert.True(audit.Positions > 0);
            Assert.True(audit.Transactions >= 0);
            Assert.True(audit.DividendPayments >= 0);
            Assert.True(audit.ZeroPriceInboundEvents >= 0);
            Assert.True(audit.StockSplitCandidates >= 0);
            Assert.True(audit.MissingTransactionHistoryPositions >= 0);
            Assert.True(audit.UnknownCostPositions >= 0);
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
}
