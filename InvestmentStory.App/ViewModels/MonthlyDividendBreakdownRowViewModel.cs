using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class MonthlyDividendBreakdownRowViewModel
{
    private const double MaxBarWidth = 260d;

    public MonthlyDividendBreakdownRowViewModel(MonthlyDividendBreakdown value, decimal maxAmount)
    {
        Label = value.Label;
        Actual = Formatters.Jpy(value.ActualJpy);
        Planned = Formatters.Jpy(value.PlannedJpy);
        Forecast = Formatters.Jpy(value.ForecastJpy);
        PreviousYear = Formatters.Jpy(value.PreviousYearActualJpy);
        Goal = Formatters.Jpy(value.MonthlyGoalJpy);
        ActualBarWidth = CalculateBarWidth(value.ActualJpy, maxAmount);
        PlannedBarWidth = CalculateBarWidth(value.PlannedJpy, maxAmount);
        PreviousBarWidth = CalculateBarWidth(value.PreviousYearActualJpy, maxAmount);
        GoalBarWidth = CalculateBarWidth(value.MonthlyGoalJpy, maxAmount);
        ToolTip = $"{value.Year}/{value.Month:00} 実績 {Actual} / 予定 {Planned} / 見込み {Forecast} / 前年 {PreviousYear} / 目標 {Goal}";
    }

    public string Label { get; }
    public string Actual { get; }
    public string Planned { get; }
    public string Forecast { get; }
    public string PreviousYear { get; }
    public string Goal { get; }
    public double ActualBarWidth { get; }
    public double PlannedBarWidth { get; }
    public double PreviousBarWidth { get; }
    public double GoalBarWidth { get; }
    public string ToolTip { get; }

    private static double CalculateBarWidth(decimal amount, decimal maxAmount)
    {
        if (amount <= 0m || maxAmount <= 0m)
        {
            return 0d;
        }

        return Math.Max(4d, (double)(amount / maxAmount) * MaxBarWidth);
    }
}
