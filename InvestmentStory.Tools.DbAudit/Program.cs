using InvestmentStory.Data;

var databasePath = args.Length > 0 ? args[0] : null;
var result = new DatabaseAuditService().Audit(databasePath);

Console.WriteLine($"Database={result.Database}");
Console.WriteLine($"Stocks={result.Stocks}");
Console.WriteLine($"MutualFunds={result.MutualFunds}");
Console.WriteLine($"DuplicatePositionGroups={result.DuplicatePositionGroups}");
Console.WriteLine($"DuplicateDividendRows={result.DuplicateDividendRows}");
Console.WriteLine($"DuplicateTradeRows={result.DuplicateTradeRows}");
Console.WriteLine($"OrphanHoldings={result.OrphanHoldings}");
Console.WriteLine($"OrphanDividends={result.OrphanDividends}");
Console.WriteLine($"OrphanTrades={result.OrphanTrades}");
Console.WriteLine($"SbiVSp500Rows={result.SbiVSp500Rows}");
Console.WriteLine($"TotalMarketValue={result.TotalMarketValue:0.##}");
Console.WriteLine($"TotalCostBasis={result.TotalCostBasis:0.##}");
Console.WriteLine($"DividendCount={result.DividendCount}");
