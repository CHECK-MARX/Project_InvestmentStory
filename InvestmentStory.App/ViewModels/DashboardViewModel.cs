using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private DashboardSummary _summary = new();

    public string TotalCurrentMarketValue => Formatters.Jpy(_summary.TotalCurrentMarketValueJpy);
    public string TotalUnrealizedGain => Formatters.SignedJpy(_summary.TotalUnrealizedGainJpy);
    public string TotalUnrealizedGainRate => Formatters.SignedPercent(_summary.TotalUnrealizedGainRate);
    public string ForeignAssetTotalUsd => Formatters.Money(_summary.ForeignAssetTotalUsd, "USD");
    public string ForeignAssetTotalJpy => Formatters.Jpy(_summary.ForeignAssetTotalJpy);
    public string FxIncludedUnrealizedGain => Formatters.SignedJpy(_summary.FxIncludedUnrealizedGainJpy);
    public string CurrentUsdJpy => _summary.CurrentUsdJpyRate == 0m ? "-" : $"{_summary.CurrentUsdJpyRate:N2} JPY/USD";
    public string ExchangeRateAcquiredAt => _summary.ExchangeRateAcquiredAt == DateTime.MinValue
        ? "-"
        : _summary.ExchangeRateAcquiredAt.ToString("yyyy/MM/dd HH:mm");
    public string ExchangeRateSource => string.IsNullOrWhiteSpace(_summary.ExchangeRateSource)
        ? "-"
        : $"{_summary.ExchangeRateSource} / {_summary.ExchangeRateInputType}";
    public string ThisYearPassiveIncome => Formatters.Jpy(_summary.ThisYearPassiveIncomeJpy);
    public string AnnualPassiveIncomeForecast => Formatters.Jpy(_summary.AnnualPassiveIncomeForecastJpy);
    public string MonthlyAveragePassiveIncomeForecast => Formatters.Jpy(_summary.MonthlyAveragePassiveIncomeForecastJpy);
    public string DailyPassiveIncomeForecast => Formatters.Jpy(_summary.AnnualPassiveIncomeForecastJpy / 365m);
    public string AnnualGoalAchievementRate => Formatters.Percent(_summary.AnnualGoalAchievementRate);
    public string AnnualGoalGap => Formatters.Jpy(_summary.AnnualGoalGapJpy);
    public string Monthly100kGap => Formatters.Jpy(Math.Max(0m, 100_000m - _summary.MonthlyAveragePassiveIncomeForecastJpy));
    public double AnnualGoalProgressValue => (double)Math.Min(Math.Max(_summary.AnnualGoalAchievementRate, 0m), 100m);
    public string MarketDataStatus => $"USD/JPY {CurrentUsdJpy} / 為替更新 {ExchangeRateAcquiredAt}";
    public string DataUpdateStatus => string.IsNullOrWhiteSpace(_summary.ExchangeRateSource)
        ? "データ取得元: 未取得"
        : $"データ取得元: {_summary.ExchangeRateSource}";

    public void Update(DashboardSummary summary)
    {
        _summary = summary;
        RefreshAllProperties();
    }
}
