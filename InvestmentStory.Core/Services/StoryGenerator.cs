using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class StoryGenerator
{
    public string Generate(StockSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Position.IsMutualFund)
        {
            return GenerateMutualFundStory(snapshot);
        }

        var stock = snapshot.Position.Stock;
        var purchase = snapshot.Position.Purchase;
        var split = snapshot.Position.Split;
        var current = snapshot.Position.CurrentHolding;
        var currency = stock.Currency;
        var shareChangeRatio = purchase.Shares == 0m ? 0m : current.CurrentShares / purchase.Shares;
        var shareChangeText = shareChangeRatio == 1m
            ? $"現在も{FormatDecimal(current.CurrentShares)}株保有しています"
            : $"株数変化により現在は{FormatDecimal(current.CurrentShares)}株保有しています";
        var splitNote = split.SplitRatio != 1m ? $"参考の株数変化倍率は{FormatDecimal(split.SplitRatio)}倍です。" : string.Empty;
        var gainLabel = snapshot.UnrealizedGain < 0m ? "含み損" : "含み益";
        var gainComment = CreateGainComment(snapshot);
        var dividendComment = CreateDividendComment(snapshot);

        return
            $"{stock.Name}は{FormatDecimal(purchase.Shares)}株を{FormatMoney(purchase.UnitPrice, currency)}で購入し、{shareChangeText}。" +
            $"{splitNote}現在の実質取得単価は{FormatMoney(snapshot.EffectiveAcquisitionPrice, currency)}です。" +
            $"現在株価が{FormatMoney(current.CurrentPrice, currency)}の場合、評価額は{FormatMoney(snapshot.CurrentMarketValue, currency)}となり、" +
            $"購入総額{FormatMoney(snapshot.PurchaseTotal, currency)}に対して{FormatSignedMoney(snapshot.UnrealizedGain, currency)}の{gainLabel}です。" +
            $"円ベースでは{FormatSignedJpy(snapshot.UnrealizedGainJpy)}、為替影響額は{FormatSignedJpy(snapshot.CurrencyImpactJpy)}です。" +
            $"通貨ベース損益率は{snapshot.UnrealizedGainRate:N2}%、円ベース損益率は{snapshot.UnrealizedGainRateJpy:N2}%、投資元本の{snapshot.Multiple:N2}倍です。{gainComment}{dividendComment}";
    }

    private static string GenerateMutualFundStory(StockSnapshot snapshot)
    {
        var fund = snapshot.Position.MutualFund;
        var name = string.IsNullOrWhiteSpace(fund.FundName)
            ? snapshot.Position.Stock.Name
            : fund.FundName;
        var unitBase = MutualFundCalculator.NormalizeUnitBase(fund.UnitBase);
        var gainLabel = snapshot.UnrealizedGainJpy < 0m ? "評価損" : "評価益";
        var navSource = string.IsNullOrWhiteSpace(fund.NavSource) ? "未取得" : fund.NavSource;
        var navDate = fund.NavDate == DateTime.MinValue ? "未更新" : fund.NavDate.ToString("yyyy/MM/dd");

        return
            $"{name}は{fund.UnitsHeld:N0}口を保有しています。取得単価は{fund.AverageCostNav:N0}円/{unitBase:N0}口、" +
            $"基準価額は{fund.CurrentNav:N0}円/{unitBase:N0}口です。取得金額{snapshot.PurchaseTotalJpy:N0}円に対して、" +
            $"評価額は{snapshot.CurrentMarketValueJpy:N0}円となり、{snapshot.UnrealizedGainJpy:+#,0;-#,0;0}円の{gainLabel}です。" +
            $"評価損益率は{snapshot.UnrealizedGainRateJpy:N2}%です。基準価額の取得元は{navSource}、更新日は{navDate}です。" +
            (string.Equals(fund.DistributionMethod, "再投資", StringComparison.Ordinal)
                ? "分配金受取方法は再投資のため、現金配当の不労所得には加算しません。"
                : string.IsNullOrWhiteSpace(fund.DistributionMethod) ? string.Empty : $"分配金受取方法は{fund.DistributionMethod}です。");
    }

    private static string CreateGainComment(StockSnapshot snapshot)
    {
        if (snapshot.UnrealizedGainRate >= 100m)
        {
            return "大きな含み益が出ており、値上がり益が投資成果の中心になっています。";
        }

        if (snapshot.UnrealizedGainRate > 0m)
        {
            return "堅調な含み益が出ています。";
        }

        if (snapshot.UnrealizedGainRate < 0m)
        {
            return "現在は含み損の状態です。今後の業績や配当方針を確認したい銘柄です。";
        }

        return "現在評価額は購入総額とほぼ同水準です。";
    }

    private static string CreateDividendComment(StockSnapshot snapshot)
    {
        var currency = snapshot.Position.Stock.Currency;
        var annualDividendPerShare = snapshot.Position.CurrentHolding.AnnualDividendPerShare;

        var dividendStatus = snapshot.Position.CurrentHolding.DividendStatus;
        if (dividendStatus == "配当なし")
        {
            return "配当はありませんが、値上がり益を重視する成長株として振り返ることができます。";
        }

        if (dividendStatus == "配当未入力")
        {
            return "配当情報は未入力です。配当額が分かれば、取得額ベース利回りと不労所得見込みを確認できます。";
        }

        if (snapshot.AnnualDividendForecast <= 0m)
        {
            return "配当情報は取得できていません。";
        }

        if (snapshot.YieldOnCost >= 5m)
        {
            return $"また、年間配当が1株あたり{FormatMoney(annualDividendPerShare, currency)}の場合、取得額ベース利回りは約{snapshot.YieldOnCost:N2}%と高い水準です。";
        }

        return $"また、年間配当が1株あたり{FormatMoney(annualDividendPerShare, currency)}の場合、取得額ベース利回りは約{snapshot.YieldOnCost:N2}%です。";
    }

    private static string FormatMoney(decimal value, string currency) => $"{value:N2} {currency}";

    private static string FormatSignedMoney(decimal value, string currency) => $"{value:+#,0.00;-#,0.00;0.00} {currency}";

    private static string FormatSignedJpy(decimal value) => $"{value:+#,0;-#,0;0} JPY";

    private static string FormatDecimal(decimal value) => value % 1m == 0m ? value.ToString("N0") : value.ToString("N2");
}
