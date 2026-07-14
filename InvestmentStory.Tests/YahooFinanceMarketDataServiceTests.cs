using System.Net;
using System.Net.Http;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class YahooFinanceMarketDataServiceTests
{
    [Fact]
    public void GetQuote_FallsBackToDirectChartWhenSearchReturnsNoQuotes()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("/v1/finance/search", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""{"quotes":[]}""");
            }

            if (uri.Contains("/v8/finance/chart/SPCX", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""
                    {"chart":{"result":[{"meta":{"currency":"USD","symbol":"SPCX","exchangeName":"NMS","fullExchangeName":"NasdaqGS","instrumentType":"EQUITY","regularMarketTime":1783972800,"regularMarketPrice":139.14,"longName":"Space Exploration Technologies Corp.","shortName":"Space Exploration Technologies"}}]}}
                    """);
            }

            if (uri.Contains("/v8/finance/chart/JPY%3DX", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("/v8/finance/chart/JPY=X", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""
                    {"chart":{"result":[{"meta":{"currency":"JPY","symbol":"JPY=X","regularMarketTime":1783972800,"regularMarketPrice":160.0,"shortName":"USD/JPY"}}]}}
                    """);
            }

            if (uri.Contains("events=div", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""{"chart":{"result":[{}]}}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new YahooFinanceMarketDataService(_ => new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com") });

        var result = service.GetQuote("spcx", new AppSettings { MarketDataMode = "Web/API" });

        Assert.True(result.IsSuccess);
        Assert.Equal("SPCX", result.Quote?.Symbol);
        Assert.Equal("Space Exploration Technologies Corp.", result.Quote?.Name);
        Assert.Equal(139.14m, result.Quote?.CurrentPrice);
        Assert.Equal("USD", result.Quote?.Currency);
        Assert.Contains(result.Logs, log => log.ApiType == "YahooSearch" && log.Summary.Contains("count=0", StringComparison.Ordinal));
        Assert.Contains(result.Logs, log => log.ApiType == "YahooChart" && log.Symbol == "SPCX" && log.Summary.Contains("meta=true", StringComparison.Ordinal));
    }

    [Fact]
    public void GetQuote_FailsOnlyWhenSearchAndDirectChartCannotResolve()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("/v1/finance/search", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""{"quotes":[]}""");
            }

            if (uri.Contains("/v8/finance/chart/", StringComparison.OrdinalIgnoreCase))
            {
                return Json("""{"chart":{"result":[]}}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new YahooFinanceMarketDataService(_ => new HttpClient(handler));

        var result = service.GetQuote("NO_SUCH_SYMBOL", new AppSettings { MarketDataMode = "Web/API" });

        Assert.False(result.IsSuccess);
        Assert.Contains("取得できませんでした", result.ErrorMessage);
        Assert.Contains(result.Logs, log => log.ApiType == "YahooSearch" && log.Summary.Contains("count=0", StringComparison.Ordinal));
        Assert.Contains(result.Logs, log => log.ApiType == "YahooChart" && log.Summary.Contains("Chart result count=0", StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
