using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class ReferencePurchaseCostWorkflowTests
{
    [Theory]
    [InlineData(20, 15.81, 0)]
    [InlineData(10, 16, 0)]
    [InlineData(20, 15.81, 40000)]
    public void ChangedHoldingBasisDoesNotReuseOldYenCost(decimal shares, decimal price, decimal newYenCost)
    {
        var existing = ReferencePosition();
        existing.Purchase.AcquisitionAmountJpy = 24000m;
        existing.Purchase.ExchangeRate = 150m;
        var holding = HoldingFor(existing, shares, price);
        holding.AcquisitionAmountJpy = newYenCost;

        var merged = Assert.Single(new BrokerHoldingMergeService().MergeHoldings(
            new[] { existing }, new[] { holding }, holding.SnapshotDate).Decisions).Merged!;

        Assert.Equal(newYenCost, merged.Purchase.AcquisitionAmountJpy);
        Assert.Equal(0m, merged.Purchase.ExchangeRate);
        Assert.Equal(0m, merged.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.Empty(merged.Purchase.ReferenceAcquisitionSource);
        var snapshot = new InvestmentCalculator().CreateSnapshot(merged);
        Assert.Equal(newYenCost > 0m, snapshot.HasKnownPurchaseCostJpy);
        Assert.Equal(newYenCost, snapshot.PurchaseTotalJpy);
        Assert.Equal(24000m, existing.Purchase.AcquisitionAmountJpy);
    }

    [Fact]
    public void SameHoldingBasisPreservesVerifiedYenCostWhenCsvOmitsIt()
    {
        var existing = ReferencePosition();
        existing.Purchase.AcquisitionAmountJpy = 24000m;
        existing.Purchase.ExchangeRate = 150m;
        var holding = HoldingFor(existing, 10m, 15.81m);
        var merged = Assert.Single(new BrokerHoldingMergeService().MergeHoldings(
            new[] { existing }, new[] { holding }, holding.SnapshotDate).Decisions).Merged!;

        Assert.Equal(24000m, new InvestmentCalculator().CreateSnapshot(merged).PurchaseTotalJpy);
        Assert.Equal(150m, merged.Purchase.ExchangeRate);
    }

    [Fact]
    public void EqualForeignTotalDoesNotKeepReferenceForDifferentQuantityAndPrice()
    {
        var existing = ReferencePosition();
        var holding = HoldingFor(existing, 20m, 7.905m);
        var merged = Assert.Single(new BrokerHoldingMergeService().MergeHoldings(
            new[] { existing }, new[] { holding }, holding.SnapshotDate).Decisions).Merged!;

        Assert.False(new InvestmentCalculator().CreateSnapshot(merged).HasKnownPurchaseCostJpy);
        Assert.Equal(0m, merged.Purchase.ReferenceAcquisitionAmountJpy);
    }

    [Fact]
    public void NisaSummaryMarksReferenceCostAndSuppressesIncompleteTotals()
    {
        var calculator = new InvestmentCalculator();
        var reference = calculator.CreateSnapshot(ReferencePosition());
        var knownPosition = ReferencePosition();
        knownPosition.Purchase.AcquisitionAmountJpy = 20000m;
        var known = calculator.CreateSnapshot(knownPosition);
        var summary = new NisaDashboardSummaryViewModel(AccountTypes.NisaGrowth, new[] { reference, known });
        Assert.StartsWith("概算 ", summary.PurchaseAmountJpy);
        Assert.StartsWith("概算 ", summary.GainLossJpy);
        Assert.StartsWith("概算 ", summary.GainLossRate);
        Assert.True(summary.HasPositiveGain);

        knownPosition.CurrentHolding.CurrentShares = 11m;
        knownPosition.Purchase.AcquisitionAmountJpy = 0m;
        var unknown = calculator.CreateSnapshot(knownPosition);
        summary = new NisaDashboardSummaryViewModel(AccountTypes.NisaGrowth, new[] { known, unknown });
        Assert.Equal("未確認", summary.PurchaseAmountJpy);
        Assert.Equal("算出不可", summary.GainLossJpy);
        Assert.Equal("算出不可", summary.GainLossRate);
        Assert.False(summary.HasPositiveGain);
        Assert.False(summary.HasNegativeGain);
    }

    [Fact]
    public void DashboardExplainsRecordedProfitAndSuppressesUnknownCostIndicators()
    {
        var vm = new DashboardViewModel();
        var summary = new DashboardSummary
        {
            ReferencePurchaseCostPositionCount = 1,
            TotalUnrealizedGainJpy = -1000m, TotalReturnJpy = -500m,
            FxIncludedUnrealizedGainJpy = -1000m
        };
        vm.Update(summary, Array.Empty<StockSnapshot>(), Array.Empty<PortfolioSnapshot>());
        Assert.Contains("概算", vm.TotalReturnLabel);
        Assert.Contains("運用成績", vm.PeriodReturnAvailability);
        Assert.Contains("確認して計算", vm.PeriodReturnAvailability);
        Assert.Contains("算出不可", vm.CapitalRecoveryAvailability);
        Assert.Contains("記録済み売却損益", vm.TotalReturnExplanation);
        Assert.StartsWith("概算 ", vm.FxIncludedUnrealizedGain);
        Assert.True(vm.HasNegativeGain);
        Assert.True(vm.HasNegativeReturn);
        Assert.False(vm.HasPositiveGain);
        Assert.False(vm.HasPositiveReturn);

        summary = new DashboardSummary
        {
            UnknownPurchaseCostPositionCount = 1, TotalUnrealizedGainJpy = -1000m,
            TotalReturnJpy = -500m, FxIncludedUnrealizedGainJpy = -1000m
        };
        vm.Update(summary, Array.Empty<StockSnapshot>(), Array.Empty<PortfolioSnapshot>());
        Assert.Contains("算出不可", vm.CapitalRecoveryAvailability);
        Assert.Equal("算出不可", vm.FxIncludedUnrealizedGain);
        Assert.False(vm.HasNegativeGain);
        Assert.False(vm.HasNegativeReturn);
    }

    private static BrokerHoldingRecord HoldingFor(StockPosition position, decimal shares, decimal price) => new()
    {
        Broker = position.Stock.Broker, Ticker = position.Stock.Ticker,
        Account = position.Stock.AccountType, Currency = position.Stock.Currency,
        Shares = shares, AverageAcquisitionPrice = price,
        PurchaseExchangeRate = 0m, CurrentExchangeRate = 158m, CurrentPrice = 21m,
        SnapshotDate = new DateTime(2026, 10, 15), Source = "SBI保有残高CSV"
    };

    [Theory]
    [InlineData(null)]
    [InlineData("現在株数・現在株価だけ証券会社データで更新")]
    public void HoldingRefreshPreservesReferenceCostAndItsSource(string? mergePolicy)
    {
        var existing = ReferencePosition();
        var holding = new BrokerHoldingRecord
        {
            Broker = existing.Stock.Broker, Ticker = existing.Stock.Ticker,
            Account = existing.Stock.AccountType, Currency = "USD",
            Shares = 10m, AverageAcquisitionPrice = 15.81m,
            PurchaseExchangeRate = 0m, CurrentPrice = 21m, CurrentExchangeRate = 158m,
            SnapshotDate = new DateTime(2026, 10, 14), Source = "SBI保有残高CSV"
        };

        var decision = Assert.Single(new BrokerHoldingMergeService().MergeHoldings(
            new[] { existing }, new[] { holding }, holding.SnapshotDate, mergePolicy).Decisions);

        Assert.Equal(BrokerMergeAction.Overwrite, decision.Action);
        var merged = Assert.IsType<StockPosition>(decision.Merged);
        Assert.Equal(21m, merged.CurrentHolding.CurrentPrice);
        AssertReferenceCost(merged);
        Assert.Equal(20m, existing.CurrentHolding.CurrentPrice);
    }

    [Fact]
    public void EditingMemoRoundTripsUnknownFxReferenceCostAndAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reference_editor_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var original = ReferencePosition();
            original.Stock.Id = 0;
            repository.SavePosition(original);
            var editor = new StockEditorViewModel(position => repository.SavePosition(position), _ => { });
            editor.Load(Assert.Single(repository.GetPositions()));
            editor.StockMemo = "メモだけ更新";

            editor.SaveCommand.Execute(null);

            Assert.Equal("保存しました。", editor.ErrorMessage);
            var saved = Assert.Single(repository.GetPositions());
            Assert.Equal("メモだけ更新", saved.Stock.Memo);
            Assert.Equal(AccountTypes.Specific, saved.Stock.AccountType);
            Assert.Equal("特定", saved.Stock.CustodyType);
            AssertReferenceCost(saved);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Shares")]
    [InlineData("UnitPrice")]
    [InlineData("Date")]
    [InlineData("Currency")]
    [InlineData("ExchangeRate")]
    public void ChangingPurchaseBasisDoesNotReusePreviousReferenceCost(string changedField)
    {
        StockPosition? saved = null;
        var editor = new StockEditorViewModel(position => saved = position, _ => { });
        editor.Load(ReferencePosition());
        switch (changedField)
        {
            case "Shares": editor.PurchaseShares = 11m; break;
            case "UnitPrice": editor.PurchaseUnitPrice = 16m; break;
            case "Date": editor.PurchaseDate = editor.PurchaseDate.AddDays(1); break;
            case "Currency": editor.Currency = "JPY"; break;
            case "ExchangeRate":
                editor.PurchaseExchangeRate = 150m;
                editor.PurchaseExchangeRateAcquiredAt = editor.PurchaseDate;
                editor.PurchaseExchangeRateSource = "取得日の為替記録";
                break;
        }

        editor.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        if (changedField == "ExchangeRate")
        {
            Assert.Equal(23715m, saved.Purchase.ReferenceAcquisitionAmountJpy);
            Assert.Equal(158.10m, saved.Purchase.ReferenceAcquisitionForeignAmount);
            Assert.Contains("入力した参考為替", saved.Purchase.ReferenceAcquisitionSource);
            Assert.True(new InvestmentCalculator().CreateSnapshot(saved).IsReferencePurchaseCostJpy);
        }
        else
        {
            Assert.Equal(0m, saved.Purchase.ReferenceAcquisitionAmountJpy);
            Assert.Equal(0m, saved.Purchase.ReferenceAcquisitionForeignAmount);
            Assert.Empty(saved.Purchase.ReferenceAcquisitionSource);
            Assert.False(new InvestmentCalculator().CreateSnapshot(saved).IsReferencePurchaseCostJpy);
        }
    }

    [Fact]
    public void NewPositionCanRemainUnknownWithoutInheritingPreviousReferenceCost()
    {
        StockPosition? saved = null;
        var editor = new StockEditorViewModel(position => saved = position, _ => { });
        editor.Load(ReferencePosition());
        editor.NewCommand.Execute(null);
        editor.Name = "New stock";
        editor.Ticker = "NEW";
        editor.PurchaseShares = 10m;
        editor.PurchaseUnitPrice = 15.81m;
        editor.CurrentShares = 10m;
        editor.PurchaseExchangeRate = 0m;

        editor.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(0m, saved.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownPurchaseCostJpy);

        editor.PurchaseExchangeRate = 150m;
        editor.PurchaseDate = new DateTime(2023, 9, 12);
        editor.PurchaseExchangeRateAcquiredAt = editor.PurchaseDate;
        editor.PurchaseExchangeRateSource = "取得日の為替記録";
        editor.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(23715m, saved.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.Contains("入力した参考為替", saved.Purchase.ReferenceAcquisitionSource);
        Assert.True(new InvestmentCalculator().CreateSnapshot(saved).IsReferencePurchaseCostJpy);
        Assert.Equal(AccountTypes.Unknown, saved.Stock.AccountType);
    }

    [Fact]
    public void NewForeignPositionDoesNotUseTodaysQuoteForHistoricalOrUnknownAcquisition()
    {
        StockPosition? saved = null;
        var editor = new StockEditorViewModel(position => saved = position, _ => { });
        editor.Name = "Example";
        editor.Ticker = "EX";
        editor.PurchaseShares = 10m;
        editor.PurchaseUnitPrice = 20m;
        editor.CurrentShares = 10m;
        editor.PurchaseExchangeRate = 150m;

        editor.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(DateTime.MinValue, saved.Purchase.PurchaseDate);
        Assert.Equal(0m, saved.Purchase.ExchangeRate);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownPurchaseCostJpy);

        editor.PurchaseDate = new DateTime(2023, 9, 12);
        editor.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(0m, saved.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.False(new InvestmentCalculator().CreateSnapshot(saved).HasKnownPurchaseCostJpy);

        editor.PurchaseExchangeRateAcquiredAt = editor.PurchaseDate;
        editor.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(0m, saved.Purchase.ReferenceAcquisitionAmountJpy);

        editor.PurchaseExchangeRateSource = "取得日の為替記録";
        editor.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(30_000m, saved.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.True(new InvestmentCalculator().CreateSnapshot(saved).IsReferencePurchaseCostJpy);
    }

    [Fact]
    public void UnknownCostDoesNotDisplayGainOrLossColor()
    {
        var position = ReferencePosition();
        position.CurrentHolding.CurrentShares = 11m;
        var row = new StockRowViewModel(new InvestmentCalculator().CreateSnapshot(position));

        Assert.Equal("未確認", row.GainLossJpy);
        Assert.False(row.HasPositiveGain);
        Assert.False(row.HasNegativeGain);
    }

    private static void AssertReferenceCost(StockPosition position)
    {
        Assert.Equal(0m, position.Purchase.ExchangeRate);
        Assert.Equal(0m, position.Purchase.AcquisitionAmountJpy);
        Assert.Equal(23_262.267m, position.Purchase.ReferenceAcquisitionAmountJpy);
        Assert.Equal(158.10m, position.Purchase.ReferenceAcquisitionForeignAmount);
        Assert.Equal("FRED DEXJPUS / 約定日参考換算", position.Purchase.ReferenceAcquisitionSource);
        var snapshot = new InvestmentCalculator().CreateSnapshot(position);
        Assert.True(snapshot.IsReferencePurchaseCostJpy);
        Assert.Equal(23_262.267m, snapshot.PurchaseTotalJpy);
    }

    private static StockPosition ReferencePosition() => new()
    {
        Stock = new Stock
        {
            Id = 1, Ticker = "CMBT", Name = "CMBT", Currency = "USD", Broker = "SBI証券",
            AccountType = AccountTypes.Specific, CustodyType = "特定"
        },
        Purchase = new Purchase
        {
            PurchaseDate = new DateTime(2023, 9, 12), Shares = 10m, UnitPrice = 15.81m,
            ExchangeRate = 0m, ReferenceAcquisitionAmountJpy = 23_262.267m,
            ReferenceAcquisitionForeignAmount = 158.10m,
            ReferenceAcquisitionSource = "FRED DEXJPUS / 約定日参考換算"
        },
        CurrentHolding = new CurrentHolding
        {
            CurrentShares = 10m, CurrentPrice = 20m, CurrentExchangeRate = 158m,
            UpdatedAt = new DateTime(2026, 10, 12)
        }
    };
}
