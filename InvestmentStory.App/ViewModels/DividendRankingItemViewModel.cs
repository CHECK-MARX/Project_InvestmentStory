using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DividendRankingItemViewModel
{
    public DividendRankingItemViewModel(DividendRankingItem item)
    {
        Rank = item.Rank.ToString();
        Ticker = item.Ticker;
        Name = item.Name;
        AmountJpy = Formatters.Jpy(item.AmountJpy);
        ShareOfTotal = Formatters.Percent(item.ShareOfTotal);
        PreviousYearDifference = Formatters.SignedJpy(item.PreviousYearDifferenceJpy);
    }

    public string Rank { get; }
    public string Ticker { get; }
    public string Name { get; }
    public string AmountJpy { get; }
    public string ShareOfTotal { get; }
    public string PreviousYearDifference { get; }
}
