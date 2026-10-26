namespace InvestmentStory.Core.Models;

public sealed class CsvImportChangeEntry
{
    public long Id { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceDisplay => Path.GetFileName(Source);
    public string Broker { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string RecordKind { get; set; } = "保有";
    public string EventDate { get; set; } = string.Empty;
    public int SourceRowNumber { get; set; }
    public string Action { get; set; } = string.Empty;
    public decimal? BeforeQuantity { get; set; }
    public decimal? AfterQuantity { get; set; }
    public decimal? BeforeExactCostJpy { get; set; }
    public decimal? AfterExactCostJpy { get; set; }
    public decimal? BeforeReferenceCostJpy { get; set; }
    public decimal? AfterReferenceCostJpy { get; set; }
    public decimal? BeforeAmountJpy { get; set; }
    public decimal? AfterAmountJpy { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BeforeDisplay => RecordKind == "保有"
        ? Format(BeforeQuantity, BeforeExactCostJpy, BeforeReferenceCostJpy)
        : BeforeAmountJpy is { } amount ? $"{amount:N2}円" : "登録なし";
    public string AfterDisplay => RecordKind == "保有"
        ? Format(AfterQuantity, AfterExactCostJpy, AfterReferenceCostJpy)
        : AfterAmountJpy is { } amount ? $"{amount:N2}円" : "反映なし";

    private static string Format(decimal? quantity, decimal? exact, decimal? reference)
    {
        if (quantity is null) return "登録なし";
        var cost = exact is > 0m ? $"確定 {exact:N0}円" :
            reference is > 0m ? $"概算 {reference:N0}円" : "取得額未確認";
        return $"数量 {quantity:N2} / {cost}";
    }
}
