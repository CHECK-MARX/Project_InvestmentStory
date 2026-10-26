using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Tests;

public sealed class StockListFilterTests
{
    [Fact]
    public void SearchAndCompoundFilters_KeepDisplayedTotalsAlignedAndRestoreSavedConditions()
    {
        string? savedJson = null;
        var snapshots = new[]
        {
            Snapshot("NVDA", "エヌビディア", "野村証券", "情報技術", 1000m),
            Snapshot("MO", "アルトリア", "SBI証券", "生活必需品", 2000m),
            Snapshot("MSFT", "マイクロソフト", "野村証券", "情報技術", 3000m)
        };
        var viewModel = Create(json => savedJson = json);
        viewModel.Update(snapshots);
        viewModel.SearchText = "m";
        viewModel.SelectedBroker = "野村証券";
        viewModel.SelectedSector = "情報技術";
        Assert.Single(viewModel.Rows);
        Assert.Equal("MSFT", viewModel.Rows[0].Ticker);
        Assert.Contains("3,000", viewModel.VisibleSummary);
        viewModel.SaveFiltersCommand.Execute(null);

        var restored = Create(_ => { }, savedJson);
        restored.Update(snapshots);
        Assert.Single(restored.Rows);
        Assert.True(restored.HasAdvancedFilters);
        Assert.Equal("MSFT", restored.Rows[0].Ticker);
        restored.ResetFiltersCommand.Execute(null);
        Assert.Equal(3, restored.Rows.Count);
    }

    [Fact]
    public void VisibleSummaryDoesNotPresentAPartialMarketValueAsTheTotal()
    {
        var known = Snapshot("A", "A", "SBI証券", "情報技術", 1000m);
        var unknown = Snapshot("B", "B", "SBI証券", "情報技術", 0m, hasKnownMarketValue: false);
        var viewModel = Create(_ => { });

        viewModel.Update(new[] { known, unknown });

        Assert.Contains("評価額合計 算出不可", viewModel.VisibleSummary);
        Assert.Contains("確認済み 1,000円 / 未確認 1 件", viewModel.VisibleSummary);
    }

    [Fact]
    public void ColumnLayoutSurvivesFilterSaveAndViewModelReload()
    {
        string? saved = null;
        var viewModel = Create(json => saved = json);
        const string layout = "[{\"Index\":0,\"Width\":100,\"DisplayIndex\":0}]";
        viewModel.SaveColumnLayout(layout);
        Assert.NotNull(saved);
        viewModel.SearchText = "NVDA";
        viewModel.SaveFiltersCommand.Execute(null);

        var restored = Create(_ => { }, saved);
        Assert.Equal(layout, restored.ColumnLayoutJson);
        Assert.Equal("NVDA", restored.SearchText);

        var legacy = Create(_ => { },
            "{\"SearchText\":\"MO\",\"Broker\":\"すべて\",\"Account\":\"すべて\",\"Sector\":\"すべて\",\"Quality\":\"すべて\",\"Filter\":\"現在保有のみ\",\"Grouping\":\"ポジション別\"}");
        Assert.Equal(string.Empty, legacy.ColumnLayoutJson);
    }

    [Fact]
    public void AggregateSumCanExpandBackToSeparateAccounts()
    {
        var taxable = Snapshot("NVDA", "NVIDIA", "SBI", "Technology", 1000m);
        var nisa = Snapshot("NVDA", "NVIDIA", "SBI", "Technology", 2000m);
        nisa.Position.Stock.AccountType = AccountTypes.NisaGrowth;
        var vm = Create(_ => { });
        vm.Update([taxable, nisa]);
        vm.SelectedGroupingMode = "銘柄集約";

        var aggregate = Assert.Single(vm.Rows);
        Assert.Contains("3,000", aggregate.MarketValueJpy);
        Assert.Equal(2, aggregate.Snapshots.Count);
        vm.ExpandSelectedCommand.Execute(null);

        Assert.Equal("ポジション別", vm.SelectedGroupingMode);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(2, vm.Rows.Select(x => x.AccountType).Distinct().Count());
        Assert.NotNull(vm.SelectedRow);
    }

    private static StockListViewModel Create(Action<string> save, string? json = null) =>
        new(() => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, () => { },
            savedFilterJson: json, saveFilterState: save);

    private static StockSnapshot Snapshot(string ticker, string name, string broker, string sector,
        decimal value, bool hasKnownMarketValue = true) =>
        new()
        {
            Position = new StockPosition
            {
                Stock = new Stock { Ticker = ticker, Name = name, Broker = broker, Sector = sector,
                    AccountType = "Specific", Currency = "JPY" },
                CurrentHolding = new CurrentHolding { CurrentShares = 1m, CurrentPrice = value },
                Purchase = new Purchase { Shares = 1m, UnitPrice = value },
                MutualFund = new MutualFundHolding()
            },
            CurrentMarketValueJpy = value, PurchaseTotalJpy = value,
            HasKnownCurrentMarketValueJpy = hasKnownMarketValue
        };
}
