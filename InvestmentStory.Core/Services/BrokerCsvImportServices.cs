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
        CsvRowValidation.ValidateHeaders(columns);
        var previewRows = rows.Skip(1)
            .Take(20)
            .Select((values, index) => CsvRowValidation.ToDictionary(columns, values, index + 2))
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
        return new BrokerCsvImportResult
        {
            IsSuccess = false,
            Logs = new[] { $"{ProviderName} / {csvType}: 汎用CSVのDB取込は未対応です。プレビューのみ利用できます。" }
        };
    }

    private static IEnumerable<IReadOnlyList<string>> ReadCsv(string filePath)
    {
        foreach (var line in CsvFileEncoding.ReadAllRecords(filePath))
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
