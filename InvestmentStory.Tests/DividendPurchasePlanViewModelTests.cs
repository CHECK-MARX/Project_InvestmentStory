using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class DividendPurchasePlanViewModelTests
{
    [Fact]
    public void RestoredPlan_BecomesDirtyAfterEditAndCleanAfterSave()
    {
        var stored = CreateStoredPlan();
        DividendPurchasePlan? saved = null;
        var viewModel = CreateViewModel(() => stored, plan =>
        {
            saved = plan;
            return 42;
        });

        viewModel.UpdateDividendPortfolio(Array.Empty<StockPosition>(), Array.Empty<TaxProfile>());

        Assert.Equal("保存済み計画", viewModel.PassiveIncomePlanName);
        Assert.Equal(2028, viewModel.DividendPlanTargetYear);
        Assert.Equal(new DateTime(2028, 5, 20), viewModel.DividendPlanPurchaseDate);
        Assert.False(viewModel.HasUnsavedDividendPlanChanges);
        var restoredNewStock = Assert.Single(viewModel.NewDividendSimulationRows);
        Assert.Equal("PG", restoredNewStock.Ticker);
        Assert.Equal(10m, restoredNewStock.PlannedAdditionalShares);
        Assert.Equal("野村證券", restoredNewStock.PlannedBroker);
        Assert.Equal(AccountTypes.NisaGrowth, restoredNewStock.PlannedAccountType);

        restoredNewStock.PlannedAdditionalShares = 15m;

        Assert.True(viewModel.HasUnsavedDividendPlanChanges);
        Assert.Equal("未保存の変更があります", viewModel.DividendPlanSaveStatus);
        Assert.True(viewModel.TrySaveDividendPurchasePlan());
        Assert.False(viewModel.HasUnsavedDividendPlanChanges);
        Assert.Contains("計画『保存済み計画』を保存しました。", viewModel.DividendPlanSaveStatus);
        Assert.NotNull(saved);
        Assert.Equal(15m, Assert.Single(saved.Items).PlannedAdditionalShares);
    }

    [Fact]
    public void SavedNewStock_IsRestoredByANewViewModelInstance()
    {
        DividendPurchasePlan? stored = null;
        var first = CreateViewModel(() => stored, plan =>
        {
            plan.Id = 77;
            stored = plan;
            return plan.Id;
        });
        first.UpdateDividendPortfolio(Array.Empty<StockPosition>(), Array.Empty<TaxProfile>());
        first.AddNewDividendStockCommand.Execute(null);
        var row = Assert.Single(first.NewDividendSimulationRows);
        row.Ticker = "KO";
        row.Name = "Coca-Cola Company";
        row.PlannedAdditionalShares = 25m;
        row.CurrentPrice = 70.25m;
        row.Country = "米国";
        row.Currency = "USD";
        row.Broker = "SBI証券";
        row.PlannedBroker = "野村證券";
        row.PlannedAccountType = AccountTypes.Specific;
        row.AnnualDividendPerShare = 2.04m;
        Assert.True(first.TrySaveDividendPurchasePlan());

        var second = CreateViewModel(() => stored, _ => 77);
        second.UpdateDividendPortfolio(Array.Empty<StockPosition>(), Array.Empty<TaxProfile>());
        var restored = Assert.Single(second.NewDividendSimulationRows);

        Assert.Equal("KO", restored.Ticker);
        Assert.Equal("Coca-Cola Company", restored.Name);
        Assert.Equal(25m, restored.PlannedAdditionalShares);
        Assert.Equal(70.25m, restored.CurrentPrice);
        Assert.Equal("United States", restored.Country);
        Assert.Equal("米国", DividendSimulationSelectionOptions.ToCountryDisplayName(restored.Country));
        Assert.Equal("USD", restored.Currency);
        Assert.Equal("野村證券", restored.PlannedBroker);
        Assert.Equal(AccountTypes.Specific, restored.PlannedAccountType);
        Assert.Equal(2.04m, restored.AnnualDividendPerShare);
        Assert.False(second.HasUnsavedDividendPlanChanges);
    }

    [Fact]
    public void TargetYear_UpdatesPageAndDividendLabels()
    {
        var viewModel = CreateViewModel(() => null, _ => 1);

        viewModel.DividendPlanTargetYear = 2030;

        Assert.Equal("2030年の購入計画シミュレーション", viewModel.DividendPlanPageTitle);
        Assert.Equal("2030年の配当見込み（税引後）", viewModel.CurrentTargetYearDividendLabel);
        Assert.Equal("購入後の2030年配当見込み", viewModel.PostAddTargetYearDividendLabel);
        Assert.Equal("2030年に増える配当", viewModel.TargetYearIncreaseLabel);
        Assert.Equal("2030年に受け取れない配当", viewModel.TargetYearMissedLabel);
        Assert.Equal("2031年の年間配当見込み", viewModel.NextYearDividendLabel);
    }

    [Fact]
    public void TargetYearText_DoesNotCommitPartialInput()
    {
        var viewModel = CreateViewModel(() => null, _ => 1);
        var originalYear = viewModel.DividendPlanTargetYear;

        viewModel.DividendPlanTargetYearText = "2";
        viewModel.DividendPlanTargetYearText = "20";
        viewModel.DividendPlanTargetYearText = "200";

        Assert.Equal(originalYear, viewModel.DividendPlanTargetYear);
        Assert.Equal("200", viewModel.DividendPlanTargetYearText);
        Assert.Equal(string.Empty, viewModel.DividendPlanTargetYearValidationMessage);
    }

    [Fact]
    public void TargetYearText_CommitsAValidFourDigitYearAndSynchronizesPurchaseDate()
    {
        var viewModel = CreateViewModel(() => null, _ => 1);
        var targetYear = DateTime.Today.Year + 1;

        viewModel.DividendPlanTargetYearText = targetYear.ToString();

        Assert.Equal(targetYear, viewModel.DividendPlanTargetYear);
        Assert.Equal(targetYear, viewModel.DividendPlanPurchaseDate.Year);
        Assert.Equal(string.Empty, viewModel.DividendPlanTargetYearValidationMessage);
    }

    [Fact]
    public void RestoredPlan_WithYear2000_IsRepairedAndPersistedWithoutChangingPlanItems()
    {
        var stored = CreateStoredPlan();
        stored.TargetYear = 2000;
        stored.PlannedPurchaseDate = new DateTime(2000, 7, 14);
        DividendPurchasePlan? repaired = null;

        var viewModel = CreateViewModel(() => stored, plan =>
        {
            repaired = plan;
            return plan.Id;
        });

        Assert.Equal(DateTime.Today.Year, viewModel.DividendPlanTargetYear);
        Assert.Equal(new DateTime(DateTime.Today.Year, 7, 14), viewModel.DividendPlanPurchaseDate);
        Assert.Equal(DateTime.Today.Year.ToString(), viewModel.DividendPlanTargetYearText);
        Assert.Contains("補正しました", viewModel.DividendPlanSaveStatus);
        Assert.NotNull(repaired);
        Assert.Equal(DateTime.Today.Year, repaired.TargetYear);
        Assert.Equal(new DateTime(DateTime.Today.Year, 7, 14), repaired.PlannedPurchaseDate);
        Assert.Single(repaired.Items);
    }

    private static SimulationViewModel CreateViewModel(
        Func<DividendPurchasePlan?> loadPlan,
        Func<DividendPurchasePlan, int> savePlan) =>
        new(
            new InvestmentCalculator(),
            new MutualFundAssetSimulationService(),
            () => new AppSettings(),
            _ => { },
            loadDividendPurchasePlan: loadPlan,
            saveDividendPurchasePlan: savePlan);

    private static DividendPurchasePlan CreateStoredPlan() => new()
    {
        Id = 42,
        Name = "保存済み計画",
        TargetYear = 2028,
        PlannedPurchaseDate = new DateTime(2028, 5, 20),
        DisplayUnit = DividendPurchasePlanDisplayUnits.Account,
        TargetAnnualNetDividendJpy = 1_300_000m,
        CreatedAt = new DateTime(2027, 12, 1),
        UpdatedAt = new DateTime(2028, 1, 2, 3, 4, 0),
        Items = new[]
        {
            new DividendPurchasePlanItem
            {
                ItemOrder = 0,
                IsNewStock = true,
                PlanKey = "New:PG",
                CanonicalSecurityKey = "PG|US",
                Ticker = "PG",
                Name = "The Procter & Gamble Company",
                Broker = "野村證券",
                AccountType = AccountTypes.NisaGrowth,
                Country = "米国",
                Currency = "USD",
                CurrentPrice = 150m,
                ExchangeRate = 162m,
                AnnualDividendPerShare = 4.2m,
                PlannedAdditionalShares = 10m,
                PlannedBroker = "野村證券",
                PlannedAccountType = AccountTypes.NisaGrowth,
                MarketDataSource = "手入力",
                MarketDataStatus = "手入力",
                DataQuality = "手入力"
            }
        }
    };
}
