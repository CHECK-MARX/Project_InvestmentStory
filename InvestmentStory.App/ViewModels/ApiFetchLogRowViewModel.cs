using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class ApiFetchLogRowViewModel
{
    public ApiFetchLogRowViewModel(ApiFetchLogEntry log)
    {
        FetchedAt = log.FetchedAt.ToString("yyyy/MM/dd HH:mm:ss");
        ApiType = log.ApiType;
        Provider = log.Provider;
        Symbol = log.Symbol;
        HttpStatusCode = log.HttpStatusCode?.ToString() ?? "-";
        Result = log.IsSuccess ? "成功" : "失敗";
        ErrorMessage = log.ErrorMessage;
        Summary = log.Summary;
    }

    public string FetchedAt { get; }
    public string ApiType { get; }
    public string Provider { get; }
    public string Symbol { get; }
    public string HttpStatusCode { get; }
    public string Result { get; }
    public string ErrorMessage { get; }
    public string Summary { get; }
}
