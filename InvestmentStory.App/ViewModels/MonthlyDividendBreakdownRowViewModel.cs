using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class MonthlyDividendBreakdownRowViewModel
{
    public MonthlyDividendBreakdownRowViewModel(MonthlyDividendBreakdown value)
    {
        Year = value.Year;
        Month = value.Month;
        Label = value.Label;
        ActualJpy = value.ActualJpy;
        PlannedJpy = value.PlannedJpy;
        ForecastJpy = value.ForecastJpy;
        PreviousYearActualJpy = value.PreviousYearActualJpy;
        MonthlyGoalJpy = value.MonthlyGoalJpy;
        HasActualRecord = value.HasActualRecord;
        HasForecastRecord = value.HasActualRecord || value.HasPlannedRecord;
        HasPreviousYearRecord = value.HasPreviousYearRecord;
        HasMonthlyGoal = value.HasMonthlyGoal;
        Actual = Year > DateTime.Today.Year || Year == DateTime.Today.Year && Month > DateTime.Today.Month
            ? "—" : HasActualRecord ? Formatters.Jpy(ActualJpy) : "記録なし";
        Planned = value.HasPlannedRecord ? Formatters.Jpy(PlannedJpy) : "記録なし";
        Forecast = HasForecastRecord ? Formatters.Jpy(ForecastJpy) : "記録なし";
        PreviousYear = HasPreviousYearRecord ? Formatters.Jpy(PreviousYearActualJpy) : "記録なし";
        Goal = HasMonthlyGoal ? Formatters.Jpy(MonthlyGoalJpy) : "未設定";
    }

    public int Year { get; }
    public int Month { get; }
    public string Label { get; }
    public decimal ActualJpy { get; }
    public decimal PlannedJpy { get; }
    public decimal ForecastJpy { get; }
    public decimal PreviousYearActualJpy { get; }
    public decimal MonthlyGoalJpy { get; }
    public bool HasActualRecord { get; }
    public bool HasForecastRecord { get; }
    public bool HasPreviousYearRecord { get; }
    public bool HasMonthlyGoal { get; }
    public string Actual { get; }
    public string Planned { get; }
    public string Forecast { get; }
    public string PreviousYear { get; }
    public string Goal { get; }
}
