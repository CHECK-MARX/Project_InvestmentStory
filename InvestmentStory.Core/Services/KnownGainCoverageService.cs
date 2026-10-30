using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed record KnownGainCoverage(
    int ActiveCount, int ConfirmedCount, int ReferenceCount,
    int UnknownCostCount, int UnknownValueCount, int UnverifiedTradeCount,
    decimal ConfirmedGainJpy, decimal ConfirmedCostJpy, decimal ReferenceGainJpy)
{
    public bool HasConfirmedSubtotal => ConfirmedCount > 0;
    public bool IsComplete => ActiveCount == ConfirmedCount && ReferenceCount == 0 &&
        UnknownCostCount == 0 && UnknownValueCount == 0 && UnverifiedTradeCount == 0;
    public decimal ConfirmedGainRate => ConfirmedCostJpy > 0m
        ? ConfirmedGainJpy / ConfirmedCostJpy * 100m : 0m;
}

public sealed class KnownGainCoverageService
{
    public KnownGainCoverage Build(IEnumerable<StockSnapshot> snapshots, IEnumerable<BrokerTrade> trades)
    {
        var active = snapshots.Where(PositionEvidenceService.IsActiveHoldingSnapshot).ToList();
        var unverifiedStockIds = trades
            .Where(x => x.TradeType is "TransferIn" or "StockSplitCandidate")
            .Select(x => x.StockId).ToHashSet();
        var confirmed = active.Where(x => x.HasKnownCurrentMarketValueJpy &&
            x.HasKnownPurchaseCostJpy && !x.IsReferencePurchaseCostJpy &&
            !unverifiedStockIds.Contains(x.Position.Stock.Id)).ToList();
        var reference = active.Where(x => x.HasKnownCurrentMarketValueJpy &&
            x.HasKnownPurchaseCostJpy && x.IsReferencePurchaseCostJpy &&
            !unverifiedStockIds.Contains(x.Position.Stock.Id)).ToList();
        return new KnownGainCoverage(
            active.Count, confirmed.Count, reference.Count,
            active.Count(x => !x.HasKnownPurchaseCostJpy),
            active.Count(x => !x.HasKnownCurrentMarketValueJpy),
            active.Count(x => unverifiedStockIds.Contains(x.Position.Stock.Id)),
            confirmed.Sum(x => x.UnrealizedGainJpy),
            confirmed.Sum(x => x.PurchaseTotalJpy),
            reference.Sum(x => x.UnrealizedGainJpy));
    }
}
