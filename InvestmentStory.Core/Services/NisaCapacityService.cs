using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class NisaCapacityService
{
    public const string RuleVersion = "FSA-2024-adult-v1";
    public const string RuleSource = "https://www.fsa.go.jp/policy/nisa2/know/index.html";
    public const decimal AnnualGrowthLimitJpy = 2_400_000m;
    public const decimal AnnualAccumulationLimitJpy = 1_200_000m;
    public const decimal LifetimeLimitJpy = 18_000_000m;
    public const decimal GrowthLifetimeLimitJpy = 12_000_000m;

    public NisaCapacityAnalysis Build(IReadOnlyList<StockSnapshot> snapshots,
        IReadOnlyList<NisaTransaction> transactions, NisaHistoryState history, DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(history);
        asOf = asOf.Date;
        var holdings = snapshots.Where(x => x.Position.Stock.AccountType is
            AccountTypes.NisaGrowth or AccountTypes.NisaAccumulation).ToArray();
        var unknown = holdings.Count(x => !x.HasKnownCurrentMarketValueJpy);
        var known = holdings.Where(x => x.HasKnownCurrentMarketValueJpy).Sum(x => x.CurrentMarketValueJpy);
        var value = unknown == 0 ? known : (decimal?)null;
        var complete = asOf.Year is >= 2024 and <= 2026 &&
            history.CompleteThroughDate?.Date >= asOf &&
            !string.IsNullOrWhiteSpace(history.Evidence) &&
            (holdings.Length == 0 || transactions.Any(x => x.Kind == NisaTransactionKinds.Buy)) &&
            transactions.All(x => x.EffectiveDate.Date <= asOf &&
                x.EffectiveDate.Year >= 2024 && x.BookCostJpy > 0m &&
                x.Frame is AccountTypes.NisaGrowth or AccountTypes.NisaAccumulation &&
                x.Kind is NisaTransactionKinds.Buy or NisaTransactionKinds.Sell);
        if (!complete)
            return new NisaCapacityAnalysis(asOf.Year, asOf, RuleVersion, RuleSource,
                known, value, unknown, null, null, null, null, null, null, null,
                asOf.Year is < 2024 or > 2026
                    ? "この年度の制度規則は未登録です。金融庁の最新情報を確認してください。"
                    : "取得・売却履歴が未確認です。評価額から投資枠使用量や残枠は計算できません。");

        var buys = transactions.Where(x => x.Kind == NisaTransactionKinds.Buy).ToArray();
        var eligibleSales = transactions.Where(x => x.Kind == NisaTransactionKinds.Sell &&
            x.EffectiveDate.Year < asOf.Year).ToArray();
        var annualGrowth = buys.Where(x => x.EffectiveDate.Year == asOf.Year && x.Frame == AccountTypes.NisaGrowth)
            .Sum(x => x.BookCostJpy);
        var annualAccumulation = buys.Where(x => x.EffectiveDate.Year == asOf.Year && x.Frame == AccountTypes.NisaAccumulation)
            .Sum(x => x.BookCostJpy);
        var lifetimeUsed = buys.Sum(x => x.BookCostJpy) - eligibleSales.Sum(x => x.BookCostJpy);
        var growthUsed = buys.Where(x => x.Frame == AccountTypes.NisaGrowth).Sum(x => x.BookCostJpy) -
            eligibleSales.Where(x => x.Frame == AccountTypes.NisaGrowth).Sum(x => x.BookCostJpy);
        var accumulationUsed = buys.Where(x => x.Frame == AccountTypes.NisaAccumulation).Sum(x => x.BookCostJpy) -
            eligibleSales.Where(x => x.Frame == AccountTypes.NisaAccumulation).Sum(x => x.BookCostJpy);
        var impossible = annualGrowth > AnnualGrowthLimitJpy || annualAccumulation > AnnualAccumulationLimitJpy ||
            lifetimeUsed is < 0m or > LifetimeLimitJpy || growthUsed is < 0m or > GrowthLifetimeLimitJpy ||
            accumulationUsed < 0m || !HistoryRespectsLimits(transactions, asOf.Year);
        if (impossible)
            return new NisaCapacityAnalysis(asOf.Year, asOf, RuleVersion, RuleSource,
                known, value, unknown, null, null, null, null, null, null, null,
                "履歴と制度上限が矛盾しています。証券会社の記録と取得簿価を確認してください。");

        return new NisaCapacityAnalysis(asOf.Year, asOf, RuleVersion, RuleSource,
            known, value, unknown, annualGrowth, annualAccumulation,
            AnnualGrowthLimitJpy - annualGrowth, AnnualAccumulationLimitJpy - annualAccumulation,
            lifetimeUsed, LifetimeLimitJpy - lifetimeUsed, GrowthLifetimeLimitJpy - growthUsed,
            "入力履歴に基づく参考計算です。売却簿価の再利用は翌年から。証券会社の残枠表示を優先してください。");
    }

    private static bool HistoryRespectsLimits(IReadOnlyList<NisaTransaction> transactions, int throughYear)
    {
        var buys = transactions.Where(x => x.Kind == NisaTransactionKinds.Buy).ToArray();
        var sales = transactions.Where(x => x.Kind == NisaTransactionKinds.Sell).ToArray();
        foreach (var frame in new[] { AccountTypes.NisaGrowth, AccountTypes.NisaAccumulation })
        {
            decimal heldBookCost = 0m;
            foreach (var day in transactions.Where(x => x.Frame == frame).GroupBy(x => x.EffectiveDate.Date).OrderBy(x => x.Key))
            {
                heldBookCost += day.Where(x => x.Kind == NisaTransactionKinds.Buy).Sum(x => x.BookCostJpy) -
                    day.Where(x => x.Kind == NisaTransactionKinds.Sell).Sum(x => x.BookCostJpy);
                if (heldBookCost < 0m) return false;
            }
        }

        for (var year = 2024; year <= throughYear; year++)
        {
            var annualGrowth = buys.Where(x => x.EffectiveDate.Year == year && x.Frame == AccountTypes.NisaGrowth)
                .Sum(x => x.BookCostJpy);
            var annualAccumulation = buys.Where(x => x.EffectiveDate.Year == year && x.Frame == AccountTypes.NisaAccumulation)
                .Sum(x => x.BookCostJpy);
            var priorSales = sales.Where(x => x.EffectiveDate.Year < year).ToArray();
            var lifetimeAtYearEnd = buys.Where(x => x.EffectiveDate.Year <= year).Sum(x => x.BookCostJpy) -
                priorSales.Sum(x => x.BookCostJpy);
            var growthAtYearEnd = buys.Where(x => x.EffectiveDate.Year <= year && x.Frame == AccountTypes.NisaGrowth)
                .Sum(x => x.BookCostJpy) - priorSales.Where(x => x.Frame == AccountTypes.NisaGrowth).Sum(x => x.BookCostJpy);
            if (annualGrowth > AnnualGrowthLimitJpy || annualAccumulation > AnnualAccumulationLimitJpy ||
                lifetimeAtYearEnd > LifetimeLimitJpy || growthAtYearEnd > GrowthLifetimeLimitJpy)
                return false;
        }
        return true;
    }
}
