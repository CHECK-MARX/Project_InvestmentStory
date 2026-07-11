namespace InvestmentStory.Core.Services;

using InvestmentStory.Core.Models;

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

    public const string AccountNisa = AccountTypes.NisaGrowth;
    public const string AccountSpecific = AccountTypes.Specific;
    public const string AccountGeneral = AccountTypes.General;
    public const string AccountUnknown = AccountTypes.Unknown;

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

    public static string NormalizeAccountType(string value) => AccountTypeNormalizer.Normalize(value);

    public static bool IsNisaAccount(string value) => AccountTypes.IsNisa(value);
}
