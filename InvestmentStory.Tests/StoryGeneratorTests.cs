using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class StoryGeneratorTests
{
    [Fact]
    public void Generate_ExplainsGainAndDividendSituation()
    {
        var calculator = new InvestmentCalculator();
        var generator = new StoryGenerator();
        var snapshot = calculator.CreateSnapshot(new StockPosition
        {
            Stock = new Stock { Name = "NVIDIA", Ticker = "NVDA", Currency = "USD" },
            Purchase = new Purchase { Shares = 5m, UnitPrice = 290m, ExchangeRate = 160m },
            Split = new StockSplit { SplitRatio = 10m },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = 50m,
                CurrentPrice = 196m,
                CurrentExchangeRate = 160m,
                AnnualDividendPerShare = 1m
            }
        });

        var story = generator.Generate(snapshot);

        Assert.Contains("株数変化", story);
        Assert.Contains("参考の株数変化倍率は10倍", story);
        Assert.Contains("実質取得単価は29.00 USD", story);
        Assert.Contains("取得額ベース利回り", story);
    }
}
