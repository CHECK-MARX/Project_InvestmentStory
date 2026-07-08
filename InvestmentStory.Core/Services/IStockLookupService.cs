using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public interface IStockLookupService
{
    StockLookupResult? Find(string query);
}
