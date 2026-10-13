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

        var lines = CsvFileEncoding.ReadAllRecords(filePath);
        var headerIndex = FindHeaderIndex(lines);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException("野村證券保有残高CSVの明細ヘッダーが見つかりません。");
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers,
            "商品", "銘柄コード", "ティッカー", "銘柄名", "保有数量", "基準日");
        var holdings = new List<BrokerHoldingRecord>();
        var unresolvedHoldings = new List<BrokerHoldingRecord>();
        var rowAudit = new CsvSourceRowAuditBuilder();
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            rowAudit.Seen(index + 1);
            var row = CsvRowValidation.ToDictionary(headers, ParseCsvLine(lines[index]), index + 1,
                "商品", "銘柄コード", "ティッカー", "銘柄名", "保有数量", "基準日");
            BrokerHoldingRecord holding;
            try { holding = CreateHolding(row); }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"CSV {index + 1}行目: {exception.Message}", exception);
            }
            if (holding.Shares < 0m)
                throw new InvalidOperationException($"CSV {index + 1}行目・保有数量: 負の残高は取り込めません。");
            if (string.IsNullOrWhiteSpace(holding.Ticker))
            {
                unresolvedHoldings.Add(holding);
                rowAudit.Pending(index + 1, "銘柄コードを確定できません。");
            }
            else
            {
                holdings.Add(holding);
                rowAudit.Parsed();
            }
        }

        return new BrokerStatementImport
        {
            Broker = "野村証券",
            Source = "野村保有残高CSV",
            CanUpdateHoldings = holdings.Count > 0,
            Holdings = holdings,
            UnresolvedHoldings = unresolvedHoldings,
            SourceRowAudits = new Dictionary<string, CsvSourceRowAudit>(StringComparer.OrdinalIgnoreCase)
            { [filePath] = rowAudit.Build() }
        };
    }

    public static bool LooksLikeNomuraHoldingsCsv(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var lines = CsvFileEncoding.ReadLines(filePath).Take(30).ToList();
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
        var shares = ParseDecimal(row, "保有数量");
        var currentExchangeRate = currency == "JPY" ? 1m : ParseDecimal(row, "評価レート");
        if (currentExchangeRate <= 0m)
        {
            currentExchangeRate = currency == "JPY" ? 1m : 0m;
        }

        var purchaseExchangeRate = currency == "JPY" ? 1m : ParseDecimal(row, "取得為替レート");
        var acquisitionAmountJpy = ParseDecimal(row, "取得金額（円）");
        var averagePrice = ResolveAverageAcquisitionPrice(row, currency, shares, acquisitionAmountJpy, purchaseExchangeRate, currentExchangeRate);
        if (purchaseExchangeRate <= 0m && currency == "USD" && averagePrice > 0m && shares > 0m && acquisitionAmountJpy > 0m)
        {
            purchaseExchangeRate = acquisitionAmountJpy / (averagePrice * shares);
        }

        var currentPrice = currency == "JPY"
            ? ParseDecimal(row, "現在値")
            : ParseDecimal(row, "参考時価");
        var marketValueJpy = ParseDecimal(row, "評価額（円）");
        var marketValue = currency == "JPY"
            ? marketValueJpy
            : ParseDecimal(row, "外貨評価額");
        if (marketValue <= 0m && currentPrice > 0m && shares > 0m)
        {
            marketValue = currentPrice * shares;
        }

        var snapshotDate = ParseDateOrDefault(row, "基準日", DateTime.MinValue);
        var ticker = currency == "JPY" ? Get(row, "銘柄コード") : Get(row, "ティッカー");
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
            UnrealizedGainLossJpy = ParseDecimal(row, "評価損益（円）"),
            Currency = currency,
            PurchaseExchangeRate = purchaseExchangeRate,
            CurrentExchangeRate = currentExchangeRate,
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
            var cost = ParseDecimal(row, "取得コスト（円）");
            if (cost > 0m)
            {
                return cost;
            }

            return shares > 0m ? acquisitionAmountJpy / shares : 0m;
        }

        var foreignPrice = ParseDecimal(row, "買付単価（外貨）");
        if (foreignPrice > 0m)
        {
            return foreignPrice;
        }

        if (shares > 0m && acquisitionAmountJpy > 0m && purchaseExchangeRate > 0m)
        {
            return acquisitionAmountJpy / shares / purchaseExchangeRate;
        }

        return 0m;
    }

    private static string NormalizeCurrency(string currency, string product)
    {
        var explicitCurrency = currency.Trim().ToUpperInvariant();
        if (explicitCurrency is "円" or "YEN") return "JPY";
        if (explicitCurrency.Length > 0) return explicitCurrency;
        return product.Contains("外株", StringComparison.Ordinal) ? "USD" : "JPY";
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim().Trim('"') : string.Empty;

    private static DateTime ParseDateOrDefault(IReadOnlyDictionary<string, string> row, string column, DateTime fallback)
    {
        var value = Get(row, column);
        try { return ParseDateOrDefault(value, fallback); }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"{column}: {exception.Message}", exception);
        }
    }

    private static DateTime ParseDateOrDefault(string value, DateTime fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback :
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException($"日付を読み取れません: {value}");

    private static decimal ParseDecimal(string value)
    {
        var normalized = value
            .Replace(",", string.Empty)
            .Replace("+", string.Empty)
            .Replace("--", string.Empty)
            .Trim();
        if (normalized.Length == 0) return 0m;
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result : throw new InvalidOperationException($"数値を読み取れません: {value}");
    }

    private static decimal ParseDecimal(IReadOnlyDictionary<string, string> row, string column)
    {
        try { return ParseDecimal(Get(row, column)); }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"{column}: {exception.Message}", exception);
        }
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
