using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class DatabaseAuditService
{
    public DatabaseAuditResult Audit(string? databasePath = null)
    {
        var path = string.IsNullOrWhiteSpace(databasePath)
            ? DatabasePaths.GetDefaultDatabasePath()
            : databasePath;
        if (!File.Exists(path))
        {
            return new DatabaseAuditResult { Database = path };
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();
        var securityKeyExpression = ColumnExists(connection, "Stocks", "CanonicalSecurityKey")
            ? "COALESCE(NULLIF(CanonicalSecurityKey, ''), Ticker)"
            : "Ticker";
        var repository = new InvestmentStoryRepository(path);
        var positions = repository.GetPositions();
        var allSnapshots = positions.Select(new InvestmentCalculator().CreateSnapshot).ToList();
        var snapshots = allSnapshots.Where(x => PositionEvidenceService.IsActiveHolding(x.Position)).ToList();
        var tradesByStockId = repository.GetAllBrokerTrades()
            .GroupBy(x => x.StockId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<BrokerTrade>)x.ToList());
        var unknownCostCount = snapshots.Count(x => !x.HasKnownPurchaseCostJpy);
        var unknownMarketValueCount = snapshots.Count(x => !x.HasKnownCurrentMarketValueJpy);
        var gainEligible = snapshots.Where(x => x.HasKnownPurchaseCostJpy && x.HasKnownCurrentMarketValueJpy).ToList();
        return new DatabaseAuditResult
        {
            Database = path,
            Stocks = Scalar(connection, "SELECT COUNT(*) FROM Stocks;"),
            SecurityMasters = Scalar(connection, $"SELECT COUNT(DISTINCT {securityKeyExpression}) FROM Stocks;"),
            Positions = Scalar(connection, "SELECT COUNT(*) FROM Stocks;"),
            Transactions = Scalar(connection, "SELECT COUNT(*) FROM BrokerTrades;"),
            DividendPayments = Scalar(connection, "SELECT COUNT(*) FROM DividendPayments;"),
            MutualFunds = Scalar(connection, "SELECT COUNT(*) FROM Stocks WHERE AssetType = 'MutualFund';"),
            DuplicatePositionGroups = Scalar(connection, $"""
                SELECT COUNT(*)
                FROM (
                    SELECT 1
                    FROM Stocks
                    GROUP BY Broker, {securityKeyExpression}, AssetType, AccountType, CustodyType, Currency
                    HAVING COUNT(*) > 1
                );
                """),
            DuplicateMutualFundGroups = Scalar(connection, $"""
                SELECT COUNT(*)
                FROM (
                    SELECT 1
                    FROM Stocks
                    WHERE AssetType = 'MutualFund'
                    GROUP BY Broker, {securityKeyExpression}, AccountType, CustodyType, Currency
                    HAVING COUNT(*) > 1
                );
                """),
            DuplicateDividendRows = Scalar(connection, """
                SELECT COALESCE(SUM(RowCount - 1), 0)
                FROM (
                    SELECT COUNT(*) AS RowCount
                    FROM DividendPayments
                    GROUP BY
                        StockId, Broker, AccountType, DividendStatus, PaymentDate, Currency,
                        Quantity, DividendPerShare, GrossAmount, TotalTaxAmount, NetAmount, NetAmountJpy
                    HAVING COUNT(*) > 1
                );
                """),
            DuplicateTradeRows = Scalar(connection, """
                SELECT COALESCE(SUM(RowCount - 1), 0)
                FROM (
                    SELECT COUNT(*) AS RowCount
                    FROM BrokerTrades
                    GROUP BY
                        StockId, TradeDate, SettlementDate, Broker, AccountType, TradeType,
                        Quantity, UnitPrice, SettlementAmountJpy, Source
                    HAVING COUNT(*) > 1
                );
                """),
            DuplicateTransactionRows = Scalar(connection, """
                SELECT COALESCE(SUM(RowCount - 1), 0)
                FROM (
                    SELECT COUNT(*) AS RowCount
                    FROM BrokerTrades
                    GROUP BY
                        StockId, TradeDate, SettlementDate, Broker, AccountType, TradeType,
                        Quantity, UnitPrice, SettlementAmountJpy, Source
                    HAVING COUNT(*) > 1
                );
                """),
            ZeroPriceInboundEvents = Scalar(connection, """
                SELECT COUNT(*)
                FROM BrokerTrades
                WHERE SignedQuantity > 0
                  AND UnitPrice = 0
                  AND SettlementAmountJpy = 0
                  AND TradeType NOT IN ('TransferIn', 'OpeningBalance', 'StockSplit', 'ReverseSplit', 'UnknownAdjustment');
                """),
            StockSplitCandidates = Scalar(connection, """
                SELECT COUNT(*)
                FROM BrokerTrades
                WHERE TradeType = 'StockSplit'
                   OR (SignedQuantity > 0 AND UnitPrice = 0 AND SettlementAmountJpy = 0);
                """),
            MissingTransactionHistoryPositions = Scalar(connection, """
                SELECT COUNT(*)
                FROM Stocks s
                LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
                LEFT JOIN BrokerTrades bt ON bt.StockId = s.Id
                WHERE IFNULL(ch.CurrentShares, 0) > 0
                  AND bt.Id IS NULL;
                """),
            UnknownCostPositions = unknownCostCount,
            HistoricalUnknownCostPositions = allSnapshots.Count(x =>
                !PositionEvidenceService.IsActiveHolding(x.Position) && !x.HasKnownPurchaseCostJpy),
            UnknownMarketValuePositions = unknownMarketValueCount,
            UnknownGainPositions = snapshots.Count - gainEligible.Count,
            KnownGainPositions = gainEligible.Count,
            KnownMarketValueExcludedFromGain = snapshots
                .Where(x => !x.HasKnownPurchaseCostJpy && x.HasKnownCurrentMarketValueJpy)
                .Sum(x => x.CurrentMarketValueJpy),
            ReferenceCostPositions = snapshots.Count(x => x.IsReferencePurchaseCostJpy),
            ReferenceCostSubtotal = snapshots.Where(x => x.IsReferencePurchaseCostJpy).Sum(x => x.PurchaseTotalJpy),
            AcquisitionDateReviewPositions = positions.Count(x => PositionEvidenceService.AcquisitionDateNeedsReview(
                x, tradesByStockId.GetValueOrDefault(x.Stock.Id) ?? Array.Empty<BrokerTrade>())),
            OrphanHoldings = Scalar(connection, """
                SELECT COUNT(*)
                FROM CurrentHoldings ch
                LEFT JOIN Stocks s ON s.Id = ch.StockId
                WHERE s.Id IS NULL;
                """),
            OrphanDividends = Scalar(connection, """
                SELECT COUNT(*)
                FROM DividendPayments d
                LEFT JOIN Stocks s ON s.Id = d.StockId
                WHERE s.Id IS NULL;
                """),
            OrphanTrades = Scalar(connection, """
                SELECT COUNT(*)
                FROM BrokerTrades t
                LEFT JOIN Stocks s ON s.Id = t.StockId
                WHERE s.Id IS NULL;
                """),
            SbiVSp500Rows = Scalar(connection, """
                SELECT COUNT(*)
                FROM Stocks
                WHERE AssetType = 'MutualFund'
                  AND (
                      UPPER(Ticker) LIKE '%SBI%500%'
                      OR Name LIKE '%S&P500%'
                      OR Name LIKE '%Ｓ＆Ｐ５００%'
                  );
                """),
            KnownMarketValueSubtotal = snapshots.Where(x => x.HasKnownCurrentMarketValueJpy).Sum(x => x.CurrentMarketValueJpy),
            KnownCostBasisSubtotal = snapshots.Where(x => x.HasKnownPurchaseCostJpy).Sum(x => x.PurchaseTotalJpy),
            KnownGainSubtotal = gainEligible.Sum(x => x.UnrealizedGainJpy),
            TotalMarketValue = unknownMarketValueCount == 0 ? snapshots.Sum(x => x.CurrentMarketValueJpy) : null,
            TotalCostBasis = unknownCostCount == 0 ? snapshots.Sum(x => x.PurchaseTotalJpy) : null,
            UnrealizedProfitLoss = gainEligible.Count == snapshots.Count
                ? gainEligible.Sum(x => x.UnrealizedGainJpy) : null,
            DividendCount = Scalar(connection, "SELECT COUNT(*) FROM DividendPayments;")
        };
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

}

public sealed class DatabaseAuditResult
{
    public string Database { get; init; } = string.Empty;
    public long Stocks { get; init; }
    public long SecurityMasters { get; init; }
    public long Positions { get; init; }
    public long Transactions { get; init; }
    public long DividendPayments { get; init; }
    public long MutualFunds { get; init; }
    public long DuplicatePositionGroups { get; init; }
    public long DuplicateMutualFundGroups { get; init; }
    public long DuplicateDividendRows { get; init; }
    public long DuplicateTradeRows { get; init; }
    public long DuplicateTransactionRows { get; init; }
    public long ZeroPriceInboundEvents { get; init; }
    public long StockSplitCandidates { get; init; }
    public long MissingTransactionHistoryPositions { get; init; }
    public long UnknownCostPositions { get; init; }
    public long HistoricalUnknownCostPositions { get; init; }
    public long UnknownMarketValuePositions { get; init; }
    public long UnknownGainPositions { get; init; }
    public long KnownGainPositions { get; init; }
    public decimal KnownMarketValueExcludedFromGain { get; init; }
    public long ReferenceCostPositions { get; init; }
    public decimal ReferenceCostSubtotal { get; init; }
    public long AcquisitionDateReviewPositions { get; init; }
    public long OrphanHoldings { get; init; }
    public long OrphanDividends { get; init; }
    public long OrphanTrades { get; init; }
    public long SbiVSp500Rows { get; init; }
    public decimal KnownMarketValueSubtotal { get; init; }
    public decimal KnownCostBasisSubtotal { get; init; }
    public decimal KnownGainSubtotal { get; init; }
    public decimal? TotalMarketValue { get; init; }
    public decimal? TotalCostBasis { get; init; }
    public decimal? UnrealizedProfitLoss { get; init; }
    public long DividendCount { get; init; }
}
