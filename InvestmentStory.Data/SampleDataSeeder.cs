using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Data;

public static class SampleDataSeeder
{
    private const decimal SampleUsdJpy = 162.35m;

    public static InvestmentStoryRepository ResetSampleSessionDatabase(AppSettings? baseSettings = null)
    {
        return ResetSampleSessionDatabase(DatabasePaths.GetSampleSessionDatabasePath(), baseSettings);
    }

    public static InvestmentStoryRepository ResetSampleSessionDatabase(string databasePath, AppSettings? baseSettings = null)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        var repository = new InvestmentStoryRepository(databasePath);
        repository.Initialize();

        var settings = baseSettings ?? new AppSettings();
        settings.DataDisplayMode = DataDisplayModes.Sample;
        settings.MarketDataMode = "Mock";
        settings.ExchangeRateProvider = "Mock";
        settings.BrokerDataMode = "Sample";
        repository.SaveSettings(settings);

        SeedPositions(repository);
        SeedDividends(repository);
        SeedSnapshots(repository);
        repository.SaveGoal(new IncomeGoal
        {
            TargetYear = DateTime.Today.Year,
            AnnualPassiveIncomeGoal = 1_200_000m,
            MonthlyPassiveIncomeGoal = 100_000m,
            TotalAssetGoal = 35_000_000m
        });

        return repository;
    }

    public static bool IsSampleDatabase(string databasePath) =>
        string.Equals(
            Path.GetFullPath(databasePath),
            Path.GetFullPath(DatabasePaths.GetSampleSessionDatabasePath()),
            StringComparison.OrdinalIgnoreCase);

    private static void SeedPositions(InvestmentStoryRepository repository)
    {
        SaveStock(repository, "7203", "トヨタ自動車", "日本", "JPY", "SBI証券", AccountTypes.Specific,
            shares: 300m, cost: 2400m, price: 3050m, dividend: 90m, sector: "自動車");
        SaveStock(repository, "9433", "KDDI", "日本", "JPY", "野村證券", AccountTypes.NisaGrowth,
            shares: 200m, cost: 4250m, price: 4980m, dividend: 145m, sector: "通信");
        SaveStock(repository, "8593", "三菱HCキャピタル", "日本", "JPY", "SBI証券", AccountTypes.NisaGrowth,
            shares: 200m, cost: 1346m, price: 1382m, dividend: 48m, sector: "金融");
        SaveStock(repository, "8058", "三菱商事", "日本", "JPY", "野村證券", AccountTypes.Specific,
            shares: 100m, cost: 2500m, price: 3275m, dividend: 110m, sector: "商社");

        SaveStock(repository, "AAPL", "Apple Inc.", "米国", "USD", "野村證券", AccountTypes.Specific,
            shares: 10m, cost: 160m, price: 313.39m, dividend: 1.04m, sector: "Technology");
        SaveStock(repository, "MSFT", "Microsoft Corp.", "米国", "USD", "SBI証券", AccountTypes.NisaGrowth,
            shares: 8m, cost: 310m, price: 503.50m, dividend: 3.32m, sector: "Technology");
        SaveStock(repository, "NVDA", "NVIDIA Corp.", "米国", "USD", "SBI証券", AccountTypes.Specific,
            shares: 50m, cost: 21.83m, price: 196m, dividend: 1.00m, sector: "Semiconductors");
        SaveStock(repository, "KO", "Coca-Cola Co.", "米国", "USD", "野村證券", AccountTypes.Specific,
            shares: 20m, cost: 55.25m, price: 82.96m, dividend: 2.08m, sector: "Consumer Defensive");
        SaveStock(repository, "MO", "Altria Group", "米国", "USD", "SBI証券", AccountTypes.Specific,
            shares: 30m, cost: 42.36m, price: 72.96m, dividend: 4.24m, sector: "Consumer Defensive");
        SaveStock(repository, "NFLX", "Netflix Inc.", "米国", "USD", "SBI証券", AccountTypes.Specific,
            shares: 10m, cost: 82.67m, price: 76.18m, dividend: 0m, sector: "Communication Services");

        SaveFund(repository,
            ticker: "FUND:SBI-V-SP500",
            fundName: "SBI・V・S&P500インデックス・ファンド",
            broker: "SBI証券",
            accountType: AccountTypes.NisaAccumulation,
            unitsHeld: 411_318m,
            averageCostNav: 29_499m,
            currentNav: 40_579m,
            acquisitionAmount: 1_213_346m,
            marketValue: 1_669_087m,
            unrealizedGainLoss: 455_741m,
            distributionMethod: "再投資");
    }

    private static int SaveStock(
        InvestmentStoryRepository repository,
        string ticker,
        string name,
        string country,
        string currency,
        string broker,
        string accountType,
        decimal shares,
        decimal cost,
        decimal price,
        decimal dividend,
        string sector)
    {
        var exchangeRate = currency.Equals("USD", StringComparison.OrdinalIgnoreCase) ? SampleUsdJpy : 1m;
        var position = new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.Stock,
                Ticker = ticker,
                Name = name,
                Country = country,
                Currency = currency,
                Broker = broker,
                AccountType = accountType,
                CustodyType = accountType,
                Sector = sector,
                DataSource = "SampleData"
            },
            Purchase = new Purchase
            {
                PurchaseDate = DateTime.Today.AddYears(-3),
                Shares = shares,
                UnitPrice = cost,
                ExchangeRate = exchangeRate,
                ExchangeRateAcquiredAt = DateTime.Today.AddYears(-3),
                ExchangeRateSource = "SampleData",
                ExchangeRateInputType = "Sample"
            },
            Split = new StockSplit { SplitRatio = 1m, SplitDate = DateTime.Today.AddYears(-3) },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = shares,
                CurrentPrice = price,
                CurrentExchangeRate = exchangeRate,
                ExchangeRateAcquiredAt = DateTime.Now,
                ExchangeRateSource = "SampleExchangeRateService",
                ExchangeRateInputType = "Sample",
                AnnualDividendPerShare = dividend,
                DividendStatus = dividend > 0m ? "配当あり" : "配当なし",
                DividendFrequency = dividend > 0m ? "年4回" : "なし",
                DividendMonths = dividend > 0m ? "3,6,9,12" : string.Empty,
                CurrentPriceAcquiredAt = DateTime.Now,
                CurrentPriceSource = "SampleMarketDataService",
                DividendInfoAcquiredAt = DateTime.Now,
                DividendInfoSource = "SampleDividendDataService",
                UpdatedAt = DateTime.Today
            }
        };

        return repository.SavePosition(position);
    }

    private static int SaveFund(
        InvestmentStoryRepository repository,
        string ticker,
        string fundName,
        string broker,
        string accountType,
        decimal unitsHeld,
        decimal averageCostNav,
        decimal currentNav,
        decimal acquisitionAmount,
        decimal marketValue,
        decimal unrealizedGainLoss,
        string distributionMethod)
    {
        var position = new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund,
                Ticker = ticker,
                Name = fundName,
                Country = "日本",
                Currency = "JPY",
                Broker = broker,
                AccountType = accountType,
                CustodyType = accountType,
                Sector = "投資信託",
                DataSource = "SampleData"
            },
            Purchase = new Purchase
            {
                PurchaseDate = DateTime.Today.AddYears(-2),
                Shares = unitsHeld,
                UnitPrice = averageCostNav,
                ExchangeRate = 1m,
                ExchangeRateSource = "SampleData",
                ExchangeRateInputType = "Sample"
            },
            Split = new StockSplit { SplitRatio = 1m, SplitDate = DateTime.Today.AddYears(-2) },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = unitsHeld,
                CurrentPrice = currentNav,
                CurrentExchangeRate = 1m,
                ExchangeRateSource = "SampleData",
                ExchangeRateInputType = "Sample",
                CurrentPriceAcquiredAt = DateTime.Today,
                CurrentPriceSource = "SampleFundMarketDataService",
                UpdatedAt = DateTime.Today
            },
            MutualFund = new MutualFundHolding
            {
                FundName = fundName,
                FundCode = ticker,
                UnitsHeld = unitsHeld,
                UnitBase = 10_000m,
                AverageCostNav = averageCostNav,
                CurrentNav = currentNav,
                AcquisitionAmount = acquisitionAmount,
                MarketValue = marketValue,
                UnrealizedGainLoss = unrealizedGainLoss,
                NavDate = DateTime.Today,
                NavSource = "SampleFundMarketDataService",
                DistributionMethod = distributionMethod,
                AccountType = accountType,
                TotalPurchaseAmount = acquisitionAmount,
                ReinvestedDistributionAmount = 0m
            }
        };

        return repository.SavePosition(position);
    }

    private static void SeedDividends(InvestmentStoryRepository repository)
    {
        var positions = repository.GetPositions()
            .Where(x => !x.IsMutualFund && x.CurrentHolding.AnnualDividendPerShare > 0m)
            .ToList();
        var currentYear = DateTime.Today.Year;

        foreach (var position in positions)
        {
            var currency = position.Stock.Currency;
            var exchangeRate = currency.Equals("USD", StringComparison.OrdinalIgnoreCase) ? SampleUsdJpy : 1m;
            var quarterly = position.CurrentHolding.AnnualDividendPerShare / 4m;
            foreach (var month in new[] { 3, 6 })
            {
                SaveDividend(repository, position, new DateTime(currentYear, month, 20), quarterly, exchangeRate, DividendConstants.Actual);
            }

            foreach (var month in new[] { 9, 12 })
            {
                SaveDividend(repository, position, new DateTime(currentYear, month, 20), quarterly, exchangeRate, DividendConstants.Estimated);
            }

            SaveDividend(repository, position, new DateTime(currentYear - 1, 6, 20), quarterly * 0.92m, exchangeRate, DividendConstants.Actual);
            SaveDividend(repository, position, new DateTime(currentYear - 1, 12, 20), quarterly * 0.95m, exchangeRate, DividendConstants.Actual);
        }
    }

    private static void SaveDividend(
        InvestmentStoryRepository repository,
        StockPosition position,
        DateTime paymentDate,
        decimal dividendPerShare,
        decimal exchangeRate,
        string status)
    {
        var quantity = position.CurrentHolding.CurrentShares;
        var gross = Math.Round(quantity * dividendPerShare, 2);
        var taxRate = AccountTypes.IsNisa(position.Stock.AccountType) ? 0m : 0.20315m;
        var tax = Math.Round(gross * taxRate, 2);
        var net = gross - tax;
        var netJpy = Math.Round(net * exchangeRate, 0);

        repository.SaveDividendPayment(new DividendPayment
        {
            StockId = position.Stock.Id,
            AccountType = position.Stock.AccountType,
            TaxAccountType = position.Stock.AccountType,
            PaymentDate = paymentDate,
            StockName = position.Stock.Name,
            Ticker = position.Stock.Ticker,
            Broker = position.Stock.Broker,
            DividendStatus = status,
            Source = "SampleData",
            Quantity = quantity,
            DividendPerShare = dividendPerShare,
            GrossAmount = gross,
            TaxAmount = tax,
            TotalTaxAmount = tax,
            NetAmount = net,
            Currency = position.Stock.Currency,
            ExchangeRate = exchangeRate,
            ExchangeRateAcquiredAt = paymentDate,
            ExchangeRateSource = "SampleExchangeRateService",
            ExchangeRateInputType = "Sample",
            GrossAmountJpy = Math.Round(gross * exchangeRate, 0),
            TotalTaxAmountJpy = Math.Round(tax * exchangeRate, 0),
            NetAmountJpy = netJpy,
            JpyAmount = netJpy,
            IsTaxEstimated = status != DividendConstants.Actual,
            IsNisa = AccountTypes.IsNisa(position.Stock.AccountType),
            IsForeignStock = position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase),
            Memo = "Sample mode generated data"
        });
    }

    private static void SeedSnapshots(InvestmentStoryRepository repository)
    {
        var today = DateTime.Today;
        var baseMarket = 18_500_000m;
        var baseCost = 14_200_000m;
        for (var i = 6; i >= 0; i--)
        {
            var date = today.AddMonths(-i);
            var market = baseMarket + (6 - i) * 720_000m;
            var cost = baseCost + (6 - i) * 180_000m;
            var dividend = (6 - i) * 48_000m;
            repository.SavePortfolioSnapshot(new PortfolioSnapshot
            {
                SnapshotDate = date,
                TotalMarketValueJpy = market,
                TotalCostBasisJpy = cost,
                UnrealizedGainLossJpy = market - cost,
                CumulativeDividendJpy = dividend,
                RealizedGainLossJpy = 185_000m,
                TotalReturnJpy = market - cost + dividend + 185_000m,
                UsdJpyRate = SampleUsdJpy,
                StockValueJpy = market - 1_669_087m,
                MutualFundValueJpy = 1_669_087m,
                CashValueJpy = 0m
            });
        }
    }
}
