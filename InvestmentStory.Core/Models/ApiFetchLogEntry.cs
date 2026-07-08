namespace InvestmentStory.Core.Models;

public sealed class ApiFetchLogEntry
{
    public int Id { get; set; }
    public string ApiType { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int? HttpStatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; } = DateTime.Now;
    public string Summary { get; set; } = string.Empty;
}
