namespace InvestmentStory.Core.Services;

public sealed class MockFundMarketDataService : IFundMarketDataService
{
    public Task<FundInfoResult> GetFundInfoAsync(string fundCodeOrName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FundInfoResult
        {
            IsSuccess = false,
            FundName = fundCodeOrName,
            Source = "MockFundMarketDataService",
            ErrorMessage = "投資信託の基準価額APIは未設定です。CSVの最終値を維持します。"
        });

    public Task<FundNavResult> GetLatestNavAsync(string fundCodeOrName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FundNavResult
        {
            IsSuccess = false,
            Source = "MockFundMarketDataService",
            ErrorMessage = "投資信託の基準価額APIは未設定です。CSVの最終値を維持します。"
        });

    public Task<IReadOnlyList<FundNavHistoryPoint>> GetNavHistoryAsync(
        string fundCodeOrName,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FundNavHistoryPoint>>(Array.Empty<FundNavHistoryPoint>());

    public Task<IReadOnlyList<FundDistributionRecord>> GetDistributionHistoryAsync(
        string fundCodeOrName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FundDistributionRecord>>(Array.Empty<FundDistributionRecord>());
}
