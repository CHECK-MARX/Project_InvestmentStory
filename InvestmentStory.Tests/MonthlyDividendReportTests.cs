using InvestmentStory.App.Services;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class MonthlyDividendReportTests
{
    [Fact]
    public void ReportUsesDashboardRowsAndTotalsIncludingOverdue()
    {
        var asOf = new DateTime(2026, 10, 24);
        var payments = new[]
        {
            new DividendPayment { Id = 1, StockId = 1, PaymentDate = new DateTime(2026, 10, 10),
                Ticker = "SAFE", DividendStatus = DividendConstants.Actual, NetAmountJpy = 100m,
                NetAmount = 100m, Currency = "JPY", Source = "Csv", SourceFile = @"C:\private\input.csv", SourceRowNumber = 7 },
            new DividendPayment { Id = 2, StockId = 2, PaymentDate = new DateTime(2026, 10, 20),
                Ticker = "WAIT", DividendStatus = DividendConstants.Planned, NetAmountJpy = 60m,
                NetAmount = 60m, Currency = "JPY", Source = "Manual" }
        };
        var analysis = new DividendDashboardService().Build(payments, 2026, 0m, asOf);
        var report = new MonthlyDividendReportService().Build(analysis, 10, asOf, false);
        Assert.Equal(2, report.Lines.Count);
        Assert.Equal(100m, report.ActualJpy);
        Assert.Equal(0m, report.PlannedJpy);
        Assert.Equal(60m, report.UnmatchedJpy);
        Assert.Equal(report.ActualJpy + report.PlannedJpy + report.UnmatchedJpy,
            report.Lines.Sum(x => x.NetJpy));
        Assert.Equal("未照合", report.Lines[1].Quality);
        var csv = new MonthlyDividendReportService().ToCsv(report);
        Assert.Contains("input.csv", csv);
        Assert.DoesNotContain("C:\\private", csv);
        Assert.Contains("DividendDashboardService/2", csv);
    }

    [Fact]
    public void CsvMasksPrivateValuesAndNeutralizesSpreadsheetFormula()
    {
        var asOf = new DateTime(2026, 10, 24);
        var payment = new DividendPayment { Id = 1, StockId = 1, PaymentDate = asOf,
            Ticker = " =1+1", StockName = "@evil", Broker = "+cmd", AccountType = "-cmd",
            DividendStatus = DividendConstants.Actual, NetAmountJpy = 123m, NetAmount = 123m,
            Currency = "JPY", Source = "private-source", SourceFile = "=source.csv" };
        var analysis = new DividendDashboardService().Build(new[] { payment }, 2026, 0m, asOf);
        var service = new MonthlyDividendReportService();
        var plain = service.ToCsv(service.Build(analysis, 10, asOf, false));
        Assert.Contains("\"' =1+1\"", plain);
        Assert.Contains("\"'@evil\"", plain);
        Assert.Contains("\"'+cmd\"", plain);
        var masked = service.ToCsv(service.Build(analysis, 10, asOf, true));
        Assert.DoesNotContain("123", masked);
        Assert.DoesNotContain("evil", masked);
        Assert.DoesNotContain("source.csv", masked);
        Assert.DoesNotContain("private-source", masked);
        Assert.Contains("\"***\"", masked);
    }

    [Fact]
    public void PdfRendersJapaneseDetailsAcrossPages()
    {
        var path = Path.Combine(Path.GetTempPath(), "InvestmentStoryMonthlyReport_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var report = new MonthlyDividendReport
            {
                Year = 2026, Month = 10, AsOf = new DateTime(2026, 10, 25),
                ActualJpy = 123_456m, PlannedJpy = 12_345m,
                Lines = Enumerable.Range(1, 45).Select(i => new MonthlyDividendReportLine
                {
                    PaymentDate = new DateTime(2026, 10, Math.Min(i, 28)),
                    Ticker = "JPTEST", StockName = "日本語の長い銘柄名・月次配当表示確認用" + new string('長', 30),
                    Status = i % 2 == 0 ? "入金済み" : "入金予定",
                    Quality = i % 2 == 0 ? "確認済み" : "未照合",
                    Currency = "JPY", NetJpy = i * 100m,
                    Source = "証券会社CSV", SourceFileName = "非常に長い出所名の配当明細" + new string('長', 40) + ".csv", SourceRowNumber = i
                }).ToArray()
            };
            MonthlyDividendPdfWriter.Write(path, report);
            var pdf = File.ReadAllBytes(path);
            Assert.True(pdf.Length > 10_000);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.AsSpan(0, 5)));
            var qaOutput = Environment.GetEnvironmentVariable("INVESTMENTSTORY_PDF_QA_OUTPUT");
            if (!string.IsNullOrWhiteSpace(qaOutput)) File.Copy(path, qaOutput, overwrite: true);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
