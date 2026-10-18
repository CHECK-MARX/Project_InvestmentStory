using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class AcquisitionDateEvidenceTests
{
    [Fact]
    public void EarlierDocumentedBuyIsFlaggedForReviewWithoutChangingThePurchaseDate()
    {
        var mismatch = Position(1, "NVDA", new DateTime(2026, 7, 6));
        var aligned = Position(2, "CSCO", new DateTime(2021, 11, 18));
        var trades = new[]
        {
            Buy(1, new DateTime(2021, 11, 18)),
            Buy(2, new DateTime(2021, 11, 18))
        };

        var dateQuality = new DataQualityService().BuildForPosition(mismatch, DateTime.Today, trades)
            .Single(x => x.FieldName == "取得日");
        Assert.Equal(DataQualityStates.Conflict, dateQuality.ConfidenceLevel);
        Assert.True(dateQuality.HasConflict);
        Assert.Contains("2021/11/18", dateQuality.Memo);
        Assert.Equal(new DateTime(2026, 7, 6), mismatch.Purchase.PurchaseDate);

        var viewModel = new StockListViewModel(
            () => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, () => { });
        var calculator = new InvestmentCalculator();
        viewModel.Update(new[] { calculator.CreateSnapshot(mismatch), calculator.CreateSnapshot(aligned) },
            trades: trades);
        viewModel.SelectedQuality = "取得日要確認";
        Assert.Equal("NVDA", Assert.Single(viewModel.Rows).Ticker);
        viewModel.SelectedQuality = "すべて";
        Assert.Equal(2, viewModel.Rows.Count);
    }

    [Fact]
    public void TransfersAndSplitsDoNotInventAnEarlierDocumentedBuy()
    {
        var position = Position(1, "EX", new DateTime(2026, 7, 6));
        var trades = new[]
        {
            new BrokerTrade { StockId = 1, TradeDate = new DateTime(2021, 1, 1),
                TradeType = "TransferIn", SignedQuantity = 10m },
            new BrokerTrade { StockId = 1, TradeDate = new DateTime(2021, 2, 1),
                TradeType = "StockSplit", SignedQuantity = 10m }
        };

        Assert.False(PositionEvidenceService.AcquisitionDateNeedsReview(position, trades));
        position.Purchase.PurchaseDate = DateTime.MinValue;
        Assert.True(PositionEvidenceService.AcquisitionDateNeedsReview(position, trades));
    }

    [Fact]
    public void DateConflictRoundTripsThroughSavedDataQuality()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"date_review_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(databasePath);
            repository.Initialize();
            var position = Position(0, "EX", new DateTime(2026, 7, 6));
            repository.SavePosition(position);
            var saved = Assert.Single(repository.GetPositions());
            var items = new DataQualityService().BuildForPosition(saved, DateTime.Today,
                new[] { Buy(saved.Stock.Id, new DateTime(2021, 11, 18)) });

            repository.SaveDataQualityInfos(items);

            var result = repository.GetDataQualityInfos(saved.Stock.Id)
                .Single(x => x.FieldName == "取得日");
            Assert.Equal(DataQualityStates.Conflict, result.ConfidenceLevel);
            Assert.True(result.HasConflict);
            Assert.Contains("2021/11/18", result.Memo);
            Assert.Equal(new DateTime(2026, 7, 6), result.RetrievedAt.Date);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static StockPosition Position(int id, string ticker, DateTime purchaseDate) => new()
    {
        Stock = new Stock { Id = id, Ticker = ticker, Name = ticker, Currency = "JPY" },
        Purchase = new Purchase { PurchaseDate = purchaseDate, Shares = 10m, UnitPrice = 100m },
        CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 110m }
    };

    private static BrokerTrade Buy(int stockId, DateTime date) => new()
    {
        StockId = stockId,
        TradeDate = date,
        TradeType = "買付",
        SignedQuantity = 10m,
        Quantity = 10m
    };
}
