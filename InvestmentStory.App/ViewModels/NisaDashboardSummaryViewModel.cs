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
    public bool HasPositiveGain => GainLossJpyValue > 0m;
    public bool HasNegativeGain => GainLossJpyValue < 0m;
    public string MarketValueJpy => Formatters.Jpy(MarketValueJpyValue);
    public string PurchaseAmountJpy => Formatters.Jpy(PurchaseAmountJpyValue);
    public string GainLossJpy => Formatters.SignedJpy(GainLossJpyValue);
    public string GainLossRate => Formatters.SignedPercent(GainLossRateValue);
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
