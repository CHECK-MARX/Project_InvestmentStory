using System.Text;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class SbiDistributionCsvParserTests
{
    [Fact]
    public void ParseStatement_RejectsUnregisteredAmountColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sbi_distribution_schema_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path,
            "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円),新税額\n" +
            "2026/10/01,特定,米国株式,Example EXMP,10,100,20\n", Encoding.UTF8);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => new SbiDistributionCsvParser().ParseStatement(path));
            Assert.Contains("SbiDistribution-v1", error.Message);
            Assert.Contains("新税額", error.Message);
        }
        finally { File.Delete(path); }
    }

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

    [Fact]
    public void ParseStatement_KeepsUnknownTickerInReviewAndBalancesSourceRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sbi_distribution_review_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            "受渡日","口座","商品","銘柄名","数量","受取額(税引後・円)"
            "2026/6/12","特定","米国株式","トーム A TRMD","70","7,820.89"
            "2026/6/13","特定","米国株式","名称だけの外国銘柄","10","500"
            """, Encoding.UTF8);
        try
        {
            var statement = new SbiDistributionCsvParser().ParseStatement(path);
            Assert.Single(statement.Dividends);
            Assert.Single(statement.UnresolvedDividends);
            var audit = statement.SourceRowAudits[path];
            Assert.True(audit.IsBalanced);
            Assert.Equal(2, audit.InputRows);
            Assert.Equal(1, audit.ParsedRows);
            Assert.Equal(1, audit.PendingRows);
            Assert.Equal(3, Assert.Single(audit.Issues).RowNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
