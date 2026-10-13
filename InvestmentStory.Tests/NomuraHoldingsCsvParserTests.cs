using System.Text;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class NomuraHoldingsCsvParserTests
{
    [Fact]
    public void Parse_ReadsDomesticAndForeignHoldings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_holdings_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            預り資産（預り証券）

            商品,銘柄コード,ティッカー,銘柄名,期間満了預り保有区分,市場,預り区分,保有数量,注文中数量,売却可能数量（単元）,売却可能数量（単元未満）,取得コスト（円）,参考時価,基準日,現在値,前日比,取得金額（円）,評価額（円）,評価損益（円）,評価レート,通貨,買付単価（外貨）,取得為替レート,外貨評価額
            "株式","8151","","東陽テクニカ","","","特定","7000","","7000","","1178","","","1927","-40","8246000","13489000","+5243000","","","","",""
            "外株","A0115","KO","コカコ－ラ","","アメリカ/ニューヨーク","特定","20","","20","","6793","82.96","2026/07/06","","","135860","267894","+132034","161.46","USD","55.25","114.47","1659.2"
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            var parser = new NomuraHoldingsCsvParser();

            Assert.True(NomuraHoldingsCsvParser.LooksLikeNomuraHoldingsCsv(path));
            var statement = parser.Parse(path);

            Assert.True(statement.CanUpdateHoldings);
            Assert.Equal(2, statement.Holdings.Count);
            var toyo = statement.Holdings.Single(x => x.Ticker == "8151");
            Assert.Equal(7000m, toyo.Shares);
            Assert.Equal(1178m, toyo.AverageAcquisitionPrice);
            Assert.Equal(1927m, toyo.CurrentPrice);
            Assert.Equal(13489000m, toyo.MarketValueJpy);

            var ko = statement.Holdings.Single(x => x.Ticker == "KO");
            Assert.Equal("USD", ko.Currency);
            Assert.Equal(20m, ko.Shares);
            Assert.Equal(55.25m, ko.AverageAcquisitionPrice);
            Assert.Equal(82.96m, ko.CurrentPrice);
            Assert.Equal(161.46m, ko.CurrentExchangeRate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_PreservesExplicitCurrencyAndUnknownSnapshotAndRates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_holdings_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            預り資産（預り証券）
            商品,銘柄コード,ティッカー,銘柄名,保有数量,基準日,通貨,参考時価,買付単価（外貨）,取得為替レート,評価レート,取得金額（円）
            外株,A0001,ABC,Example,10,,EUR,20,15,,,0
            """, new UTF8Encoding(false));
        try
        {
            var holding = Assert.Single(new NomuraHoldingsCsvParser().Parse(path).Holdings);
            Assert.Equal("EUR", holding.Currency);
            Assert.Equal(DateTime.MinValue, holding.SnapshotDate);
            Assert.Equal(0m, holding.PurchaseExchangeRate);
            Assert.Equal(0m, holding.CurrentExchangeRate);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_RejectsMissingColumnsWithRecordNumber()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_holdings_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            預り資産（預り証券）
            商品,銘柄コード,ティッカー,銘柄名,保有数量,基準日,通貨
            外株,A0001,ABC,Example,10,2026/07/01
            """, new UTF8Encoding(false));
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new NomuraHoldingsCsvParser().Parse(path));
            Assert.Contains("3行目", error.Message);
            Assert.Contains("列数", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_QueuesForeignHoldingWithoutTickerForReview()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_holdings_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            預り資産（預り証券）
            商品,銘柄コード,ティッカー,銘柄名,保有数量,基準日,通貨
            外株,A0001,,Example,10,2026/07/01,USD
            """, new UTF8Encoding(false));
        try
        {
            var statement = new NomuraHoldingsCsvParser().Parse(path);
            Assert.Empty(statement.Holdings);
            Assert.Equal("Example", Assert.Single(statement.UnresolvedHoldings).Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LooksLikeAndParse_AcceptCp932EncodedNomuraCsv()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"nomura_holdings_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            預り資産（預り証券）
            商品,銘柄コード,ティッカー,銘柄名,保有数量,基準日,通貨
            株式,8151,,東陽テクニカ,10,2026/07/01,JPY
            """, Encoding.GetEncoding(932));
        try
        {
            Assert.True(NomuraHoldingsCsvParser.LooksLikeNomuraHoldingsCsv(path));
            Assert.Equal("8151", Assert.Single(new NomuraHoldingsCsvParser().Parse(path).Holdings).Ticker);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_RejectsDuplicateHeader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_holdings_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            預り資産（預り証券）
            商品,銘柄コード,ティッカー,銘柄名,保有数量,基準日,保有数量
            株式,8151,,東陽テクニカ,10,2026/07/01,10
            """, new UTF8Encoding(false));
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new NomuraHoldingsCsvParser().Parse(path));
            Assert.Contains("重複列", error.Message);
        }
        finally { File.Delete(path); }
    }
}
