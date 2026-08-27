namespace InvestmentStory.Core.Models;

public sealed class DividendCalendarEvent
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public DateTime? DeclarationDate { get; set; }
    public DateTime? ExDividendDate { get; set; }
    public DateTime? RecordDate { get; set; }
    public DateTime? PaymentDate { get; set; }
    public decimal AmountPerShare { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string DataQuality { get; set; } = DividendPlanDataQuality.Missing;
    public DateTime AcquiredAt { get; set; }
    public bool IsConfirmed { get; set; }

    public static string CreateEventKey(
        DateTime? declarationDate,
        DateTime? exDividendDate,
        DateTime? recordDate,
        DateTime? paymentDate,
        decimal amountPerShare,
        string currency)
    {
        var anchor = exDividendDate ?? recordDate ?? paymentDate ?? declarationDate;
        return string.Join(
            "|",
            anchor?.ToString("yyyy-MM-dd") ?? "unknown",
            amountPerShare.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
            (currency ?? string.Empty).Trim().ToUpperInvariant());
    }
}
