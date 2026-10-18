using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class NisaDashboardSummaryViewModel
{
    public NisaDashboardSummaryViewModel(string accountType, IReadOnlyList<StockSnapshot> snapshots)
    {
        AccountType = accountType;
        Title = ResolveTitle(accountType);
        Subtitle = ResolveSubtitle(accountType);
        PositionCount = snapshots.Count;
        HasUnknownPurchaseCost = snapshots.Any(x => !x.HasKnownPurchaseCostJpy);
        HasUnknownMarketValue = snapshots.Any(x => !x.HasKnownCurrentMarketValueJpy);
        HasReferencePurchaseCost = snapshots.Any(x => x.IsReferencePurchaseCostJpy);
        MarketValueJpyValue = snapshots.Sum(x => x.CurrentMarketValueJpy);
        PurchaseAmountJpyValue = snapshots.Sum(x => x.PurchaseTotalJpy);
        GainLossJpyValue = snapshots.Sum(x => x.UnrealizedGainJpy);
        GainLossRateValue = PurchaseAmountJpyValue == 0m ? 0m : GainLossJpyValue / PurchaseAmountJpyValue * 100m;
    }

    public string AccountType { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public int PositionCount { get; }
    public decimal MarketValueJpyValue { get; }
    public decimal PurchaseAmountJpyValue { get; }
    public decimal GainLossJpyValue { get; }
    public decimal GainLossRateValue { get; }
    public bool HasUnknownPurchaseCost { get; }
    public bool HasUnknownMarketValue { get; }
    public bool HasReferencePurchaseCost { get; }
    private string ReferencePrefix => HasReferencePurchaseCost ? "概算 " : string.Empty;
    public bool HasPositiveGain => !HasUnknownPurchaseCost && !HasUnknownMarketValue && GainLossJpyValue > 0m;
    public bool HasNegativeGain => !HasUnknownPurchaseCost && !HasUnknownMarketValue && GainLossJpyValue < 0m;
    public string MarketValueJpy => HasUnknownMarketValue ? "算出不可" : Formatters.Jpy(MarketValueJpyValue);
    public string PurchaseAmountJpy => HasUnknownPurchaseCost ? "未確認" : ReferencePrefix + Formatters.Jpy(PurchaseAmountJpyValue);
    public string GainLossJpy => HasUnknownPurchaseCost || HasUnknownMarketValue ? "算出不可" : ReferencePrefix + Formatters.SignedJpy(GainLossJpyValue);
    public string GainLossRate => HasUnknownPurchaseCost || HasUnknownMarketValue ? "算出不可" : ReferencePrefix + Formatters.SignedPercent(GainLossRateValue);
    public string PositionCountText => $"{PositionCount:N0}件";

    private static string ResolveTitle(string accountType) =>
        accountType switch
        {
            AccountTypes.NisaGrowth => "NISA成長投資枠",
            AccountTypes.NisaAccumulation => "NISAつみたて投資枠",
            AccountTypes.NisaLegacy => "旧つみたてNISA",
            _ => AccountTypes.DisplayName(accountType)
        };

    private static string ResolveSubtitle(string accountType) =>
        accountType switch
        {
            AccountTypes.NisaGrowth => "個別株・投資信託の成長枠",
            AccountTypes.NisaAccumulation => "積立投資の現在評価",
            AccountTypes.NisaLegacy => "旧制度分の積立評価",
            _ => "NISA口座"
        };
}
