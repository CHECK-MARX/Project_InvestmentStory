using System.Text;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class CsvSchemaValidationTests
{
    [Theory]
    [InlineData("国内約定日,銘柄,銘柄コード,商品区分,注文種別,取引,約定数量,約定単価,国内受渡日,受渡金額/決済損益,未知金額", "SbiPaymentRecords-v1")]
    [InlineData("発注日,注文状況,ファンド名,注文金額,約定単価,約定数量,未知金額", "SbiAccumulation-v1")]
    [InlineData("国内約定日,通貨,銘柄名,取引,約定数量,約定単価,国内受渡日,受渡金額,未知金額", "SbiForeignTrades-v1")]
    [InlineData("約定日,銘柄,銘柄コード,取引,約定数量,約定単価,受渡日,受渡金額/決済損益,未知金額", "SbiDomesticTrades-v1")]
    public void SbiTradeParsers_StopOnUnregisteredColumn(string header, string version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_schema_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, header + "\n", Encoding.UTF8);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => new SbiStatementCsvParser().ParseFiles(new[] { path }));
            Assert.Contains(version, error.Message);
            Assert.Contains("未知金額", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("nomura_holdings", "預り資産\n商品,銘柄コード,ティッカー,銘柄名,保有数量,基準日,未知金額\n", "NomuraHoldings-v1")]
    [InlineData("nomura_transactions", "取引履歴\n約定日,受渡日,取引区分,商品,銘柄コード,銘柄名,発行通貨,数量,単価,受渡金額/決済損益,レート,未知金額\n", "NomuraTransactions-v1")]
    [InlineData("nomura_dividends", "交付日,配当等の額,源泉徴収税額,銘柄コード,銘柄名,商品,数量,未知金額\n", "NomuraDividends-v1")]
    public void NomuraParsers_StopOnUnregisteredColumn(string kind, string csv, string version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{kind}_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv, Encoding.UTF8);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => kind switch
            {
                "nomura_holdings" => new NomuraHoldingsCsvParser().Parse(path),
                "nomura_transactions" => new NomuraTransactionCsvParser().Parse(path),
                _ => new NomuraDividendCsvParser().Parse(path)
            });
            Assert.Contains(version, error.Message);
            Assert.Contains("未知金額", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SbiDistribution_StopsOnBlankColumnName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path,
            "受渡日,口座,商品,銘柄名,数量,受取額(税引後・円),\n" +
            "2026/10/01,特定,米国株式,Example EXMP,10,100,20\n", Encoding.UTF8);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => new SbiDistributionCsvParser().ParseStatement(path));
            Assert.Contains("見出し空欄", error.Message);
        }
        finally { File.Delete(path); }
    }
}
