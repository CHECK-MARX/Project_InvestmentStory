using InvestmentStory.Data;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

var flags = args.Where(x => x.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
var shouldMigrate = flags.Contains("--migrate");
var shouldPrintTradeSummary = flags.Contains("--trade-summary");
var shouldRefreshDividendSchedules = flags.Contains("--refresh-dividend-schedules");
var shouldPrintDividendSchedules = flags.Contains("--dividend-schedules");
var shouldPrintDividendPayments = flags.Contains("--dividend-payments");
var shouldPrintDividendPlan = flags.Contains("--dividend-plan");
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
        DateTime.Today,
        repository.GetDividendCalendarEvents());

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

if (shouldPrintDividendPayments)
{
    var repository = new InvestmentStoryRepository(databasePath ?? DatabasePaths.GetDefaultDatabasePath());
    var targetYear = DateTime.Today.Year;
    var payments = repository.GetDividendPayments()
        .Where(x => x.PaymentDate.Year == targetYear && !DividendConstants.IsUnconfirmed(x.DividendStatus))
        .OrderBy(x => x.PaymentDate)
        .ThenBy(x => x.Ticker)
        .ThenBy(x => x.Broker)
        .ThenBy(x => x.AccountType)
        .ToList();

    foreach (var payment in payments)
    {
        Console.WriteLine(
            $"DividendPayment=Id:{payment.Id}|StockId:{payment.StockId}|{payment.PaymentDate:yyyy-MM-dd}|{payment.Ticker}|{payment.Broker}|{payment.AccountType}|Qty={payment.Quantity:0.####}|PerShare={payment.DividendPerShare:0.####} {payment.Currency}|Gross={payment.GrossAmount:0.####}|Net={payment.NetAmount:0.####}|Rate={payment.ExchangeRate:0.####}|GrossJpy={payment.GrossAmountJpy:0}|NetJpy={ResolveNetJpy(payment):0}|Status={payment.DividendStatus}|Source={payment.Source}");
    }

    foreach (var month in Enumerable.Range(1, 12))
    {
        Console.WriteLine($"DividendPaymentMonth={targetYear}-{month:00}|NetJpy={payments.Where(x => x.PaymentDate.Month == month).Sum(ResolveNetJpy):0}|Count={payments.Count(x => x.PaymentDate.Month == month)}");
    }
}

if (shouldPrintDividendPlan)
{
    var repository = new InvestmentStoryRepository(databasePath ?? DatabasePaths.GetDefaultDatabasePath());
    var targetYear = DateTime.Today.Year;
    var planItems = new DividendGrowthSimulationService().CreateDefaultPlanItems(
        repository.GetPositions(),
        DividendGrowthDisplayModes.AggregateBySecurity,
        repository.GetDividendCalendarEvents());
    foreach (var item in planItems.Where(x => new[] { "TRMD", "MO", "CMBT", "8151" }.Contains(x.Ticker, StringComparer.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"DividendPlanInput={item.Ticker}|Currency:{item.Currency}|Shares:{item.CurrentShares:0.####}|AnnualDps:{item.AnnualDividendPerShare:0.####}|Rate:{item.ExchangeRate:0.####}|Frequency:{item.DividendFrequency}|Months:{item.DividendMonths}|Components:{item.Components.Count}|Calendar:{item.DividendEvents.Count}");
        foreach (var component in item.Components)
        {
            Console.WriteLine($"DividendPlanComponent={component.StockId}|{component.Ticker}|{component.Broker}|{component.AccountType}|Currency:{component.Currency}|Shares:{component.CurrentShares:0.####}|AnnualDps:{component.AnnualDividendPerShare:0.####}|Rate:{component.ExchangeRate:0.####}|Months:{component.DividendMonths}|Calendar:{component.DividendEvents.Count}");
        }
    }
    var simulation = new DividendPurchasePlanSimulationService().Simulate(
        new DividendPurchasePlanInput
        {
            PlanName = "DB audit",
            TargetYear = targetYear,
            PlannedPurchaseDate = DateTime.Today,
            DisplayUnit = DividendPurchasePlanDisplayUnits.AllAccounts,
            TargetAnnualNetDividendJpy = 1_200_000m,
            PlanItems = planItems,
            DividendPayments = repository.GetDividendPayments()
        },
        repository.GetTaxProfiles());

    Console.WriteLine($"DividendPlanSummary=Current:{simulation.Summary.CurrentTargetYearNetDividendJpy:0}|Planned:{simulation.Summary.PlannedTargetYearNetDividendJpy:0}|NextYear:{simulation.Summary.NextYearAnnualNetDividendJpy:0}|Holdings:{simulation.Holdings.Count}");
    foreach (var month in simulation.Months)
    {
        Console.WriteLine($"DividendPlanMonth={month.Year}-{month.Month:00}|Current:{month.CurrentNetDividendJpy:0}|Additional:{month.AdditionalNetDividendJpy:0}|Planned:{month.PlannedNetDividendJpy:0}|Events:{month.Events.Count}");
        foreach (var item in month.Events.Where(x => x.CurrentNetDividendJpy + x.AdditionalNetDividendJpy > 50_000m))
        {
            Console.WriteLine($"DividendPlanLargeEvent={month.Year}-{month.Month:00}|{item.Ticker}|{item.Broker}|{item.AccountType}|Current:{item.CurrentNetDividendJpy:0}|Additional:{item.AdditionalNetDividendJpy:0}|Paid:{item.IsPaid}|Source:{item.Source}");
        }
    }
}

static decimal ResolveNetJpy(DividendPayment payment)
{
    if (payment.NetAmountJpy > 0m)
    {
        return payment.NetAmountJpy;
    }

    if (payment.JpyAmount > 0m)
    {
        return payment.JpyAmount;
    }

    return string.Equals(payment.Currency, "JPY", StringComparison.OrdinalIgnoreCase)
        ? payment.NetAmount
        : payment.NetAmount * (payment.ExchangeRate > 0m ? payment.ExchangeRate : 1m);
}
