using System.Text;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class SbiDistributionCsvParserTests
{
    [Fact]
    public void Parse_ReadsSbiDistributionRows()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"sbi_distribution_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            "検索件数","1"
            "受渡日","2026/1/1-2026/7/7"

            "商品","受取額(税引後・円)","受取額(税引後・USD)"
            "米国株式","7,820.89",""

            "受渡日","口座","商品","銘柄名","数量","受取額(税引後・円)"
            "2026/6/12","NISA（成長投資枠）","米国株式","トーム A TRMD","70","7,820.89"
            """, Encoding.GetEncoding(932));

        try
        {
            var parser = new SbiDistributionCsvParser();

            var records = parser.Parse(path);

            var record = Assert.Single(records);
            Assert.Equal(new DateTime(2026, 6, 12), record.PaymentDate);
            Assert.Equal("SBI証券", record.Broker);
            Assert.Equal("NISA（成長投資枠）", record.Account);
            Assert.Equal("米国株式", record.Product);
            Assert.Equal("トーム A", record.Name);
            Assert.Equal("TRMD", record.Ticker);
            Assert.Equal(70m, record.Quantity);
            Assert.Equal(7820.89m, record.NetAmountJpy);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
