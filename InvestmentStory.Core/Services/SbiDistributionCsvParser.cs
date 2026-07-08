using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class SbiDistributionCsvParser
{
    public IReadOnlyList<BrokerDividendRecord> Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CSVファイルが見つかりません。", filePath);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var lines = File.ReadAllLines(filePath, Encoding.GetEncoding(932));
        var headerIndex = Array.FindIndex(lines, line =>
            line.Contains("受渡日", StringComparison.Ordinal) &&
            line.Contains("銘柄名", StringComparison.Ordinal) &&
            line.Contains("受取額", StringComparison.Ordinal));

        if (headerIndex < 0)
        {
            throw new InvalidOperationException("SBI配当CSVの明細ヘッダーが見つかりません。");
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        var records = new List<BrokerDividendRecord>();
        foreach (var line in lines.Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var values = ParseCsvLine(line);
            var row = ToDictionary(headers, values);
            var rawName = Get(row, "銘柄名");
            var (name, ticker) = SplitNameAndTicker(rawName);
            if (string.IsNullOrWhiteSpace(ticker))
            {
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
                GrossAmount = ParseDecimal(Get(row, "受取額(税引後・円)")),
                TaxAmount = 0m,
                NetAmount = ParseDecimal(Get(row, "受取額(税引後・円)")),
                Currency = "JPY",
                ExchangeRate = 1m,
                NetAmountJpy = ParseDecimal(Get(row, "受取額(税引後・円)")),
                Source = "SBI配当CSV"
            });
        }

        return records;
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

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return File.ReadLines(filePath, Encoding.GetEncoding(932)).Take(12).Any(line =>
            line.Contains("受渡日", StringComparison.Ordinal) &&
            line.Contains("銘柄名", StringComparison.Ordinal) &&
            line.Contains("受取額", StringComparison.Ordinal));
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
        row.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException($"日付を読み取れません: {value}");

    private static decimal ParseDecimal(string value)
    {
        var normalized = value.Replace(",", string.Empty).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
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
