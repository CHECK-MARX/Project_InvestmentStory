using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

/// <summary>Current holdings only. Cash, tax, fees and order execution are outside this analysis.</summary>
public sealed class AllocationTargetService
{
    public IReadOnlyList<AllocationHoldingCategory> CategorizeHoldings(
        IReadOnlyList<StockSnapshot> snapshots, AllocationDimension dimension) =>
        snapshots.Where(x => x.Position.IsMutualFund
                ? x.Position.MutualFund.UnitsHeld > 0m
                : x.Position.CurrentHolding.CurrentShares > 0m)
            .Select(x => Categorize(x, dimension)).ToArray();

    public static void ValidateTargets(IReadOnlyList<AllocationTarget> targets)
    {
        if (targets.Count == 0) throw new ArgumentException("配分目標を1件以上入力してください。");
        if (targets.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Label)))
            throw new ArgumentException("分類キーと表示名は必須です。");
        if (targets.Any(x => x.Percent < 0m || x.Percent > 100m))
            throw new ArgumentException("各目標は0～100%で入力してください。");
        if (targets.Select(x => x.Key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
            throw new ArgumentException("同じ分類の目標が重複しています。");
        if (targets.Sum(x => x.Percent) != 100m)
            throw new ArgumentException("配分目標の合計を100%にしてください。");
    }

    public AllocationAnalysis Analyze(
        IReadOnlyList<StockSnapshot> snapshots,
        AllocationDimension dimension,
        IReadOnlyList<AllocationTarget> targets,
        decimal? additionalFundingJpy = null)
    {
        ValidateTargets(targets);
        if (additionalFundingJpy is < 0m)
            throw new ArgumentException("追加資金は0円以上にしてください。");

        var holdings = CategorizeHoldings(snapshots, dimension);
        var problems = new List<string>();
        var missing = holdings.Where(x => x.MarketValueJpy is null).ToArray();
        if (missing.Length > 0)
            problems.Add($"円評価額が不明な保有銘柄が{missing.Length}件あります。比率と売買額は計算できません。");
        if (holdings.Any(x => x.MarketValueJpy < 0m))
            problems.Add("円評価額が負の保有銘柄があります。データを確認してください。");

        var known = holdings.Where(x => x.MarketValueJpy >= 0m).Sum(x => x.MarketValueJpy ?? 0m);
        var total = problems.Count == 0 ? known : (decimal?)null;
        if (total == 0m)
            problems.Add("保有評価額が0円のため、現在比率と売買案は計算できません。");

        var categoryGroups = holdings.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var targetMap = targets.ToDictionary(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase);
        var keys = categoryGroups.Keys.Union(targetMap.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        decimal? minimumFunding = null;
        var canBuyOnly = total > 0m;
        if (canBuyOnly)
        {
            minimumFunding = 0m;
            foreach (var key in keys)
            {
                var current = categoryGroups.TryGetValue(key, out var group)
                    ? group.Sum(x => x.MarketValueJpy ?? 0m) : 0m;
                var target = targetMap.GetValueOrDefault(key)?.Percent ?? 0m;
                if (target == 0m && current > 0m)
                {
                    canBuyOnly = false;
                    minimumFunding = null;
                    break;
                }
                if (target > 0m)
                    minimumFunding = Math.Max(minimumFunding!.Value, current * 100m / target - total!.Value);
            }
        }
        if (total > 0m && !canBuyOnly)
            problems.Add("目標0%の保有資産があるため、追加資金だけでは目標に到達できません。");
        var fundingReady = canBuyOnly && additionalFundingJpy is decimal cash && cash >= minimumFunding;
        if (canBuyOnly && additionalFundingJpy is decimal amount && amount < minimumFunding)
            problems.Add($"指定した追加資金では目標に届きません。追加資金だけで達成するには最低{Math.Ceiling(minimumFunding!.Value):#,0}円が必要です。");

        var categories = keys.Select(key =>
        {
            categoryGroups.TryGetValue(key, out var group);
            var target = targetMap.GetValueOrDefault(key);
            var current = group?.Sum(x => x.MarketValueJpy ?? 0m) ?? 0m;
            var percentage = target?.Percent ?? 0m;
            var label = target?.Label ?? group?.First().Label ?? key;
            var evidence = group is { Length: > 0 }
                ? string.Join(" / ", group.Select(x => x.ClassificationEvidence).Distinct())
                : "保有なし（目標のみ）";
            var gap = total > 0m ? total.Value * percentage / 100m - current : (decimal?)null;
            return new AllocationCategoryResult(key, label, evidence,
                total is not null ? current : null,
                total > 0m ? current * 100m / total.Value : null,
                percentage,
                gap,
                fundingReady ? (total!.Value + additionalFundingJpy!.Value) * percentage / 100m - current : null,
                gap);
        }).OrderByDescending(x => x.CurrentJpy ?? 0m).ThenBy(x => x.Label).ToArray();

        return new AllocationAnalysis(dimension, holdings, categories, known, total,
            minimumFunding is decimal minimum ? Math.Max(0m, minimum) : null,
            additionalFundingJpy, fundingReady, problems);
    }

    private static AllocationHoldingCategory Categorize(StockSnapshot snapshot, AllocationDimension dimension)
    {
        var stock = snapshot.Position.Stock;
        string value;
        string evidence;
        switch (dimension)
        {
            case AllocationDimension.Country:
                value = stock.Country;
                evidence = "銘柄マスターの国（投資先の国別内訳ではありません）";
                break;
            case AllocationDimension.Sector:
                if (snapshot.Position.IsMutualFund)
                {
                    value = "投資信託（内訳未分類）";
                    evidence = "資産種別から判定。ファンドの組入先は未確認";
                }
                else if (StockSectorClassifier.IsGenericProductType(stock.Sector))
                {
                    value = StockSectorClassifier.Classify(stock).Sector;
                    evidence = value == "未分類" ? "分野の根拠なし" : "銘柄辞書による推定";
                }
                else
                {
                    value = stock.Sector;
                    evidence = "銘柄マスターの分類（自動補完を含む）";
                }
                break;
            case AllocationDimension.Security:
                value = string.IsNullOrWhiteSpace(stock.CanonicalSecurityKey)
                    ? stock.Ticker : stock.CanonicalSecurityKey;
                evidence = "銘柄マスターの識別子";
                break;
            case AllocationDimension.Currency:
                value = stock.Currency;
                evidence = "銘柄マスターの取引通貨（投資先の通貨内訳ではありません）";
                break;
            case AllocationDimension.Broker:
                value = stock.Broker;
                evidence = "銘柄マスターの証券会社";
                break;
            default:
                value = snapshot.Position.IsMutualFund ? "投資信託" :
                    string.Equals(stock.AssetType, AssetTypes.Stock, StringComparison.OrdinalIgnoreCase) ? "株式" : "未分類";
                evidence = "資産種別（現金は対象外）";
                break;
        }
        var key = string.IsNullOrWhiteSpace(value) ? "未分類" : value.Trim();
        var label = dimension == AllocationDimension.Security && key != "未分類"
            ? $"{stock.Ticker} {stock.Name}".Trim() : key;
        return new AllocationHoldingCategory(stock.Id, stock.Ticker, key, label, evidence,
            snapshot.HasKnownCurrentMarketValueJpy ? snapshot.CurrentMarketValueJpy : null);
    }
}
