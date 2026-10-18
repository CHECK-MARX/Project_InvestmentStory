using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public enum AcquisitionCostQuality
{
    Confirmed,
    Estimated,
    Unconfirmed
}

public enum DataFreshness
{
    Latest,
    Delayed,
    TimeUnknown
}

public static class PositionEvidenceService
{
    public static bool IsActiveHolding(StockPosition position) => position.IsMutualFund
        ? position.MutualFund.UnitsHeld > 0m
        : position.CurrentHolding.CurrentShares > 0m;

    // Purchase and current-rate fields historically defaulted to 1. For foreign
    // currencies that value cannot establish a JPY conversion without separate evidence.
    public static bool HasUsableForeignExchangeRate(decimal rate) => rate > 0m && rate != 1m;

    // A displayed quote is only a reference for the purchase when its basis date
    // is explicit and matches the purchase date. A current quote cannot price an
    // undated or historical acquisition.
    public static bool HasDatedReferencePurchaseRate(DateTime purchaseDate, DateTime rateDate, decimal rate) =>
        purchaseDate != DateTime.MinValue && rateDate != DateTime.MinValue &&
        purchaseDate.Date == rateDate.Date && HasUsableForeignExchangeRate(rate);

    public static DateTime EarliestDocumentedBuyDate(int stockId, IEnumerable<BrokerTrade> trades) =>
        trades.Where(x => x.StockId == stockId && x.TradeDate != DateTime.MinValue &&
                x.SignedQuantity > 0m && x.TradeType is "買付" or "Buy" or "Purchase")
            .Select(x => x.TradeDate.Date)
            .DefaultIfEmpty(DateTime.MinValue)
            .Min();

    public static bool AcquisitionDateNeedsReview(StockPosition position, IEnumerable<BrokerTrade> trades)
    {
        if (position.IsMutualFund) return false;
        if (position.Purchase.PurchaseDate == DateTime.MinValue ||
            position.Purchase.Memo.Contains("かんたん登録", StringComparison.Ordinal)) return true;
        var firstBuy = EarliestDocumentedBuyDate(position.Stock.Id, trades);
        return firstBuy != DateTime.MinValue && firstBuy < position.Purchase.PurchaseDate.Date;
    }

    public static AcquisitionCostQuality CostQuality(StockSnapshot snapshot) =>
        !snapshot.HasKnownPurchaseCostJpy ? AcquisitionCostQuality.Unconfirmed :
        snapshot.IsReferencePurchaseCostJpy ? AcquisitionCostQuality.Estimated :
        AcquisitionCostQuality.Confirmed;

    public static DataFreshness Freshness(DateTime acquiredAt, DateTime asOf) =>
        acquiredAt == DateTime.MinValue ? DataFreshness.TimeUnknown :
        acquiredAt.Date < asOf.Date.AddDays(-7) ? DataFreshness.Delayed : DataFreshness.Latest;

    public static string DisplayName(DataFreshness freshness) => freshness switch
    {
        DataFreshness.Delayed => "更新遅れ",
        DataFreshness.TimeUnknown => "時刻不明",
        _ => "最新"
    };
}
