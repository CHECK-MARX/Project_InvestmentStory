using InvestmentStory.Data;
using InvestmentStory.Core.Services;

var flags = args.Where(x => x.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
var shouldMigrate = flags.Contains("--migrate");
var shouldPrintTradeSummary = flags.Contains("--trade-summary");
var shouldRefreshDividendSchedules = flags.Contains("--refresh-dividend-schedules");
var shouldPrintDividendSchedules = flags.Contains("--dividend-schedules");
var databasePath = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
databasePath = string.IsNullOrWhiteSpace(databasePath) ? null : databasePath;

if (shouldMigrate)
{
    new DatabaseInitializer().Initialize(databasePath ?? DatabasePaths.GetDefaultDatabasePath());
}

if (shouldRefreshDividendSchedules)
{
    var repository = new InvestmentStoryRepository(databasePath ?? DatabasePaths.GetDefaultDatabasePath());
    var scheduleResult = new DividendScheduleService().BuildSchedules(
        repository.GetPositions(),
        repository.GetDividendPayments(),
        repository.GetTaxProfiles(),
        DateTime.Today);

    foreach (var schedule in scheduleResult.Schedules)
    {
        repository.SaveDividendPayment(schedule);
    }

    foreach (var obsoleteScheduleId in scheduleResult.ObsoleteScheduleIds)
    {
        repository.DeleteDividendPayment(obsoleteScheduleId);
    }

    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine(
        $"DividendScheduleRefresh=Saved:{scheduleResult.Schedules.Count} Created:{scheduleResult.CreatedCount} Updated:{scheduleResult.UpdatedCount} Deleted:{scheduleResult.ObsoleteScheduleIds.Count} PaymentDue:{scheduleResult.PaymentDueCount}");
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

if (shouldPrintTradeSummary)
{
    var repository = new InvestmentStoryRepository(databasePath ?? DatabasePaths.GetDefaultDatabasePath());
    foreach (var position in repository.GetPositions().OrderBy(x => x.Stock.Ticker).ThenBy(x => x.Stock.Broker).ThenBy(x => x.Stock.AccountType))
    {
        var trades = repository.GetBrokerTrades(position.Stock.Id);
        if (trades.Count == 0)
        {
            continue;
        }

        var latest = trades.OrderByDescending(x => x.TradeDate).ThenByDescending(x => x.Id).First();
        var buys = trades.Where(x => x.SignedQuantity > 0m).Sum(x => x.SignedQuantity);
        var sells = trades.Where(x => x.SignedQuantity < 0m).Sum(x => Math.Abs(x.SignedQuantity));
        Console.WriteLine(
            $"TradeSummary={position.Stock.Ticker}|{position.Stock.Name}|{position.Stock.Broker}|{position.Stock.AccountType}/{position.Stock.CustodyType}|Trades={trades.Count}|BuyQty={buys:0.####}|SellQty={sells:0.####}|LatestQty={latest.AfterTradeQuantity:0.####}|LatestAvg={latest.AfterTradeAverageCost:0.####}");
    }
}

if (shouldPrintDividendSchedules)
{
    var repository = new InvestmentStoryRepository(databasePath ?? DatabasePaths.GetDefaultDatabasePath());
    foreach (var payment in repository.GetDividendPayments()
                 .Where(x => DividendConstants.IsUnconfirmed(x.DividendStatus))
                 .OrderBy(x => x.PaymentDate)
                 .ThenBy(x => x.Ticker)
                 .ThenBy(x => x.Broker))
    {
        Console.WriteLine(
            $"DividendSchedule={payment.PaymentDate:yyyy-MM-dd}|{payment.Ticker}|{payment.StockName}|{payment.Broker}|Qty={payment.Quantity:0.####}|PerShare={payment.DividendPerShare:0.####} {payment.Currency}|Gross={payment.GrossAmount:0.####} {payment.Currency}|NetJpy={payment.NetAmountJpy:0}|Status={payment.DividendStatus}|Source={payment.Source}");
    }
}
