namespace InvestmentStory.Core.Models;

public sealed class DividendDashboardAnalysis
{
    public int Year { get; init; }
    public decimal ActualNetJpy { get; init; }
    public decimal UpcomingNetJpy { get; init; }
    public decimal YearEndForecastJpy { get; init; }
    public decimal AnnualGoalJpy { get; init; }
    public decimal AchievementRate { get; init; }
    public decimal RemainingJpy { get; init; }
    public decimal GrossTotalJpy { get; init; }
    public decimal ForeignTaxTotalJpy { get; init; }
    public decimal DomesticTaxTotalJpy { get; init; }
    public int UnmatchedCount { get; init; }
    public IReadOnlyList<DividendDashboardEntry> Entries { get; init; } = Array.Empty<DividendDashboardEntry>();
    public IReadOnlyList<DividendDashboardMonth> Months { get; init; } = Array.Empty<DividendDashboardMonth>();
    public IReadOnlyList<DividendDashboardRanking> Rankings { get; init; } = Array.Empty<DividendDashboardRanking>();
    public DividendBiasAnalysis Bias { get; init; } = new();
}

public sealed class DividendDashboardEntry
{
    public required DividendPayment Payment { get; init; }
    public DividendScheduleStatus ScheduleStatus { get; init; }
    public required string DisplayStatus { get; init; }
    public required string DataQuality { get; init; }
    public bool IsActual { get; init; }
    public bool IsUpcoming { get; init; }
    public bool IsUnmatched { get; init; }
    public bool IsEstimated { get; init; }
    public decimal GrossJpy { get; init; }
    public decimal ForeignTaxJpy { get; init; }
    public decimal DomesticTaxJpy { get; init; }
    public decimal NetJpy { get; init; }
}

public sealed class DividendDashboardMonth
{
    public int Month { get; init; }
    public decimal ActualJpy { get; init; }
    public decimal UpcomingJpy { get; init; }
    public decimal UnmatchedJpy { get; init; }
    public decimal ForecastJpy { get; init; }
    public decimal GoalJpy { get; init; }
    public decimal DifferenceJpy { get; init; }
    public decimal CumulativeActualJpy { get; init; }
    public decimal CumulativeForecastJpy { get; init; }
    public decimal CumulativeGoalJpy { get; init; }
    public IReadOnlyList<DividendDashboardEntry> Entries { get; init; } = Array.Empty<DividendDashboardEntry>();
}

public sealed class DividendDashboardRanking
{
    public string Ticker { get; init; } = string.Empty;
    public string StockName { get; init; } = string.Empty;
    public decimal GrossJpy { get; init; }
    public decimal NetJpy { get; init; }
    public decimal CompositionRate { get; init; }
    public int PaymentCount { get; init; }
    public decimal ActualJpy { get; init; }
    public decimal UpcomingJpy { get; init; }
    public string DataQuality { get; init; } = string.Empty;
}

public sealed class DividendBiasAnalysis
{
    public int MaximumMonth { get; init; }
    public decimal MaximumMonthJpy { get; init; }
    public int MinimumMonth { get; init; }
    public decimal MinimumMonthJpy { get; init; }
    public decimal MonthlyAverageJpy { get; init; }
    public decimal MonthlyMedianJpy { get; init; }
    public int ZeroDividendMonthCount { get; init; }
    public decimal TopTwoMonthConcentrationRate { get; init; }
}
