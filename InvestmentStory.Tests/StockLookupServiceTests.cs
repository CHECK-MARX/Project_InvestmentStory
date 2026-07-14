using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class StockLookupServiceTests
{
    [Theory]
    [InlineData("8151", "8151", "東陽テクニカ", "日本", "JPY")]
    [InlineData("三菱HC", "8593", "三菱HCキャピタル", "日本", "JPY")]
    [InlineData("KO", "KO", "The Coca-Cola Company", "米国", "USD")]
    [InlineData("PEP", "PEP", "PepsiCo, Inc.", "米国", "USD")]
    [InlineData("伊藤園", "2593", "伊藤園", "日本", "JPY")]
    [InlineData("ライオン", "4912", "ライオン", "日本", "JPY")]
    [InlineData("マクニカホールディングス", "3132", "マクニカホールディングス", "日本", "JPY")]
    public void LocalStockLookupService_FindsKnownStocks(
        string query,
        string expectedTicker,
        string expectedName,
        string expectedCountry,
        string expectedCurrency)
    {
        var service = new LocalStockLookupService();

        var result = service.Find(query);

        Assert.NotNull(result);
        Assert.Equal(expectedTicker, result.Ticker);
        Assert.Equal(expectedName, result.Name);
        Assert.Equal(expectedCountry, result.Country);
        Assert.Equal(expectedCurrency, result.Currency);
    }

    [Fact]
    public void LocalStockLookupService_ReturnsKnownDividendFallbackForItoEn()
    {
        var service = new LocalStockLookupService();

        var result = service.Find("2593");

        Assert.NotNull(result);
        Assert.Equal(48m, result.AnnualDividendPerShare);
        Assert.Equal("年2回", result.DividendFrequency);
    }
}
