using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class NomuraDividendCsvParser
{
    public static bool LooksLikeNomuraDividendCsv(string filePath) =>
        File.Exists(filePath) && CsvFileEncoding.ReadLines(filePath).Take(40)
            .Any(x => x.Contains("交付日", StringComparison.Ordinal) &&
                      x.Contains("配当等の額", StringComparison.Ordinal) &&
                      x.Contains("源泉徴収税額", StringComparison.Ordinal));

    public BrokerStatementImport Parse(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("CSVファイルが見つかりません。", filePath);
        var lines = CsvFileEncoding.ReadAllRecords(filePath);
        var headerIndex = Array.FindIndex(lines, x => x.Contains("交付日", StringComparison.Ordinal) &&
            x.Contains("配当等の額", StringComparison.Ordinal) && x.Contains("源泉徴収税額", StringComparison.Ordinal));
        if (headerIndex < 0) throw new InvalidOperationException("野村配当履歴CSVの明細ヘッダーが見つかりません。");
        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers, "交付日", "配当等の額", "源泉徴収税額",
            "銘柄コード", "銘柄名", "商品", "数量");
        CsvRowValidation.ValidateKnownHeaders(headers, "NomuraDividends-v1",
            "交付日", "商品", "銘柄コード", "銘柄名", "配当等の単価", "数量",
            "配当等の額", "加算対象額", "源泉徴収税額", "上場株式配当等控除額",
            "元本払戻金（特別分配金）");
        var dividends = new List<BrokerDividendRecord>();
        var unresolved = new List<BrokerUnresolvedDividend>();
        var ignored = 0;
        var rowAudit = new CsvSourceRowAuditBuilder();
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            rowAudit.Seen(index + 1);
            var values = ParseCsvLine(lines[index]);
            var row = CsvRowValidation.ToDictionary(headers, values, index + 1,
                "交付日", "配当等の額", "源泉徴収税額", "銘柄コード", "銘柄名", "商品", "数量");
            if (!DateTime.TryParse(row["交付日"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException($"CSV {index + 1}行目・交付日: 日付が不正です: {row["交付日"]}");
            var ticker = NomuraTransactionCsvParser.ResolveTicker(row["銘柄コード"], row["銘柄名"]);
            if (string.IsNullOrWhiteSpace(ticker))
            {
                var isMrf = row["商品"] == "投信" &&
                    (row["銘柄名"].Contains("ＭＲＦ", StringComparison.OrdinalIgnoreCase) ||
                     row["銘柄名"].Contains("MRF", StringComparison.OrdinalIgnoreCase));
                var reason = isMrf
                    ? "MRF分配金です。銘柄コードと口座が特定できないため、自動加算せず保留します。"
                    : "銘柄コードを確定できません。";
                ignored++;
                unresolved.Add(new BrokerUnresolvedDividend
                    {
                        PaymentDate = date, Broker = "野村証券", Name = row["銘柄名"],
                        Product = row["商品"], Quantity = ParseDecimal(row["数量"]),
                        GrossAmount = ParseDecimal(row["配当等の額"]),
                        TaxAmount = ParseDecimal(row["源泉徴収税額"]),
                        Source = "野村配当履歴CSV", Reason = reason
                    });
                rowAudit.Pending(index + 1, reason);
                continue;
            }
            var gross = ParseDecimal(row["配当等の額"]);
            var tax = ParseDecimal(row["源泉徴収税額"]);
            dividends.Add(new BrokerDividendRecord
            {
                Broker = "野村証券", PaymentDate = date, Product = row["商品"],
                Ticker = ticker, Name = row["銘柄名"], Quantity = ParseDecimal(row["数量"]),
                GrossAmount = gross, TaxAmount = tax, NetAmount = gross - tax,
                Currency = "JPY", ExchangeRate = 1m, NetAmountJpy = gross - tax,
                Source = "野村配当履歴CSV（口座区分不明）",
                SourceFile = Path.GetFullPath(filePath), SourceRowNumber = index + 1
            });
            rowAudit.Parsed();
        }
        return new BrokerStatementImport
        {
            Broker = "野村証券", Source = "野村配当履歴CSV", CanUpdateHoldings = false,
            Dividends = dividends, IgnoredRowCount = ignored,
            UnresolvedDividends = unresolved,
            SourceRowAudits = new Dictionary<string, CsvSourceRowAudit>(StringComparer.OrdinalIgnoreCase)
            { [filePath] = rowAudit.Build() },
            SourceFormatVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { [filePath] = "NomuraDividends-v1" }
        };
    }

    private static decimal ParseDecimal(string value)
    {
        var normalized = value.Replace(",", string.Empty).Trim();
        if (normalized.Length == 0) return 0m;
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number : throw new InvalidOperationException($"数値を読み取れません: {value}");
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (line[i] == ',' && !quoted) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(line[i]);
        }
        result.Add(current.ToString());
        return result;
    }
}
