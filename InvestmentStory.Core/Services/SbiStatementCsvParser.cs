using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class SbiStatementCsvParser
{
    private readonly SbiDistributionCsvParser _distributionParser = new();

    public BrokerStatementImport ParseFromSeedFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CSVファイルが見つかりません。", filePath);
        }

        return ParseFiles(DiscoverSbiFiles(filePath));
    }

    public BrokerStatementImport ParseFiles(IEnumerable<string> filePaths)
    {
        var files = filePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var holdings = new List<BrokerHoldingRecord>();
        var dividends = new List<BrokerDividendRecord>();
        var trades = new List<BrokerTradeRecord>();
        var ignoredRows = 0;

        foreach (var file in files)
        {
            if (SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(file))
            {
                dividends.AddRange(_distributionParser.Parse(file));
                continue;
            }

            var lines = ReadLines(file);
            if (LooksLikeJapanStockHoldings(lines))
            {
                holdings.AddRange(ParseJapanStockAndFundHoldings(lines));
                continue;
            }

            if (LooksLikeForeignStockTrades(lines))
            {
                trades.AddRange(ParseForeignStockTrades(lines));
                continue;
            }

            if (LooksLikeDomesticOrFundTrades(lines))
            {
                trades.AddRange(ParseDomesticOrFundTrades(lines));
                continue;
            }

            ignoredRows += Math.Max(0, lines.Count - 1);
        }

        return new BrokerStatementImport
        {
            Broker = "SBI証券",
            Source = "SBI一括CSV",
            CanUpdateHoldings = holdings.Count > 0 || trades.Count > 0,
            Holdings = holdings,
            Dividends = dividends,
            Trades = trades,
            IgnoredRowCount = ignoredRows
        };
    }

    public static bool LooksLikeSbiStatementCsv(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var lines = ReadLines(filePath).Take(40).ToList();
        return LooksLikeJapanStockHoldings(lines) ||
               LooksLikeForeignStockTrades(lines) ||
               LooksLikeSbiDomesticOrFundTrades(lines);
    }

    private static IReadOnlyList<string> DiscoverSbiFiles(string seedFilePath)
    {
        var directory = Path.GetDirectoryName(seedFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new[] { seedFilePath };
        }

        var sbiFiles = Directory.GetFiles(directory, "SBI*.csv")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return sbiFiles.Count > 0 ? sbiFiles : new[] { seedFilePath };
    }

    private static IReadOnlyList<BrokerTradeRecord> ParseForeignStockTrades(IReadOnlyList<string> lines)
    {
        var headerIndex = FindHeaderIndex(lines, "国内約定日", "銘柄名", "約定数量");
        if (headerIndex < 0)
        {
            return Array.Empty<BrokerTradeRecord>();
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        var records = new List<BrokerTradeRecord>();
        foreach (var line in lines.Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var row = ToDictionary(headers, ParseCsvLine(line));
            var rawName = Get(row, "銘柄名");
            var (name, ticker) = SplitSbiForeignName(rawName);
            if (string.IsNullOrWhiteSpace(ticker))
            {
                continue;
            }
            ticker = SecuritySymbolNormalizer.NormalizeTicker(ticker);
            name = SecuritySymbolNormalizer.NormalizeName(ticker, name);

            var quantity = ParseDecimal(Get(row, "約定数量"));
            var unitPrice = ParseDecimal(Get(row, "約定単価"));
            var settlementAmount = ParseDecimal(Get(row, "受渡金額"));
            var settlementCurrency = Get(row, "通貨");
            var securityCurrency = rawName.Contains("/ NASDAQ", StringComparison.OrdinalIgnoreCase) ||
                                   rawName.Contains("New York Stock Exchange", StringComparison.OrdinalIgnoreCase)
                ? "USD"
                : ResolveCurrency(settlementCurrency);
            var exchangeRate = securityCurrency == "USD" &&
                               settlementCurrency.Contains("日本円", StringComparison.Ordinal) &&
                               quantity > 0m &&
                               unitPrice > 0m
                ? settlementAmount / (quantity * unitPrice)
                : 0m;
            var tradeType = NormalizeTradeType(Get(row, "取引"));

            records.Add(new BrokerTradeRecord
            {
                Broker = "SBI証券",
                TradeDate = ParseJapaneseDate(Get(row, "国内約定日")),
                SettlementDate = ParseShortDate(Get(row, "国内受渡日")),
                Account = Get(row, "預り区分"),
                Product = "米国株式",
                TradeType = tradeType,
                Ticker = ticker,
                Name = name,
                Quantity = quantity,
                SignedQuantity = IsSellTrade(tradeType) ? -quantity : quantity,
                UnitPrice = unitPrice,
                SettlementAmountJpy = securityCurrency == "JPY" ? settlementAmount : 0m,
                ExchangeRate = exchangeRate,
                Currency = securityCurrency,
                Source = "SBI米国株約定CSV"
            });
        }

        return records;
    }

    private static IReadOnlyList<BrokerHoldingRecord> ParseJapanStockAndFundHoldings(IReadOnlyList<string> lines)
    {
        var records = new List<BrokerHoldingRecord>();
        foreach (var headerIndex in FindHeaderIndexes(lines, "銘柄コード", "銘柄名称", "保有株数", "評価額"))
        {
            var account = ResolvePreviousSectionTitle(lines, headerIndex);
            var headers = ParseCsvLine(lines[headerIndex]);
            foreach (var line in lines.Skip(headerIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                var row = ToDictionary(headers, ParseCsvLine(line));
                var ticker = Get(row, "銘柄コード");
                var name = Get(row, "銘柄名称");
                var shares = ParseDecimal(Get(row, "保有株数"));
                if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(name) || shares <= 0m)
                {
                    continue;
                }

                records.Add(new BrokerHoldingRecord
                {
                    Broker = "SBI証券",
                    Account = account,
                    Product = "国内株式",
                    Ticker = SecuritySymbolNormalizer.NormalizeTicker(ticker),
                    Name = name.Trim(),
                    Shares = shares,
                    AverageAcquisitionPrice = ParseDecimal(Get(row, "取得単価")),
                    CurrentPrice = ParseDecimal(Get(row, "現在値")),
                    AcquisitionAmount = ParseDecimal(Get(row, "取得金額")),
                    MarketValue = ParseDecimal(Get(row, "評価額")),
                    MarketValueJpy = ParseDecimal(Get(row, "評価額")),
                    UnrealizedGainLossJpy = ParseDecimal(Get(row, "評価損益")),
                    Currency = "JPY",
                    PurchaseExchangeRate = 1m,
                    CurrentExchangeRate = 1m,
                    SnapshotDate = DateTime.Today,
                    Source = "SBI日本株保有CSV"
                });
            }
        }

        foreach (var headerIndex in FindHeaderIndexes(lines, "ファンド名", "保有口数", "基準価額", "評価額"))
        {
            var account = ResolvePreviousSectionTitle(lines, headerIndex);
            var headers = ParseCsvLine(lines[headerIndex]);
            foreach (var line in lines.Skip(headerIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                var row = ToDictionary(headers, ParseCsvLine(line));
                var name = Get(row, "ファンド名");
                var shares = ParseDecimal(Get(row, "保有口数"));
                if (string.IsNullOrWhiteSpace(name) || shares <= 0m)
                {
                    continue;
                }

                var acquisitionAmount = ParseDecimal(Get(row, "取得金額"));
                var marketValue = ParseDecimal(Get(row, "評価額"));
                records.Add(new BrokerHoldingRecord
                {
                    Broker = "SBI証券",
                    Account = account,
                    Product = "投資信託",
                    Ticker = SecuritySymbolNormalizer.NormalizeTicker(ResolveDomesticTicker(string.Empty, name)),
                    Name = name.Trim(),
                    Shares = shares,
                    AverageAcquisitionPrice = shares > 0m ? acquisitionAmount / shares : ParseDecimal(Get(row, "取得単価")) / 10000m,
                    CurrentPrice = shares > 0m ? marketValue / shares : ParseDecimal(Get(row, "基準価額")) / 10000m,
                    AcquisitionAmount = acquisitionAmount,
                    MarketValue = marketValue,
                    MarketValueJpy = marketValue,
                    UnrealizedGainLossJpy = ParseDecimal(Get(row, "評価損益")),
                    Currency = "JPY",
                    PurchaseExchangeRate = 1m,
                    CurrentExchangeRate = 1m,
                    SnapshotDate = DateTime.Today,
                    Source = "SBI投信保有CSV"
                });
            }
        }

        return AggregateHoldings(records);
    }

    private static IReadOnlyList<BrokerTradeRecord> ParseDomesticOrFundTrades(IReadOnlyList<string> lines)
    {
        var headerIndex = FindHeaderIndex(lines, "約定日", "銘柄", "約定数量");
        if (headerIndex < 0)
        {
            return Array.Empty<BrokerTradeRecord>();
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        var records = new List<BrokerTradeRecord>();
        foreach (var line in lines.Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var row = ToDictionary(headers, ParseCsvLine(line));
            var name = Get(row, "銘柄");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var quantity = ParseDecimal(Get(row, "約定数量"));
            var amount = ParseDecimal(Get(row, "受渡金額/決済損益"));
            var unitPrice = ParseDecimal(Get(row, "約定単価"));
            if (Get(row, "取引").Contains("投信", StringComparison.Ordinal) && quantity > 0m && amount > 0m)
            {
                unitPrice = amount / quantity;
            }

            var tradeType = NormalizeTradeType(Get(row, "取引"));
            records.Add(new BrokerTradeRecord
            {
                Broker = "SBI証券",
                TradeDate = ParseDate(Get(row, "約定日")),
                SettlementDate = ParseDate(Get(row, "受渡日")),
                Account = Get(row, "預り"),
                Product = Get(row, "取引").Contains("投信", StringComparison.Ordinal) ? "投資信託" : "国内株式",
                TradeType = tradeType,
                Ticker = SecuritySymbolNormalizer.NormalizeTicker(ResolveDomesticTicker(Get(row, "銘柄コード"), name)),
                Name = name,
                Quantity = quantity,
                SignedQuantity = IsSellTrade(tradeType) ? -quantity : quantity,
                UnitPrice = unitPrice,
                SettlementAmountJpy = amount,
                FeeJpy = ParseDecimal(Get(row, "手数料/諸経費等")),
                ExchangeRate = 1m,
                Currency = "JPY",
                Source = "SBI国内約定CSV"
            });
        }

        return records;
    }

    private static bool LooksLikeForeignStockTrades(IReadOnlyList<string> lines) =>
        lines.Any(line => line.Contains("国内約定日", StringComparison.Ordinal) &&
                          line.Contains("通貨", StringComparison.Ordinal) &&
                          line.Contains("受渡金額", StringComparison.Ordinal));

    private static bool LooksLikeJapanStockHoldings(IReadOnlyList<string> lines) =>
        lines.Any(line => line.Contains("保有証券一覧", StringComparison.Ordinal)) &&
        (lines.Any(line => line.Contains("銘柄コード", StringComparison.Ordinal) &&
                           line.Contains("銘柄名称", StringComparison.Ordinal) &&
                           line.Contains("保有株数", StringComparison.Ordinal) &&
                           line.Contains("評価額", StringComparison.Ordinal)) ||
         lines.Any(line => line.Contains("ファンド名", StringComparison.Ordinal) &&
                           line.Contains("保有口数", StringComparison.Ordinal) &&
                           line.Contains("基準価額", StringComparison.Ordinal) &&
                           line.Contains("評価額", StringComparison.Ordinal)));

    private static bool LooksLikeDomesticOrFundTrades(IReadOnlyList<string> lines) =>
        lines.Any(line => line.Contains("約定履歴照会", StringComparison.Ordinal)) ||
        lines.Any(line => line.Contains("約定日", StringComparison.Ordinal) &&
                          line.Contains("銘柄コード", StringComparison.Ordinal) &&
                          line.Contains("受渡金額/決済損益", StringComparison.Ordinal));

    private static bool LooksLikeSbiDomesticOrFundTrades(IReadOnlyList<string> lines) =>
        lines.Any(line => line.Contains("約定履歴照会", StringComparison.Ordinal)) &&
        lines.Any(line => line.Contains("約定日", StringComparison.Ordinal) &&
                          line.Contains("銘柄", StringComparison.Ordinal) &&
                          line.Contains("約定数量", StringComparison.Ordinal) &&
                          line.Contains("手数料/諸経費等", StringComparison.Ordinal));

    private static IReadOnlyList<string> ReadLines(string filePath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return File.ReadAllLines(filePath, Encoding.GetEncoding(932));
    }

    private static int FindHeaderIndex(IReadOnlyList<string> lines, params string[] requiredColumns)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (requiredColumns.All(column => lines[i].Contains(column, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<int> FindHeaderIndexes(IReadOnlyList<string> lines, params string[] requiredColumns)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (requiredColumns.All(column => lines[i].Contains(column, StringComparison.Ordinal)))
            {
                yield return i;
            }
        }
    }

    private static IReadOnlyList<BrokerHoldingRecord> AggregateHoldings(IReadOnlyList<BrokerHoldingRecord> records)
    {
        var aggregated = new List<BrokerHoldingRecord>();
        foreach (var group in records.GroupBy(x => $"{x.Broker}|{x.Ticker}", StringComparer.OrdinalIgnoreCase))
        {
            var holdings = group.ToList();
            if (holdings.Count == 1)
            {
                aggregated.Add(holdings[0]);
                continue;
            }

            var shares = holdings.Sum(x => x.Shares);
            var acquisitionAmount = holdings.Sum(x => x.AcquisitionAmount);
            var marketValue = holdings.Sum(x => x.MarketValue);
            var marketValueJpy = holdings.Sum(x => x.MarketValueJpy);
            var first = holdings[0];
            aggregated.Add(new BrokerHoldingRecord
            {
                Broker = first.Broker,
                Account = "複数口座合算",
                Product = first.Product,
                Ticker = first.Ticker,
                Name = first.Name,
                Shares = shares,
                AverageAcquisitionPrice = shares > 0m ? acquisitionAmount / shares : 0m,
                CurrentPrice = shares > 0m ? marketValue / shares : 0m,
                AcquisitionAmount = acquisitionAmount,
                MarketValue = marketValue,
                MarketValueJpy = marketValueJpy,
                UnrealizedGainLossJpy = holdings.Sum(x => x.UnrealizedGainLossJpy),
                Currency = first.Currency,
                PurchaseExchangeRate = 1m,
                CurrentExchangeRate = 1m,
                SnapshotDate = holdings.Max(x => x.SnapshotDate),
                Source = first.Source
            });
        }

        return aggregated;
    }

    private static string ResolvePreviousSectionTitle(IReadOnlyList<string> lines, int headerIndex)
    {
        for (var i = headerIndex - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!string.IsNullOrWhiteSpace(line) &&
                !line.Contains(",", StringComparison.Ordinal) &&
                !line.EndsWith("合計", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return string.Empty;
    }

    private static string NormalizeTradeType(string value)
    {
        if (value.Contains("売", StringComparison.Ordinal))
        {
            return "現物売却";
        }

        if (value.Contains("買", StringComparison.Ordinal))
        {
            return "現物買付";
        }

        return value;
    }

    private static bool IsSellTrade(string tradeType) =>
        tradeType.Contains("売", StringComparison.Ordinal);

    private static string ResolveCurrency(string value) =>
        value.Contains("米国ドル", StringComparison.Ordinal) || value.Equals("USD", StringComparison.OrdinalIgnoreCase)
            ? "USD"
            : "JPY";

    private static string ResolveDomesticTicker(string code, string name)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code.Trim().ToUpperInvariant();
        }

        return $"FUND:{name.Trim()}";
    }

    private static (string Name, string Ticker) SplitSbiForeignName(string rawName)
    {
        var left = rawName.Split('/')[0].Trim();
        var lastSpace = left.LastIndexOf(' ');
        if (lastSpace < 0 || lastSpace == left.Length - 1)
        {
            return (left, string.Empty);
        }

        var ticker = left[(lastSpace + 1)..].Trim().ToUpperInvariant();
        var name = left[..lastSpace].Trim();
        return ticker.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-')
            ? (name, ticker)
            : (left, string.Empty);
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

    private static DateTime ParseJapaneseDate(string value) =>
        DateTime.TryParseExact(value, "yyyy年MM月dd日", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : ParseDate(value);

    private static DateTime ParseShortDate(string value)
    {
        if (DateTime.TryParseExact(value, "yy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return ParseDate(value);
    }

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException($"日付を読み取れません: {value}");

    private static decimal ParseDecimal(string value)
    {
        var normalized = value
            .Replace(",", string.Empty)
            .Replace("+", string.Empty)
            .Replace("口", string.Empty)
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
