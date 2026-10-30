using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class MarketAnomalyRowViewModel
{
    public MarketAnomalyRowViewModel(MarketAnomalyCandidate candidate) => Candidate = candidate;
    public MarketAnomalyCandidate Candidate { get; }
    public string Category => Candidate.Category;
    public string Difference => $"{Candidate.PreviousValue} → {Candidate.IncomingValue}";
    public string Evidence => Candidate.Evidence;
    public string SuggestedCheck => Candidate.SuggestedCheck;
    public string ObservedAt => Candidate.ObservedAt.ToString("yyyy/MM/dd HH:mm");
    public string Status => Candidate.Status switch
    {
        "Confirmed" => "異常として保留",
        "Dismissed" => "確認済み・候補を除外",
        _ => "確認待ち"
    };
}
