using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class PositionChangeAuditRowViewModel
{
    public PositionChangeAuditRowViewModel(PositionChangeAudit entry)
    {
        Entry = entry;
        RecordedAt = entry.RecordedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        Reason = entry.Reason;
        Before = Describe(entry.BeforeJson);
        After = Describe(entry.AfterJson);
        ConfirmedCostChange = Difference(entry.BeforeJson, entry.AfterJson, "ConfirmedCostJpy");
        MarketValueChange = entry.MarketValueChangeJpy.HasValue
            ? $"{entry.MarketValueChangeJpy:+#,0;-#,0;0}円（記録時の概算）" : "算出不可";
    }

    public PositionChangeAudit Entry { get; }
    public string Id => Entry.Id.ToString();
    public string RecordedAt { get; }
    public string Reason { get; }
    public string Before { get; }
    public string After { get; }
    public string ConfirmedCostChange { get; }
    public string MarketValueChange { get; }

    private static string Describe(string? json)
    {
        if (string.IsNullOrEmpty(json)) return "新規登録前";
        using var document = JsonDocument.Parse(json);
        var state = document.RootElement;
        return $"{state.GetProperty("Ticker").GetString()} / {state.GetProperty("Broker").GetString()} / " +
               $"{AccountTypes.DisplayName(state.GetProperty("Account").GetString() ?? AccountTypes.Unknown)} / " +
               $"数量 {state.GetProperty("CurrentShares").GetDecimal():N4} / " +
               $"確定原価 {state.GetProperty("ConfirmedCostJpy").GetDecimal():N0}円 / " +
               $"参考原価 {state.GetProperty("ReferenceCostJpy").GetDecimal():N0}円";
    }

    private static string Difference(string? beforeJson, string afterJson, string field)
    {
        if (string.IsNullOrEmpty(beforeJson)) return "新規登録";
        using var before = JsonDocument.Parse(beforeJson);
        using var after = JsonDocument.Parse(afterJson);
        var delta = after.RootElement.GetProperty(field).GetDecimal() -
                    before.RootElement.GetProperty(field).GetDecimal();
        return $"{delta:+#,0;-#,0;0}円";
    }
}
