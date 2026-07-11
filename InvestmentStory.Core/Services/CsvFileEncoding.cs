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

    private static Encoding Detect(string filePath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(932);
        }
    }
}
