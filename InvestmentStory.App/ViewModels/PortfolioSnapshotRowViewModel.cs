using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class PortfolioSnapshotRowViewModel
{
    public PortfolioSnapshotRowViewModel(PortfolioSnapshot snapshot)
    {
        SnapshotDate = snapshot.SnapshotDate.Date;
        TotalMarketValueJpy = snapshot.TotalMarketValueJpy;
        TotalCostBasisJpy = snapshot.TotalCostBasisJpy;
        UnrealizedGainLossJpy = snapshot.UnrealizedGainLossJpy;
        Date = snapshot.SnapshotDate.ToString("yyyy/MM/dd");
        TotalMarketValue = Formatters.Jpy(snapshot.TotalMarketValueJpy);
        TotalCostBasis = Formatters.Jpy(snapshot.TotalCostBasisJpy);
        UnrealizedGainLoss = Formatters.SignedJpy(snapshot.UnrealizedGainLossJpy);
        CumulativeDividend = Formatters.Jpy(snapshot.CumulativeDividendJpy);
        TotalReturn = Formatters.SignedJpy(snapshot.TotalReturnJpy);
    }

    public DateTime SnapshotDate { get; }
    public decimal TotalMarketValueJpy { get; }
    public decimal TotalCostBasisJpy { get; }
    public decimal UnrealizedGainLossJpy { get; }
    public string Date { get; }
    public string TotalMarketValue { get; }
    public string TotalCostBasis { get; }
    public string UnrealizedGainLoss { get; }
    public string CumulativeDividend { get; }
    public string TotalReturn { get; }
}
