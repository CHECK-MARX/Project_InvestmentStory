using System.Text;

namespace InvestmentStory.Core.Services;

internal static class CsvFileEncoding
{
    public static string[] ReadAllLines(string filePath)
    {
        return File.ReadAllLines(filePath, Detect(filePath));
    }

    public static IEnumerable<string> ReadLines(string filePath)
    {
        return File.ReadLines(filePath, Detect(filePath));
    }

    public static string[] ReadAllRecords(string filePath)
    {
        var records = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var line in ReadLines(filePath))
        {
            if (current.Length > 0) current.Append('\n');
            current.Append(line);
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] != '"') continue;
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { i++; continue; }
                quoted = !quoted;
            }
            if (!quoted)
            {
                records.Add(current.ToString());
                current.Clear();
            }
        }

        if (quoted) throw new InvalidOperationException("CSVの引用符が閉じられていません。");
        return records.ToArray();
    }

    private static Encoding Detect(string filePath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(filePath);
        Span<byte> bom = stackalloc byte[3];
        var read = stream.Read(bom);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        if (read == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return utf8;
        }

        stream.Position = 0;
        try
        {
            using var reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096, leaveOpen: true);
            var chars = new char[4096];
            while (reader.Read(chars, 0, chars.Length) > 0) { }
            return utf8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(932);
        }
    }
}
