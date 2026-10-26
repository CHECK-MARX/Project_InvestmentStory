using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MonthlyDividendReportService
{
    public MonthlyDividendReport Build(DividendDashboardAnalysis analysis, int month, DateTime asOf, bool maskSensitive)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var summary = analysis.Months.Single(x => x.Month == month);
        var lines = summary.Entries.Select(x => new MonthlyDividendReportLine
        {
            PaymentDate = x.Payment.PaymentDate,
            Ticker = x.Payment.Ticker,
            StockName = x.Payment.StockName,
            Broker = x.Payment.Broker,
            AccountType = x.Payment.AccountType,
            Status = x.DisplayStatus,
            Quality = x.DataQuality,
            Currency = x.Payment.Currency,
            NetAmount = x.Payment.NetAmount,
            ExchangeRate = x.Payment.ExchangeRate,
            NetJpy = x.NetJpy,
            Source = x.Payment.Source,
            SourceFileName = Path.GetFileName(x.Payment.SourceFile),
            SourceRowNumber = x.Payment.SourceRowNumber,
            IsActual = x.IsActual
        }).OrderBy(x => x.PaymentDate).ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase).ToArray();
        return new MonthlyDividendReport
        {
            Year = analysis.Year,
            Month = month,
            AsOf = asOf.Date,
            MaskSensitive = maskSensitive,
            ActualJpy = summary.ActualJpy,
            PlannedJpy = summary.UpcomingJpy,
            UnmatchedJpy = summary.UnmatchedJpy,
            Lines = lines
        };
    }

    public string ToCsv(MonthlyDividendReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var b = new StringBuilder();
        Row(b, "月次配当明細（税務用確定資料ではありません）");
        Row(b, "対象月", $"{report.Year:0000}-{report.Month:00}");
        Row(b, "基準日", report.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Row(b, "集計条件", "配当ダッシュボードと同じ照合済み実績・有効な予定／推定。置換済み予定は除外。入金日基準。");
        Row(b, "換算通貨", "JPY（円換算額）。元通貨と適用為替は明細を参照。");
        Row(b, "算式版", MonthlyDividendReport.FormulaVersion);
        Row(b, "共有用マスキング", report.MaskSensitive ? "有効" : "無効");
        Row(b, "実績合計JPY", Amount(report.ActualJpy, report.MaskSensitive));
        Row(b, "予定・推定合計JPY", Amount(report.PlannedJpy, report.MaskSensitive));
        Row(b, "未照合合計JPY（見込み対象外）", Amount(report.UnmatchedJpy, report.MaskSensitive));
        Row(b, "注意", "未照合・推定は入金確定値ではありません。元データを確認してください。");
        b.AppendLine();
        Row(b, "入金日", "ティッカー", "銘柄", "証券会社", "口座", "区分", "品質", "元通貨", "税引後元通貨", "適用為替", "税引後JPY", "出所", "元ファイル", "元行");
        foreach (var line in report.Lines)
        {
            Row(b,
                line.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Private(line.Ticker, report.MaskSensitive), Private(line.StockName, report.MaskSensitive),
                Private(line.Broker, report.MaskSensitive), Private(line.AccountType, report.MaskSensitive),
                line.Status, line.Quality, line.Currency,
                Amount(line.NetAmount, report.MaskSensitive), Amount(line.ExchangeRate, report.MaskSensitive),
                Amount(line.NetJpy, report.MaskSensitive), Private(line.Source, report.MaskSensitive),
                Private(line.SourceFileName, report.MaskSensitive),
                report.MaskSensitive ? "***" : line.SourceRowNumber.ToString(CultureInfo.InvariantCulture));
        }
        return b.ToString();
    }

    private static string Private(string value, bool mask) => mask ? "***" : value;
    private static string Amount(decimal value, bool mask) => mask ? "***" : value.ToString("0.########", CultureInfo.InvariantCulture);

    private static void Row(StringBuilder b, params string[] values) =>
        b.AppendLine(string.Join(",", values.Select(Cell)));

    private static string Cell(string value)
    {
        // Escape user-controlled text before CSV quoting. Leading whitespace/control characters
        // must not bypass the spreadsheet formula check.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
