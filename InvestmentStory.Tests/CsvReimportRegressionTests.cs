using System.Reflection;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class CsvReimportRegressionTests
{
    [Fact]
    public void SbiStatementReimport_DoesNotCreateDuplicateUnknownOrEquityFundPositions()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"investment_story_reimport_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            SeedNormalPositions(repository);

            var firstStatement = CreateSbiTradeStatement();
            var copiedStatement = CreateSbiTradeStatement();

            Import(repository, firstStatement, "SBI_A_trades.csv");
            var afterFirst = Snapshot(repository);

            Import(repository, firstStatement, "SBI_A_trades.csv");
            var afterSecondSameFile = Snapshot(repository);

            Import(repository, copiedStatement, "SBI_B_trades.csv");
            var afterThirdDifferentFile = Snapshot(repository);

            Assert.Equal(afterFirst.PositionCount, afterSecondSameFile.PositionCount);
            Assert.Equal(afterFirst.PositionCount, afterThirdDifferentFile.PositionCount);
            Assert.Equal(afterFirst.TotalMarketValueJpy, afterSecondSameFile.TotalMarketValueJpy);
            Assert.Equal(afterFirst.TotalMarketValueJpy, afterThirdDifferentFile.TotalMarketValueJpy);
            Assert.Equal(afterFirst.TotalCostJpy, afterSecondSameFile.TotalCostJpy);
            Assert.Equal(afterFirst.TotalCostJpy, afterThirdDifferentFile.TotalCostJpy);
            Assert.Equal(0, afterThirdDifferentFile.FundEquityRows);
            Assert.Equal(0, afterThirdDifferentFile.Unknown8593Rows);
            Assert.Equal(0, afterThirdDifferentFile.UnknownFundRows);
            Assert.Equal(1, afterThirdDifferentFile.Rows8593);
            Assert.Equal(2, afterThirdDifferentFile.MutualFundRows);
            Assert.Equal(2_702_684m, afterThirdDifferentFile.FundMarketValueJpy);
            Assert.Equal(1_680_021m, afterThirdDifferentFile.FundCostJpy);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void MutualFundSimulation_FundScopeAggregatesLegacyAndAccumulationAccounts()
    {
        var service = new MutualFundAssetSimulationService();
        var positions = new[]
        {
            CreateFundPosition(AccountTypes.NisaAccumulation, 1_689_118m, 1_213_346m),
            CreateFundPosition(AccountTypes.NisaLegacy, 1_013_566m, 466_675m)
        };
        var option = service.CreateScopeOptions(positions)
            .Single(x => x.Key.StartsWith(MutualFundSimulationScopeKeys.FundPrefix, StringComparison.Ordinal));

        var result = service.SimulateScenarios(
            positions,
            option.Key,
            new MutualFundSimulationInput { MonthlyContributionJpy = 40_000m, TargetAmountJpy = 20_000_000m, ProjectionYears = 5 },
            new[] { new MutualFundScenarioInput { Key = "Standard", Name = "Standard", AnnualReturnRate = 5m, IsEnabled = true } });

        Assert.Equal(2_702_684m, result.Summary.CurrentMarketValueJpy);
        Assert.Equal(1_680_021m, result.Summary.CurrentCostJpy);
        Assert.Equal(2, result.Summary.PositionCount);
        Assert.Contains(result.AccountBreakdowns, x => x.AccountType == AccountTypes.NisaAccumulation && x.MonthlyContributionJpy == 40_000m);
        Assert.Contains(result.AccountBreakdowns, x => x.AccountType == AccountTypes.NisaLegacy && x.MonthlyContributionJpy == 0m);
        Assert.NotEmpty(result.MonthlyComparisons);
        Assert.Equal(option.DisplayName, new SimulationScopeOptionViewModel(option).DisplayName);

        var legacyResult = service.SimulateScenarios(
            positions,
            option.LegacyKey,
            new MutualFundSimulationInput { MonthlyContributionJpy = 40_000m, TargetAmountJpy = 20_000_000m, ProjectionYears = 5 },
            new[] { new MutualFundScenarioInput { Key = "Standard", Name = "Standard", AnnualReturnRate = 5m, IsEnabled = true } });

        Assert.Equal(result.Summary.CurrentMarketValueJpy, legacyResult.Summary.CurrentMarketValueJpy);
        Assert.Equal(result.Summary.PositionCount, legacyResult.Summary.PositionCount);
    }

    [Fact]
    public void MutualFundSimulation_UsesBrokerTradesForActualAnnualizedReturnWhenPurchaseDateIsTooRecent()
    {
        var service = new MutualFundAssetSimulationService();
        var positions = new[]
        {
            CreateFundPosition(AccountTypes.NisaAccumulation, 1_689_118m, 1_213_346m),
            CreateFundPosition(AccountTypes.NisaLegacy, 1_013_566m, 466_675m)
        };
        positions[0].Stock.Id = 136;
        positions[1].Stock.Id = 137;
        positions[0].Purchase.PurchaseDate = DateTime.Today.AddDays(-2);
        positions[1].Purchase.PurchaseDate = DateTime.Today.AddDays(-2);
        var trades = new[]
        {
            new BrokerTrade
            {
                StockId = 136,
                TradeDate = new DateTime(2024, 7, 2),
                SignedQuantity = 1m,
                SettlementAmountJpy = 1_213_346m,
                TradeType = "Buy"
            },
            new BrokerTrade
            {
                StockId = 137,
                TradeDate = new DateTime(2024, 7, 2),
                SignedQuantity = 1m,
                SettlementAmountJpy = 466_675m,
                TradeType = "Buy"
            }
        };

        var option = service.CreateScopeOptions(positions)
            .Single(x => x.Key.StartsWith(MutualFundSimulationScopeKeys.FundPrefix, StringComparison.Ordinal));
        var result = service.SimulateScenarios(
            positions,
            option.Key,
            new MutualFundSimulationInput { MonthlyContributionJpy = 40_000m, TargetAmountJpy = 20_000_000m, ProjectionYears = 5 },
            new[] { new MutualFundScenarioInput { Key = "Standard", Name = "Standard", AnnualReturnRate = 5m, IsEnabled = true } },
            trades);

        Assert.Equal(2, result.Summary.PositionCount);
        Assert.NotNull(result.Summary.ActualAnnualizedReturnEstimate);
        Assert.Equal("XIRR", result.Summary.ActualAnnualizedReturnEstimate.Method);
        Assert.Equal(new DateTime(2024, 7, 2), result.Summary.ActualAnnualizedReturnEstimate.PeriodStart);
        Assert.True(result.Summary.ActualAnnualizedReturnRate is > 0m and < 1_000_000m);
    }

    private static void Import(InvestmentStoryRepository repository, BrokerStatementImport statement, string sourceLabel)
    {
        var viewModel = new CsvImportViewModel(
            () => repository.GetPositions(),
            repository.SavePosition,
            repository.SaveDividendPayment,
            () => repository.GetDividendPayments(),
            repository.SaveBrokerTrades);
        var method = typeof(CsvImportViewModel).GetMethod(
            "ImportBrokerStatement",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ImportBrokerStatement was not found.");
        method.Invoke(viewModel, new object[] { new Func<BrokerStatementImport>(() => statement), sourceLabel });
    }

    private static void SeedNormalPositions(InvestmentStoryRepository repository)
    {
        repository.SavePosition(CreateStockPosition8593());
        repository.SavePosition(CreateFundPosition(AccountTypes.NisaAccumulation, 1_689_118m, 1_213_346m));
        repository.SavePosition(CreateFundPosition(AccountTypes.NisaLegacy, 1_013_566m, 466_675m));
    }

    private static BrokerStatementImport CreateSbiTradeStatement()
    {
        return new BrokerStatementImport
        {
            Broker = "SBI",
            Source = "SBI",
            CanUpdateHoldings = true,
            Trades = new[]
            {
                new BrokerTradeRecord
                {
                    Broker = "SBI",
                    TradeDate = new DateTime(2026, 6, 15),
                    SettlementDate = new DateTime(2026, 6, 17),
                    Account = "NISA(成)",
                    Product = "DomesticStock",
                    TradeType = "Buy",
                    Ticker = "8593",
                    Name = "Mitsubishi HC Capital",
                    Quantity = 100m,
                    SignedQuantity = 100m,
                    UnitPrice = 1345.8m,
                    SettlementAmountJpy = 134_580m,
                    ExchangeRate = 1m,
                    Currency = "JPY",
                    Source = "SBI"
                },
                new BrokerTradeRecord
                {
                    Broker = "SBI",
                    TradeDate = new DateTime(2024, 7, 2),
                    SettlementDate = new DateTime(2024, 7, 5),
                    Account = "NISA(つ)",
                    Product = "Fund",
                    TradeType = "Buy",
                    Ticker = "FUND:SBI-V-SP500",
                    Name = "SBI V S&P500 Index Fund",
                    Quantity = 13_723m,
                    SignedQuantity = 13_723m,
                    UnitPrice = 29_148m,
                    SettlementAmountJpy = 40_000m,
                    ExchangeRate = 1m,
                    Currency = "JPY",
                    Source = "SBI"
                }
            }
        };
    }

    private static ImportSnapshot Snapshot(InvestmentStoryRepository repository)
    {
        var positions = repository.GetPositions();
        var calculator = new InvestmentCalculator();
        var snapshots = positions.Select(calculator.CreateSnapshot).ToList();
        var funds = positions.Where(x => x.IsMutualFund).ToList();
        return new ImportSnapshot(
            positions.Count,
            Math.Round(snapshots.Sum(x => x.CurrentMarketValueJpy), 2),
            Math.Round(snapshots.Sum(x => x.PurchaseTotalJpy), 2),
            positions.Count(x => !x.IsMutualFund && IsFundLike(x)),
            positions.Count(x => x.Stock.Ticker == "8593" && x.Stock.AccountType == AccountTypes.Unknown),
            positions.Count(x => x.IsMutualFund && x.Stock.AccountType == AccountTypes.Unknown),
            positions.Count(x => x.Stock.Ticker == "8593"),
            funds.Count,
            funds.Sum(x => x.MutualFund.MarketValue),
            funds.Sum(x => x.MutualFund.AcquisitionAmount));
    }

    private static bool IsFundLike(StockPosition position) =>
        position.Stock.Ticker.StartsWith("FUND:", StringComparison.OrdinalIgnoreCase) ||
        position.Stock.Name.Contains("S&P", StringComparison.OrdinalIgnoreCase) ||
        position.Stock.Name.Contains("SP500", StringComparison.OrdinalIgnoreCase);

    private sealed record ImportSnapshot(
        int PositionCount,
        decimal TotalMarketValueJpy,
        decimal TotalCostJpy,
        int FundEquityRows,
        int Unknown8593Rows,
        int UnknownFundRows,
        int Rows8593,
        int MutualFundRows,
        decimal FundMarketValueJpy,
        decimal FundCostJpy);

    private static StockPosition CreateStockPosition8593() =>
        new()
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.Stock,
                Name = "Mitsubishi HC Capital",
                Ticker = "8593",
                Country = "Japan",
                Currency = "JPY",
                Broker = "SBI",
                AccountType = AccountTypes.NisaGrowth,
                CustodyType = "NISA(成)",
                Sector = "Finance"
            },
            Purchase = new Purchase
            {
                PurchaseDate = new DateTime(2026, 6, 15),
                Shares = 200m,
                UnitPrice = 1346m,
                ExchangeRate = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 200m,
                CurrentPrice = 1364.5m,
                CurrentExchangeRate = 1m
            }
        };

    private static StockPosition CreateFundPosition(string accountType, decimal marketValue, decimal cost) =>
        new()
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund,
                Name = "SBI V S&P500 Index Fund",
                Ticker = "FUND:SBI-V-SP500",
                Country = "Japan",
                Currency = "JPY",
                Broker = "SBI",
                AccountType = accountType,
                CustodyType = accountType,
                Sector = "Fund"
            },
            Purchase = new Purchase
            {
                PurchaseDate = new DateTime(2024, 7, 2),
                Shares = 100_000m,
                UnitPrice = cost / 100_000m,
                ExchangeRate = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 100_000m,
                CurrentPrice = marketValue / 100_000m,
                CurrentExchangeRate = 1m
            },
            MutualFund = new MutualFundHolding
            {
                FundName = "SBI V S&P500 Index Fund",
                FundCode = "SBI-V-SP500",
                UnitsHeld = 100_000m,
                UnitBase = 10_000m,
                AverageCostNav = cost / 10m,
                CurrentNav = marketValue / 10m,
                AcquisitionAmount = cost,
                MarketValue = marketValue,
                AccountType = accountType
            }
        };
}
