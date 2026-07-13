using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using System.Text.Json;

namespace InvestmentStory.Tests;

public sealed class DividendGrowthSimulationServiceTests
{
    private readonly DividendGrowthSimulationService _service = new();

    [Fact]
    public void CreateDefaultPlanItems_UsesCurrentDividendStockPositions()
    {
        var positions = new[]
        {
            CreateStockPosition("KO", "Coca-Cola", shares: 20m, price: 80m, dividend: 2m),
            CreateStockPosition("FUND", "Fund", shares: 10_000m, price: 0m, dividend: 0m, assetType: AssetTypes.MutualFund)
        };

        var items = _service.CreateDefaultPlanItems(positions, DividendGrowthDisplayModes.Position);

        Assert.Single(items);
        Assert.Equal("KO", items[0].Ticker);
        Assert.Equal(20m, items[0].CurrentShares);
    }

    [Fact]
    public void CreateDefaultPlanItems_ExcludesNonDividendUnknownDividendMutualFundsAndClosedPositions()
    {
        var positions = new[]
        {
            CreateStockPosition("KO", "Coca-Cola", shares: 20m, price: 80m, dividend: 2m),
            CreateStockPosition("NFLX", "Netflix", shares: 10m, price: 70m, dividend: 0m),
            CreateStockPosition("AMD", "AMD", shares: 5m, price: 100m, dividend: 0m),
            CreateStockPosition("FUND", "Mutual Fund", shares: 10_000m, price: 1m, dividend: 1m, assetType: AssetTypes.MutualFund),
            CreateStockPosition("PG", "P&G", shares: 0m, price: 150m, dividend: 4m)
        };

        var items = _service.CreateDefaultPlanItems(positions, DividendGrowthDisplayModes.Position);

        var item = Assert.Single(items);
        Assert.Equal("KO", item.Ticker);
    }

    [Fact]
    public void CreateDefaultPlanItems_AggregatesSameCanonicalSecurity()
    {
        var positions = new[]
        {
            CreateStockPosition("MO", "Altria", broker: "SBI", accountType: AccountTypes.NisaGrowth, shares: 30m, canonicalKey: "US:MO"),
            CreateStockPosition("MO", "Altria", broker: "Nomura", accountType: AccountTypes.Specific, shares: 20m, canonicalKey: "US:MO")
        };

        var items = _service.CreateDefaultPlanItems(positions, DividendGrowthDisplayModes.AggregateBySecurity);

        Assert.Single(items);
        Assert.Equal(50m, items[0].CurrentShares);
        Assert.Equal("複数", items[0].Broker);
    }

    [Fact]
    public void CreateDefaultPlanItems_PositionModeKeepsSeparatePositions()
    {
        var positions = new[]
        {
            CreateStockPosition("MO", "Altria", broker: "SBI", accountType: AccountTypes.NisaGrowth, shares: 30m, canonicalKey: "US:MO"),
            CreateStockPosition("MO", "Altria", broker: "Nomura", accountType: AccountTypes.Specific, shares: 20m, canonicalKey: "US:MO")
        };

        var items = _service.CreateDefaultPlanItems(positions, DividendGrowthDisplayModes.Position);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Simulate_CalculatesPlannedPurchaseAmount()
    {
        var item = CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, plannedShares: 10m, exchangeRate: 160m);

        var result = Simulate(item);

        Assert.Equal(128_000m, result.Summary.ExistingPlannedInvestmentJpy);
        Assert.Equal(128_000m, result.Holdings[0].PlannedPurchaseAmountJpy);
    }

    [Fact]
    public void Simulate_CalculatesAdditionalAnnualDividend()
    {
        var item = CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, plannedShares: 10m, exchangeRate: 160m);

        var result = Simulate(item);

        Assert.Equal(6_400m, result.Summary.CurrentGrossAnnualDividendJpy);
        Assert.Equal(9_600m, result.Summary.PostAddGrossAnnualDividendJpy);
        Assert.Equal(3_200m, result.Summary.AnnualDividendIncreaseJpy);
    }

    [Fact]
    public void Simulate_ConvertsUsdDividendToJpy()
    {
        var item = CreatePlanItem("AAPL", shares: 10m, price: 100m, dividend: 1m, exchangeRate: 160m);

        var result = Simulate(item);

        Assert.Equal(1_600m, result.Summary.CurrentGrossAnnualDividendJpy);
    }

    [Fact]
    public void Simulate_UsNisaKeepsForeignTaxAndExemptsDomesticTax()
    {
        var item = CreatePlanItem("AAPL", shares: 10m, price: 100m, dividend: 1m, exchangeRate: 160m, accountType: AccountTypes.NisaGrowth);

        var result = Simulate(item);

        Assert.Equal(1_600m, result.Summary.CurrentGrossAnnualDividendJpy);
        Assert.Equal(1_440m, result.Summary.CurrentNetAnnualDividendJpy);
        Assert.Equal(160m, result.Summary.ForeignTaxJpy);
        Assert.Equal(0m, result.Summary.DomesticTaxJpy);
    }

    [Fact]
    public void Simulate_UsSpecificAppliesForeignAndDomesticTax()
    {
        var item = CreatePlanItem("AAPL", shares: 10m, price: 100m, dividend: 1m, exchangeRate: 160m, accountType: AccountTypes.Specific);

        var result = Simulate(item);

        Assert.Equal(1_600m, result.Summary.CurrentGrossAnnualDividendJpy);
        Assert.InRange(result.Summary.CurrentNetAnnualDividendJpy, 1_147m, 1_148m);
        Assert.Equal(160m, result.Summary.ForeignTaxJpy);
        Assert.InRange(result.Summary.DomesticTaxJpy, 292m, 293m);
    }

    [Fact]
    public void Simulate_JapaneseNisaExemptsAllDividendTax()
    {
        var item = CreatePlanItem("8151", shares: 100m, price: 2000m, dividend: 50m, currency: "JPY", exchangeRate: 1m, accountType: AccountTypes.NisaGrowth);

        var result = Simulate(item);

        Assert.Equal(5_000m, result.Summary.CurrentGrossAnnualDividendJpy);
        Assert.Equal(5_000m, result.Summary.CurrentNetAnnualDividendJpy);
        Assert.Equal(0m, result.Summary.TotalTaxJpy);
    }

    [Fact]
    public void Simulate_NewStockIsSimulationOnlyAndSeparated()
    {
        var existing = CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m);
        var newStock = CreatePlanItem("PEP", shares: 0m, price: 140m, dividend: 5m, plannedShares: 10m, isNewStock: true);

        var result = Simulate(existing, newStock);

        Assert.Single(result.Holdings);
        Assert.Single(result.NewStocks);
        Assert.Equal("PEP", result.NewStocks[0].Ticker);
    }

    [Fact]
    public void Simulate_SummaryTotalsMatchDetailRows()
    {
        var items = new[]
        {
            CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, plannedShares: 5m),
            CreatePlanItem("PEP", shares: 10m, price: 140m, dividend: 5m, plannedShares: 3m)
        };

        var result = Simulate(items);

        var detailTotal = result.Holdings.Concat(result.NewStocks).Sum(x => x.PostAddNetAnnualDividendJpy);
        Assert.Equal(detailTotal, result.Summary.PostAddNetAnnualDividendJpy);
    }

    [Fact]
    public void Simulate_CalculatesGoalAchievementAndGap()
    {
        var item = CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, exchangeRate: 160m);

        var result = Simulate(new[] { item }, target: 10_000m);

        Assert.InRange(result.Summary.TargetAchievementRate, 45m, 46m);
        Assert.InRange(result.Summary.TargetGapJpy, 5_400m, 5_410m);
    }

    [Fact]
    public void Simulate_AnnualDividendGrowthIsReflectedInProjection()
    {
        var item = CreatePlanItem("KO", shares: 10m, price: 100m, dividend: 1m, exchangeRate: 100m, growth: 10m);

        var result = Simulate(new[] { item }, years: 3);

        Assert.True(result.Projections[1].PlannedGrossDividendJpy > result.Projections[0].PlannedGrossDividendJpy);
        Assert.True(result.Projections[2].PlannedGrossDividendJpy > result.Projections[1].PlannedGrossDividendJpy);
    }

    [Fact]
    public void Simulate_ZeroPlannedSharesKeepsPostAddEqualCurrent()
    {
        var item = CreatePlanItem("KO", shares: 10m, price: 100m, dividend: 1m, exchangeRate: 100m, plannedShares: 0m);

        var result = Simulate(item);

        Assert.Equal(result.Summary.CurrentGrossAnnualDividendJpy, result.Summary.PostAddGrossAnnualDividendJpy);
        Assert.Equal(0m, result.Summary.TotalPlannedInvestmentJpy);
    }

    [Fact]
    public void Simulate_ChangingPlannedSharesUpdatesPlannedInvestmentAndDividend()
    {
        var before = Simulate(CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, plannedShares: 0m, exchangeRate: 160m));
        var after = Simulate(CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, plannedShares: 5m, exchangeRate: 160m));

        Assert.Equal(0m, before.Summary.TotalPlannedInvestmentJpy);
        Assert.Equal(64_000m, after.Summary.TotalPlannedInvestmentJpy);
        Assert.Equal(before.Summary.PostAddGrossAnnualDividendJpy + 1_600m, after.Summary.PostAddGrossAnnualDividendJpy);
    }

    [Fact]
    public void Simulate_NewStockManualInputsProducePurchaseAndDividendPreview()
    {
        var item = CreatePlanItem("PEP", shares: 0m, price: 100m, dividend: 2m, plannedShares: 10m, exchangeRate: 160m, isNewStock: true);

        var result = Simulate(item);

        Assert.Empty(result.Holdings);
        var newStock = Assert.Single(result.NewStocks);
        Assert.Equal(160_000m, result.Summary.NewPlannedInvestmentJpy);
        Assert.Equal(3_200m, newStock.PostAddAnnualDividendJpy);
        Assert.Equal(3_200m, result.Summary.PostAddGrossAnnualDividendJpy);
    }

    [Fact]
    public void Simulate_AggregatedCurrentDividendKeepsComponentAccountTaxRules()
    {
        var nisa = CreatePlanItem("AAPL", shares: 10m, price: 100m, dividend: 1m, exchangeRate: 100m, accountType: AccountTypes.NisaGrowth);
        var specific = CreatePlanItem("AAPL", shares: 10m, price: 100m, dividend: 1m, exchangeRate: 100m, accountType: AccountTypes.Specific);
        var aggregate = CreatePlanItem(
            "AAPL",
            shares: 20m,
            price: 100m,
            dividend: 1m,
            exchangeRate: 100m,
            accountType: AccountTypes.Specific,
            components: new[] { nisa, specific });

        var result = Simulate(aggregate);

        Assert.Equal(2_000m, result.Summary.CurrentGrossAnnualDividendJpy);
        Assert.InRange(result.Summary.CurrentNetAnnualDividendJpy, 1_617m, 1_618m);
        Assert.Equal(200m, result.Summary.ForeignTaxJpy);
        Assert.InRange(result.Summary.DomesticTaxJpy, 182m, 183m);
    }

    [Fact]
    public void Simulate_MissingDividendDoesNotThrowAndMarksSource()
    {
        var item = CreatePlanItem("AMD", shares: 5m, price: 100m, dividend: 0m);

        var result = Simulate(item);

        Assert.Equal(0m, result.Summary.PostAddGrossAnnualDividendJpy);
        Assert.Equal("配当情報未取得", result.Holdings[0].AnnualDividendSource);
    }

    [Fact]
    public void Simulate_UsesProvidedItemsOnlyForSampleAndNormalSeparation()
    {
        var normal = Simulate(CreatePlanItem("KO", shares: 10m, price: 100m, dividend: 1m));
        var sample = Simulate(CreatePlanItem("KO", shares: 20m, price: 100m, dividend: 1m));

        Assert.NotEqual(normal.Summary.CurrentGrossAnnualDividendJpy, sample.Summary.CurrentGrossAnnualDividendJpy);
        Assert.Equal(normal.Summary.CurrentGrossAnnualDividendJpy * 2m, sample.Summary.CurrentGrossAnnualDividendJpy);
    }

    [Fact]
    public void Simulate_ReturnsTargetAchievementYearFromProjection()
    {
        var item = CreatePlanItem("KO", shares: 10m, price: 100m, dividend: 10m, exchangeRate: 1m, currency: "JPY", accountType: AccountTypes.NisaGrowth, growth: 10m);

        var result = Simulate(new[] { item }, target: 120m, years: 5, startYear: 2026);

        Assert.Equal(2028, result.Summary.TargetAchievementYear);
    }

    [Fact]
    public void Simulate_WarnsWhenJapanesePurchaseIsNotBoardLot()
    {
        var item = CreatePlanItem("8151", shares: 100m, price: 2000m, dividend: 50m, currency: "JPY", exchangeRate: 1m, plannedShares: 50m);

        var result = Simulate(item);

        Assert.Contains("100", result.Holdings[0].Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Simulate_DoesNotWarnForUsSingleShareLot()
    {
        var item = CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, plannedShares: 3m);

        var result = Simulate(item);

        Assert.Equal(string.Empty, result.Holdings[0].Warning);
    }

    [Fact]
    public void Simulate_FirstProjectionMatchesPostAddCard()
    {
        var item = CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, plannedShares: 10m, exchangeRate: 160m);

        var result = Simulate(new[] { item }, years: 2);

        Assert.Equal(result.Summary.PostAddNetAnnualDividendJpy, result.Projections[0].PlannedNetDividendJpy);
    }

    [Fact]
    public void Simulate_PreservesDividendSourceWhenAvailable()
    {
        var item = CreatePlanItem("KO", shares: 20m, price: 80m, dividend: 2m, dividendSource: "Yahoo Finance");

        var result = Simulate(item);

        Assert.Equal("Yahoo Finance", result.Holdings[0].AnnualDividendSource);
    }

    [Fact]
    public void DividendGrowthPlanItem_PersistsNewStockMarketDataStatus()
    {
        var acquiredAt = new DateTime(2026, 7, 13, 9, 30, 0);
        var item = new DividendGrowthPlanItem
        {
            PlanKey = "New:PG",
            CanonicalKey = "PG",
            Ticker = "PG",
            Name = "Procter & Gamble",
            Country = "United States",
            Currency = "USD",
            CurrentPrice = 160m,
            ExchangeRate = 160m,
            AnnualDividendPerShare = 4.24m,
            PlannedAdditionalShares = 10m,
            IsNewStock = true,
            MarketDataSource = "Yahoo Finance",
            MarketDataAcquiredAt = acquiredAt,
            MarketDataStatus = "取得成功 2026/07/13 09:30"
        };

        var json = JsonSerializer.Serialize(new[] { item });
        var restored = JsonSerializer.Deserialize<List<DividendGrowthPlanItem>>(json);

        var restoredItem = Assert.Single(restored!);
        Assert.Equal("Yahoo Finance", restoredItem.MarketDataSource);
        Assert.Equal(acquiredAt, restoredItem.MarketDataAcquiredAt);
        Assert.Equal("取得成功 2026/07/13 09:30", restoredItem.MarketDataStatus);
    }

    private DividendGrowthSimulationResult Simulate(params DividendGrowthPlanItem[] items) =>
        Simulate(items, target: 1_200_000m);

    private DividendGrowthSimulationResult Simulate(IEnumerable<DividendGrowthPlanItem> items, decimal target = 1_200_000m, int years = 10, int startYear = 2026) =>
        _service.Simulate(
            new DividendGrowthSimulationInput
            {
                TargetAnnualDividendJpy = target,
                ProjectionYears = years,
                StartYear = startYear,
                PlanItems = items.ToArray()
            },
            Array.Empty<TaxProfile>());

    private static DividendGrowthPlanItem CreatePlanItem(
        string ticker,
        decimal shares,
        decimal price,
        decimal dividend,
        decimal plannedShares = 0m,
        decimal exchangeRate = 160m,
        string currency = "USD",
        string accountType = AccountTypes.Specific,
        string broker = "Broker",
        decimal growth = 0m,
        bool isNewStock = false,
        string dividendSource = "Yahoo Finance",
        IReadOnlyList<DividendGrowthPlanItem>? components = null) =>
        new()
        {
            PlanKey = $"{broker}|{ticker}|{accountType}|{Guid.NewGuid():N}",
            CanonicalKey = ticker,
            PositionKey = $"{broker}|{ticker}|{accountType}",
            Ticker = ticker,
            Name = ticker,
            Broker = broker,
            AccountType = accountType,
            Country = currency == "JPY" ? "Japan" : "United States",
            Currency = currency,
            CurrentShares = shares,
            CurrentPrice = price,
            ExchangeRate = exchangeRate,
            AnnualDividendPerShare = dividend,
            AnnualDividendSource = dividendSource,
            PlannedAdditionalShares = plannedShares,
            PlannedBroker = broker,
            PlannedAccountType = accountType,
            AnnualDividendGrowthRate = growth,
            PurchaseMode = DividendGrowthPurchaseModes.OneTime,
            IsNewStock = isNewStock,
            Components = components ?? Array.Empty<DividendGrowthPlanItem>()
        };

    private static StockPosition CreateStockPosition(
        string ticker,
        string name,
        string broker = "Broker",
        string accountType = AccountTypes.Specific,
        decimal shares = 10m,
        decimal price = 100m,
        decimal dividend = 1m,
        string currency = "USD",
        string canonicalKey = "",
        string assetType = AssetTypes.Stock) =>
        new()
        {
            Stock = new Stock
            {
                Id = Random.Shared.Next(1, 100000),
                AssetType = assetType,
                CanonicalSecurityKey = string.IsNullOrWhiteSpace(canonicalKey) ? ticker : canonicalKey,
                Ticker = ticker,
                Name = name,
                Broker = broker,
                AccountType = accountType,
                CustodyType = accountType,
                Currency = currency,
                Country = currency == "JPY" ? "Japan" : "United States"
            },
            Purchase = new Purchase
            {
                Shares = shares,
                UnitPrice = price,
                ExchangeRate = currency == "JPY" ? 1m : 160m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = shares,
                CurrentPrice = price,
                CurrentExchangeRate = currency == "JPY" ? 1m : 160m,
                AnnualDividendPerShare = dividend,
                DividendInfoSource = dividend > 0m ? "Yahoo Finance" : string.Empty
            },
            MutualFund = new MutualFundHolding
            {
                UnitsHeld = assetType == AssetTypes.MutualFund ? shares : 0m
            }
        };
}
