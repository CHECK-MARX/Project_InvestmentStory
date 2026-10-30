using System.Text.RegularExpressions;

namespace InvestmentStory.Core.Services;

public static class ApiDiagnosticRedactor
{
    private static readonly Regex CredentialPattern = new(
        @"(?i)(?<![a-z])(?:api[_-]?key|access[_-]?token|token|password)\s*[:=]\s*[^\s&;,]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static string Redact(string? value, IEnumerable<string?> knownSecrets)
    {
        var sanitized = value ?? string.Empty;
        foreach (var secret in knownSecrets.Where(x => !string.IsNullOrWhiteSpace(x)))
            sanitized = sanitized.Replace(secret!, "[REDACTED]", StringComparison.Ordinal);
        return CredentialPattern.Replace(sanitized, "[REDACTED]");
    }
}
