using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class MonthEndPortfolioTests
{
    [Fact]
    public void AddingMonthEndArchiveToExistingDatabaseCreatesMigrationBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"investment_story_month_end_migration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portfolio.db");
        try
        {
            new InvestmentStoryRepository(path).Initialize();
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE MonthEndPortfolioArchives;";
                command.ExecuteNonQuery();
            }
            new InvestmentStoryRepository(path).Initialize();
            Assert.Single(Directory.GetFiles(directory, "*_before_month_end_portfolio_archive.db"));
            Assert.Empty(new MonthEndPortfolioRepository(path).GetRecords());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var target = Path.GetFullPath(directory);
            if (target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(target)) Directory.Delete(target, true);
        }
    }

    [Fact]
    public void MissingEvidenceStaysUnknownInsteadOfBecomingZero()
    {
        var date = new DateTime(2026, 10, 31);
        var snapshot = new StockSnapshot
        {
            Position = new StockPosition
            {
                Stock = new Stock { Id = 1, Ticker = "ABC", Currency = "USD" },
                CurrentHolding = new CurrentHolding { CurrentShares = 5m, CurrentPrice = 10m,
                    CurrentExchangeRate = 150m, CurrentPriceAcquiredAt = date,
                    CurrentPriceSource = "証券会社CSV", ExchangeRateSource = "確認済み為替" }
            },
            HasKnownCurrentMarketValueJpy = true, CurrentMarketValueJpy = 7500m,
            HasKnownPurchaseCostJpy = false
        };
        var service = new MonthEndPortfolioService();
        var record = service.Capture(date, [snapshot], new CashAssetSummary(), [], [],
            new ExchangeRateQuote { Rate = 150m, Source = "確認済み為替", AcquiredAt = date });

        Assert.Equal(7500m, record.StoredSecuritiesJpy);
        Assert.Null(record.StoredCashJpy);
        Assert.Null(record.StoredTotalJpy);
        Assert.Null(record.StoredUnrealizedJpy);
        Assert.Contains("未確認1件", record.QualitySummary);
        Assert.Equal("証券会社CSV", Assert.Single(record.Positions).PriceSource);
        Assert.Throws<ArgumentException>(() => service.Capture(date.AddDays(-1), [snapshot],
            new CashAssetSummary(), [], [], new ExchangeRateQuote()));
    }

    [Fact]
    public void ArchiveFreezesLastMonthEndDraftAndPreservesEvidence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_month_end_{Guid.NewGuid():N}.db");
        try
        {
            new InvestmentStoryRepository(path).Initialize();
            var repository = new MonthEndPortfolioRepository(path);
            var record = new MonthEndPortfolioRecord
            {
                SnapshotDate = new DateTime(2026, 10, 31),
                Positions = [new MonthEndPositionEvidence { StockId = 1, Ticker = "ABC",
                    MarketValueJpy = 1000m, CostJpy = 800m }],
                CashBalances = [new MonthEndCashEvidence { Account = "現金", Currency = "JPY", IncludedJpy = 200m }],
                StoredSecuritiesJpy = 1000m, StoredCostJpy = 800m,
                StoredCashJpy = 200m, StoredTotalJpy = 1200m
            };
            Assert.True(repository.SaveDraft(record));
            record.StoredTotalJpy = 1300m;
            Assert.True(repository.SaveDraft(record));
            Assert.Contains("総額の再集計差", new MonthEndPortfolioService().Recalculate(record).DifferenceReason);
            Assert.Equal(0, repository.FreezePastMonthEnds(new DateTime(2026, 10, 31)));
            Assert.Equal(1, repository.FreezePastMonthEnds(new DateTime(2026, 11, 1)));
            var frozen = Assert.Single(repository.GetRecords());
            Assert.True(frozen.IsFrozen);
            Assert.Equal(1300m, frozen.StoredTotalJpy);
            Assert.Equal(-100m, new MonthEndPortfolioService().Recalculate(frozen).TotalDifferenceJpy);
            Assert.False(repository.SaveDraft(new MonthEndPortfolioRecord { SnapshotDate = record.SnapshotDate }));
            Assert.Equal(0, repository.FreezePastMonthEnds(new DateTime(2026, 11, 2)));
            Assert.Equal(1300m, Assert.Single(repository.GetRecords()).StoredTotalJpy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
