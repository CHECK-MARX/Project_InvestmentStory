using System.Reflection;
using System.Text;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class AdditionalCsvFormatsTests
{
    [Fact]
    public void SbiForeignTrade_PreservesNonUsdCurrency()
    {
        WithCsv("""
            国内約定日,通貨,銘柄名,取引,預り区分,約定数量,約定単価,国内受渡日,受渡金額
            2026/08/01,ユーロ,Example Corp EXMP / XETRA,買付,特定,10,50,2026/08/03,500
            """, path =>
        {
            var trade = Assert.Single(new SbiStatementCsvParser().ParseFromSeedFile(path).Trades);
            Assert.Equal("EUR", trade.Currency);
            Assert.Equal(0m, trade.ExchangeRate);
        });
    }

    [Fact]
    public void SbiPaymentRecords_ImportsUsdTradesWithoutInventingYenConversion()
    {
        WithCsv("""
            国内約定日,銘柄,銘柄コード,市場,商品区分,注文種別,取引,預り区分,約定数量,約定単価,国内受渡日,受渡金額/決済損益
            2026/08/01,Example Corp,EXMP,NASDAQ,米国株式,成行,現買,特定,10,50,2026/08/03,500USD
            """, path =>
        {
            Assert.True(SbiStatementCsvParser.LooksLikeSbiStatementCsv(path));
            var statement = new SbiStatementCsvParser().ParseFromSeedFile(path);
            var trade = Assert.Single(statement.Trades);
            Assert.Equal("USD", trade.Currency);
            Assert.Equal(0m, trade.ExchangeRate);
            Assert.Equal(0m, trade.SettlementAmountJpy);
            Assert.Equal(10m, trade.SignedQuantity);
        });
    }

    [Fact]
    public void SbiPaymentRecords_ParsesQuotedNewlineAsOneRecord()
    {
        WithCsv("国内約定日,銘柄,銘柄コード,市場,商品区分,注文種別,取引,預り区分,約定数量,約定単価,国内受渡日,受渡金額/決済損益\n" +
                "2026/08/01,\"Example\nCorp\",EXMP,NASDAQ,米国株式,成行,現買,特定,10,50USD,2026/08/03,500USD\n",
            path =>
            {
                var trade = Assert.Single(new SbiStatementCsvParser().ParseFromSeedFile(path).Trades);
                Assert.Equal("Example\nCorp", trade.Name);
            });
    }

    [Fact]
    public void SbiAccumulation_OnlyCompletedOrdersBecomeFundTradesAndHoldings()
    {
        WithCsv("""
            発注日,注文状況,ファンド名,協会コード,預り区分,取引種別,コース,注文金額,手数料等,税額,約定単価,約定数量
            2026/08/01,完了,Example Fund,12345678,NISA (つみたて),積立,毎月,10000,0,0,20000,5000
            2026/08/02,取引不可,Example Fund,12345678,NISA (つみたて),積立,毎月,10000,0,0,20000,5000
            """, path =>
        {
            Assert.True(SbiStatementCsvParser.LooksLikeSbiStatementCsv(path));
            var statement = new SbiStatementCsvParser().ParseFromSeedFile(path);
            var trade = Assert.Single(statement.Trades);
            Assert.Equal(2m, trade.UnitPrice);
            Assert.Equal(5000m, trade.Quantity);
            Assert.Equal(10000m, trade.SettlementAmountJpy);
            var audit = Assert.Single(statement.SourceRowAudits).Value;
            Assert.True(audit.IsBalanced);
            Assert.Equal(2, audit.InputRows);
            Assert.Equal(1, audit.ParsedRows);
            Assert.Equal(1, audit.ExcludedRows);
            Assert.Equal(3, Assert.Single(audit.Issues).RowNumber);

            WithRepository(repository =>
            {
                Import(repository, statement);
                var fund = Assert.Single(repository.GetPositions());
                Assert.True(fund.IsMutualFund);
                Assert.Equal(5000m, fund.MutualFund.UnitsHeld);
                Assert.Equal(20000m, fund.MutualFund.AverageCostNav);
                Assert.Equal(10000m, fund.MutualFund.AcquisitionAmount);
                Assert.Single(repository.GetBrokerTrades(fund.Stock.Id));
            });
        });
    }

    [Fact]
    public void NomuraStandaloneDividend_KnownTickerIsParsedAndMrfIsHeldWithReason()
    {
        WithCsv("""
            交付日,商品,銘柄コード,銘柄名,配当等の単価,数量,配当等の額,加算対象額,源泉徴収税額,上場株式配当等控除額,元本払戻金（特別分配金）
            2026/08/01,外株,EXMP,Example Corp,1,10,1500,,300,,0
            2026/08/01,外株,,Unknown Corporation,1,10,1500,,300,,0
            2026/08/31,投信,,野村ＭＲＦ,,,10,,1,,0
            """, path =>
        {
            Assert.True(NomuraDividendCsvParser.LooksLikeNomuraDividendCsv(path));
            var statement = new NomuraDividendCsvParser().Parse(path);
            Assert.Single(statement.Dividends);
            Assert.Equal(2, statement.IgnoredRowCount);
            Assert.Equal(2, statement.UnresolvedDividends.Count);
            Assert.Contains(statement.UnresolvedDividends, x => x.Name == "野村ＭＲＦ" &&
                x.Reason.Contains("MRF分配金", StringComparison.Ordinal));
            var record = statement.Dividends[0];
            Assert.Equal("EXMP", record.Ticker);
            Assert.Equal(1200m, record.NetAmountJpy);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    records => repository.SaveBrokerTrades(records), null, repository.GetBrokerTrades,
                    repository.RunImportAtomically, null, repository.SaveCsvImportReviewTrade,
                    repository.SaveCsvImportReviewDividend);
                typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "synthetic" });
                Assert.DoesNotContain("失敗", vm.Message);
                Assert.Contains(vm.ImportLogs, x => x.Contains("要確認", StringComparison.Ordinal));
            });
        });
    }

    [Fact]
    public void InvalidDividendAmount_IsRejectedInsteadOfSilentlyBecomingZero()
    {
        WithCsv("""
            受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)
            2026/08/01,特定,米国株式,Example Corp EXMP,10,invalid
            """, path =>
            Assert.Throws<InvalidOperationException>(() => new SbiDistributionCsvParser().Parse(path)));
    }

    [Fact]
    public void SbiDistribution_UnknownTickerIsPersistedForReview()
    {
        WithCsv("""
            受渡日,口座,商品,銘柄名,数量,受取額(税引後・円)
            2026/08/01,特定,米国株式,ティッカー不明の銘柄,10,500
            """, path =>
        {
            var statement = new SbiDistributionCsvParser().ParseStatement(path);
            Assert.Single(statement.UnresolvedDividends);
            WithRepository(repository =>
            {
                var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
                    repository.SaveDividendPayment, repository.GetDividendPayments,
                    records => repository.SaveBrokerTrades(records), null, repository.GetBrokerTrades,
                    repository.RunImportAtomically, null, repository.SaveCsvImportReviewTrade,
                    repository.SaveCsvImportReviewDividend);
                typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "SBI配当CSV" });
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = repository.DatabasePath, Pooling = false }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM CsvImportReviewDividends";
                Assert.Equal(1L, (long)command.ExecuteScalar()!);
            });
        });
    }

    private static void WithCsv(string content, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"csv_format_{Guid.NewGuid():N}.csv");
        try { File.WriteAllText(path, content, new UTF8Encoding(false)); action(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void WithRepository(Action<InvestmentStoryRepository> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"csv_format_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            action(repository);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Import(InvestmentStoryRepository repository, BrokerStatementImport statement)
    {
        var vm = new CsvImportViewModel(repository.GetPositions, repository.SavePosition,
            repository.SaveDividendPayment, repository.GetDividendPayments,
            records => repository.SaveBrokerTrades(records), null, repository.GetBrokerTrades,
            repository.RunImportAtomically);
        typeof(CsvImportViewModel).GetMethod("ImportBrokerStatement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { new Func<BrokerStatementImport>(() => statement), "synthetic" });
        Assert.DoesNotContain("失敗", vm.Message);
    }
}
