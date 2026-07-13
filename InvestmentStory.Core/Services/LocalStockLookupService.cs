using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class LocalStockLookupService : IStockLookupService
{
    private readonly IReadOnlyDictionary<string, StockLookupResult> _entries;

    public LocalStockLookupService()
    {
        var entries = new Dictionary<string, StockLookupResult>(StringComparer.OrdinalIgnoreCase);

        Add(entries, new StockLookupResult(
            "8151",
            "東陽テクニカ",
            "日本",
            "JPY",
            "東証プライム",
            "内蔵銘柄マスター",
            CurrentPrice: 1967m,
            AnnualDividendPerShare: 70m,
            DividendFrequency: "年2回"),
            "8151.T",
            "東陽テクニカ",
            "東陽",
            "toyo");

        Add(entries, new StockLookupResult(
            "8593",
            "三菱HCキャピタル",
            "日本",
            "JPY",
            "東証プライム",
            "内蔵銘柄マスター",
            CurrentPrice: 1372m,
            AnnualDividendPerShare: 51m,
            DividendFrequency: "年2回"),
            "8593.T",
            "三菱HC",
            "三菱HCキャピタル",
            "三菱ＨＣ",
            "mitsubishi hc");

        Add(entries, new StockLookupResult(
            "KO",
            "The Coca-Cola Company",
            "米国",
            "USD",
            "NYSE",
            "内蔵銘柄マスター",
            AnnualDividendPerShare: 2.12m,
            DividendFrequency: "年4回"),
            "Coca-Cola",
            "Coca Cola",
            "コカコーラ",
            "コカ・コーラ");

        Add(entries, new StockLookupResult(
            "PEP",
            "PepsiCo, Inc.",
            "米国",
            "USD",
            "NASDAQ",
            "内蔵銘柄マスター"),
            "PepsiCo",
            "Pepsico",
            "ペプシコ");

        Add(entries, new StockLookupResult(
            "2593",
            "伊藤園",
            "日本",
            "JPY",
            "東証プライム",
            "内蔵銘柄マスター",
            AnnualDividendPerShare: 48m,
            DividendFrequency: "年2回"),
            "2593.T",
            "伊藤園",
            "ITO EN",
            "Ito En",
            "おーいお茶");

        Add(entries, new StockLookupResult(
            "3132",
            "マクニカホールディングス",
            "日本",
            "JPY",
            "東証プライム",
            "内蔵銘柄マスター",
            AnnualDividendPerShare: 70m,
            DividendFrequency: "年2回"),
            "3132.T",
            "マクニカ",
            "マクニカホールディングス",
            "MACNICA",
            "Macnica Holdings");

        Add(entries, new StockLookupResult(
            "7203",
            "トヨタ自動車",
            "日本",
            "JPY",
            "東証プライム",
            "内蔵銘柄マスター"),
            "7203.T",
            "トヨタ",
            "Toyota");

        Add(entries, new StockLookupResult(
            "2914",
            "日本たばこ産業",
            "日本",
            "JPY",
            "東証プライム",
            "内蔵銘柄マスター"),
            "2914.T",
            "JT",
            "日本たばこ",
            "Japan Tobacco");

        Add(entries, new StockLookupResult(
            "PG",
            "Procter & Gamble Co.",
            "米国",
            "USD",
            "NYSE",
            "内蔵銘柄マスター",
            CurrentPrice: 160m,
            AnnualDividendPerShare: 4.24m,
            DividendFrequency: "年4回"),
            "Procter & Gamble",
            "P&G",
            "プロクター",
            "プロクター アンド ギャンブル");

        Add(entries, new StockLookupResult("NVDA", "NVIDIA", "米国", "USD", "NASDAQ", "内蔵銘柄マスター", 196m, 1m),
            "NVIDIA",
            "エヌビディア");
        Add(entries, new StockLookupResult("TSLA", "Tesla", "米国", "USD", "NASDAQ", "内蔵銘柄マスター", 416m, 0m),
            "Tesla",
            "テスラ");
        Add(entries, new StockLookupResult("AMD", "AMD", "米国", "USD", "NASDAQ", "内蔵銘柄マスター", 563m, 0m),
            "Advanced Micro Devices");

        _entries = entries;
    }

    public StockLookupResult? Find(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalized = Normalize(query);
        return _entries.TryGetValue(normalized, out var result) ? result : null;
    }

    private static void Add(
        Dictionary<string, StockLookupResult> entries,
        StockLookupResult result,
        params string[] aliases)
    {
        entries[Normalize(result.Ticker)] = result;
        foreach (var alias in aliases)
        {
            entries[Normalize(alias)] = result;
        }
    }

    private static string Normalize(string value)
    {
        return value.Trim()
            .Replace("　", " ")
            .Replace("（株）", string.Empty)
            .Replace("(株)", string.Empty)
            .Replace("株式会社", string.Empty)
            .ToUpperInvariant();
    }
}
