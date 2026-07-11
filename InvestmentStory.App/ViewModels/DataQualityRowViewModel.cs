using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DataQualityRowViewModel
{
    public DataQualityRowViewModel(DataQualityInfo info)
    {
        FieldName = info.FieldName;
        SourceType = info.SourceType;
        SourceName = info.SourceName;
        RetrievedAt = info.RetrievedAt == DateTime.MinValue ? "未取得" : info.RetrievedAt.ToString("yyyy/MM/dd HH:mm");
        Status = DataQualityStates.DisplayName(info.ConfidenceLevel);
        Memo = string.IsNullOrWhiteSpace(info.Memo) ? "-" : info.Memo;
    }

    public string FieldName { get; }
    public string SourceType { get; }
    public string SourceName { get; }
    public string RetrievedAt { get; }
    public string Status { get; }
    public string Memo { get; }
}
