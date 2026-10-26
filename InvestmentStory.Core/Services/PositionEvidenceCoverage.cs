using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed record PositionEvidenceCoverage(
    int ConfirmedCostCount,
    int EstimatedCostCount,
    decimal? EstimatedCostMarketValueJpy,
    int UnconfirmedCostCount,
    decimal? ExcludedMarketValueJpy,
    int UnknownMarketValueCount,
    int MissingPriceCount,
    int DelayedPriceCount,
    int UnknownPriceTimeCount,
    int MissingFxCount,
    int DelayedFxCount,
    int UnknownFxTimeCount)
{
    public static PositionEvidenceCoverage Build(IEnumerable<StockSnapshot> snapshots, DateTime asOf)
    {
        var current = snapshots.Where(PositionEvidenceService.IsActiveHoldingSnapshot).ToArray();
        var estimated = current.Where(x => PositionEvidenceService.CostQuality(x) == AcquisitionCostQuality.Estimated).ToArray();
        var unconfirmed = current.Where(x => PositionEvidenceService.CostQuality(x) == AcquisitionCostQuality.Unconfirmed).ToArray();
        var prices = current.Where(x => x.Position.IsMutualFund
            ? x.Position.MutualFund.CurrentNav > 0m : x.Position.CurrentHolding.CurrentPrice > 0m).ToArray();
        var priceFreshness = prices.Select(x => PositionEvidenceService.PriceFreshness(x.Position, asOf)).ToArray();
        var foreign = current.Where(x => !x.Position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)).ToArray();
        var fxFreshness = foreign.Where(x => PositionEvidenceService.HasUsableForeignExchangeRate(x.Position.CurrentHolding.CurrentExchangeRate))
            .Select(x => PositionEvidenceService.Freshness(x.Position.CurrentHolding.ExchangeRateAcquiredAt, asOf)).ToArray();
        return new PositionEvidenceCoverage(
            current.Length - estimated.Length - unconfirmed.Length,
            estimated.Length, ValuationOf(estimated),
            unconfirmed.Length, ValuationOf(unconfirmed),
            current.Count(x => !x.HasKnownCurrentMarketValueJpy),
            current.Length - prices.Length,
            priceFreshness.Count(x => x == DataFreshness.Delayed),
            priceFreshness.Count(x => x == DataFreshness.TimeUnknown),
            foreign.Length - fxFreshness.Length,
            fxFreshness.Count(x => x == DataFreshness.Delayed),
            fxFreshness.Count(x => x == DataFreshness.TimeUnknown));
    }

    private static decimal? ValuationOf(IReadOnlyList<StockSnapshot> snapshots) =>
        snapshots.Any(x => !x.HasKnownCurrentMarketValueJpy)
            ? null : snapshots.Sum(x => x.CurrentMarketValueJpy);
}
