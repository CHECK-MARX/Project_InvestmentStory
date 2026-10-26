using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using Xunit;

namespace InvestmentStory.Tests;

public sealed class DashboardHistoryTests
{
    [Fact]
    public void HistoryChart_UsesAllDatesInPeriodAndLatestSnapshotForSameDay()
    {
        var today = DateTime.Today;
        var history = Enumerable.Range(0, 41)
            .Select(daysAgo => new PortfolioSnapshot
            {
                Id = daysAgo + 1,
                SnapshotDate = today.AddDays(-daysAgo),
                UpdatedAt = today.AddMinutes(-10),
                TotalMarketValueJpy = 1_000_000m + daysAgo
            })
            .ToList();
        history.Add(new PortfolioSnapshot
        {
            Id = 99,
            SnapshotDate = today,
            UpdatedAt = today,
            TotalMarketValueJpy = 2_000_000m
        });

        var viewModel = new DashboardViewModel { SelectedHistoryPeriod = "全期間" };
        viewModel.Update(new DashboardSummary(), Array.Empty<StockSnapshot>(), history);

        Assert.Equal(41, viewModel.PortfolioHistoryRows.Count);
        Assert.Equal(today.AddDays(-40), viewModel.PortfolioHistoryRows[0].SnapshotDate);
        Assert.Equal(2_000_000m, viewModel.PortfolioHistoryRows[^1].TotalMarketValueJpy);

        viewModel.SelectedHistoryPeriod = "1か月";
        Assert.All(viewModel.PortfolioHistoryRows,
            row => Assert.True(row.SnapshotDate >= today.AddMonths(-1)));
    }
}
