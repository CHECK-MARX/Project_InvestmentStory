using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class TodayReviewNavigationTests
{
    [Fact]
    public void CostFocusClearsPreviousFiltersAndSelectsExactBrokerAccountPosition()
    {
        var target = Snapshot(1, "A証券", "Specific");
        var other = Snapshot(2, "B証券", "NisaGrowth");
        var vm = new StockListViewModel(() => { }, _ => { }, _ => { }, _ => { },
            _ => { }, () => { }, () => { });
        vm.Update([target, other]);
        vm.SearchText = "見つからない";
        vm.SelectedGroupingMode = "銘柄集約";
        Assert.True(vm.FocusPosition(SecurityIdentityService.BuildPositionKey(other.Position)));
        Assert.Equal("B証券", vm.SelectedRow?.Broker);
        Assert.Equal("ポジション別", vm.SelectedGroupingMode);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.False(vm.FocusPosition("missing"));
        Assert.Null(vm.SelectedRow);
    }

    [Fact]
    public void DividendFocusChangesYearAndSelectsExactPlannedPayment()
    {
        var vm = new DividendsViewModel(_ => { }, _ => { });
        var payments = new[]
        {
            new DividendPayment { Id = 11, StockId = 1, Ticker = "ABC", PaymentDate = new DateTime(2025, 12, 1),
                DividendStatus = DividendConstants.Planned, Currency = "JPY", NetAmountJpy = 100m },
            new DividendPayment { Id = 12, StockId = 1, Ticker = "ABC", PaymentDate = new DateTime(2026, 1, 1),
                DividendStatus = DividendConstants.Planned, Currency = "JPY", NetAmountJpy = 100m }
        };
        vm.Update([], payments, asOf: new DateTime(2026, 10, 24));
        Assert.True(vm.FocusPayment(11));
        Assert.Equal(2025, vm.SelectedYear);
        Assert.Equal(11, vm.SelectedPayment?.Id);
        Assert.False(vm.FocusPayment(999));
        Assert.Null(vm.SelectedPayment);
    }

    [Fact]
    public void SettingsFocusSelectsApiLogByPersistentId()
    {
        var settings = new SettingsViewModel(() => new AppSettings(), _ => { }, () =>
        [
            new ApiFetchLogEntry { Id = 31, Symbol = "A" },
            new ApiFetchLogEntry { Id = 32, Symbol = "B" }
        ]);
        Assert.True(settings.FocusApiLog(32));
        Assert.Equal("B", settings.SelectedApiLog?.Symbol);
        Assert.False(settings.FocusApiLog(999));
        Assert.Null(settings.SelectedApiLog);
    }

    [Fact]
    public void CsvReviewFocusKeepsStoredSourceRowAndDoesNotMarkHistoryUnresolved()
    {
        var records = new[]
        {
            new CsvReviewRecord("trade:1", "取引・未解決", "2026-10-01", "ABC", "A", "特定",
                "report.csv", 17, "重複候補", "未解決"),
            new CsvReviewRecord("holding:2", "保有・要確認履歴", "2026-10-02", "DEF", "B", "一般",
                "", 0, "口座不明", "解決状態なし")
        };
        var vm = new CsvReviewViewModel(() => records, () => { });
        vm.Load();
        Assert.True(vm.FocusKey("trade:1"));
        Assert.Contains("report.csv / 17行", vm.SelectedDetail);
        Assert.True(vm.FocusKey("holding:2"));
        Assert.Contains("解決状態なし", vm.SelectedDetail);
        Assert.False(vm.FocusKey("missing"));
        Assert.Null(vm.Selected);
    }

    private static StockSnapshot Snapshot(int id, string broker, string account) => new()
    {
        Position = new StockPosition
        {
            Stock = new Stock { Id = id, Ticker = "ABC", Name = "ABC", Broker = broker,
                AccountType = account, Currency = "JPY" },
            CurrentHolding = new CurrentHolding { CurrentShares = 1m, CurrentPrice = 100m },
            Purchase = new Purchase { Shares = 1m, UnitPrice = 100m }
        },
        HasKnownCurrentMarketValueJpy = true, CurrentMarketValueJpy = 100m
    };
}
