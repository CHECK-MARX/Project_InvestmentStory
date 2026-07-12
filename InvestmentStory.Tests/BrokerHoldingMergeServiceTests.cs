using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class BrokerHoldingMergeServiceTests
{
    [Fact]
    public void MergeHoldings_OverwritesManualValues_WhenBrokerAndTickerMatch()
    {
        var service = new BrokerHoldingMergeService();
        var existing = new StockPosition
        {
            Stock = new Stock
            {
                Id = 10,
                Ticker = "2593",
                Name = "Manual Ito En",
                Broker = "SBI証券",
                Currency = "JPY",
                DataSource = "手入力",
                Memo = "manual memo"
            },
            Purchase = new Purchase
            {
                Id = 20,
                StockId = 10,
                PurchaseDate = new DateTime(2024, 1, 10),
                Shares = 100m,
                UnitPrice = 1000m,
                ExchangeRate = 1m,
                Memo = "manual purchase memo"
            },
            Split = new StockSplit
            {
                Id = 30,
                StockId = 10,
                SplitDate = new DateTime(2024, 1, 10),
                SplitRatio = 1m,
                Memo = "split memo"
            },
            CurrentHolding = new CurrentHolding
            {
                Id = 40,
                StockId = 10,
                CurrentShares = 100m,
                CurrentPrice = 1000m,
                CurrentExchangeRate = 1m
            }
        };
        var brokerHolding = new BrokerHoldingRecord
        {
            Broker = "SBI証券",
            Ticker = "2593.T",
            Name = "伊藤園",
            Shares = 200m,
            AverageAcquisitionPrice = 3039m,
            MarketValue = 620000m,
            Currency = "JPY"
        };

        var result = service.MergeHoldings(new[] { existing }, new[] { brokerHolding }, new DateTime(2026, 7, 7, 15, 0, 0));

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(BrokerMergeAction.Overwrite, decision.Action);
        Assert.NotNull(decision.Merged);
        Assert.Equal(200m, decision.Merged.Purchase.Shares);
        Assert.Equal(3039m, decision.Merged.Purchase.UnitPrice);
        Assert.Equal(200m, decision.Merged.CurrentHolding.CurrentShares);
        Assert.Equal(3100m, decision.Merged.CurrentHolding.CurrentPrice);
        Assert.Equal("伊藤園", decision.Merged.Stock.Name);
        Assert.Equal("証券会社データ", decision.Merged.Stock.DataSource);
        Assert.Equal("manual memo", decision.Merged.Stock.Memo);
        Assert.Equal("manual purchase memo", decision.Merged.Purchase.Memo);
        Assert.Equal("split memo", decision.Merged.Split.Memo);
    }

    [Fact]
    public void MergeHoldings_CreatesSeparateHolding_WhenTickerMatchesDifferentBroker()
    {
        var service = new BrokerHoldingMergeService();
        var existing = new StockPosition
        {
            Stock = new Stock
            {
                Ticker = "KO",
                Name = "Coca-Cola",
                Broker = "SBI証券",
                Currency = "USD"
            }
        };
        var brokerHolding = new BrokerHoldingRecord
        {
            Broker = "楽天証券",
            Ticker = "KO",
            Name = "The Coca-Cola Company",
            Shares = 10m,
            AverageAcquisitionPrice = 60m,
            MarketValue = 650m,
            Currency = "USD"
        };

        var result = service.MergeHoldings(new[] { existing }, new[] { brokerHolding }, DateTime.Now);

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(BrokerMergeAction.Create, decision.Action);
        Assert.NotNull(decision.Merged);
        Assert.Equal("楽天証券", decision.Merged.Stock.Broker);
        Assert.Equal("KO", decision.Merged.Stock.Ticker);
    }

    [Fact]
    public void MergeHoldings_UsesSingleRepresentative_WhenDuplicateIdentityAlreadyExists()
    {
        var service = new BrokerHoldingMergeService();
        var older = CreateExistingPosition(
            id: 1,
            shares: 10m,
            currentPrice: 50m,
            updatedAt: new DateTime(2026, 1, 1));
        var newer = CreateExistingPosition(
            id: 2,
            shares: 20m,
            currentPrice: 55m,
            updatedAt: new DateTime(2026, 7, 1));
        var brokerHolding = new BrokerHoldingRecord
        {
            Broker = "SBI",
            Ticker = "KO",
            Name = "Coca-Cola",
            Account = AccountTypes.Specific,
            Shares = 20m,
            AverageAcquisitionPrice = 45m,
            CurrentPrice = 60m,
            Currency = "USD"
        };

        var result = service.MergeHoldings(new[] { older, newer }, new[] { brokerHolding }, DateTime.Now);

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(BrokerMergeAction.Overwrite, decision.Action);
        Assert.NotNull(decision.Existing);
        Assert.Equal(2, decision.Existing.Stock.Id);
        Assert.NotNull(decision.Merged);
        Assert.Equal(60m, decision.Merged.CurrentHolding.CurrentPrice);
    }

    [Fact]
    public void MergeHoldings_OverwritesMutualFund_WhenExistingAccountTypeWasMisclassifiedAsGrowth()
    {
        var service = new BrokerHoldingMergeService();
        var existing = new StockPosition
        {
            Stock = new Stock
            {
                Id = 10,
                AssetType = AssetTypes.MutualFund,
                Ticker = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                Name = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                Broker = "SBI証券",
                AccountType = AccountTypes.NisaGrowth,
                CustodyType = "投資信託（金額/NISA預り（つみたて投資枠））",
                Currency = "JPY"
            },
            MutualFund = new MutualFundHolding
            {
                FundName = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                FundCode = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
                UnitsHeld = 411_318m,
                UnitBase = 10_000m,
                AverageCostNav = 29_499m,
                CurrentNav = 40_579m,
                AcquisitionAmount = 1_213_346m,
                MarketValue = 1_669_087m,
                UnrealizedGainLoss = 455_741m
            },
            CurrentHolding = new CurrentHolding { CurrentShares = 411_318m, CurrentPrice = 40_579m },
            Purchase = new Purchase { Shares = 411_318m, UnitPrice = 29_499m },
            Split = new StockSplit { SplitRatio = 1m }
        };
        var brokerHolding = new BrokerHoldingRecord
        {
            AssetType = AssetTypes.MutualFund,
            Broker = "SBI証券",
            Account = "投資信託（金額/NISA預り（つみたて投資枠））",
            Ticker = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
            Name = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
            FundName = "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
            FundCode = "FUND:ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド",
            Shares = 411_318m,
            UnitsHeld = 411_318m,
            UnitBase = 10_000m,
            AverageCostNav = 29_499m,
            CurrentNav = 41_000m,
            AcquisitionAmount = 1_213_346m,
            MarketValue = 1_686_405m,
            MarketValueJpy = 1_686_405m,
            UnrealizedGainLossJpy = 473_059m,
            Currency = "JPY"
        };

        var result = service.MergeHoldings(new[] { existing }, new[] { brokerHolding }, DateTime.Now);

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(BrokerMergeAction.Overwrite, decision.Action);
        Assert.NotNull(decision.Existing);
        Assert.Equal(10, decision.Existing.Stock.Id);
        Assert.NotNull(decision.Merged);
        Assert.Equal(AccountTypes.NisaAccumulation, decision.Merged.Stock.AccountType);
        Assert.Equal(1_686_405m, decision.Merged.MutualFund.MarketValue);
    }

    private static StockPosition CreateExistingPosition(int id, decimal shares, decimal currentPrice, DateTime updatedAt)
    {
        return new StockPosition
        {
            Stock = new Stock
            {
                Id = id,
                AssetType = AssetTypes.Stock,
                Ticker = "KO",
                Name = "Coca-Cola",
                Broker = "SBI",
                AccountType = AccountTypes.Specific,
                CustodyType = AccountTypes.Specific,
                Currency = "USD"
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = shares,
                CurrentPrice = currentPrice,
                UpdatedAt = updatedAt
            },
            Purchase = new Purchase
            {
                Shares = shares,
                UnitPrice = 45m,
                ExchangeRate = 160m
            },
            Split = new StockSplit { SplitRatio = 1m }
        };
    }
}
