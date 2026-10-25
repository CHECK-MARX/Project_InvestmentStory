using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.Tests;

public sealed class NisaCapacityTests
{
    private static readonly DateTime AsOf = new(2026, 10, 24);

    [Fact]
    public void MissingHistoryLeavesCapacityUnknownWhileValuationIsShown()
    {
        var snapshot = new StockSnapshot
        {
            Position = new StockPosition { Stock = new Stock { AccountType = AccountTypes.NisaGrowth } },
            CurrentMarketValueJpy = 1_400_000m,
            HasKnownCurrentMarketValueJpy = true
        };
        var result = new NisaCapacityService().Build([snapshot], [],
            new NisaHistoryState(null, null, ""), AsOf);
        Assert.Equal(1_400_000m, result.TotalValuationJpy);
        Assert.Null(result.LifetimeRemainingJpy);
        Assert.Null(result.AnnualGrowthUsedJpy);
    }

    [Fact]
    public void PriorYearSaleReleasesBookCostButCurrentYearSaleDoesNot()
    {
        var records = new[]
        {
            Item(1, new DateTime(2024, 4, 1), AccountTypes.NisaGrowth, NisaTransactionKinds.Buy, 1_000_000m),
            Item(2, new DateTime(2025, 6, 1), AccountTypes.NisaGrowth, NisaTransactionKinds.Sell, 200_000m),
            Item(3, new DateTime(2026, 6, 1), AccountTypes.NisaGrowth, NisaTransactionKinds.Sell, 300_000m),
            Item(4, new DateTime(2026, 7, 1), AccountTypes.NisaAccumulation, NisaTransactionKinds.Buy, 100_000m)
        };
        var state = new NisaHistoryState(AsOf, AsOf.ToUniversalTime(), "証券会社の全履歴を照合");
        var result = new NisaCapacityService().Build([], records, state, AsOf);
        Assert.Equal(900_000m, result.LifetimeUsedJpy);
        Assert.Equal(17_100_000m, result.LifetimeRemainingJpy);
        Assert.Equal(11_200_000m, result.GrowthLifetimeRemainingJpy);
        Assert.Equal(100_000m, result.AnnualAccumulationUsedJpy);
        Assert.Equal(1_100_000m, result.AnnualAccumulationRemainingJpy);
    }

    [Fact]
    public void PastYearOverLimitIsNotHiddenByLaterSale()
    {
        var records = new[]
        {
            Item(1, new DateTime(2024, 4, 1), AccountTypes.NisaGrowth, NisaTransactionKinds.Buy, 2_500_000m),
            Item(2, new DateTime(2025, 6, 1), AccountTypes.NisaGrowth, NisaTransactionKinds.Sell, 1_000_000m)
        };
        var state = new NisaHistoryState(AsOf, AsOf.ToUniversalTime(), "全取引を照合");

        var result = new NisaCapacityService().Build([], records, state, AsOf);

        Assert.Null(result.LifetimeRemainingJpy);
        Assert.Contains("矛盾", result.Status);
    }

    [Fact]
    public void SaleCannotExceedPreviouslyBoughtBookCostInTheSameFrame()
    {
        var records = new[]
        {
            Item(1, new DateTime(2024, 4, 1), AccountTypes.NisaGrowth, NisaTransactionKinds.Sell, 100_000m),
            Item(2, new DateTime(2025, 6, 1), AccountTypes.NisaGrowth, NisaTransactionKinds.Buy, 200_000m)
        };
        var state = new NisaHistoryState(AsOf, AsOf.ToUniversalTime(), "全取引を照合");

        var result = new NisaCapacityService().Build([], records, state, AsOf);

        Assert.Null(result.LifetimeRemainingJpy);
        Assert.Contains("矛盾", result.Status);
    }

    [Fact]
    public void EditingLedgerInvalidatesCompletenessConfirmation()
    {
        var path = Path.Combine(Path.GetTempPath(), "InvestmentStory_nisa_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            new DatabaseInitializer().Initialize(path);
            var repo = new NisaHistoryRepository(path);
            repo.Save(Item(0, new DateTime(2026, 1, 8), AccountTypes.NisaGrowth,
                NisaTransactionKinds.Buy, 300_000m));
            repo.ConfirmCompleteThrough(DateTime.Today, "全証券会社の明細と照合");
            Assert.NotNull(repo.GetHistoryState().CompleteThroughDate);
            repo.Save(Item(0, new DateTime(2026, 2, 8), AccountTypes.NisaGrowth,
                NisaTransactionKinds.Buy, 100_000m));
            Assert.Null(repo.GetHistoryState().CompleteThroughDate);
            Assert.Equal(2, repo.GetTransactions().Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CannotConfirmEmptyLedgerWhenNewNisaHoldingExists()
    {
        var path = Path.Combine(Path.GetTempPath(), "InvestmentStory_nisa_vm_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            new DatabaseInitializer().Initialize(path);
            var repo = new NisaHistoryRepository(path);
            var vm = new NisaViewModel(() => repo);
            vm.Load([new StockSnapshot
            {
                Position = new StockPosition { Stock = new Stock { AccountType = AccountTypes.NisaGrowth } },
                CurrentMarketValueJpy = 100_000m
            }]);
            vm.HistoryEvidence = "確認済み";
            vm.ConfirmHistoryCommand.Execute(null);
            Assert.Null(repo.GetHistoryState().CompleteThroughDate);
            Assert.Contains("買付履歴", vm.Message);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ScreenSeparatesMarketValueFromBookCostBasedCapacity()
    {
        var path = Path.Combine(Path.GetTempPath(), "InvestmentStory_nisa_screen_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            new DatabaseInitializer().Initialize(path);
            var repo = new NisaHistoryRepository(path);
            repo.Save(Item(0, new DateTime(2026, 1, 8), AccountTypes.NisaGrowth,
                NisaTransactionKinds.Buy, 300_000m));
            repo.ConfirmCompleteThrough(AsOf, "全証券会社の履歴を照合");
            var vm = new NisaViewModel(() => repo);
            vm.Load([new StockSnapshot
            {
                Position = new StockPosition
                {
                    Stock = new Stock { AccountType = AccountTypes.NisaGrowth },
                    CurrentHolding = new CurrentHolding { CurrentShares = 1m, CurrentPrice = 1_400_000m }
                },
                CurrentMarketValueJpy = 1_400_000m,
                HasKnownCurrentMarketValueJpy = true,
                PurchaseTotalJpy = 300_000m,
                HasKnownPurchaseCostJpy = true
            }], AsOf);

            Assert.Contains("1,400,000円", vm.Valuation);
            Assert.Contains("300,000円", vm.AnnualUse);
            Assert.Contains("17,700,000円", vm.Remaining);
            Assert.Contains("制度年度 2026", vm.AsOfLabel);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static NisaTransaction Item(long id, DateTime date, string frame, string kind, decimal cost) =>
        new(id, date, frame, kind, cost, "証券CSV", "取引明細", NisaCapacityService.RuleVersion);
}
