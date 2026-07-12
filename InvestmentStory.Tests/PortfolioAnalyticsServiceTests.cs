using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class PortfolioAnalyticsServiceTests
{
    [Theory]
    [InlineData("NISA成長投資枠", AccountTypes.NisaGrowth)]
    [InlineData("新NISA", AccountTypes.NisaGrowth)]
    [InlineData("NISAつみたて投資枠", AccountTypes.NisaAccumulation)]
    [InlineData("投資信託（金額/NISA預り（つみたて投資枠））", AccountTypes.NisaAccumulation)]
    [InlineData("投資信託（金額/旧つみたてNISA預り）", AccountTypes.NisaAccumulation)]
    [InlineData("旧NISA", AccountTypes.NisaLegacy)]
    [InlineData("特定口座", AccountTypes.Specific)]
    [InlineData("一般口座", AccountTypes.General)]
    [InlineData(AccountTypes.NisaGrowth, AccountTypes.NisaGrowth)]
    [InlineData(AccountTypes.NisaAccumulation, AccountTypes.NisaAccumulation)]
    [InlineData(AccountTypes.NisaLegacy, AccountTypes.NisaLegacy)]
    [InlineData(AccountTypes.Specific, AccountTypes.Specific)]
    [InlineData(AccountTypes.General, AccountTypes.General)]
    [InlineData("", AccountTypes.Unknown)]
    public void AccountTypeNormalizer_NormalizesBrokerLabels(string input, string expected)
    {
        Assert.Equal(expected, AccountTypeNormalizer.Normalize(input));
    }

    [Fact]
    public void MonthlyDividendBreakdown_SeparatesActualPlannedPreviousAndGoal()
    {
        var service = new PortfolioAnalyticsService();
        var dividends = new[]
        {
            Payment(1, new DateTime(2026, 6, 10), DividendConstants.Actual, 1000m),
            Payment(1, new DateTime(2026, 8, 10), DividendConstants.Estimated, 3000m),
            Payment(1, new DateTime(2026, 9, 10), DividendConstants.Replaced, 9999m),
            Payment(1, new DateTime(2025, 6, 10), DividendConstants.Actual, 700m),
            Payment(2, new DateTime(2026, 8, 10), DividendConstants.PaymentDue, 2000m)
        };

        var result = service.BuildMonthlyDividendBreakdown(dividends, 2026, 10_000m);

        var june = result.Single(x => x.Month == 6);
        Assert.Equal(1000m, june.ActualJpy);
        Assert.Equal(0m, june.PlannedJpy);
        Assert.Equal(700m, june.PreviousYearActualJpy);
        Assert.Equal(10_000m, june.MonthlyGoalJpy);

        var august = result.Single(x => x.Month == 8);
        Assert.Equal(0m, august.ActualJpy);
        Assert.Equal(5000m, august.PlannedJpy);
        Assert.Equal(5000m, august.ForecastJpy);

        Assert.Equal(0m, result.Single(x => x.Month == 9).PlannedJpy);
    }

    [Fact]
    public void PortfolioSnapshot_UpsertsSameDateAndKeepsDifferentDates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_snapshot_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();

            repository.SavePortfolioSnapshot(new PortfolioSnapshot
            {
                SnapshotDate = new DateTime(2026, 7, 10),
                TotalMarketValueJpy = 100m,
                TotalCostBasisJpy = 80m,
                UnrealizedGainLossJpy = 20m
            });
            repository.SavePortfolioSnapshot(new PortfolioSnapshot
            {
                SnapshotDate = new DateTime(2026, 7, 10),
                TotalMarketValueJpy = 150m,
                TotalCostBasisJpy = 90m,
                UnrealizedGainLossJpy = 60m
            });
            repository.SavePortfolioSnapshot(new PortfolioSnapshot
            {
                SnapshotDate = new DateTime(2026, 7, 11),
                TotalMarketValueJpy = 170m,
                TotalCostBasisJpy = 100m,
                UnrealizedGainLossJpy = 70m
            });

            var snapshots = repository.GetPortfolioSnapshots();

            Assert.Equal(2, snapshots.Count);
            Assert.Equal(150m, snapshots.Single(x => x.SnapshotDate == new DateTime(2026, 7, 10)).TotalMarketValueJpy);
            Assert.Equal(170m, snapshots.Single(x => x.SnapshotDate == new DateTime(2026, 7, 11)).TotalMarketValueJpy);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void PortfolioSnapshot_SplitsStockAndMutualFundValues()
    {
        var calculator = new InvestmentCalculator();
        var stockSnapshot = calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Ticker = "8151", Name = "東陽テクニカ", Currency = "JPY" },
            Purchase = new Purchase { Shares = 100m, UnitPrice = 1_000m, ExchangeRate = 1m },
            CurrentHolding = new CurrentHolding { CurrentShares = 100m, CurrentPrice = 1_500m, CurrentExchangeRate = 1m }
        });
        var fundSnapshot = calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Ticker = "FUND:SBI-V-SP500", Name = "SBI V S&P500", Currency = "JPY", AssetType = AssetTypes.MutualFund },
            MutualFund = new MutualFundHolding
            {
                UnitsHeld = 411_318m,
                UnitBase = 10_000m,
                AverageCostNav = 29_499m,
                CurrentNav = 40_579m,
                AcquisitionAmount = 1_213_346m,
                MarketValue = 1_669_087m
            }
        });

        var result = new PortfolioAnalyticsService().CreatePortfolioSnapshot(
            new[] { stockSnapshot, fundSnapshot },
            Array.Empty<DividendPayment>(),
            realizedGainLossJpy: 0m,
            usdJpyRate: 162m,
            snapshotDate: new DateTime(2026, 7, 11));

        Assert.Equal(150_000m, result.StockValueJpy);
        Assert.Equal(1_669_087m, result.MutualFundValueJpy);
        Assert.Equal(1_819_087m, result.TotalMarketValueJpy);
        Assert.Equal(result.CumulativeDividendJpy, result.CumulativeNetDividendJpy);
    }

    [Fact]
    public void DataQualityService_IncludesDisplayValueAndMissingState()
    {
        var result = new DataQualityService().BuildForPosition(new StockPosition
        {
            Stock = new Stock { Id = 10, Ticker = "PEP", Name = "ペプシコ", Currency = "USD", AccountType = AccountTypes.Unknown },
            Purchase = new Purchase { Shares = 20m, UnitPrice = 142.98m, ExchangeRate = 160m },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 20m,
                CurrentPrice = 0m,
                CurrentExchangeRate = 162m,
                DividendStatus = "配当未入力"
            }
        }, new DateTime(2026, 7, 11));

        var price = result.Single(x => x.FieldName == "現在株価");
        var account = result.Single(x => x.FieldName == "口座区分");

        Assert.Equal("0.00", price.Value);
        Assert.Equal(DataQualityStates.Missing, price.ConfidenceLevel);
        Assert.Equal(AccountTypes.Unknown, account.Value);
        Assert.Equal(DataQualityStates.Missing, account.ConfidenceLevel);
    }

    [Fact]
    public void ReturnSummary_AddsUnrealizedRealizedAndCumulativeDividends()
    {
        var calculator = new InvestmentCalculator();
        var snapshots = new[]
        {
            calculator.CreateSnapshot(new StockPosition
            {
                Stock = new Stock { Ticker = "A", Name = "A", Currency = "JPY" },
                Purchase = new Purchase { Shares = 10m, UnitPrice = 100m, ExchangeRate = 1m },
                CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 150m, CurrentExchangeRate = 1m }
            }),
            calculator.CreateSnapshot(new StockPosition
            {
                Stock = new Stock { Ticker = "B", Name = "B", Currency = "JPY" },
                Purchase = new Purchase { Shares = 5m, UnitPrice = 100m, ExchangeRate = 1m },
                CurrentHolding = new CurrentHolding { CurrentShares = 5m, CurrentPrice = 100m, CurrentExchangeRate = 1m }
            })
        };

        var summary = new PortfolioAnalyticsService().BuildReturnSummary(
            snapshots,
            new[] { Payment(1, new DateTime(2026, 1, 1), DividendConstants.Actual, 120m) },
            realizedGainLossJpy: 80m);

        Assert.Equal(500m, summary.UnrealizedGainLossJpy);
        Assert.Equal(80m, summary.RealizedGainLossJpy);
        Assert.Equal(120m, summary.CumulativeDividendJpy);
        Assert.Equal(700m, summary.TotalReturnJpy);
        Assert.Equal(46.67m, summary.TotalReturnRate, precision: 2);
        Assert.Equal(8m, summary.CapitalRecoveryRate);
        Assert.True(summary.Top5ConcentrationRate > 99m);
    }

    [Fact]
    public void TradeLedger_UsesCsvRealizedProfitLossWhenAvailable()
    {
        var result = new TradeLedgerService().BuildLedger(1, new[]
        {
            new BrokerTradeRecord
            {
                TradeDate = new DateTime(2026, 1, 1),
                TradeType = "買付",
                Quantity = 10m,
                SignedQuantity = 10m,
                UnitPrice = 100m,
                Currency = "JPY"
            },
            new BrokerTradeRecord
            {
                TradeDate = new DateTime(2026, 2, 1),
                TradeType = "売却",
                Quantity = 5m,
                SignedQuantity = -5m,
                UnitPrice = 120m,
                ProfitLossJpy = 300m,
                Currency = "JPY"
            }
        });

        var sale = result.Last();

        Assert.Equal(300m, sale.RealizedGainLossJpy);
        Assert.Equal(5m, sale.AfterTradeQuantity);
    }

    [Fact]
    public void BuildFxSensitivity_CalculatesUsdJpyImpact()
    {
        var service = new PortfolioAnalyticsService();
        var snapshots = new[]
        {
            new StockSnapshot
            {
                Position = new StockPosition
                {
                    Stock = new Stock { Ticker = "AAPL", Currency = "USD" },
                    CurrentHolding = new CurrentHolding { CurrentExchangeRate = 160m }
                },
                CurrentMarketValue = 1_000m,
                CurrentMarketValueJpy = 160_000m
            },
            new StockSnapshot
            {
                Position = new StockPosition
                {
                    Stock = new Stock { Ticker = "7203", Currency = "JPY" }
                },
                CurrentMarketValue = 50_000m,
                CurrentMarketValueJpy = 50_000m
            }
        };

        var result = service.BuildFxSensitivity(snapshots, 160m);

        Assert.Contains(result, x =>
            x.RateDelta == 10m &&
            x.UsdJpyRate == 170m &&
            x.TotalMarketValueJpy == 220_000m &&
            x.ChangeFromCurrentJpy == 10_000m);
        Assert.Contains(result, x =>
            x.RateDelta == -10m &&
            x.UsdJpyRate == 150m &&
            x.TotalMarketValueJpy == 200_000m &&
            x.ChangeFromCurrentJpy == -10_000m);
    }

    private static DividendPayment Payment(int stockId, DateTime date, string status, decimal netAmountJpy) =>
        new()
        {
            StockId = stockId,
            Ticker = $"T{stockId}",
            StockName = $"Stock {stockId}",
            PaymentDate = date,
            DividendStatus = status,
            NetAmountJpy = netAmountJpy,
            JpyAmount = netAmountJpy,
            Currency = "JPY",
            ExchangeRate = 1m
        };
}
