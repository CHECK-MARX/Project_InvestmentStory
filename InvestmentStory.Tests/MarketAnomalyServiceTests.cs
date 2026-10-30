using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class MarketAnomalyServiceTests
{
    [Fact]
    public void SplitQuantityAndOppositeProfitSignAreReviewCandidatesWithoutChangingTrades()
    {
        var split = new BrokerTrade
        {
            StockId = 7, TradeDate = new DateTime(2026, 4, 1),
            TradeType = "StockSplitCandidate", SignedQuantity = 100m,
            AfterTradeQuantity = 200m, SourceFile = "trade.csv", SourceRowNumber = 14
        };
        var splitCandidate = Assert.Single(new MarketAnomalyService().EvaluateTrades(7, [split]));
        Assert.Equal("数量・分割候補", splitCandidate.Category);
        Assert.Contains("trade.csv:14", splitCandidate.Evidence);
        Assert.Equal(200m, split.AfterTradeQuantity);

        var buy = new BrokerTrade
        {
            StockId = 7, TradeDate = new DateTime(2026, 5, 1), Broker = "SBI", AccountType = "Specific",
            Currency = "JPY", SignedQuantity = 100m, AfterTradeQuantity = 100m,
            AfterTradeAverageCost = 1000m
        };
        var sell = new BrokerTrade
        {
            StockId = 7, TradeDate = new DateTime(2026, 6, 1), Broker = "SBI", AccountType = "Specific",
            Currency = "JPY", SignedQuantity = -50m, UnitPrice = 1500m,
            RealizedGainLossJpy = -20000m, AfterTradeQuantity = 50m
        };
        var signCandidate = Assert.Single(new MarketAnomalyService().EvaluateTrades(7, [buy, sell]));
        Assert.Equal("損益符号不一致", signCandidate.Category);
        Assert.Equal("25000", signCandidate.PreviousValue);
        Assert.Equal("-20000", signCandidate.IncomingValue);
        Assert.Equal(-20000m, sell.RealizedGainLossJpy);
    }

    [Fact]
    public void UnverifiedBasisOrSmallFeeSensitiveProfitDoesNotTriggerSignCandidate()
    {
        var buy = new BrokerTrade
        {
            StockId = 7, TradeDate = new DateTime(2026, 5, 1), Broker = "SBI", Currency = "JPY",
            TradeType = "TransferIn", SignedQuantity = 100m, AfterTradeQuantity = 100m,
            AfterTradeAverageCost = 1000m
        };
        var sell = new BrokerTrade
        {
            StockId = 7, TradeDate = new DateTime(2026, 6, 1), Broker = "SBI", Currency = "JPY",
            SignedQuantity = -50m, UnitPrice = 1500m, RealizedGainLossJpy = -20000m,
            AfterTradeQuantity = 50m
        };
        Assert.Empty(new MarketAnomalyService().EvaluateTrades(7, [buy, sell]));
        buy.TradeType = "Buy";
        sell.UnitPrice = 1001m;
        sell.RealizedGainLossJpy = -100m;
        Assert.Empty(new MarketAnomalyService().EvaluateTrades(7, [buy, sell]));
    }

    [Fact]
    public void LargeMoveAndCurrencyMismatchCreateCandidatesWithoutChangingHolding()
    {
        var position = new StockPosition
        {
            Stock = new Stock { Id = 7, Ticker = "ABC", Currency = "USD" },
            CurrentHolding = new CurrentHolding
            {
                CurrentPrice = 100m,
                CurrentPriceAcquiredAt = new DateTime(2026, 10, 1),
                CurrentPriceSource = "前回API"
            }
        };
        var quote = new MarketDataQuote
        {
            Symbol = "ABC", Currency = "JPY", CurrentPrice = 200m,
            PriceAcquiredAt = new DateTime(2026, 10, 2), Source = "今回API"
        };

        var candidates = new MarketAnomalyService().EvaluateQuote(position, quote);

        Assert.Equal(new[] { "株価急変", "通貨不一致" }, candidates.Select(x => x.Category));
        Assert.All(candidates, x => Assert.Equal(7, x.StockId));
        Assert.Contains("前回API", candidates[0].Evidence);
        Assert.Contains("今回API", candidates[0].Evidence);
        Assert.Equal(100m, position.CurrentHolding.CurrentPrice);
        Assert.Equal("USD", position.Stock.Currency);
    }

    [Fact]
    public void OlderOrModeratePriceDoesNotProduceFalseAlarm()
    {
        var position = new StockPosition
        {
            Stock = new Stock { Id = 7, Ticker = "ABC", Currency = "USD" },
            CurrentHolding = new CurrentHolding
            {
                CurrentPrice = 100m,
                CurrentPriceAcquiredAt = new DateTime(2026, 10, 2)
            }
        };
        var service = new MarketAnomalyService();

        Assert.Empty(service.EvaluateQuote(position, new MarketDataQuote
        {
            Currency = "USD", CurrentPrice = 200m,
            PriceAcquiredAt = new DateTime(2026, 10, 1)
        }));
        Assert.Empty(service.EvaluateQuote(position, new MarketDataQuote
        {
            Currency = "USD", CurrentPrice = 120m,
            PriceAcquiredAt = new DateTime(2026, 10, 3)
        }));
    }
}
