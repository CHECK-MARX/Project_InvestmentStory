namespace InvestmentStory.Core.Models;

public sealed class Stock
{
    public int Id { get; set; }
    public string AssetType { get; set; } = AssetTypes.Stock;
    public string Name { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Country { get; set; } = "米国";
    public string Currency { get; set; } = "USD";
    public string Broker { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string DataSource { get; set; } = "手入力";
    public string Memo { get; set; } = string.Empty;
}
