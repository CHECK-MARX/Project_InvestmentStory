using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class MonthEndPortfolioRowViewModel
{
    public MonthEndPortfolioRowViewModel(MonthEndPortfolioRecord record)
    {
        var recalculated = new MonthEndPortfolioService().Recalculate(record);
        Date = record.SnapshotDate.ToString("yyyy/MM/dd");
        Status = record.IsFrozen ? "固定済み" : "月末当日・暫定";
        SavedTotal = record.StoredTotalJpy is decimal saved ? Formatters.Jpy(saved) : "算出不可";
        RecalculatedTotal = recalculated.TotalJpy is decimal current ? Formatters.Jpy(current) : "算出不可";
        Difference = recalculated.TotalDifferenceJpy is decimal difference
            ? Formatters.SignedJpy(difference) : "算出不可";
        DifferenceReason = recalculated.DifferenceReason;
        Quality = record.QualitySummary;
        Formula = $"保存時 {record.FormulaVersion} / 現行 {MonthEndPortfolioService.FormulaVersion}";
        FxBasis = record.UsdJpyRate is decimal rate
            ? $"USD/JPY {rate:N4}円 / {record.UsdJpySource} / {record.UsdJpyAcquiredAt:yyyy/MM/dd HH:mm}"
            : "USD/JPY 取得不可。各銘柄の換算根拠を確認してください。";
        PositionRows = record.Positions.Select(x => new MonthEndPositionRowViewModel(x)).ToList();
        CashRows = record.CashBalances.Select(x => new MonthEndCashRowViewModel(x)).ToList();
    }

    public string Date { get; }
    public string Status { get; }
    public string SavedTotal { get; }
    public string RecalculatedTotal { get; }
    public string Difference { get; }
    public string DifferenceReason { get; }
    public string Quality { get; }
    public string Formula { get; }
    public string FxBasis { get; }
    public IReadOnlyList<MonthEndPositionRowViewModel> PositionRows { get; }
    public IReadOnlyList<MonthEndCashRowViewModel> CashRows { get; }
}

public sealed class MonthEndPositionRowViewModel(MonthEndPositionEvidence evidence)
{
    private static string DateText(DateTime? date) => date is DateTime known
        ? known.ToString("yyyy/MM/dd") : "日付不明";
    private static string Freshness(string value) => value switch
    {
        "Latest" => "直近7日以内", "Delayed" => "更新遅れ",
        "TimeUnknown" => "時刻不明", "Missing" => "未取得",
        "NotRequired" => "換算不要", _ => value
    };
    public string Ticker => evidence.Ticker;
    public string Account => $"{evidence.Broker} / {evidence.AccountType}";
    public string Quantity => Formatters.Number(evidence.Quantity);
    public string MarketValue => evidence.MarketValueJpy is decimal amount ? Formatters.Jpy(amount) : "未確認";
    public string Cost => evidence.CostJpy is decimal amount ? Formatters.Jpy(amount) : "未確認";
    public string Quality => evidence.CostQuality switch
    {
        "Confirmed" => "取得額に根拠あり", "Estimated" => "参考取得額",
        _ => "取得額未確認"
    };
    public string CostEvidence => $"{evidence.CostSource} / {DateText(evidence.CostConfirmedAt)}";
    public string PriceEvidence => $"{evidence.PriceSource} / {DateText(evidence.PriceAcquiredAt)} / {Freshness(evidence.PriceFreshness)}";
    public string FxEvidence => evidence.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
        ? "円建て・換算不要"
        : evidence.FxRate is decimal rate
        ? $"{rate:N4} / {evidence.FxSource} / {DateText(evidence.FxAcquiredAt)} / {Freshness(evidence.FxFreshness)}"
        : "未確認";
}

public sealed class MonthEndCashRowViewModel(MonthEndCashEvidence evidence)
{
    public string Account => evidence.Account;
    public string Currency => evidence.Currency;
    public string Amount => evidence.IncludedJpy is decimal value ? Formatters.Jpy(value) : "未確認";
    public string Basis => $"{(evidence.BalanceDate is DateTime date ? date.ToString("yyyy/MM/dd") : "日付不明")} / {evidence.Status} / {evidence.Evidence}";
}
