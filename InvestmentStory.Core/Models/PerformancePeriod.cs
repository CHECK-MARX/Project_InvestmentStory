namespace InvestmentStory.Core.Models;

public sealed record PerformanceFx(DateTime Date, string Currency, decimal JpyPerUnit,
    DateTime QuoteDate, string Evidence);
public sealed record PerformanceFlowValuation(string OperationId, int Sequence,
    decimal? BeforeJpy, decimal? AfterJpy, bool Confirmed, string Evidence);
public sealed record PerformanceBenchmark(string Name, DateTime StartDate, DateTime EndDate,
    string Currency, decimal? OpeningLevel, decimal? ClosingLevel,
    bool IncludesNetDividends, bool SourceConfirmed, bool IsHistoricalEstimate, string Evidence);
public sealed record PerformanceBenchmarkComparison(double? BenchmarkPeriodReturn,
    double? PortfolioPeriodReturn, double? Difference, string Status);

public sealed class PerformancePeriodInput
{
    // Both dates refer to end-of-day values. Flows are in (StartDate, EndDate].
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public decimal? OpeningSecuritiesJpy { get; init; }
    public decimal? ClosingSecuritiesJpy { get; init; }
    public string OpeningEvidence { get; init; } = "";
    public string ClosingEvidence { get; init; } = "";
    public bool SecuritiesConfirmed { get; init; }
    public bool AllAccountsConfirmed { get; init; }
    public bool NoInKindTransfersConfirmed { get; init; }
    public IReadOnlyList<PerformanceFx> Fx { get; init; } = [];
    public IReadOnlyList<PerformanceFlowValuation> FlowValuations { get; init; } = [];
    public PerformanceBenchmark? Benchmark { get; init; }
}

public sealed record PerformanceCashBalance(string Account, string Currency,
    decimal? Opening, decimal? Closing, decimal? OpeningJpy, decimal? ClosingJpy, string Status);
public sealed record PerformanceFlow(DateTime Date, string Account, string Currency,
    decimal Amount, decimal? JpyAmount, string Evidence, string OperationId);
public sealed record ReturnMeasure(double? Value, string Status);

public sealed class PerformancePeriodResult
{
    public string FormulaVersion { get; init; } = "JPY-Period-v1";
    public decimal? OpeningTotalJpy { get; init; }
    public decimal? ClosingTotalJpy { get; init; }
    public decimal? ExternalNetFlowJpy { get; init; }
    public decimal? ProfitJpy { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = [];
    public IReadOnlyList<PerformanceCashBalance> Balances { get; init; } = [];
    public IReadOnlyList<PerformanceFlow> Flows { get; init; } = [];
    public ReturnMeasure TimeWeighted { get; init; } = new(null, "未計算");
    public ReturnMeasure MoneyWeighted { get; init; } = new(null, "未計算");
    public ReturnMeasure ModifiedDietz { get; init; } = new(null, "未計算");
    public PerformanceBenchmarkComparison Benchmark { get; init; } = new(null, null, null, "比較指数未指定");
}

public sealed record PerformanceReport(string Id, DateTime CreatedAtUtc, string LedgerFingerprint,
    PerformancePeriodInput Input, PerformancePeriodResult Result, CashLedgerState LedgerEvidence);
