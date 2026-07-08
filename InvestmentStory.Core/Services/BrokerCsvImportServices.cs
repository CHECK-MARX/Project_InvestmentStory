using InvestmentStory.Core.Models;
using System.Text;

namespace InvestmentStory.Core.Services;

public abstract class BrokerCsvImportServiceBase : IBrokerCsvImportService
{
    public abstract string ProviderName { get; }
    public bool IsReadOnly => true;

    public BrokerCsvPreview Preview(string filePath, string csvType)
    {
        if (!File.Exists(filePath))
        {
            return new BrokerCsvPreview { Logs = new[] { "CSVファイルが見つかりません。" } };
        }

        var rows = ReadCsv(filePath).ToList();
        if (rows.Count == 0)
        {
            return new BrokerCsvPreview { Logs = new[] { "CSVにデータがありません。" } };
        }

        var columns = rows[0];
        var previewRows = rows.Skip(1)
            .Take(20)
            .Select(values => ToDictionary(columns, values))
            .Cast<IReadOnlyDictionary<string, string>>()
            .ToList();

        return new BrokerCsvPreview
        {
            Columns = columns,
            Rows = previewRows,
            Logs = new[] { $"{ProviderName} / {csvType}: {previewRows.Count}件をプレビューしました。" }
        };
    }

    public BrokerCsvImportResult Import(string filePath, string csvType, IReadOnlyDictionary<string, string> columnMappings)
    {
        if (!File.Exists(filePath))
        {
            return new BrokerCsvImportResult { IsSuccess = false, Logs = new[] { "CSVファイルが見つかりません。" } };
        }

        var rows = ReadCsv(filePath).ToList();
        if (rows.Count <= 1)
        {
            return new BrokerCsvImportResult { IsSuccess = false, Logs = new[] { "取込対象データがありません。" } };
        }

        var columns = rows[0];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var duplicates = 0;

        foreach (var values in rows.Skip(1))
        {
            var row = ToDictionary(columns, values);
            var key = BuildDuplicateKey(csvType, row, columnMappings);
            if (!seen.Add(key))
            {
                duplicates++;
                continue;
            }

            imported++;
        }

        return new BrokerCsvImportResult
        {
            IsSuccess = true,
            ImportedCount = imported,
            DuplicateCount = duplicates,
            Logs = new[]
            {
                $"{ProviderName} / {csvType}: {imported}件を検証しました。",
                $"重複候補: {duplicates}件。",
                "この版ではCSVのプレビューと重複検証まで行います。DB反映は列マッピング確定後に拡張します。"
            }
        };
    }

    private static string BuildDuplicateKey(
        string csvType,
        IReadOnlyDictionary<string, string> row,
        IReadOnlyDictionary<string, string> mappings)
    {
        var ticker = GetMappedValue(row, mappings, "銘柄コード");
        var date = GetMappedValue(row, mappings, csvType == "配当履歴" ? "入金日" : "取引日");
        var quantity = GetMappedValue(row, mappings, "数量");
        var amount = GetMappedValue(row, mappings, "金額");
        return $"{ticker}|{date}|{quantity}|{amount}";
    }

    private static string GetMappedValue(
        IReadOnlyDictionary<string, string> row,
        IReadOnlyDictionary<string, string> mappings,
        string logicalName)
    {
        if (mappings.TryGetValue(logicalName, out var columnName) &&
            row.TryGetValue(columnName, out var mappedValue))
        {
            return mappedValue;
        }

        var direct = row.FirstOrDefault(x => x.Key.Contains(logicalName, StringComparison.OrdinalIgnoreCase));
        return direct.Value ?? string.Empty;
    }

    private static Dictionary<string, string> ToDictionary(IReadOnlyList<string> columns, IReadOnlyList<string> values)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            row[columns[i]] = i < values.Count ? values[i] : string.Empty;
        }

        return row;
    }

    private static IEnumerable<IReadOnlyList<string>> ReadCsv(string filePath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var line in File.ReadLines(filePath, Encoding.GetEncoding(932)))
        {
            yield return ParseCsvLine(line);
        }
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
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

public sealed class SbiCsvImportService : BrokerCsvImportServiceBase
{
    public override string ProviderName => "SBI証券";
}

public sealed class NomuraCsvImportService : BrokerCsvImportServiceBase
{
    public override string ProviderName => "野村證券";
}

public sealed class BrokerDataProviderFactory
{
    public IBrokerCsvImportService CreateCsvImportService(string broker)
    {
        return broker.Contains("野村", StringComparison.OrdinalIgnoreCase)
            ? new NomuraCsvImportService()
            : new SbiCsvImportService();
    }
}
