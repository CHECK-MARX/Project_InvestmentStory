using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class DataQualityRowViewModel
{
    public DataQualityRowViewModel(DataQualityInfo info, DateTime? asOf = null)
    {
        FieldName = info.FieldName;
        Value = string.IsNullOrWhiteSpace(info.Value) ? "-" : info.Value;
        SourceType = info.SourceType;
        SourceName = info.SourceName;
        RetrievedAt = info.RetrievedAt == DateTime.MinValue ? "未取得" : info.RetrievedAt.ToString("yyyy/MM/dd HH:mm");
        Confidence = DataQualityStates.DisplayName(info.ConfidenceLevel == DataQualityStates.Stale
            ? DataQualityStates.Reliable : info.ConfidenceLevel);
        var freshnessApplies = info.FieldName is "現在株価" or "基準価額" or "現在為替";
        Freshness = !freshnessApplies || info.SourceName == "円建て換算不要" ? "対象外" :
            info.ConfidenceLevel == DataQualityStates.Missing ? "未取得" :
            PositionEvidenceService.DisplayName(PositionEvidenceService.Freshness(info.RetrievedAt, asOf ?? DateTime.Today));
        Memo = string.IsNullOrWhiteSpace(info.Memo) ? "-" : info.Memo;
    }

    public string FieldName { get; }
    public string Value { get; }
    public string SourceType { get; }
    public string SourceName { get; }
    public string RetrievedAt { get; }
    public string Confidence { get; }
    public string Freshness { get; }
    public string Memo { get; }
}
