using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class CompositeStockLookupService : IStockLookupService
{
    private readonly IStockLookupService _localLookupService;
    private readonly IStockLookupService _remoteLookupService;

    public CompositeStockLookupService(
        IStockLookupService localLookupService,
        IStockLookupService remoteLookupService)
    {
        _localLookupService = localLookupService;
        _remoteLookupService = remoteLookupService;
    }

    public StockLookupResult? Find(string query)
    {
        var local = _localLookupService.Find(query);
        var remoteQuery = local?.Ticker ?? query;
        var remote = _remoteLookupService.Find(remoteQuery);

        return Merge(local, remote);
    }

    private static StockLookupResult? Merge(StockLookupResult? local, StockLookupResult? remote)
    {
        if (local is null)
        {
            return remote;
        }

        if (remote is null)
        {
            return local;
        }

        return local with
        {
            Currency = string.IsNullOrWhiteSpace(remote.Currency) ? local.Currency : remote.Currency,
            Country = string.IsNullOrWhiteSpace(remote.Country) ? local.Country : remote.Country,
            Market = string.IsNullOrWhiteSpace(remote.Market) ? local.Market : remote.Market,
            Source = $"{local.Source} + {remote.Source}",
            CurrentPrice = remote.CurrentPrice ?? local.CurrentPrice,
            AnnualDividendPerShare = local.AnnualDividendPerShare ?? remote.AnnualDividendPerShare,
            DividendFrequency = string.IsNullOrWhiteSpace(local.DividendFrequency) ? remote.DividendFrequency : local.DividendFrequency
        };
    }
}
