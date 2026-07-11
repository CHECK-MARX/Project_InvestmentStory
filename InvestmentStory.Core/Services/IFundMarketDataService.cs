namespace InvestmentStory.Core.Services;

public sealed class FundInfoResult
{
    public bool IsSuccess { get; init; }
    public string FundName { get; init; } = string.Empty;
    public string FundCode { get; init; } = string.Empty;
    public string AssociationCode { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

public sealed class FundNavResult
{
    public bool IsSuccess { get; init; }
    public decimal Nav { get; init; }
    public decimal UnitBase { get; init; } = 10000m;
    public DateTime NavDate { get; init; } = DateTime.MinValue;
    public string Source { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

public sealed class FundNavHistoryPoint
{
    public DateTime Date { get; init; }
    public decimal Nav { get; init; }
}

public sealed class FundDistributionRecord
{
    public DateTime Date { get; init; }
    public decimal AmountPerUnitBase { get; init; }
    public string DistributionMethod { get; init; } = string.Empty;
}

public interface IFundMarketDataService
{
    Task<FundInfoResult> GetFundInfoAsync(string fundCodeOrName, CancellationToken cancellationToken = default);

    Task<FundNavResult> GetLatestNavAsync(string fundCodeOrName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FundNavHistoryPoint>> GetNavHistoryAsync(
        string fundCodeOrName,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FundDistributionRecord>> GetDistributionHistoryAsync(
        string fundCodeOrName,
        CancellationToken cancellationToken = default);
}
