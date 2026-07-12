using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class TsumitateNisaProjectionRowViewModel
{
    public TsumitateNisaProjectionRowViewModel(TsumitateNisaProjection projection)
    {
        YearMonth = projection.YearMonth.ToString("yyyy/MM");
        MonthsFromNow = $"{projection.MonthsFromNow:N0}か月後";
        MarketValue = Formatters.Jpy(projection.MarketValueJpy);
        Cost = Formatters.Jpy(projection.CostJpy);
        GainLoss = Formatters.SignedJpy(projection.GainLossJpy);
        GainLossRate = Formatters.SignedPercent(projection.GainLossRate);
        CumulativeContribution = Formatters.Jpy(projection.CumulativeContributionJpy);
        TargetAchievementRate = Formatters.Percent(projection.TargetAchievementRate);
        HasPositiveGain = projection.GainLossJpy > 0m;
        HasNegativeGain = projection.GainLossJpy < 0m;
    }

    public string YearMonth { get; }
    public string MonthsFromNow { get; }
    public string MarketValue { get; }
    public string Cost { get; }
    public string GainLoss { get; }
    public string GainLossRate { get; }
    public string CumulativeContribution { get; }
    public string TargetAchievementRate { get; }
    public bool HasPositiveGain { get; }
    public bool HasNegativeGain { get; }
}
