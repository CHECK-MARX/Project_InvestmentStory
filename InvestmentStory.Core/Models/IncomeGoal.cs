namespace InvestmentStory.Core.Models;

public sealed class IncomeGoal
{
    public int Id { get; set; }
    public int TargetYear { get; set; } = DateTime.Today.Year;
    public decimal AnnualPassiveIncomeGoal { get; set; }
    public decimal MonthlyPassiveIncomeGoal { get; set; }
    public decimal TotalAssetGoal { get; set; }
}
