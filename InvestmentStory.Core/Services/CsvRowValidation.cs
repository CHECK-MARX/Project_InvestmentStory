namespace InvestmentStory.Core.Services;

internal static class CsvRowValidation
{
    public static Dictionary<string, string> ToDictionary(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> values,
        int recordNumber,
        params string[] requiredHeaders)
    {
        var normalizedHeaders = ValidateHeaders(headers, requiredHeaders);
        if (normalizedHeaders.Length != values.Count)
            throw new InvalidOperationException($"CSV {recordNumber}行目: 列数が一致しません（ヘッダー {normalizedHeaders.Length}列、明細 {values.Count}列）。");

        return normalizedHeaders.Select((header, index) =>
                (header: header.Length == 0 ? $"__column_{index}" : header, value: values[index]))
            .ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase);
    }

    public static string[] ValidateHeaders(IReadOnlyList<string> headers, params string[] requiredHeaders)
    {
        var normalizedHeaders = headers.Select(x => x.Trim().Trim('"')).ToArray();
        var duplicate = normalizedHeaders.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Key.Length > 0 && x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"CSVヘッダーに重複列があります: {duplicate.Key}");

        foreach (var required in requiredHeaders)
        {
            if (!normalizedHeaders.Contains(required, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"CSVの必須列がありません: {required}");
        }

        return normalizedHeaders;
    }
}
