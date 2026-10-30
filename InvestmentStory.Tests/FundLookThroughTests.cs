using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class FundLookThroughTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryFundExposure_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public FundLookThroughTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "test.db");
        new DatabaseInitializer().Initialize(_path);
    }

    private static StockSnapshot Holding(string ticker, decimal value, bool fund = false, bool known = true) => new()
    {
        Position = new StockPosition
        {
            Stock = new Stock { Ticker = ticker, Name = ticker, AssetType = fund ? AssetTypes.MutualFund : AssetTypes.Stock,
                Currency = "JPY" },
            CurrentHolding = new CurrentHolding { CurrentShares = 1m },
            MutualFund = new MutualFundHolding { UnitsHeld = fund ? 1m : 0m }
        },
        CurrentMarketValueJpy = value,
        HasKnownCurrentMarketValueJpy = known
    };

    private static FundDisclosure Disclosure(string fund, DateTime date, params FundConstituent[] parts) =>
        new("", fund, date, "発行者の月次資料", true, DateTime.UtcNow, parts)
        { UsageRightsNote = "端末内での手入力・個人利用", IsFundConfirmed = true };

    [Fact]
    public void ReplacesFundValueWithConstituentsWithoutDoubleCountingDirectHolding()
    {
        var date = new DateTime(2026, 10, 1);
        var newer = Disclosure("FUND", date, new FundConstituent("A", "銘柄A", 25m), new FundConstituent("B", "銘柄B", 50m));
        var older = Disclosure("FUND", date.AddMonths(-1), new FundConstituent("A", "銘柄A", 100m));
        var result = new FundLookThroughService().Build(
            [Holding("FUND", 100m, fund: true), Holding("A", 40m)], [older, newer], date);
        Assert.Equal(140m, result.PortfolioValueJpy);
        Assert.Equal(140m, result.Rows.Sum(x => x.TotalJpy));
        Assert.Equal((40m, 25m), (result.Rows.Single(x => x.Ticker == "A").DirectJpy,
            result.Rows.Single(x => x.Ticker == "A").IndirectJpy));
        Assert.Equal(50m, result.Rows.Single(x => x.Ticker == "B").IndirectJpy);
        Assert.Equal(25m, result.Rows.Single(x => x.IsUndisclosed).IndirectJpy);
        Assert.DoesNotContain(result.Rows, x => x.Ticker == "FUND");
    }

    [Fact]
    public void RejectsUnlicensedOrUnidentifiedDisclosureAndPreservesUnknownValue()
    {
        var valid = Disclosure("FUND", DateTime.Today, new FundConstituent("A", "A", 100m));
        Assert.Throws<ArgumentException>(() => FundLookThroughService.Validate(valid with { UsageRightsConfirmed = false }));
        Assert.Throws<ArgumentException>(() => FundLookThroughService.Validate(valid with { IsFundConfirmed = false }));
        Assert.Throws<ArgumentException>(() => FundLookThroughService.Validate(valid with
        {
            Constituents = [new("A", "A", 60m), new("a", "A", 50m)]
        }));
        var result = new FundLookThroughService().Build([Holding("FUND", 100m, true), Holding("X", 0m, known: false)],
            [valid], DateTime.Today);
        Assert.Null(result.PortfolioValueJpy);
        Assert.Equal(100m, result.KnownValueJpy);
    }

    [Fact]
    public void SavesVersionHistoryAndKeepsRoundingInvariant()
    {
        var repo = new FundDisclosureRepository(_path);
        repo.Save(Disclosure("FUND", DateTime.Today.AddDays(-1), new FundConstituent("A", "A", 50m)));
        repo.Save(Disclosure("FUND", DateTime.Today, new FundConstituent("A", "A", 33.333333m),
            new FundConstituent("B", "B", 66.666667m)));
        var history = repo.GetHistory();
        Assert.Equal(2, history.Count);
        Assert.NotEqual(history[0].Id, history[1].Id);
        var result = new FundLookThroughService().Build([Holding("FUND", 101m, fund: true)], history, DateTime.Today);
        Assert.Equal(101m, result.Rows.Sum(x => x.TotalJpy));
        Assert.DoesNotContain(result.Rows, x => x.IsUndisclosed);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
