using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class PortfolioSnapshotRowViewModel
{
    private const double MaxBarWidth = 440d;

    public PortfolioSnapshotRowViewModel(PortfolioSnapshot snapshot, decimal maxAmount)
    {
        Date = snapshot.SnapshotDate.ToString("yyyy/MM/dd");
        TotalMarketValue = Formatters.Jpy(snapshot.TotalMarketValueJpy);
        TotalCostBasis = Formatters.Jpy(snapshot.TotalCostBasisJpy);
        UnrealizedGainLoss = Formatters.SignedJpy(snapshot.UnrealizedGainLossJpy);
        CumulativeDividend = Formatters.Jpy(snapshot.CumulativeDividendJpy);
        TotalReturn = Formatters.SignedJpy(snapshot.TotalReturnJpy);
        TotalBarWidth = CalculateBarWidth(snapshot.TotalMarketValueJpy, maxAmount);
        CostBarWidth = CalculateBarWidth(snapshot.TotalCostBasisJpy, maxAmount);
        GainBarWidth = CalculateBarWidth(Math.Abs(snapshot.UnrealizedGainLossJpy), maxAmount);
    }

    public string Date { get; }
    public string TotalMarketValue { get; }
    public string TotalCostBasis { get; }
    public string UnrealizedGainLoss { get; }
    public string CumulativeDividend { get; }
    public string TotalReturn { get; }
    public double TotalBarWidth { get; }
    public double CostBarWidth { get; }
    public double GainBarWidth { get; }

    private static double CalculateBarWidth(decimal amount, decimal maxAmount)
    {
        if (amount <= 0m || maxAmount <= 0m)
        {
            return 0d;
        }

        return Math.Max(6d, (double)(amount / maxAmount) * MaxBarWidth);
    }
}
