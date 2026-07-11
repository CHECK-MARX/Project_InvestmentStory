using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class SecurityIdentityServiceTests
{
    [Fact]
    public void BuildCanonicalKey_IgnoresBrokerAndAccount_ForSameEquity()
    {
        var nomura = CreateStockPosition("野村證券", AccountTypes.Specific, "CSCO", "Cisco Systems");
        var sbi = CreateStockPosition("SBI証券", AccountTypes.NisaGrowth, "CSCO", "Cisco Systems");

        var nomuraCanonical = SecurityIdentityService.BuildCanonicalKey(nomura);
        var sbiCanonical = SecurityIdentityService.BuildCanonicalKey(sbi);
        var nomuraPosition = SecurityIdentityService.BuildPositionKey(nomura);
        var sbiPosition = SecurityIdentityService.BuildPositionKey(sbi);

        Assert.Equal(nomuraCanonical, sbiCanonical);
        Assert.NotEqual(nomuraPosition, sbiPosition);
        Assert.Contains("CSCO", nomuraCanonical, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalKey_UsesFundCode_ForMutualFundNameVariants()
    {
        var first = CreateFundPosition("SBI証券", AccountTypes.NisaGrowth, "ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド");
        var second = CreateFundPosition("SBI証券", AccountTypes.NisaGrowth, "SBI V S&P500 Index Fund");

        first.MutualFund.FundCode = "SBI-V-SP500";
        second.MutualFund.FundCode = "SBI-V-SP500";

        Assert.Equal(
            SecurityIdentityService.BuildCanonicalKey(first),
            SecurityIdentityService.BuildCanonicalKey(second));
    }

    private static StockPosition CreateStockPosition(
        string broker,
        string accountType,
        string ticker,
        string name) =>
        new()
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.Stock,
                Broker = broker,
                AccountType = accountType,
                CustodyType = accountType,
                Country = "米国",
                Currency = "USD",
                Ticker = ticker,
                Name = name
            }
        };

    private static StockPosition CreateFundPosition(
        string broker,
        string accountType,
        string fundName) =>
        new()
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund,
                Broker = broker,
                AccountType = accountType,
                CustodyType = accountType,
                Country = "日本",
                Currency = "JPY",
                Ticker = fundName,
                Name = fundName
            },
            MutualFund = new MutualFundHolding
            {
                FundName = fundName,
                AccountType = accountType
            }
        };
}
