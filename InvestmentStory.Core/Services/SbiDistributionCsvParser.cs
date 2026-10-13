using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class SbiDistributionCsvParser
{
    public IReadOnlyList<BrokerDividendRecord> Parse(string filePath) => ParseStatement(filePath).Dividends;

    public BrokerStatementImport ParseStatement(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CSVファイルが見つかりません。", filePath);
        }

        var lines = CsvFileEncoding.ReadAllRecords(filePath);
        var headerIndex = Array.FindIndex(lines, line =>
            line.Contains("受渡日", StringComparison.Ordinal) &&
            line.Contains("銘柄名", StringComparison.Ordinal) &&
            line.Contains("受取額", StringComparison.Ordinal));

        if (headerIndex < 0)
        {
            throw new InvalidOperationException("SBI配当CSVの明細ヘッダーが見つかりません。");
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers, "受渡日", "口座", "商品", "銘柄名", "受取額(税引後・円)", "数量");
        var records = new List<BrokerDividendRecord>();
        var unresolved = new List<BrokerUnresolvedDividend>();
        var rowAudit = new CsvSourceRowAuditBuilder();
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            rowAudit.Seen(index + 1);
            var values = ParseCsvLine(lines[index]);
            var row = CsvRowValidation.ToDictionary(headers, values, index + 1);
            var rawName = Get(row, "銘柄名");
            var (name, ticker) = SplitNameAndTicker(rawName);
            if (string.IsNullOrWhiteSpace(ticker))
            {
                unresolved.Add(new BrokerUnresolvedDividend
                {
                    PaymentDate = ParseDate(Get(row, "受渡日")), Broker = "SBI証券",
                    Name = rawName, Product = Get(row, "商品"),
                    Quantity = ParseDecimal(Get(row, "数量")),
                    Source = "SBI配当CSV", Reason = "銘柄名からティッカーを確定できません。"
                });
                rowAudit.Pending(index + 1, "銘柄名からティッカーを確定できません。");
                continue;
            }
            ticker = SecuritySymbolNormalizer.NormalizeTicker(ticker);
            name = SecuritySymbolNormalizer.NormalizeName(ticker, name);

            records.Add(new BrokerDividendRecord
            {
                Broker = "SBI証券",
                PaymentDate = ParseDate(Get(row, "受渡日")),
                Account = Get(row, "口座"),
                Product = Get(row, "商品"),
                Name = name,
                Ticker = ticker,
                Quantity = ParseDecimal(Get(row, "数量")),
                // The export contains net JPY receipts only. Gross and tax are unknown.
                GrossAmount = 0m,
                TaxAmount = 0m,
                NetAmount = ParseDecimal(Get(row, "受取額(税引後・円)")),
                Currency = "JPY",
                ExchangeRate = 1m,
                NetAmountJpy = ParseDecimal(Get(row, "受取額(税引後・円)")),
                Source = "SBI配当CSV",
                SourceFile = Path.GetFullPath(filePath),
                SourceRowNumber = index + 1
            });
            rowAudit.Parsed();
        }

        return new BrokerStatementImport
        {
            Broker = "SBI証券", Source = "SBI配当CSV", CanUpdateHoldings = false,
            Dividends = records, UnresolvedDividends = unresolved,
            IgnoredRowCount = unresolved.Count,
            SourceRowCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { [filePath] = records.Count },
            SourceRowAudits = new Dictionary<string, CsvSourceRowAudit>(StringComparer.OrdinalIgnoreCase)
            { [filePath] = rowAudit.Build() }
        };
    }

    public static bool LooksLikeSbiDistributionCsv(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.Contains("DISTRIBUTION", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!File.Exists(filePath))
        {
            return false;
        }

        return CsvFileEncoding.ReadLines(filePath).Take(12).Any(line =>
            line.Contains("受渡日", StringComparison.Ordinal) &&
            line.Contains("銘柄名", StringComparison.Ordinal) &&
            line.Contains("受取額", StringComparison.Ordinal));
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException($"日付を読み取れません: {value}");

    private static decimal ParseDecimal(string value)
    {
        var normalized = value.Replace(",", string.Empty).Trim();
        if (normalized.Length == 0) return 0m;
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidOperationException($"数値を読み取れません: {value}");
    }

    private static (string Name, string Ticker) SplitNameAndTicker(string rawName)
    {
        var text = rawName.Trim();
        var lastSpace = text.LastIndexOf(' ');
        if (lastSpace < 0 || lastSpace == text.Length - 1)
        {
            return (text, string.Empty);
        }

        var ticker = text[(lastSpace + 1)..].Trim().ToUpperInvariant();
        var name = text[..lastSpace].Trim();
        return ticker.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-')
            ? (name, ticker)
            : (text, string.Empty);
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
