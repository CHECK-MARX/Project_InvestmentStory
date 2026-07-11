using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class FxSensitivityRowViewModel
{
    private const double MaxBarWidth = 260d;

    public FxSensitivityRowViewModel(FxSensitivityPoint point, decimal maxChange)
    {
        RateDelta = point.RateDelta > 0m ? $"+{point.RateDelta:N0}円" : $"{point.RateDelta:N0}円";
        UsdJpyRate = $"{point.UsdJpyRate:N2}";
        TotalMarketValue = Formatters.Jpy(point.TotalMarketValueJpy);
        ChangeFromCurrent = Formatters.SignedJpy(point.ChangeFromCurrentJpy);
        BarWidth = maxChange <= 0m
            ? 0d
            : Math.Max(8d, (double)(Math.Abs(point.ChangeFromCurrentJpy) / maxChange) * MaxBarWidth);
        IsPositive = point.ChangeFromCurrentJpy >= 0m;
    }

    public string RateDelta { get; }
    public string UsdJpyRate { get; }
    public string TotalMarketValue { get; }
    public string ChangeFromCurrent { get; }
    public double BarWidth { get; }
    public bool IsPositive { get; }
}
