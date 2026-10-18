using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class PurchaseCostEvidenceTests
{
    [Fact]
    public void ForeignDefaultOneAndMissingJapaneseUnitPriceAreNotConfirmedCosts()
    {
        var calculator = new InvestmentCalculator();
        var foreign = calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Currency = "USD", Ticker = "A" },
            Purchase = new Purchase { Shares = 10m, UnitPrice = 20m },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 25m, CurrentExchangeRate = 150m }
        });
        var domestic = calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Currency = "JPY", Ticker = "B" },
            Purchase = new Purchase { Shares = 10m },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 1_000m }
        });

        Assert.False(foreign.HasKnownPurchaseCostJpy);
        Assert.Equal(0m, foreign.PurchaseTotalJpy);
        Assert.Equal(0m, foreign.UnrealizedGainJpy);
        Assert.False(domestic.HasKnownPurchaseCostJpy);
        Assert.Equal(0m, domestic.UnrealizedGainJpy);
        var costQuality = new DataQualityService().BuildForPosition(foreign.Position, new DateTime(2026, 10, 18));
        Assert.Equal(DataQualityStates.Missing,
            costQuality.Single(x => x.FieldName == "円建て取得額").ConfidenceLevel);
        Assert.Throws<InvalidOperationException>(() => new PortfolioAnalyticsService().CreatePortfolioSnapshot(
            new[] { foreign }, Array.Empty<DividendPayment>(), 0m, 150m,
            new DateTime(2026, 10, 18)));
    }

    [Fact]
    public void MissingCurrentPriceOrForeignRateCannotProduceYenGain()
    {
        var calculator = new InvestmentCalculator();
        var missingPrice = calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Currency = "JPY" },
            Purchase = new Purchase { Shares = 10m, UnitPrice = 100m },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m }
        });
        var missingFx = calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Currency = "USD" },
            Purchase = new Purchase { Shares = 10m, UnitPrice = 10m, AcquisitionAmountJpy = 15_000m },
            CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 12m }
        });

        Assert.False(missingPrice.HasKnownCurrentMarketValueJpy);
        Assert.False(missingFx.HasKnownCurrentMarketValueJpy);
        Assert.Equal(0m, missingPrice.UnrealizedGainJpy);
        Assert.Equal(0m, missingFx.UnrealizedGainJpy);
        Assert.Equal("算出不可", new StockRowViewModel(missingFx).MarketValueJpy);
        var nisa = new NisaDashboardSummaryViewModel(AccountTypes.NisaGrowth, new[] { missingFx });
        Assert.Equal("算出不可", nisa.MarketValueJpy);
        Assert.Equal("算出不可", nisa.GainLossJpy);
        var fxQuality = new DataQualityService().BuildForPosition(missingFx.Position, new DateTime(2026, 10, 18));
        var fx = fxQuality.Single(x => x.FieldName == "現在為替");
        Assert.Equal("未確認", fx.Value);
        Assert.Equal(DataQualityStates.Missing, fx.ConfidenceLevel);
        Assert.Throws<InvalidOperationException>(() => new PortfolioAnalyticsService().CreatePortfolioSnapshot(
            new[] { missingPrice, missingFx }, Array.Empty<DividendPayment>(), 0m, 150m,
            new DateTime(2026, 10, 18)));
        var summary = calculator.CreateDashboardSummary(new[] { missingPrice, missingFx },
            Array.Empty<DividendPayment>(), null, new DateTime(2026, 10, 18));
        Assert.Equal(2, summary.UnknownCurrentMarketValuePositionCount);
        var dashboard = new DashboardViewModel();
        dashboard.Update(summary, new[] { missingPrice, missingFx }, Array.Empty<PortfolioSnapshot>());
        Assert.Equal("算出不可", dashboard.TotalCurrentMarketValue);
        Assert.Equal("算出不可", dashboard.TotalUnrealizedGain);
        Assert.False(dashboard.RecordSnapshotCommand.CanExecute(null));
        Assert.Contains("記録できません", dashboard.RecordSnapshotStatus);
        Assert.Contains("為替: 未取得 1 件", dashboard.DataCoverageStatus);
    }

    [Fact]
    public void SimpleRegistrationKeepsQuoteBasedCostAsReferenceAndDoesNotInventMissingPurchasePrice()
    {
        StockPosition? saved = null;
        var viewModel = new SimpleRegistrationViewModel(
            position => saved = position,
            new InvestmentCalculator(), new FixedRateService(), new EmptyLookupService(),
            new EmptyMarketDataService(), () => new AppSettings(), _ => { });
        viewModel.Ticker = "EX";
        viewModel.Name = "Example";
        viewModel.PurchaseShares = 10m;
        viewModel.PurchaseDate = DateTime.Today;
        viewModel.PurchaseUnitPrice = 20m;
        viewModel.CurrentPrice = 25m;
        viewModel.CurrentExchangeRate = 151m;
        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(0m, saved.Purchase.ExchangeRate);
        Assert.Equal(30_000m, saved.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.Equal(0m, saved.Purchase.AcquisitionAmountJpy);
        var snapshot = new InvestmentCalculator().CreateSnapshot(saved);
        Assert.True(snapshot.IsReferencePurchaseCostJpy);
        Assert.StartsWith("概算 ", viewModel.PurchaseTotalJpy);
        var quickRegistrationDate = new DataQualityService().BuildForPosition(saved, DateTime.Today)
            .Single(x => x.FieldName == "取得日");
        Assert.Equal(DataQualityStates.Estimated, quickRegistrationDate.ConfidenceLevel);
        Assert.Contains("保存日が自動設定", quickRegistrationDate.Memo);

        viewModel.PurchaseDate = DateTime.Today.AddYears(-2);
        saved = null;
        viewModel.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(0m, saved.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownPurchaseCostJpy);

        viewModel.PurchaseDate = null;
        saved = null;
        viewModel.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(DateTime.MinValue, saved.Purchase.PurchaseDate);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownPurchaseCostJpy);
        var unknownDate = new DataQualityService().BuildForPosition(saved, DateTime.Today)
            .Single(x => x.FieldName == "取得日");
        Assert.Equal("未確認", unknownDate.Value);
        Assert.Equal(DataQualityStates.Missing, unknownDate.ConfidenceLevel);

        viewModel.PurchaseDate = DateTime.Today;

        viewModel.PurchaseUnitPrice = 0m;
        saved = null;
        viewModel.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(0m, saved.Purchase.UnitPrice);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownPurchaseCostJpy);

        viewModel.PurchaseUnitPrice = 20m;
        viewModel.CurrentPrice = 0m;
        saved = null;
        viewModel.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(0m, saved.CurrentHolding.CurrentPrice);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownCurrentMarketValueJpy);
        Assert.Equal("算出不可", viewModel.UnrealizedGainJpy);

        viewModel.Currency = "EUR";
        viewModel.PurchaseUnitPrice = 20m;
        viewModel.CurrentPrice = 25m;
        viewModel.PurchaseExchangeRate = 150m;
        saved = null;
        viewModel.SaveCommand.Execute(null);
        Assert.Null(saved);
        Assert.Contains("為替レート", viewModel.Message);
    }

    [Fact]
    public void SimpleRegistrationDoesNotTreatAnOldQuoteAsTodaysPurchaseRate()
    {
        StockPosition? saved = null;
        var viewModel = new SimpleRegistrationViewModel(
            position => saved = position,
            new InvestmentCalculator(), new StaleRateService(), new EmptyLookupService(),
            new EmptyMarketDataService(), () => new AppSettings(), _ => { });
        viewModel.Ticker = "EX";
        viewModel.Name = "Example";
        viewModel.PurchaseDate = DateTime.Today;
        viewModel.PurchaseShares = 10m;
        viewModel.PurchaseUnitPrice = 20m;
        viewModel.CurrentPrice = 25m;
        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(0m, saved.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownPurchaseCostJpy);
    }

    private sealed class FixedRateService : IExchangeRateService
    {
        public ExchangeRateQuote GetUsdJpyRate() => new() { Rate = 150m, Source = "テスト為替" };
    }

    private sealed class StaleRateService : IExchangeRateService
    {
        public ExchangeRateQuote GetUsdJpyRate() => new()
        {
            Rate = 150m, AcquiredAt = DateTime.Today.AddDays(-1), Source = "前日の為替"
        };
    }

    private sealed class EmptyLookupService : IStockLookupService
    {
        public StockLookupResult? Find(string query) => null;
    }

    private sealed class EmptyMarketDataService : IMarketDataService
    {
        public MarketDataResult GetQuote(string symbol, AppSettings settings) => MarketDataResult.Failure("未取得");
    }
}
