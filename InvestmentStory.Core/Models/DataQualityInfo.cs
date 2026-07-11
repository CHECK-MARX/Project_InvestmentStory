namespace InvestmentStory.Core.Models;

public sealed class DataQualityInfo
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string SourceType { get; set; } = DataSourceTypes.Unknown;
    public string SourceName { get; set; } = string.Empty;
    public DateTime RetrievedAt { get; set; } = DateTime.MinValue;
    public string ConfidenceLevel { get; set; } = DataQualityStates.Missing;
    public bool IsEstimated { get; set; }
    public bool IsStale { get; set; }
    public bool HasConflict { get; set; }
    public string ConflictDescription { get; set; } = string.Empty;
    public bool ManualOverride { get; set; }
    public string Memo { get; set; } = string.Empty;
}

public static class DataSourceTypes
{
    public const string BrokerCsv = "BrokerCsv";
    public const string TransactionCsv = "TransactionCsv";
    public const string DividendCsv = "DividendCsv";
    public const string Api = "Api";
    public const string Manual = "Manual";
    public const string Calculated = "Calculated";
    public const string Estimated = "Estimated";
    public const string Unknown = "Unknown";
}

public static class DataQualityStates
{
    public const string Confirmed = "Confirmed";
    public const string Reliable = "Reliable";
    public const string Estimated = "Estimated";
    public const string Stale = "Stale";
    public const string Missing = "Missing";
    public const string Conflict = "Conflict";
    public const string ManualOverride = "ManualOverride";

    public static string DisplayName(string state) =>
        state switch
        {
            Confirmed => "CSV確定",
            Reliable => "API更新済",
            Estimated => "推定",
            Stale => "期限切れ",
            Conflict => "差分あり",
            ManualOverride => "手入力固定",
            _ => "未取得"
        };
}
