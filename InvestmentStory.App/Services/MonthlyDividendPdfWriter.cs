using InvestmentStory.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using System.Globalization;
using System.IO;

namespace InvestmentStory.App.Services;

public static class MonthlyDividendPdfWriter
{
    public static void Write(string path, MonthlyDividendReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new JapaneseFontResolver();
        using var document = new PdfDocument();
        document.Info.Title = $"月次配当 {report.Year}年{report.Month}月";
        var titleFont = new XFont("InvestmentStory Noto JP", 17, XFontStyleEx.Regular);
        var font = new XFont("InvestmentStory Noto JP", 9, XFontStyleEx.Regular);
        var smallFont = new XFont("InvestmentStory Noto JP", 8, XFontStyleEx.Regular);
        XGraphics? graphics = null;
        double y = 0;
        double pageWidth = 0;
        var pageNumber = 0;

        void Footer()
        {
            graphics!.DrawString("未照合・推定は入金確定値ではありません。税務用確定資料ではありません。",
                smallFont, XBrushes.Gray, new XRect(42, 798, pageWidth - 140, 18), XStringFormats.TopLeft);
            graphics.DrawString($"{pageNumber}", smallFont, XBrushes.Gray,
                new XRect(pageWidth - 90, 798, 48, 18), XStringFormats.TopRight);
        }

        void NewPage()
        {
            if (graphics is not null) Footer();
            graphics?.Dispose();
            var page = document.AddPage();
            pageNumber++;
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            pageWidth = page.Width.Point;
            y = 48;
            graphics.DrawString($"月次配当レポート  {report.Year}年{report.Month}月{(pageNumber == 1 ? "" : "（続き）")}", titleFont,
                XBrushes.Black, new XRect(42, y, pageWidth - 84, 25), XStringFormats.TopLeft);
            y += 30;
            graphics.DrawString($"基準日: {report.AsOf:yyyy/MM/dd}  通貨: JPY  算式版: {MonthlyDividendReport.FormulaVersion}",
                smallFont, XBrushes.Black, new XRect(42, y, pageWidth - 84, 18), XStringFormats.TopLeft);
            y += 20;
            graphics.DrawString("入金日基準。実績と今後予定・推定を集計。未照合は別記し、見込みから除外。",
                smallFont, XBrushes.Black, new XRect(42, y, pageWidth - 84, 18), XStringFormats.TopLeft);
            y += 19;
            graphics.DrawString($"共有用マスキング: {(report.MaskSensitive ? "有効" : "無効")}",
                smallFont, XBrushes.Black, new XRect(42, y, pageWidth - 84, 18), XStringFormats.TopLeft);
            y += 27;
        }

        void Line(string label, string value)
        {
            if (y > 760) NewPage();
            graphics!.DrawString(label, font, XBrushes.Black, new XRect(44, y, 335, 17), XStringFormats.TopLeft);
            graphics.DrawString(value, font, XBrushes.Black, new XRect(380, y, pageWidth - 424, 17), XStringFormats.TopRight);
            y += 19;
        }

        NewPage();
        var actual = report.MaskSensitive ? "***" : $"{report.ActualJpy:N0} 円";
        var planned = report.MaskSensitive ? "***" : $"{report.PlannedJpy:N0} 円";
        var unmatched = report.MaskSensitive ? "***" : $"{report.UnmatchedJpy:N0} 円";
        Line("入金済み実績", actual);
        Line("今後予定・推定", planned);
        Line("未照合（見込み対象外）", unmatched);
        y += 12;
        foreach (var item in report.Lines)
        {
            if (y > 735) NewPage();
            var name = report.MaskSensitive ? "***" : $"{item.Ticker} {item.StockName}";
            var amount = report.MaskSensitive ? "***" : $"{item.NetJpy:N0} 円";
            Line(Fit(graphics!, font, $"{item.PaymentDate:MM/dd}  {name}", 330), amount);
            var source = report.MaskSensitive ? "***" :
                $"{item.Source} {item.SourceFileName} 行{item.SourceRowNumber}";
            graphics!.DrawString(Fit(graphics, smallFont,
                $"  {item.Status} / {item.Quality} / {item.Currency} / {source}", pageWidth - 88), smallFont,
                XBrushes.Gray, new XRect(44, y, pageWidth - 88, 16), XStringFormats.TopLeft);
            y += 19;
        }
        Footer();
        graphics!.Dispose();
        document.Save(path);
    }

    private static string Fit(XGraphics graphics, XFont font, string value, double maxWidth)
    {
        if (graphics.MeasureString(value, font).Width <= maxWidth) return value;
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        while (elements.Count > 0)
        {
            elements.RemoveAt(elements.Count - 1);
            var candidate = string.Concat(elements) + "…";
            if (graphics.MeasureString(candidate, font).Width <= maxWidth) return candidate;
        }
        return "…";
    }

    private sealed class JapaneseFontResolver : IFontResolver
    {
        private const string FaceName = "InvestmentStory-NotoJP-Regular";
        private static readonly Lazy<byte[]> FontData = new(() =>
        {
            using var stream = typeof(MonthlyDividendPdfWriter).Assembly.GetManifestResourceStream(
                "InvestmentStory.App.Assets.Fonts.NotoSansJP-Regular.ttf")
                ?? throw new InvalidOperationException("PDF用日本語フォントが見つかりません。");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            familyName == "InvestmentStory Noto JP" ? new FontResolverInfo(FaceName) : null;

        public byte[]? GetFont(string faceName) => faceName == FaceName ? FontData.Value : null;
    }
}
