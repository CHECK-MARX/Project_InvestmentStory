using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public interface IPriceHistoryService
{
    PriceHistoryResult GetHistory(string symbol, AppSettings settings, string range, string interval);
}
