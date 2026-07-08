using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class SimulationProjectionRowViewModel
{
    public SimulationProjectionRowViewModel(PassiveIncomeProjection projection)
    {
        Year = projection.Year;
        YearsFromNow = $"{projection.YearsFromNow}年後";
        AnnualPassiveIncome = Formatters.Jpy(projection.AnnualPassiveIncome);
        YearOverYearIncrease = Formatters.SignedJpy(projection.YearOverYearIncrease);
        TargetAchievementRate = Formatters.Percent(projection.TargetAchievementRate);
    }

    public int Year { get; }
    public string YearsFromNow { get; }
    public string AnnualPassiveIncome { get; }
    public string YearOverYearIncrease { get; }
    public string TargetAchievementRate { get; }
}
