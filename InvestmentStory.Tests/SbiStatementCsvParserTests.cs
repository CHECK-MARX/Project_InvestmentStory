using System.Text;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class SbiStatementCsvParserTests
{
    [Fact]
    public void ParseFiles_ReadsSbiJapanStockHoldings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"SBI_Jap_SaveFile_{Guid.NewGuid():N}.csv");
        var encoding = Encoding.GetEncoding(932);
        File.WriteAllText(path, """
            保有証券一覧

            株式（NISA預り（成長投資枠））

            銘柄コード,銘柄名称,保有株数,売却注文中,取得単価,現在値,取得金額,評価額,評価損益
            "8593","三菱ＨＣキャピタル",200,,1346,1382,269200,276400,+7200
            """, encoding);

        try
        {
            var parser = new SbiStatementCsvParser();

            Assert.True(SbiStatementCsvParser.LooksLikeSbiStatementCsv(path));
            var statement = parser.ParseFiles(new[] { path });

            Assert.True(statement.CanUpdateHoldings);
            var holding = Assert.Single(statement.Holdings);
            Assert.Equal("SBI証券", holding.Broker);
            Assert.Equal("8593", holding.Ticker);
            Assert.Equal("三菱ＨＣキャピタル", holding.Name);
            Assert.Equal(200m, holding.Shares);
            Assert.Equal(1346m, holding.AverageAcquisitionPrice);
            Assert.Equal(1382m, holding.CurrentPrice);
            Assert.Equal(276400m, holding.MarketValueJpy);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseFiles_ReadsSbiMutualFundHoldingsAsMutualFund()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"SBI_Fund_SaveFile_{Guid.NewGuid():N}.csv");
        var encoding = Encoding.GetEncoding(932);
        File.WriteAllText(path, """
            保有証券一覧

            投資信託（金額/口数指定）

            ファンド名,保有口数,取得単価,基準価額,取得金額,評価額,評価損益,預り区分,分配金受取方法
            "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド","411,318","29,499","40,579","1,213,346","1,669,087","+455,741","NISA","再投資"
            """, encoding);

        try
        {
            var parser = new SbiStatementCsvParser();

            Assert.True(SbiStatementCsvParser.LooksLikeSbiStatementCsv(path));
            var statement = parser.ParseFiles(new[] { path });

            var holding = Assert.Single(statement.Holdings);
            Assert.Equal(AssetTypes.MutualFund, holding.AssetType);
            Assert.True(holding.IsMutualFund);
            Assert.Equal("ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド", holding.FundName);
            Assert.Equal(411318m, holding.UnitsHeld);
            Assert.Equal(10000m, holding.UnitBase);
            Assert.Equal(29499m, holding.AverageCostNav);
            Assert.Equal(40579m, holding.CurrentNav);
            Assert.Equal(1_213_346m, holding.AcquisitionAmount);
            Assert.Equal(1_669_087m, holding.MarketValue);
            Assert.Equal(455_741m, holding.UnrealizedGainLossJpy);
            Assert.Equal("再投資", holding.DistributionMethod);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseFromSeedFile_ReadsSbiFilesInSameFolder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var directory = Path.Combine(Path.GetTempPath(), $"sbi_csv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var encoding = Encoding.GetEncoding(932);

        var domesticPath = Path.Combine(directory, "SBI_000001_000025.csv");
        File.WriteAllText(domesticPath, """
            
            約定履歴照会 

            商品指定,約定開始年月日,約定終了年月日,明細数,明細指定開始,明細指定終了
            "すべての商品","2024年01月01日","2026年07月07日","1","1","1"

            約定日,銘柄,銘柄コード,市場,取引,期限,預り,課税,約定数量,約定単価,手数料/諸経費等,税額,受渡日,受渡金額/決済損益
            "2024/07/02","ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",,,投信金額買付,"--"," NISA(つ) ","--",13723,29148,--,--,"2024/07/05",40000
            """, encoding);

        var distributionPath = Path.Combine(directory, "SBI_DISTRIBUTION_20260707205114.csv");
        File.WriteAllText(distributionPath, """
            "検索件数","1"

            "受渡日","口座","商品","銘柄名","数量","受取額(税引後・円)"
            "2026/6/10","NISA（成長投資枠）","米国株式","ジョンソン & ジョンソン JNJ","20","3,842.79"
            """, encoding);

        var foreignPath = Path.Combine(directory, "SBI_yakujo20260707215330.csv");
        File.WriteAllText(foreignPath, """
            通貨指定,商品指定,期間（国内約定日）開始,期間（国内約定日）終了,明細数
            "すべての通貨","株式","2021年01月01日","2026年07月08日","1"
            国内約定日,通貨,銘柄名,取引,預り区分,約定数量,約定単価,国内受渡日,受渡金額
            "2026年06月15日","米国ドル","ペプシコ PEP / NASDAQ","買付","NISA","20","142.98","26/06/17","2859.6"
            "2026年06月15日","米国ドル","ユーロナブ EURN / New York Stock Exchange","買付","NISA","40","17.24","26/06/17","689.6"
            """, encoding);

        try
        {
            var parser = new SbiStatementCsvParser();

            var statement = parser.ParseFromSeedFile(foreignPath);

            Assert.Equal("SBI証券", statement.Broker);
            Assert.Single(statement.Dividends);
            Assert.Equal("JNJ", statement.Dividends[0].Ticker);
            Assert.Equal(3, statement.Trades.Count);
            Assert.Contains(statement.Trades, x => x.Ticker == "PEP" && x.Currency == "USD" && x.SignedQuantity == 20m);
            Assert.Contains(statement.Trades, x => x.Ticker == "CMBT" && x.Name == "CMB テック" && x.Currency == "USD" && x.SignedQuantity == 40m);
            Assert.Contains(statement.Trades, x => x.Ticker.StartsWith("FUND:", StringComparison.Ordinal) && x.Currency == "JPY");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LooksLikeSbiStatementCsv_DoesNotMatchNomuraTransactionCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Nomura_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            取引履歴

            基準日,取引期間From,取引期間To,商品区分,取引区分,預り区分,銘柄コード
            "約定日","2022年01月01日","2026年07月07日","すべて（MRF除く）","すべて","特定預り",""

            明細数：1件

            約定日,受渡日,商品,銘柄コード,銘柄名,摘要,取引区分,預り区分,発行通貨,数量,単価,受渡金額/決済損益,手数料（税込）,レート,決済通貨,売買損益（円）
            "2026/07/06","2026/07/06","外株","","コカコ－ラ","","入金（配当金）","","USD","20","0.53","1233","","162.05","JPY",""
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            Assert.False(SbiStatementCsvParser.LooksLikeSbiStatementCsv(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LooksLikeSbiStatementCsv_DoesNotMatchDividendOnlyCsv()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"SBI_DISTRIBUTION_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            "受渡日","口座","商品","銘柄名","数量","受取額(税引後・円)"
            "2026/6/10","NISA（成長投資枠）","米国株式","ジョンソン & ジョンソン JNJ","20","3,842.79"
            """, Encoding.GetEncoding(932));

        try
        {
            Assert.False(SbiStatementCsvParser.LooksLikeSbiStatementCsv(path));
            Assert.True(SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
