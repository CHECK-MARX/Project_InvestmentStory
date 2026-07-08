namespace InvestmentStory.Core.Models;

public sealed class StockSplit
{
    public int Id { get; set; }
    public int StockId { get; set; }
    public DateTime SplitDate { get; set; } = DateTime.Today;
    public decimal SplitRatio { get; set; } = 1m;
    public string Memo { get; set; } = string.Empty;
}
