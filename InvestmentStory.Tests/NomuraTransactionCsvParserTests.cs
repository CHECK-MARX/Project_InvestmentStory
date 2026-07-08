using System.Text;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class NomuraTransactionCsvParserTests
{
    [Fact]
    public void ParseDividends_ReadsForeignAndDomesticDividendRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            取引履歴

            基準日,取引期間From,取引期間To,商品区分,取引区分,預り区分,銘柄コード
            "約定日","2022年01月01日","2026年07月07日","すべて（MRF除く）","すべて","特定預り",""

            明細数：2件

            約定日,受渡日,商品,銘柄コード,銘柄名,摘要,取引区分,預り区分,発行通貨,数量,単価,受渡金額/決済損益,手数料（税込）,レート,決済通貨,売買損益（円）
            "2026/07/06","2026/07/06","外株","","コカコ－ラ","","入金（配当金）","","USD","20","0.53","1233","","162.05","JPY",""
            "2026/06/09","2026/06/09","株式","8151","東陽テクニカ","","入金（配当金）","","","6000","30","143433","","","",""
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            var parser = new NomuraTransactionCsvParser();

            var records = parser.ParseDividends(path);

            Assert.Equal(2, records.Count);
            var ko = records[0];
            Assert.Equal("野村証券", ko.Broker);
            Assert.Equal("KO", ko.Ticker);
            Assert.Equal("コカコ－ラ", ko.Name);
            Assert.Equal("USD", ko.Currency);
            Assert.Equal(10.60m, ko.GrossAmount);
            Assert.Equal(162.05m, ko.ExchangeRate);
            Assert.Equal(1233m, ko.NetAmountJpy);

            var toyo = records[1];
            Assert.Equal("8151", toyo.Ticker);
            Assert.Equal("JPY", toyo.Currency);
            Assert.Equal(180000m, toyo.GrossAmount);
            Assert.Equal(143433m, toyo.NetAmount);
            Assert.Equal(36567m, toyo.TaxAmount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_ReadsPortfolioTradeRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nomura_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            取引履歴

            基準日,取引期間From,取引期間To,商品区分,取引区分,預り区分,銘柄コード
            "約定日","2022年01月01日","2026年07月07日","すべて（MRF除く）","すべて","特定預り",""

            明細数：3件

            約定日,受渡日,商品,銘柄コード,銘柄名,摘要,取引区分,預り区分,発行通貨,数量,単価,受渡金額/決済損益,手数料（税込）,レート,決済通貨,売買損益（円）
            "2024/01/24","2024/01/26","外株","","プロクタ－　アンド　ギヤンブル（Ｐ＆Ｇ）","","現物売却","特定","USD","20","153.41","444627","5405","147.64","JPY","+84627"
            "2022/02/07","2022/02/09","外株","","シスコ　システムズ","","現物買付","特定","USD","10","54.5","67782","2389","115.74","JPY",""
            "2024/06/13","2024/06/13","外株","","エヌビデイア　コ－プ","","入庫（増減資）","特定","USD","45","","","","156.80","JPY",""
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            var parser = new NomuraTransactionCsvParser();

            var statement = parser.Parse(path);

            Assert.Empty(statement.Dividends);
            Assert.Equal(3, statement.Trades.Count);
            var sell = statement.Trades[0];
            Assert.Equal("PG", sell.Ticker);
            Assert.Equal(-20m, sell.SignedQuantity);
            Assert.Equal(5405m, sell.FeeJpy);
            var buy = statement.Trades[1];
            Assert.Equal("CSCO", buy.Ticker);
            Assert.Equal(10m, buy.SignedQuantity);
            Assert.Equal(54.5m, buy.UnitPrice);
            var splitIncrease = statement.Trades[2];
            Assert.Equal("NVDA", splitIncrease.Ticker);
            Assert.Equal(45m, splitIncrease.SignedQuantity);
            Assert.Equal("入庫（増減資）", splitIncrease.TradeType);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
