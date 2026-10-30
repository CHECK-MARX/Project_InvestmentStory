using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class CorporateActionRowViewModel(CorporateAction action, bool isSuperseded = false)
{
    public CorporateAction Action { get; } = action;
    public string Id { get; } = action.Id.ToString();
    public string EventDate { get; } = action.EventDate.ToString("yyyy/MM/dd");
    public string Kind { get; } = action.Kind switch
    {
        "OpeningBalance" => "開始残高", "Transfer" => "口座移管",
        "StockSplit" => "株式分割・併合", "TickerChange" => "銘柄コード変更",
        _ => action.Kind
    };
    public string Status { get; } = isSuperseded ? "訂正済み" :
        action.Status == "Confirmed" ? "根拠確認済み" : "要確認";
    public string Source { get; } = $"{action.SourceBroker} / {AccountTypes.DisplayName(action.SourceAccount)}";
    public string Destination { get; } = $"{action.DestinationBroker} / {AccountTypes.DisplayName(action.DestinationAccount)}";
    public string Quantity { get; } = action.Status == "Pending" && action.Quantity == 0m
        ? "未確認" : action.Quantity.ToString("N4");
    public string CarriedCost { get; } = action.CarriedCostJpy.HasValue ? $"{action.CarriedCostJpy:N0}円" : "不明";
    public string Evidence { get; } = string.IsNullOrWhiteSpace(action.SourceFile)
        ? action.Evidence : $"{action.Evidence} / {action.SourceFile}:{action.SourceRowNumber}行";
    public string Reason { get; } = action.SupersedesId.HasValue
        ? $"ID {action.SupersedesId} の訂正: {action.Reason}" : action.Reason;
}
