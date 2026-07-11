using InvestmentStory.Data;

var shouldMigrate = args.Any(x => string.Equals(x, "--migrate", StringComparison.OrdinalIgnoreCase));
var databasePath = args.FirstOrDefault(x => !string.Equals(x, "--migrate", StringComparison.OrdinalIgnoreCase));
databasePath = string.IsNullOrWhiteSpace(databasePath) ? null : databasePath;

if (shouldMigrate)
{
    new DatabaseInitializer().Initialize(databasePath ?? DatabasePaths.GetDefaultDatabasePath());
}

var result = new DatabaseAuditService().Audit(databasePath);

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"Migration={(shouldMigrate ? "Executed" : "NotExecuted")}");
Console.WriteLine($"Database={result.Database}");
Console.WriteLine($"SecurityMasters={result.SecurityMasters}");
Console.WriteLine($"Positions={result.Positions}");
Console.WriteLine($"Transactions={result.Transactions}");
Console.WriteLine($"DividendPayments={result.DividendPayments}");
Console.WriteLine($"Stocks={result.Stocks}");
Console.WriteLine($"MutualFunds={result.MutualFunds}");
Console.WriteLine($"DuplicatePositionGroups={result.DuplicatePositionGroups}");
Console.WriteLine($"DuplicateTransactionRows={result.DuplicateTransactionRows}");
Console.WriteLine($"DuplicateDividendRows={result.DuplicateDividendRows}");
Console.WriteLine($"DuplicateMutualFundGroups={result.DuplicateMutualFundGroups}");
Console.WriteLine($"ZeroPriceInboundEvents={result.ZeroPriceInboundEvents}");
Console.WriteLine($"StockSplitCandidates={result.StockSplitCandidates}");
Console.WriteLine($"MissingTransactionHistoryPositions={result.MissingTransactionHistoryPositions}");
Console.WriteLine($"UnknownCostPositions={result.UnknownCostPositions}");
Console.WriteLine($"OrphanHoldings={result.OrphanHoldings}");
Console.WriteLine($"OrphanDividends={result.OrphanDividends}");
Console.WriteLine($"OrphanTrades={result.OrphanTrades}");
Console.WriteLine($"SbiVSp500Rows={result.SbiVSp500Rows}");
Console.WriteLine($"TotalAssetValue={result.TotalMarketValue:0.##}");
Console.WriteLine($"TotalAcquisitionAmount={result.TotalCostBasis:0.##}");
Console.WriteLine($"UnrealizedProfitLoss={result.UnrealizedProfitLoss:0.##}");
Console.WriteLine($"DividendCount={result.DividendCount}");
