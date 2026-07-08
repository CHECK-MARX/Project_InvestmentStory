namespace InvestmentStory.Core.Models;

public sealed class BrokerCsvPreview
{
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();
}

public sealed class BrokerCsvImportResult
{
    public bool IsSuccess { get; init; }
    public int ImportedCount { get; init; }
    public int DuplicateCount { get; init; }
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();
}

public sealed class BrokerHoldingRecord
{
    public string Broker { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Shares { get; set; }
    public decimal AverageAcquisitionPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal AcquisitionAmount { get; set; }
    public decimal MarketValue { get; set; }
    public decimal MarketValueJpy { get; set; }
    public decimal UnrealizedGainLossJpy { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal PurchaseExchangeRate { get; set; } = 1m;
    public decimal CurrentExchangeRate { get; set; } = 1m;
    public DateTime SnapshotDate { get; set; } = DateTime.MinValue;
    public string Market { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class BrokerDividendRecord
{
    public DateTime PaymentDate { get; set; }
    public string Broker { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "JPY";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal NetAmountJpy { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class BrokerTradeRecord
{
    public DateTime TradeDate { get; set; }
    public DateTime SettlementDate { get; set; }
    public string Broker { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string TradeType { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SignedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SettlementAmountJpy { get; set; }
    public decimal FeeJpy { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public string Currency { get; set; } = "JPY";
    public decimal ProfitLossJpy { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class BrokerStatementImport
{
    public string Broker { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool CanUpdateHoldings { get; init; }
    public IReadOnlyList<BrokerHoldingRecord> Holdings { get; init; } = Array.Empty<BrokerHoldingRecord>();
    public IReadOnlyList<BrokerDividendRecord> Dividends { get; init; } = Array.Empty<BrokerDividendRecord>();
    public IReadOnlyList<BrokerTradeRecord> Trades { get; init; } = Array.Empty<BrokerTradeRecord>();
    public int IgnoredRowCount { get; init; }
}

public sealed class BrokerMergeResult
{
    public IReadOnlyList<BrokerMergeDecision> Decisions { get; init; } = Array.Empty<BrokerMergeDecision>();
    public int CreatedCount => Decisions.Count(x => x.Action == BrokerMergeAction.Create);
    public int OverwrittenCount => Decisions.Count(x => x.Action == BrokerMergeAction.Overwrite);
    public int ReviewCount => Decisions.Count(x => x.Action == BrokerMergeAction.NeedsReview);
}

public sealed class BrokerMergeDecision
{
    public BrokerMergeAction Action { get; init; }
    public BrokerHoldingRecord Source { get; init; } = new();
    public StockPosition? Existing { get; init; }
    public StockPosition? Merged { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedFields { get; init; } = Array.Empty<string>();
}

public enum BrokerMergeAction
{
    Create,
    Overwrite,
    NeedsReview
}
