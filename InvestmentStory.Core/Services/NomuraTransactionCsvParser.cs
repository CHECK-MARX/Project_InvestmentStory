using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class NomuraTransactionCsvParser
{
    private static readonly IReadOnlyDictionary<string, string> ForeignNameToTicker =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [NormalizeName("アツプル　インク")] = "AAPL",
            [NormalizeName("エ－テイ－アンドテイ－　インク")] = "T",
            [NormalizeName("エヌビデイア　コ－プ")] = "NVDA",
            [NormalizeName("コカコ－ラ")] = "KO",
            [NormalizeName("シスコ　システムズ")] = "CSCO",
            [NormalizeName("ステランテイス　エヌブイ")] = "STLA",
            [NormalizeName("ダナハ－　コ－プ")] = "DHR",
            [NormalizeName("ビヨンド　ミート　インク")] = "BYND",
            [NormalizeName("プロクタ－　アンド　ギヤンブル（Ｐ＆Ｇ）")] = "PG",
            [NormalizeName("メタ　プラツトフオームズ　クラス　Ａ")] = "META",
            [NormalizeName("クラウドストライク　ホ－ルデイングス　イン")] = "CRWD",
            [NormalizeName("テスラ　インク")] = "TSLA",
            [NormalizeName("ワ－ナ－　ブロス　デイスカバリ－　インク")] = "WBD"
        };

    public BrokerStatementImport Parse(string filePath)
    {
        var rows = ReadRows(filePath);
        var dividends = new List<BrokerDividendRecord>();
        var trades = new List<BrokerTradeRecord>();
        var unresolvedDividends = new List<BrokerUnresolvedDividend>();
        var unresolvedTrades = new List<BrokerTradeRecord>();
        var ignoredRows = 0;
        var rowAudit = new CsvSourceRowAuditBuilder();

        foreach (var row in rows)
        {
            var rowNumber = int.Parse(row["__SourceRowNumber"], CultureInfo.InvariantCulture);
            rowAudit.Seen(rowNumber);
            var tradeType = Get(row, "取引区分");
            if (string.Equals(tradeType, "入金（配当金）", StringComparison.Ordinal))
            {
                var dividend = CreateDividendRecord(row, Path.GetFullPath(filePath));
                if (!string.IsNullOrWhiteSpace(dividend.Ticker) &&
                    (dividend.Currency == "JPY" || dividend.ExchangeRate > 0m))
                {
                    dividends.Add(dividend);
                    rowAudit.Parsed();
                }
                else
                {
                    unresolvedDividends.Add(new BrokerUnresolvedDividend
                    {
                        PaymentDate = dividend.PaymentDate,
                        Broker = dividend.Broker,
                        Name = dividend.Name,
                        Product = dividend.Product,
                        Quantity = dividend.Quantity,
                        GrossAmount = dividend.GrossAmount,
                        TaxAmount = dividend.TaxAmount,
                        Source = dividend.Source,
                        Reason = string.IsNullOrWhiteSpace(dividend.Ticker)
                            ? "銘柄コードがなく、名称からティッカーを確定できません。"
                            : "外貨配当の為替レートを確定できません。"
                    });
                    ignoredRows++;
                    rowAudit.Pending(rowNumber, string.IsNullOrWhiteSpace(dividend.Ticker)
                        ? "銘柄コードを確定できません。" : "為替レートを確定できません。");
                }

                continue;
            }

            if (IsPortfolioTradeType(tradeType))
            {
                var trade = CreateTradeRecord(row);
                if (!string.IsNullOrWhiteSpace(trade.Ticker))
                {
                    trades.Add(trade);
                    rowAudit.Parsed();
                }
                else
                {
                    unresolvedTrades.Add(trade);
                    ignoredRows++;
                    rowAudit.Pending(rowNumber, "銘柄コードを確定できません。");
                }

                continue;
            }

            ignoredRows++;
            rowAudit.Excluded(rowNumber, $"投資成績の対象外の取引区分: {tradeType}");
        }

        return new BrokerStatementImport
        {
            Broker = "野村証券",
            Source = "野村取引履歴CSV",
            CanUpdateHoldings = trades.Count > 0,
            Dividends = dividends,
            Trades = trades,
            IgnoredRowCount = ignoredRows,
            UnresolvedDividends = unresolvedDividends,
            UnresolvedTrades = unresolvedTrades,
            SourceRowAudits = new Dictionary<string, CsvSourceRowAudit>(StringComparer.OrdinalIgnoreCase)
            { [filePath] = rowAudit.Build() }
        };
    }

    public IReadOnlyList<BrokerDividendRecord> ParseDividends(string filePath) => Parse(filePath).Dividends;

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CSVファイルが見つかりません。", filePath);
        }

        var lines = CsvFileEncoding.ReadAllRecords(filePath);
        var headerIndex = Array.FindIndex(lines, line =>
            line.Contains("約定日", StringComparison.Ordinal) &&
            line.Contains("受渡日", StringComparison.Ordinal) &&
            line.Contains("取引区分", StringComparison.Ordinal));

        if (headerIndex < 0)
        {
            throw new InvalidOperationException("野村證券取引履歴CSVの明細ヘッダーが見つかりません。");
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers, "約定日", "受渡日", "取引区分", "商品",
            "銘柄コード", "銘柄名", "発行通貨", "数量", "単価", "受渡金額/決済損益", "レート");
        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            var values = ParseCsvLine(lines[index]);
            var row = CsvRowValidation.ToDictionary(headers, values, index + 1);
            row["__SourceRowNumber"] = (index + 1).ToString(CultureInfo.InvariantCulture);
            rows.Add(row);
        }

        return rows;
    }

    private static BrokerDividendRecord CreateDividendRecord(IReadOnlyDictionary<string, string> row, string fileName)
    {
        var product = Get(row, "商品");
        var rawName = Get(row, "銘柄名");
        var ticker = ResolveTicker(Get(row, "銘柄コード"), rawName);
        var issueCurrency = Get(row, "発行通貨");
        var quantity = ParseDecimal(Get(row, "数量"));
        var unitDividend = ParseDecimal(Get(row, "単価"));
        var netJpy = ParseDecimal(Get(row, "受渡金額/決済損益"));
        var rate = ParseDecimal(Get(row, "レート"));
        var currency = ResolveCurrency(issueCurrency);
        var gross = quantity * unitDividend;
        var exchangeRate = currency == "JPY" ? 1m : rate > 0m ? rate : 0m;
        var net = currency != "JPY" && exchangeRate > 0m
            ? decimal.Round(netJpy / exchangeRate, 6)
            : currency == "JPY" ? netJpy : 0m;
        var tax = exchangeRate > 0m ? Math.Max(0m, gross - net) : 0m;

        return new BrokerDividendRecord
        {
            Broker = "野村証券",
            PaymentDate = ParseDate(Get(row, "受渡日")),
            Account = Get(row, "預り区分"),
            Product = product,
            Name = SecuritySymbolNormalizer.NormalizeName(ticker, NormalizeDisplayName(rawName)),
            Ticker = ticker,
            Quantity = quantity,
            GrossAmount = gross,
            TaxAmount = tax,
            NetAmount = net,
            Currency = currency,
            ExchangeRate = exchangeRate,
            NetAmountJpy = netJpy,
            Source = "野村取引履歴CSV",
            SourceFile = fileName,
            SourceRowNumber = int.Parse(Get(row, "__SourceRowNumber"), CultureInfo.InvariantCulture)
        };
    }

    private static BrokerTradeRecord CreateTradeRecord(IReadOnlyDictionary<string, string> row)
    {
        var product = Get(row, "商品");
        var rawName = Get(row, "銘柄名");
        var ticker = ResolveTicker(Get(row, "銘柄コード"), rawName);
        var tradeType = Get(row, "取引区分");
        var issueCurrency = Get(row, "発行通貨");
        var quantity = ParseDecimal(Get(row, "数量"));
        var rate = ParseDecimal(Get(row, "レート"));
        var currency = ResolveCurrency(issueCurrency);
        var exchangeRate = currency == "JPY" ? 1m : rate > 0m ? rate : 0m;

        return new BrokerTradeRecord
        {
            Broker = "野村証券",
            TradeDate = ParseDate(Get(row, "約定日")),
            SettlementDate = ParseDate(Get(row, "受渡日")),
            Account = Get(row, "預り区分"),
            Product = product,
            TradeType = tradeType,
            Name = SecuritySymbolNormalizer.NormalizeName(ticker, NormalizeDisplayName(rawName)),
            Ticker = ticker,
            Quantity = quantity,
            SignedQuantity = GetSignedQuantity(tradeType, quantity),
            UnitPrice = ParseDecimal(Get(row, "単価")),
            SettlementAmountJpy = ParseDecimal(Get(row, "受渡金額/決済損益")),
            FeeJpy = ParseDecimal(Get(row, "手数料（税込）")),
            ExchangeRate = exchangeRate,
            Currency = currency,
            ProfitLossJpy = ParseDecimal(Get(row, "売買損益（円）")),
            Source = "野村取引履歴CSV"
        };
    }

    public static bool LooksLikeNomuraTransactionCsv(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var lines = CsvFileEncoding.ReadLines(filePath).Take(10).ToList();
        return lines.Any(x => x.Contains("取引履歴", StringComparison.Ordinal)) &&
               lines.Any(x => x.Contains("約定日", StringComparison.Ordinal) &&
                              x.Contains("取引区分", StringComparison.Ordinal));
    }

    internal static string ResolveTicker(string code, string rawName)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return SecuritySymbolNormalizer.NormalizeTicker(code);
        }

        return ForeignNameToTicker.TryGetValue(NormalizeName(rawName), out var ticker)
            ? SecuritySymbolNormalizer.NormalizeTicker(ticker)
            : string.Empty;
    }

    private static bool IsPortfolioTradeType(string tradeType) =>
        string.Equals(tradeType, "現物買付", StringComparison.Ordinal) ||
        string.Equals(tradeType, "現物売却", StringComparison.Ordinal) ||
        string.Equals(tradeType, "入庫（増減資）", StringComparison.Ordinal);

    private static decimal GetSignedQuantity(string tradeType, decimal quantity)
    {
        if (string.Equals(tradeType, "現物売却", StringComparison.Ordinal))
        {
            return -quantity;
        }

        return quantity;
    }

    private static string ResolveCurrency(string issueCurrency) =>
        string.IsNullOrWhiteSpace(issueCurrency) ? "JPY" : issueCurrency.Trim().ToUpperInvariant() switch
        {
            "円" or "YEN" => "JPY",
            var currency => currency
        };

    private static string NormalizeDisplayName(string rawName) =>
        rawName.Trim().Replace("　", " ");

    private static string NormalizeName(string rawName) =>
        rawName.Trim()
            .Replace(" ", string.Empty)
            .Replace("　", string.Empty)
            .Replace("－", "-")
            .ToUpperInvariant();

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException($"日付を読み取れません: {value}");

    private static decimal ParseDecimal(string value)
    {
        var normalized = value.Replace(",", string.Empty).Trim();
        if (normalized.Length == 0 || normalized == "--") return 0m;
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidOperationException($"数値を読み取れません: {value}");
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
