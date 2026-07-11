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

        return new DatabaseAuditResult
        {
            Database = path,
            Stocks = Scalar(connection, "SELECT COUNT(*) FROM Stocks;"),
            MutualFunds = Scalar(connection, "SELECT COUNT(*) FROM Stocks WHERE AssetType = 'MutualFund';"),
            DuplicatePositionGroups = Scalar(connection, """
                SELECT COUNT(*)
                FROM (
                    SELECT 1
                    FROM Stocks
                    GROUP BY Broker, Ticker, AssetType, AccountType, CustodyType, Currency
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
            DividendCount = Scalar(connection, "SELECT COUNT(*) FROM DividendPayments;")
        };
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
    public long MutualFunds { get; init; }
    public long DuplicatePositionGroups { get; init; }
    public long DuplicateDividendRows { get; init; }
    public long DuplicateTradeRows { get; init; }
    public long OrphanHoldings { get; init; }
    public long OrphanDividends { get; init; }
    public long OrphanTrades { get; init; }
    public long SbiVSp500Rows { get; init; }
    public decimal TotalMarketValue { get; init; }
    public decimal TotalCostBasis { get; init; }
    public long DividendCount { get; init; }
}
