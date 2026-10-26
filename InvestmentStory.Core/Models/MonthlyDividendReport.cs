namespace InvestmentStory.Core.Models;

public sealed class MonthlyDividendReport
{
    public const string FormulaVersion = "DividendDashboardService/2";

    public int Year { get; init; }
    public int Month { get; init; }
    public DateTime AsOf { get; init; }
    public bool MaskSensitive { get; init; }
    public decimal ActualJpy { get; init; }
    public decimal PlannedJpy { get; init; }
    public decimal UnmatchedJpy { get; init; }
    public IReadOnlyList<MonthlyDividendReportLine> Lines { get; init; } = Array.Empty<MonthlyDividendReportLine>();
}

public sealed class MonthlyDividendReportLine
{
    public DateTime PaymentDate { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public string StockName { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal NetAmount { get; init; }
    public decimal ExchangeRate { get; init; }
    public decimal NetJpy { get; init; }
    public string Source { get; init; } = string.Empty;
    public string SourceFileName { get; init; } = string.Empty;
    public int SourceRowNumber { get; init; }
    public bool IsActual { get; init; }
}
