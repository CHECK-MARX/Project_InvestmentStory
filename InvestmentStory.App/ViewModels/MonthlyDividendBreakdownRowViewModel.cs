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
        Actual = Formatters.Jpy(ActualJpy);
        Planned = Formatters.Jpy(PlannedJpy);
        Forecast = Formatters.Jpy(ForecastJpy);
        PreviousYear = Formatters.Jpy(PreviousYearActualJpy);
        Goal = Formatters.Jpy(MonthlyGoalJpy);
    }

    public int Year { get; }
    public int Month { get; }
    public string Label { get; }
    public decimal ActualJpy { get; }
    public decimal PlannedJpy { get; }
    public decimal ForecastJpy { get; }
    public decimal PreviousYearActualJpy { get; }
    public decimal MonthlyGoalJpy { get; }
    public string Actual { get; }
    public string Planned { get; }
    public string Forecast { get; }
    public string PreviousYear { get; }
    public string Goal { get; }
}
