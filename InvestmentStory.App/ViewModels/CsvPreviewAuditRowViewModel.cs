namespace InvestmentStory.App.ViewModels;

public sealed class CsvPreviewAuditRowViewModel
{
    public string FileName { get; init; } = string.Empty;
    public int InputRows { get; init; }
    public int ParsedRows { get; init; }
    public int PendingRows { get; init; }
    public int ExcludedRows { get; init; }
    public string Balance => InputRows == ParsedRows + PendingRows + ExcludedRows ? "一致" : "不一致";
}
