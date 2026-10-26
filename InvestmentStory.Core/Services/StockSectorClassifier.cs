using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed record StockSectorClassification(string Sector, string Industry);

/// <summary>
/// Classifies known portfolio securities when broker CSVs only supply a product type such as 外株.
/// User-entered, specific sectors always take precedence over this catalog.
/// </summary>
public static class StockSectorClassifier
{
    private static readonly IReadOnlyDictionary<string, StockSectorClassification> KnownStocks =
        new Dictionary<string, StockSectorClassification>(StringComparer.OrdinalIgnoreCase)
        {
            // JPX: 8151 卸売業, 8593 その他金融業.
            ["8151"] = new("卸売・商社", "計測機器・技術商社"),
            ["8593"] = new("金融", "リース・金融サービス"),
            ["AAPL"] = new("情報技術", "電子機器"),
            ["AMD"] = new("情報技術", "半導体"),
            ["BYND"] = new("生活必需品", "食品"),
            // CMB.TECH and TORM describe their principal businesses as marine transportation.
            ["CMBT"] = new("海運", "総合海運"),
            ["CRWD"] = new("情報技術", "サイバーセキュリティ"),
            ["CSCO"] = new("情報技術", "通信機器"),
            ["DHR"] = new("ヘルスケア", "医療・研究機器"),
            ["JNJ"] = new("ヘルスケア", "医薬品・医療機器"),
            ["KO"] = new("生活必需品", "飲料"),
            ["META"] = new("コミュニケーション", "デジタル広告"),
            ["MO"] = new("生活必需品", "たばこ"),
            ["NFLX"] = new("コミュニケーション", "動画配信"),
            ["NVDA"] = new("情報技術", "半導体"),
            ["PEP"] = new("生活必需品", "飲料・食品"),
            ["PG"] = new("生活必需品", "日用品"),
            ["STLA"] = new("一般消費財", "自動車"),
            ["T"] = new("コミュニケーション", "通信"),
            ["TRMD"] = new("海運", "石油製品輸送"),
            ["TSLA"] = new("一般消費財", "自動車"),
            ["WBD"] = new("コミュニケーション", "映像・メディア")
        };

    private static readonly IReadOnlyDictionary<string, string> SectorNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Information Technology"] = "情報技術",
            ["Technology"] = "情報技術",
            ["Consumer Staples"] = "生活必需品",
            ["Consumer Discretionary"] = "一般消費財",
            ["Health Care"] = "ヘルスケア",
            ["Healthcare"] = "ヘルスケア",
            ["Communication Services"] = "コミュニケーション",
            ["Financials"] = "金融",
            ["Finance"] = "金融",
            ["Industrials"] = "資本財・サービス",
            ["Energy"] = "エネルギー",
            ["Materials"] = "素材",
            ["Utilities"] = "公益事業",
            ["Real Estate"] = "不動産"
        };

    public static StockSectorClassification Classify(Stock stock)
    {
        if (stock.AssetType == AssetTypes.MutualFund ||
            stock.Ticker.StartsWith("FUND:", StringComparison.OrdinalIgnoreCase))
            return new StockSectorClassification("投資信託", "分散型ファンド");

        var ticker = SecuritySymbolNormalizer.NormalizeTicker(stock.Ticker);
        var known = KnownStocks.GetValueOrDefault(ticker);
        if (!IsGenericProductType(stock.Sector))
        {
            var sector = stock.Sector.Trim();
            var normalizedSector = SectorNames.GetValueOrDefault(sector, sector);
            var industry = string.IsNullOrWhiteSpace(stock.Industry)
                ? known?.Sector == normalizedSector ? known.Industry : string.Empty
                : stock.Industry.Trim();
            return new StockSectorClassification(normalizedSector, industry);
        }

        return known ?? new StockSectorClassification("未分類", string.Empty);
    }

    public static bool IsGenericProductType(string? value) => string.IsNullOrWhiteSpace(value) ||
        value.Trim() is "外株" or "株式" or "国内株式" or "外国株式" or "米国株式" or
            "国内株" or "海外株" or "未設定" or "未分類" or "-";

    public static void FillMissingClassification(Stock stock)
    {
        var resolved = Classify(stock);
        if (IsGenericProductType(stock.Sector) && resolved.Sector != "未分類")
            stock.Sector = resolved.Sector;
        if (string.IsNullOrWhiteSpace(stock.Industry) && resolved.Industry.Length > 0)
            stock.Industry = resolved.Industry;
    }
}
