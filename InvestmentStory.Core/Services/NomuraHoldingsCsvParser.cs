using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class NomuraHoldingsCsvParser
{
    public BrokerStatementImport Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CSVファイルが見つかりません。", filePath);
        }

        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        var headerIndex = FindHeaderIndex(lines);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException("野村證券保有残高CSVの明細ヘッダーが見つかりません。");
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        var holdings = new List<BrokerHoldingRecord>();
        foreach (var line in lines.Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var row = ToDictionary(headers, ParseCsvLine(line));
            var holding = CreateHolding(row);
            if (!string.IsNullOrWhiteSpace(holding.Ticker) && holding.Shares > 0m)
            {
                holdings.Add(holding);
            }
        }

        return new BrokerStatementImport
        {
            Broker = "野村証券",
            Source = "野村保有残高CSV",
            CanUpdateHoldings = holdings.Count > 0,
            Holdings = holdings
        };
    }

    public static bool LooksLikeNomuraHoldingsCsv(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var lines = File.ReadLines(filePath, Encoding.UTF8).Take(30).ToList();
        return lines.Any(x => x.Contains("預り資産", StringComparison.Ordinal)) &&
               lines.Any(x => x.Contains("商品", StringComparison.Ordinal) &&
                              x.Contains("銘柄コード", StringComparison.Ordinal) &&
                              x.Contains("ティッカー", StringComparison.Ordinal) &&
                              x.Contains("保有数量", StringComparison.Ordinal));
    }

    private static int FindHeaderIndex(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("商品", StringComparison.Ordinal) &&
                lines[i].Contains("銘柄コード", StringComparison.Ordinal) &&
                lines[i].Contains("ティッカー", StringComparison.Ordinal) &&
                lines[i].Contains("銘柄名", StringComparison.Ordinal) &&
                lines[i].Contains("保有数量", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static BrokerHoldingRecord CreateHolding(IReadOnlyDictionary<string, string> row)
    {
        var product = Get(row, "商品");
        var currency = NormalizeCurrency(Get(row, "通貨"), product);
        var shares = ParseDecimal(Get(row, "保有数量"));
        var currentExchangeRate = currency == "JPY" ? 1m : ParseDecimal(Get(row, "評価レート"));
        if (currentExchangeRate <= 0m)
        {
            currentExchangeRate = currency == "JPY" ? 1m : 0m;
        }

        var purchaseExchangeRate = currency == "JPY" ? 1m : ParseDecimal(Get(row, "取得為替レート"));
        var acquisitionAmountJpy = ParseDecimal(Get(row, "取得金額（円）"));
        var averagePrice = ResolveAverageAcquisitionPrice(row, currency, shares, acquisitionAmountJpy, purchaseExchangeRate, currentExchangeRate);
        if (purchaseExchangeRate <= 0m && currency == "USD" && averagePrice > 0m && shares > 0m && acquisitionAmountJpy > 0m)
        {
            purchaseExchangeRate = acquisitionAmountJpy / (averagePrice * shares);
        }

        if (purchaseExchangeRate <= 0m)
        {
            purchaseExchangeRate = currentExchangeRate > 0m ? currentExchangeRate : 1m;
        }

        var currentPrice = currency == "JPY"
            ? ParseDecimal(Get(row, "現在値"))
            : ParseDecimal(Get(row, "参考時価"));
        var marketValueJpy = ParseDecimal(Get(row, "評価額（円）"));
        var marketValue = currency == "JPY"
            ? marketValueJpy
            : ParseDecimal(Get(row, "外貨評価額"));
        if (marketValue <= 0m && currentPrice > 0m && shares > 0m)
        {
            marketValue = currentPrice * shares;
        }

        var snapshotDate = ParseDateOrDefault(Get(row, "基準日"), DateTime.Today);
        var ticker = currency == "JPY" ? Get(row, "銘柄コード") : Get(row, "ティッカー");
        if (string.IsNullOrWhiteSpace(ticker))
        {
            ticker = Get(row, "銘柄コード");
        }

        ticker = SecuritySymbolNormalizer.NormalizeTicker(ticker);
        var name = SecuritySymbolNormalizer.NormalizeName(ticker, Get(row, "銘柄名").Replace("　", " ").Trim());

        return new BrokerHoldingRecord
        {
            Broker = "野村証券",
            Account = Get(row, "預り区分"),
            Product = product,
            Ticker = ticker,
            Name = name,
            Shares = shares,
            AverageAcquisitionPrice = averagePrice,
            CurrentPrice = currentPrice,
            AcquisitionAmount = currency == "JPY" ? acquisitionAmountJpy : averagePrice * shares,
            MarketValue = marketValue,
            MarketValueJpy = marketValueJpy,
            UnrealizedGainLossJpy = ParseDecimal(Get(row, "評価損益（円）")),
            Currency = currency,
            PurchaseExchangeRate = purchaseExchangeRate,
            CurrentExchangeRate = currentExchangeRate > 0m ? currentExchangeRate : purchaseExchangeRate,
            SnapshotDate = snapshotDate,
            Market = Get(row, "市場"),
            Source = "野村保有残高CSV"
        };
    }

    private static decimal ResolveAverageAcquisitionPrice(
        IReadOnlyDictionary<string, string> row,
        string currency,
        decimal shares,
        decimal acquisitionAmountJpy,
        decimal purchaseExchangeRate,
        decimal currentExchangeRate)
    {
        if (currency == "JPY")
        {
            var cost = ParseDecimal(Get(row, "取得コスト（円）"));
            if (cost > 0m)
            {
                return cost;
            }

            return shares > 0m ? acquisitionAmountJpy / shares : 0m;
        }

        var foreignPrice = ParseDecimal(Get(row, "買付単価（外貨）"));
        if (foreignPrice > 0m)
        {
            return foreignPrice;
        }

        var rate = purchaseExchangeRate > 0m ? purchaseExchangeRate : currentExchangeRate;
        if (shares > 0m && acquisitionAmountJpy > 0m && rate > 0m)
        {
            return acquisitionAmountJpy / shares / rate;
        }

        return 0m;
    }

    private static string NormalizeCurrency(string currency, string product)
    {
        if (string.Equals(currency.Trim(), "USD", StringComparison.OrdinalIgnoreCase) ||
            product.Contains("外株", StringComparison.Ordinal))
        {
            return "USD";
        }

        return "JPY";
    }

    private static Dictionary<string, string> ToDictionary(IReadOnlyList<string> headers, IReadOnlyList<string> values)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            row[headers[i]] = i < values.Count ? values[i] : string.Empty;
        }

        return row;
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim().Trim('"') : string.Empty;

    private static DateTime ParseDateOrDefault(string value, DateTime fallback) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : fallback;

    private static decimal ParseDecimal(string value)
    {
        var normalized = value
            .Replace(",", string.Empty)
            .Replace("+", string.Empty)
            .Replace("--", string.Empty)
            .Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString());
        return values;
    }
}
