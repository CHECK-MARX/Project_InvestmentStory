using InvestmentStory.Core.Models;

namespace InvestmentStory.App.Controls;

public sealed class DividendChartTooltipModel
{
    public DateTime? Date { get; init; }
    public int? Month { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public string SecurityName { get; init; } = string.Empty;
    public string Broker { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public decimal? Quantity { get; init; }
    public decimal? DividendPerShare { get; init; }
    public decimal? GrossJpy { get; init; }
    public decimal? ForeignTaxJpy { get; init; }
    public decimal? DomesticTaxJpy { get; init; }
    public decimal? NetJpy { get; init; }
    public decimal? CurrentJpy { get; init; }
    public decimal? AddedJpy { get; init; }
    public decimal? PlannedJpy { get; init; }
    public decimal? MissedJpy { get; init; }
    public decimal? CumulativeJpy { get; init; }
    public decimal? TargetJpy { get; init; }
    public DividendScheduleStatus? Status { get; init; }
    public string Source { get; init; } = string.Empty;
    public string DataQuality { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;

    public string ToDisplayText()
    {
        var lines = new List<string>();
        var dateLabel = Date is not null ? Date.Value.ToString("yyyy/MM/dd") : Month is not null ? $"{Month}月" : string.Empty;
        var security = string.Join(" / ", new[] { Ticker, SecurityName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        Add(lines, string.Join("  ", new[] { dateLabel, security }.Where(x => !string.IsNullOrWhiteSpace(x))));
        Add(lines, Join("証券会社", Broker, "口座", AccountType));
        if (Status is not null)
            Add(lines, $"状態: {DividendChartColorRegistry.ForStatus(Status.Value).DisplayName}" +
                       (string.IsNullOrWhiteSpace(DataQuality) ? string.Empty : $"  品質: {DataQuality}"));
        if (Quantity is not null || DividendPerShare is not null)
            Add(lines, $"株数: {NumberOrDash(Quantity)}  1株配当: {MoneyOrDash(DividendPerShare)}");
        AddMoney(lines, "税引前", GrossJpy);
        if (ForeignTaxJpy is not null || DomesticTaxJpy is not null)
            Add(lines, $"外国税: {MoneyOrDash(ForeignTaxJpy)}  国内税: {MoneyOrDash(DomesticTaxJpy)}");
        AddMoney(lines, "税引後", NetJpy);
        AddMoney(lines, "現在配当", CurrentJpy);
        AddMoney(lines, "追加配当", AddedJpy);
        AddMoney(lines, "購入後配当", PlannedJpy);
        AddMoney(lines, "受取不可", MissedJpy);
        AddMoney(lines, "累計", CumulativeJpy);
        AddMoney(lines, "目標", TargetJpy);
        Add(lines, string.IsNullOrWhiteSpace(Source) ? string.Empty : $"取得元: {Source}");
        Add(lines, Note);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Join(string firstLabel, string first, string secondLabel, string second) =>
        string.Join("  ", new[]
        {
            string.IsNullOrWhiteSpace(first) ? string.Empty : $"{firstLabel}: {first}",
            string.IsNullOrWhiteSpace(second) ? string.Empty : $"{secondLabel}: {second}"
        }.Where(x => x.Length > 0));
    private static string NumberOrDash(decimal? value) => value is null ? "-" : $"{value:N2}";
    private static string MoneyOrDash(decimal? value) => value is null ? "未取得" : $"{value:N0}円";
    private static void AddMoney(ICollection<string> lines, string label, decimal? value)
    {
        if (value is not null) lines.Add($"{label}: {value:N0}円");
    }
    private static void Add(ICollection<string> lines, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(value);
    }
}
