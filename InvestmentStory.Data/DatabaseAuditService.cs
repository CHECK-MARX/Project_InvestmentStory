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
            UnknownCostPositions = Scalar(connection, """
                SELECT COUNT(*)
                FROM Stocks s
                LEFT JOIN Purchases p ON p.StockId = s.Id
                LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id
                WHERE (s.AssetType <> 'MutualFund' AND IFNULL(p.UnitPrice, 0) = 0)
                   OR (s.AssetType = 'MutualFund' AND IFNULL(mf.AcquisitionAmount, 0) = 0 AND IFNULL(mf.AverageCostNav, 0) = 0);
                """),
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
            TotalMarketValue = DecimalScalar(connection, """
                SELECT COALESCE(SUM(
                    CASE
                        WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.MarketValue, 0)
                        ELSE IFNULL(ch.CurrentShares, 0) * IFNULL(ch.CurrentPrice, 0) * IFNULL(ch.CurrentExchangeRate, 1)
                    END), 0)
                FROM Stocks s
                LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
                LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id;
                """),
            TotalCostBasis = DecimalScalar(connection, """
                SELECT COALESCE(SUM(
                    CASE
                        WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.AcquisitionAmount, 0)
                        ELSE IFNULL(p.Shares, 0) * IFNULL(p.UnitPrice, 0) * IFNULL(p.ExchangeRate, 1)
                    END), 0)
                FROM Stocks s
                LEFT JOIN Purchases p ON p.Id = (
                    SELECT Id FROM Purchases WHERE StockId = s.Id ORDER BY PurchaseDate, Id LIMIT 1
                )
                LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id;
                """),
            UnrealizedProfitLoss = DecimalScalar(connection, """
                SELECT COALESCE(SUM(
                    CASE
                        WHEN s.AssetType = 'MutualFund' THEN IFNULL(mf.MarketValue, 0) - IFNULL(mf.AcquisitionAmount, 0)
                        ELSE IFNULL(ch.CurrentShares, 0) * IFNULL(ch.CurrentPrice, 0) * IFNULL(ch.CurrentExchangeRate, 1)
                           - IFNULL(p.Shares, 0) * IFNULL(p.UnitPrice, 0) * IFNULL(p.ExchangeRate, 1)
                    END), 0)
                FROM Stocks s
                LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
                LEFT JOIN Purchases p ON p.Id = (
                    SELECT Id FROM Purchases WHERE StockId = s.Id ORDER BY PurchaseDate, Id LIMIT 1
                )
                LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id;
                """),
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

    private static decimal DecimalScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToDecimal(command.ExecuteScalar());
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
    public long OrphanHoldings { get; init; }
    public long OrphanDividends { get; init; }
    public long OrphanTrades { get; init; }
    public long SbiVSp500Rows { get; init; }
    public decimal TotalMarketValue { get; init; }
    public decimal TotalCostBasis { get; init; }
    public decimal UnrealizedProfitLoss { get; init; }
    public long DividendCount { get; init; }
}
