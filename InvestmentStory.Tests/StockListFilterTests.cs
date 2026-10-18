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
