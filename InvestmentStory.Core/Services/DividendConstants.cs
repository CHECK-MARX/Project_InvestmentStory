namespace InvestmentStory.Core.Services;

public static class DividendConstants
{
    public const string Planned = "Planned";
    public const string Estimated = "Estimated";
    public const string Confirmed = "Confirmed";
    public const string Actual = "Actual";
    public const string Replaced = "Replaced";
    public const string PaymentDue = "PaymentDue";

    public const string SourceManual = "Manual";
    public const string SourceCsv = "Csv";
    public const string SourceApi = "Api";
    public const string SourceEstimatedFromHistory = "EstimatedFromHistory";
    public const string SourceEstimatedFromAnnualDividend = "EstimatedFromAnnualDividend";
    public const string SourceImportedFromBroker = "ImportedFromBroker";

    public const string AccountNisa = "NISA";
    public const string AccountSpecific = "Specific";
    public const string AccountGeneral = "General";
    public const string AccountUnknown = "Unknown";

    public static bool IsActual(string status) =>
        string.IsNullOrWhiteSpace(status) ||
        string.Equals(status, Actual, StringComparison.OrdinalIgnoreCase);

    public static bool IsUnconfirmed(string status) =>
        string.Equals(status, Planned, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Estimated, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Confirmed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, PaymentDue, StringComparison.OrdinalIgnoreCase);

    public static bool IsVisibleActual(string status) =>
        IsActual(status);

    public static string NormalizeAccountType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AccountUnknown;
        }

        var normalized = value.Trim();
        if (normalized.Contains("NISA", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("成長投資", StringComparison.Ordinal) ||
            normalized.Contains("つみたて", StringComparison.Ordinal))
        {
            return AccountNisa;
        }

        if (normalized.Contains("特定", StringComparison.Ordinal))
        {
            return AccountSpecific;
        }

        if (normalized.Contains("一般", StringComparison.Ordinal))
        {
            return AccountGeneral;
        }

        return AccountUnknown;
    }
}
