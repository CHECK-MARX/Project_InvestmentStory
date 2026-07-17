using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DividendDashboardEntryViewModel
{
    public DividendDashboardEntryViewModel(DividendDashboardEntry entry)
    {
        Entry = entry;
    }

    public DividendDashboardEntry Entry { get; }
    public int Month => Entry.Payment.PaymentDate.Month;
    public string PaymentDate => Entry.Payment.PaymentDate.ToString("yyyy/MM/dd");
    public string Ticker => string.IsNullOrWhiteSpace(Entry.Payment.Ticker) ? "-" : Entry.Payment.Ticker;
    public string StockName => string.IsNullOrWhiteSpace(Entry.Payment.StockName) ? "-" : Entry.Payment.StockName;
    public string Status => Entry.DisplayStatus;
    public string DataQuality => Entry.DataQuality;
    public bool IsActual => Entry.IsActual;
    public DividendScheduleStatus ScheduleStatus => Entry.ScheduleStatus;
    public decimal NetJpyValue => Entry.NetJpy;
    public string NetJpy => Formatters.Jpy(Entry.NetJpy);
    public string GrossJpy => Formatters.Jpy(Entry.GrossJpy);
    public string ForeignTaxJpy => Formatters.Jpy(Entry.ForeignTaxJpy);
    public string DomesticTaxJpy => Formatters.Jpy(Entry.DomesticTaxJpy);
    public string Quantity => Entry.Payment.Quantity == 0m ? "-" : Formatters.Number(Entry.Payment.Quantity);
    public string DividendPerShare => Entry.Payment.DividendPerShare == 0m
        ? "-"
        : Formatters.Money(Entry.Payment.DividendPerShare, Entry.Payment.Currency);
    public string ToolTipText => string.Join(Environment.NewLine,
        $"{Entry.Payment.PaymentDate:yyyy/MM/dd}  {Ticker} / {StockName}",
        $"状態: {Status}  品質: {DataQuality}",
        $"証券会社: {Entry.Payment.Broker}  口座: {Entry.Payment.AccountType}",
        $"株数: {Quantity}  1株配当: {DividendPerShare}",
        $"税引前: {GrossJpy}",
        $"外国税: {ForeignTaxJpy}  国内税: {DomesticTaxJpy}",
        $"税引後: {NetJpy}",
        $"取得元: {Entry.Payment.Source}");
}

public sealed class DividendMonthlySummaryRowViewModel
{
    public DividendMonthlySummaryRowViewModel(DividendDashboardMonth month)
    {
        Month = month.Month;
        ActualValue = month.ActualJpy;
        UpcomingValue = month.UpcomingJpy;
        UnmatchedValue = month.UnmatchedJpy;
        ForecastValue = month.ForecastJpy;
        GoalValue = month.GoalJpy;
        DifferenceValue = month.DifferenceJpy;
        CumulativeActualValue = month.CumulativeActualJpy;
        CumulativeForecastValue = month.CumulativeForecastJpy;
        CumulativeGoalValue = month.CumulativeGoalJpy;
        Entries = month.Entries.Select(x => new DividendDashboardEntryViewModel(x)).ToList();
    }

    public int Month { get; }
    public string MonthLabel => $"{Month}月";
    public decimal ActualValue { get; }
    public decimal UpcomingValue { get; }
    public decimal UnmatchedValue { get; }
    public decimal ForecastValue { get; }
    public decimal GoalValue { get; }
    public decimal DifferenceValue { get; }
    public decimal CumulativeActualValue { get; }
    public decimal CumulativeForecastValue { get; }
    public decimal CumulativeGoalValue { get; }
    public IReadOnlyList<DividendDashboardEntryViewModel> Entries { get; }
    public string Actual => Formatters.Jpy(ActualValue);
    public string Upcoming => Formatters.Jpy(UpcomingValue);
    public string Unmatched => Formatters.Jpy(UnmatchedValue);
    public string Forecast => Formatters.Jpy(ForecastValue);
    public string Goal => Formatters.Jpy(GoalValue);
    public string Difference => Formatters.SignedJpy(DifferenceValue);
    public string ToolTipText => string.Join(Environment.NewLine,
        $"{Month}月",
        $"入金済み: {Actual}",
        $"今後予定: {Upcoming}",
        $"未照合: {Unmatched}",
        $"年末見込み対象: {Forecast}",
        $"累計（実績）: {Formatters.Jpy(CumulativeActualValue)}",
        $"累計（実績＋予定）: {Formatters.Jpy(CumulativeForecastValue)}",
        $"累計目標: {Formatters.Jpy(CumulativeGoalValue)}");
}

public sealed class DividendAnnualRankingRowViewModel
{
    public DividendAnnualRankingRowViewModel(DividendDashboardRanking ranking, int rank)
    {
        Rank = rank;
        Ticker = ranking.Ticker;
        StockName = ranking.StockName;
        GrossValue = ranking.GrossJpy;
        NetValue = ranking.NetJpy;
        CompositionRateValue = ranking.CompositionRate;
        PaymentCount = ranking.PaymentCount;
        ActualValue = ranking.ActualJpy;
        UpcomingValue = ranking.UpcomingJpy;
        DataQuality = ranking.DataQuality;
    }

    public int Rank { get; }
    public string Ticker { get; }
    public string StockName { get; }
    public decimal GrossValue { get; }
    public decimal NetValue { get; }
    public decimal CompositionRateValue { get; }
    public int PaymentCount { get; }
    public decimal ActualValue { get; }
    public decimal UpcomingValue { get; }
    public string DataQuality { get; }
    public string Gross => Formatters.Jpy(GrossValue);
    public string Net => Formatters.Jpy(NetValue);
    public string CompositionRate => Formatters.Percent(CompositionRateValue);
    public string Actual => Formatters.Jpy(ActualValue);
    public string Upcoming => Formatters.Jpy(UpcomingValue);
}
