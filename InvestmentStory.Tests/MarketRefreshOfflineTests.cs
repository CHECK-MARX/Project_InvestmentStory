using System.Threading;
using System.Windows;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

[Collection("WPF visual acceptance")]
public sealed class MarketRefreshOfflineTests
{
    [Fact]
    public void FailedQuoteKeepsLastPriceAndRetryUpdatesOnlyFailedStock()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            InvestmentStory.App.App? createdApp = null;
            var path = Path.Combine(Path.GetTempPath(), $"investment_story_offline_{Guid.NewGuid():N}.db");
            try
            {
                if (Application.Current is null)
                {
                    createdApp = new InvestmentStory.App.App();
                    createdApp.InitializeComponent();
                }
                var repository = new InvestmentStoryRepository(path);
                repository.Initialize();
                var settings = repository.GetSettings();
                settings.AlphaVantageApiKey = "super-secret-key";
                repository.SaveSettings(settings);
                repository.SavePosition(new StockPosition
                {
                    Stock = new Stock { Ticker = "1234", Name = "テスト銘柄", Currency = "JPY" },
                    Purchase = new Purchase { PurchaseDate = DateTime.Today.AddMonths(-2), Shares = 10m, UnitPrice = 100m },
                    CurrentHolding = new CurrentHolding
                    {
                        CurrentShares = 10m, CurrentPrice = 150m,
                        CurrentPriceAcquiredAt = DateTime.Today.AddDays(-3),
                        CurrentPriceSource = "前回取得"
                    }
                });

                var provider = new SwitchableMarketService();
                var vm = new MainViewModel(repository, new UnavailableRateService(), provider,
                    new OfflineHistoryService());
                vm.StockList.RefreshAllCommand.Execute(null);

                Assert.Equal(150m, Assert.Single(repository.GetPositions()).CurrentHolding.CurrentPrice);
                Assert.Contains("失敗", vm.Dashboard.MarketRefreshStatus);
                Assert.DoesNotContain("super-secret-key", vm.Dashboard.MarketRefreshStatus);
                Assert.True(vm.RetryFailedMarketDataCommand.CanExecute(null));
                Assert.DoesNotContain("super-secret-key",
                    Assert.Single(repository.GetRecentApiFetchLogs(10)).ErrorMessage);

                provider.Fail = false;
                vm.RetryFailedMarketDataCommand.Execute(null);

                Assert.Equal(220m, Assert.Single(repository.GetPositions()).CurrentHolding.CurrentPrice);
                Assert.False(vm.RetryFailedMarketDataCommand.CanExecute(null));
                Assert.Equal(2, provider.CallCount);

                provider.Price = 500m;
                vm.StockList.RefreshAllCommand.Execute(null);
                Assert.Equal(220m, Assert.Single(repository.GetPositions()).CurrentHolding.CurrentPrice);
                Assert.Contains("異常値確認待ち", vm.Dashboard.MarketRefreshStatus);
                Assert.True(vm.RetryFailedMarketDataCommand.CanExecute(null));
                var stockId = Assert.Single(repository.GetPositions()).Stock.Id;
                var anomalyRepository = new MarketAnomalyRepository(path);
                var candidate = Assert.Single(anomalyRepository.GetCandidates(stockId, includeResolved: false));
                Assert.Equal("株価急変", candidate.Category);

                anomalyRepository.Review(candidate.Id, "Dismissed", "分割後価格を証券会社表示で確認");
                vm.RetryFailedMarketDataCommand.Execute(null);
                Assert.Equal(500m, Assert.Single(repository.GetPositions()).CurrentHolding.CurrentPrice);
                Assert.False(vm.RetryFailedMarketDataCommand.CanExecute(null));
                Assert.Equal(4, provider.CallCount);

                provider.Currency = "USD";
                provider.Price = 510m;
                vm.StockList.RefreshAllCommand.Execute(null);
                var currencyCandidate = Assert.Single(anomalyRepository.GetCandidates(stockId, includeResolved: false));
                Assert.Equal("通貨不一致", currencyCandidate.Category);
                anomalyRepository.Review(currencyCandidate.Id, "Dismissed", "取得元の通貨表示を確認したが登録通貨は未修正");
                vm.RetryFailedMarketDataCommand.Execute(null);
                Assert.Equal(500m, Assert.Single(repository.GetPositions()).CurrentHolding.CurrentPrice);
                Assert.Contains("異常値確認待ち", vm.Dashboard.MarketRefreshStatus);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                createdApp?.Shutdown();
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { /* The assertion result is more useful than temp cleanup. */ }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The offline refresh test did not finish.");
        if (error is not null) throw error;
    }

    private sealed class SwitchableMarketService : IMarketDataService
    {
        public bool Fail { get; set; } = true;
        public decimal Price { get; set; } = 220m;
        public string Currency { get; set; } = "JPY";
        public int CallCount { get; private set; }
        public MarketDataResult GetQuote(string symbol, AppSettings settings)
        {
            CallCount++;
            return Fail
                ? MarketDataResult.Failure("apikey=super-secret-key network error",
                    new ApiFetchLogEntry { ErrorMessage = "apikey=super-secret-key network error" })
                : MarketDataResult.Success(new MarketDataQuote
                {
                    Symbol = symbol,
                    Currency = Currency,
                    CurrentPrice = Price,
                    PriceAcquiredAt = DateTime.Now.AddMinutes(1),
                    Source = "固定テスト"
                });
        }
    }

    private sealed class UnavailableRateService : IExchangeRateService
    {
        public ExchangeRateQuote GetUsdJpyRate() => new()
        {
            Rate = 0m,
            AcquiredAt = DateTime.MinValue,
            InputType = "Unavailable",
            Source = "オフライン"
        };
    }

    private sealed class OfflineHistoryService : IPriceHistoryService
    {
        public PriceHistoryResult GetHistory(string symbol, AppSettings settings, string range, string interval) =>
            PriceHistoryResult.Failure(symbol, "固定テスト", "オフライン");
    }
}
