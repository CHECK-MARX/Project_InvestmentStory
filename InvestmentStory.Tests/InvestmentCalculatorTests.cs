using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class InvestmentCalculatorTests
{
    private readonly InvestmentCalculator _calculator = new();

    [Fact]
    public void CreateSnapshot_CalculatesSplitAdjustedMetrics()
    {
        var snapshot = _calculator.CreateSnapshot(CreateNvdaPosition());

        Assert.Equal(1450m, snapshot.PurchaseTotal);
        Assert.Equal(29m, snapshot.EffectiveAcquisitionPrice);
        Assert.Equal(9800m, snapshot.CurrentMarketValue);
        Assert.Equal(8350m, snapshot.UnrealizedGain);
        Assert.Equal(575.86m, snapshot.UnrealizedGainRate, precision: 2);
        Assert.Equal(575.86m, snapshot.UnrealizedGainRateJpy, precision: 2);
        Assert.Equal(6.76m, snapshot.Multiple, precision: 2);
        Assert.Equal(50m, snapshot.AnnualDividendForecast);
        Assert.Equal(3.45m, snapshot.YieldOnCost, precision: 2);
        Assert.Equal(0.51m, snapshot.CurrentDividendYield, precision: 2);
        Assert.Equal(1_568_000m, snapshot.CurrentMarketValueJpy);
        Assert.Equal(1_336_000m, snapshot.UnrealizedGainJpy);
        Assert.Equal(0m, snapshot.CurrencyImpactJpy);
        Assert.Equal(snapshot.PurchaseTotal, snapshot.PurchaseTotalUsd);
        Assert.Equal(snapshot.CurrentMarketValue, snapshot.CurrentMarketValueUsd);
        Assert.Equal(snapshot.UnrealizedGain, snapshot.UnrealizedGainUsd);
    }

    [Fact]
    public void CreateSnapshot_CalculatesCurrencyImpact()
    {
        var position = CreateNvdaPosition();
        position.Purchase.Shares = 10m;
        position.Purchase.UnitPrice = 100m;
        position.Purchase.ExchangeRate = 100m;
        position.CurrentHolding.CurrentShares = 10m;
        position.CurrentHolding.CurrentPrice = 150m;
        position.CurrentHolding.CurrentExchangeRate = 160m;

        var snapshot = _calculator.CreateSnapshot(position);

        Assert.Equal(1000m, snapshot.PurchaseTotalUsd);
        Assert.Equal(100_000m, snapshot.PurchaseTotalJpy);
        Assert.Equal(1500m, snapshot.CurrentMarketValueUsd);
        Assert.Equal(240_000m, snapshot.CurrentMarketValueJpy);
        Assert.Equal(500m, snapshot.UnrealizedGainUsd);
        Assert.Equal(50m, snapshot.UnrealizedGainRateUsd);
        Assert.Equal(140_000m, snapshot.UnrealizedGainJpy);
        Assert.Equal(140m, snapshot.UnrealizedGainRateJpy);
        Assert.Equal(60_000m, snapshot.CurrencyImpactJpy);
    }

    [Fact]
    public void CreateSnapshot_TreatsYenCurrencyAsJpyWithoutFxMultiplication()
    {
        var snapshot = _calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock
            {
                Name = "東陽テクニカ",
                Ticker = "8151",
                Country = "日本",
                Currency = "YEN",
                Broker = "サンプル証券"
            },
            Purchase = new Purchase
            {
                Shares = 100m,
                UnitPrice = 1500m,
                ExchangeRate = 160m
            },
            Split = new StockSplit { SplitRatio = 1m },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 100m,
                CurrentPrice = 1700m,
                CurrentExchangeRate = 160m,
                AnnualDividendPerShare = 50m
            }
        });

        Assert.Equal(150_000m, snapshot.PurchaseTotal);
        Assert.Equal(150_000m, snapshot.PurchaseTotalJpy);
        Assert.Equal(170_000m, snapshot.CurrentMarketValue);
        Assert.Equal(170_000m, snapshot.CurrentMarketValueJpy);
        Assert.Equal(20_000m, snapshot.UnrealizedGainJpy);
        Assert.Equal(0m, snapshot.CurrencyImpactJpy);
    }

    [Fact]
    public void CreateSnapshot_CalculatesMutualFundMetrics()
    {
        var snapshot = _calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund,
                Name = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                Ticker = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                Country = "日本",
                Currency = "JPY",
                Broker = "SBI証券"
            },
            MutualFund = new MutualFundHolding
            {
                FundName = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                UnitsHeld = 411318m,
                UnitBase = 10000m,
                AverageCostNav = 29499m,
                CurrentNav = 40579m
            }
        });

        Assert.Equal(1_213_346.9682m, snapshot.PurchaseTotal);
        Assert.Equal(1_669_087.3122m, snapshot.CurrentMarketValue);
        Assert.InRange(snapshot.UnrealizedGainJpy, 455_740m, 455_742m);
        Assert.Equal(37.56m, snapshot.UnrealizedGainRateJpy, precision: 2);
    }

    [Fact]
    public void CreateSnapshot_PrefersMutualFundCsvAmounts()
    {
        var snapshot = _calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund,
                Name = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                Ticker = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                Country = "日本",
                Currency = "JPY",
                Broker = "SBI証券"
            },
            MutualFund = new MutualFundHolding
            {
                FundName = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                UnitsHeld = 411318m,
                UnitBase = 10000m,
                AverageCostNav = 29499m,
                CurrentNav = 40579m,
                AcquisitionAmount = 1_213_346m,
                MarketValue = 1_669_087m,
                UnrealizedGainLoss = 455_741m
            }
        });

        Assert.Equal(1_213_346m, snapshot.PurchaseTotal);
        Assert.Equal(1_669_087m, snapshot.CurrentMarketValue);
        Assert.Equal(455_741m, snapshot.UnrealizedGainJpy);
        Assert.Equal(37.56m, snapshot.UnrealizedGainRateJpy, precision: 2);
    }

    [Fact]
    public void CreateDashboardSummary_CalculatesTotalsAndGoalGap()
    {
        var snapshot = _calculator.CreateSnapshot(CreateNvdaPosition());
        var dividends = new[]
        {
            new DividendPayment { PaymentDate = new DateTime(2026, 3, 15), JpyAmount = 1440m },
            new DividendPayment { PaymentDate = new DateTime(2026, 6, 15), JpyAmount = 1440m }
        };
        var goal = new IncomeGoal
        {
            TargetYear = 2026,
            AnnualPassiveIncomeGoal = 100_000m,
            MonthlyPassiveIncomeGoal = 10_000m
        };

        var summary = _calculator.CreateDashboardSummary(
            new[] { snapshot },
            dividends,
            goal,
            new DateTime(2026, 6, 20),
            new ExchangeRateQuote
            {
                Rate = 160m,
                AcquiredAt = new DateTime(2026, 6, 20, 9, 30, 0),
                Source = "MockExchangeRateService",
                InputType = "Mock"
            });

        Assert.Equal(1_568_000m, summary.TotalCurrentMarketValueJpy);
        Assert.Equal(1_336_000m, summary.TotalUnrealizedGainJpy);
        Assert.Equal(9800m, summary.ForeignAssetTotalUsd);
        Assert.Equal(1_568_000m, summary.ForeignAssetTotalJpy);
        Assert.Equal(1_336_000m, summary.FxIncludedUnrealizedGainJpy);
        Assert.Equal(160m, summary.CurrentUsdJpyRate);
        Assert.Equal("MockExchangeRateService", summary.ExchangeRateSource);
        Assert.Equal("Mock", summary.ExchangeRateInputType);
        Assert.Equal(1440m, summary.ThisMonthPassiveIncomeJpy);
        Assert.Equal(2880m, summary.ThisYearPassiveIncomeJpy);
        Assert.Equal(8000m, summary.AnnualPassiveIncomeForecastJpy);
        Assert.Equal(97_120m, summary.AnnualGoalGapJpy);
        Assert.Equal(2.88m, summary.AnnualGoalAchievementRate, precision: 2);
    }

    [Fact]
    public void AggregateMonthlyDividends_ReturnsAllMonths()
    {
        var dividends = new[]
        {
            new DividendPayment { PaymentDate = new DateTime(2026, 1, 5), JpyAmount = 100m },
            new DividendPayment { PaymentDate = new DateTime(2026, 1, 20), JpyAmount = 200m },
            new DividendPayment { PaymentDate = new DateTime(2026, 12, 20), JpyAmount = 1200m }
        };

        var result = _calculator.AggregateMonthlyDividends(dividends, 2026);

        Assert.Equal(12, result.Count);
        Assert.Equal("1月", result[0].Label);
        Assert.Equal(300m, result[0].AmountJpy);
        Assert.Equal(1200m, result[11].AmountJpy);
    }

    [Fact]
    public void SimulatePassiveIncome_ProjectsTenYearsAndFindsTarget()
    {
        var result = _calculator.SimulatePassiveIncome(new PassiveIncomeSimulationInput
        {
            CurrentAnnualPassiveIncome = 100_000m,
            MonthlyAdditionalInvestment = 100_000m,
            AssumedDividendYieldRate = 4m,
            AnnualDividendGrowthRate = 5m,
            TargetAnnualPassiveIncome = 300_000m,
            StartYear = 2026
        });

        Assert.Equal(10, result.Projections.Count);
        Assert.Equal(153_000m, result.Projections[0].AnnualPassiveIncome);
        Assert.Equal(53_000m, result.Projections[0].YearOverYearIncrease);
        Assert.Equal(51m, result.Projections[0].TargetAchievementRate);
        Assert.Equal(2030, result.TargetAchievementYear);
        Assert.Equal(4, result.YearsToTarget);
    }

    [Fact]
    public void MockExchangeRateService_ReturnsUsdJpy160()
    {
        var service = new MockExchangeRateService();

        var quote = service.GetUsdJpyRate();

        Assert.Equal("USD", quote.BaseCurrency);
        Assert.Equal("JPY", quote.QuoteCurrency);
        Assert.Equal(160.00m, quote.Rate);
        Assert.Equal("MockExchangeRateService", quote.Source);
        Assert.Equal("Mock", quote.InputType);
    }

    private static StockPosition CreateNvdaPosition()
    {
        return new StockPosition
        {
            Stock = new Stock
            {
                Id = 1,
                Name = "NVIDIA",
                Ticker = "NVDA",
                Country = "米国",
                Currency = "USD",
                Broker = "サンプル証券"
            },
            Purchase = new Purchase
            {
                StockId = 1,
                PurchaseDate = new DateTime(2021, 1, 15),
                Shares = 5m,
                UnitPrice = 290m,
                ExchangeRate = 160m
            },
            Split = new StockSplit
            {
                StockId = 1,
                SplitDate = new DateTime(2024, 6, 10),
                SplitRatio = 10m
            },
            CurrentHolding = new CurrentHolding
            {
                StockId = 1,
                CurrentShares = 50m,
                CurrentPrice = 196m,
                CurrentExchangeRate = 160m,
                AnnualDividendPerShare = 1m
            }
        };
    }
}
