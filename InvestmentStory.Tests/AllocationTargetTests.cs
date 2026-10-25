using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class AllocationTargetTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryAllocation_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly AllocationTargetService _service = new();

    public AllocationTargetTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "test.db");
        new DatabaseInitializer().Initialize(_path);
    }

    private static StockSnapshot Holding(string ticker, decimal value, string sector = "情報技術", bool known = true,
        string assetType = AssetTypes.Stock, string canonical = "") => new()
    {
        Position = new StockPosition
        {
            Stock = new Stock { Id = ticker.GetHashCode(), Ticker = ticker, Name = ticker,
                AssetType = assetType, CanonicalSecurityKey = canonical, Sector = sector,
                Currency = "USD", Country = "米国", Broker = "証券A" },
            CurrentHolding = new CurrentHolding { CurrentShares = 1m },
            MutualFund = new MutualFundHolding { UnitsHeld = assetType == AssetTypes.MutualFund ? 1m : 0m }
        },
        CurrentMarketValueJpy = value,
        HasKnownCurrentMarketValueJpy = known
    };

    [Fact]
    public void RejectsInvalidTargetSets()
    {
        Assert.Throws<ArgumentException>(() => AllocationTargetService.ValidateTargets([new("A", "A", 99m)]));
        Assert.Throws<ArgumentException>(() => AllocationTargetService.ValidateTargets([new("A", "A", 50m), new("a", "a", 50m)]));
        Assert.Throws<ArgumentException>(() => AllocationTargetService.ValidateTargets([new("A", "A", -1m), new("B", "B", 101m)]));
    }

    [Fact]
    public void UnknownValueBlocksSharesAndRebalancingAmounts()
    {
        var result = _service.Analyze([Holding("A", 100m), Holding("B", 0m, "金融", known: false)],
            AllocationDimension.Security, [new("A", "A", 50m), new("B", "B", 50m)], 100m);
        Assert.Null(result.TotalValueJpy);
        Assert.Equal(100m, result.KnownValueJpy);
        Assert.All(result.Categories, row => { Assert.Null(row.CurrentPercent); Assert.Null(row.BuySellJpy); Assert.Null(row.AdditionalFundingPurchaseJpy); });
    }

    [Fact]
    public void ClassifiesUnknownSeparatelyAndDoesNotAssumeFundLookThrough()
    {
        var rows = _service.CategorizeHoldings([
            Holding("MYSTERY", 10m, "外株"), Holding("FUND:1", 20m, assetType: AssetTypes.MutualFund)],
            AllocationDimension.Sector);
        Assert.Contains(rows, x => x.Key == "未分類" && x.ClassificationEvidence.Contains("根拠なし"));
        Assert.Contains(rows, x => x.Key == "投資信託（内訳未分類）" && x.ClassificationEvidence.Contains("組入先は未確認"));
    }

    [Theory]
    [InlineData(AllocationDimension.Country, "米国")]
    [InlineData(AllocationDimension.Currency, "USD")]
    [InlineData(AllocationDimension.Broker, "証券A")]
    [InlineData(AllocationDimension.AssetType, "株式")]
    public void RequiredAllocationAxesKeepClassificationEvidence(AllocationDimension dimension, string expected)
    {
        var category = Assert.Single(_service.CategorizeHoldings([Holding("EX", 100m)], dimension));
        Assert.Equal(expected, category.Key);
        Assert.False(string.IsNullOrWhiteSpace(category.ClassificationEvidence));
    }

    [Fact]
    public void ConsolidatesSameCanonicalSecurityAcrossAccounts()
    {
        var result = _service.Analyze([Holding("A", 100m, canonical: "SEC-A"), Holding("A", 200m, canonical: "SEC-A")],
            AllocationDimension.Security, [new("SEC-A", "A", 100m)]);
        Assert.Single(result.Categories);
        Assert.Equal(300m, result.Categories[0].CurrentJpy);
    }

    [Fact]
    public void ReportsMinimumFundingAndSeparatesBuyOnlyFromBuySell()
    {
        var target = new[] { new AllocationTarget("A", "A", 50m), new AllocationTarget("B", "B", 50m) };
        var snapshots = new[] { Holding("A", 80m), Holding("B", 20m) };
        var insufficient = _service.Analyze(snapshots, AllocationDimension.Security, target, 40m);
        Assert.Equal(60m, insufficient.MinimumAdditionalFundingJpy);
        Assert.False(insufficient.CanReachTargetsWithAdditionalFunding);
        Assert.All(insufficient.Categories, row => Assert.Null(row.AdditionalFundingPurchaseJpy));
        Assert.Equal(-30m, insufficient.Categories.Single(x => x.Key == "A").BuySellJpy);
        Assert.Equal(30m, insufficient.Categories.Single(x => x.Key == "B").BuySellJpy);

        var enough = _service.Analyze(snapshots, AllocationDimension.Security, target, 60m);
        Assert.True(enough.CanReachTargetsWithAdditionalFunding);
        Assert.Equal(0m, enough.Categories.Single(x => x.Key == "A").AdditionalFundingPurchaseJpy);
        Assert.Equal(60m, enough.Categories.Single(x => x.Key == "B").AdditionalFundingPurchaseJpy);
    }

    [Fact]
    public void ZeroPercentHeldCategoryMakesBuyOnlyImpossible()
    {
        var result = _service.Analyze([Holding("A", 90m), Holding("B", 10m)],
            AllocationDimension.Security, [new("A", "A", 100m)], 1000m);
        Assert.Null(result.MinimumAdditionalFundingJpy);
        Assert.False(result.CanReachTargetsWithAdditionalFunding);
        Assert.Equal(-10m, result.Categories.Single(x => x.Key == "B").BuySellJpy);
    }

    [Fact]
    public void SavesRevisionHistoryAndRejectsStaleEdit()
    {
        var repo = new AllocationTargetRepository(_path);
        var one = repo.Save(AllocationDimension.Sector, [new("情報技術", "情報技術", 100m)], 0,
            [new(1, "NVDA", "情報技術", "銘柄マスターの分類（自動補完を含む）")]);
        Assert.Equal(1, one.Revision);
        Assert.Equal("NVDA", repo.GetLatest(AllocationDimension.Sector)?.ClassificationEvidence?.Single().Ticker);
        var two = repo.Save(AllocationDimension.Sector, [new("情報技術", "情報技術", 60m), new("金融", "金融", 40m)], 1);
        Assert.Equal(2, two.Revision);
        Assert.Equal(2, repo.GetLatest(AllocationDimension.Sector)?.Revision);
        Assert.Equal(2, repo.GetHistory(AllocationDimension.Sector).Count);
        Assert.Throws<InvalidOperationException>(() => repo.Save(AllocationDimension.Sector, [new("金融", "金融", 100m)], 1));
        Assert.Equal(2, repo.GetHistory(AllocationDimension.Sector).Count);
        Assert.NotEmpty(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
    }

    [Fact]
    public void ViewModelUsesCurrentDatabaseFactoryAndRetainsDraftOnDimensionSwitch()
    {
        var other = Path.Combine(_directory, "other.db");
        new DatabaseInitializer().Initialize(other);
        var activePath = _path;
        var vm = new AllocationViewModel(() => new AllocationTargetRepository(activePath));
        vm.Load([Holding("A", 100m)]);
        vm.Targets[0].Percent = "100";
        vm.SelectedDimension = vm.Dimensions.Single(x => x.Value == AllocationDimension.Sector);
        vm.SelectedDimension = vm.Dimensions.Single(x => x.Value == AllocationDimension.Country);
        Assert.Equal("100", vm.Targets[0].Percent);
        vm.SaveCommand.Execute(null);
        Assert.NotNull(new AllocationTargetRepository(_path).GetLatest(AllocationDimension.Country));
        activePath = other; vm.Reset(); vm.Load([Holding("A", 100m)]);
        Assert.Equal(0, vm.Revision);
        Assert.Null(new AllocationTargetRepository(other).GetLatest(AllocationDimension.Country));
    }

    [Fact]
    public void MigrationBacksUpExistingDatabaseAndRestorePreviewCountsSavedTargets()
    {
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE AllocationTargetRevisions;";
            command.ExecuteNonQuery();
        }
        new DatabaseInitializer().Initialize(_path);
        var backup = Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        new DatabaseInitializer().Initialize(_path);
        Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        new AllocationTargetRepository(_path).Save(AllocationDimension.Country, [new("日本", "日本", 100m)], 0);
        var impact = new DatabaseRestoreService(_path).GetRestoreImpact(Path.GetFileName(backup));
        Assert.Contains("資産配分目標の改訂履歴 1件", impact);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
