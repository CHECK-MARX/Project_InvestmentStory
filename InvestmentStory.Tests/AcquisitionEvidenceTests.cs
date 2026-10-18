using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

public sealed class AcquisitionEvidenceTests
{
    [Fact]
    public void ExactYenSettlementRequiresEvidence_AndOverridesReferenceEstimateWithoutErasingIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryCost_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new InvestmentStoryRepository(Path.Combine(directory, "portfolio.db"));
            repository.Initialize();
            var position = new StockPosition
            {
                Stock = new Stock { Name = "Example", Ticker = "EX", Currency = "USD", Broker = "SBI証券" },
                Purchase = new Purchase
                {
                    PurchaseDate = new DateTime(2026, 1, 1), Shares = 10m, UnitPrice = 10m,
                    ExchangeRate = 0m, ReferenceAcquisitionAmountJpy = 15000m,
                    ReferenceAcquisitionForeignAmount = 100m, ReferenceAcquisitionSource = "公表為替"
                },
                CurrentHolding = new CurrentHolding { CurrentShares = 10m, CurrentPrice = 11m,
                    CurrentExchangeRate = 150m }
            };
            repository.SavePosition(position);
            var editor = new StockEditorViewModel(p => repository.SavePosition(p), _ => { });
            editor.Load(Assert.Single(repository.GetPositions()));
            editor.ConfirmedAcquisitionAmountJpy = 15200m;
            editor.SaveCommand.Execute(null);
            Assert.Contains("根拠", editor.ErrorMessage);
            Assert.Equal(0m, Assert.Single(repository.GetPositions()).Purchase.AcquisitionAmountJpy);

            editor.AcquisitionEvidence = "取引報告書 2026/01/01";
            Assert.Contains("+200", editor.ReferenceDifference);
            editor.SaveCommand.Execute(null);
            var saved = Assert.Single(repository.GetPositions());
            Assert.Equal(15200m, saved.Purchase.AcquisitionAmountJpy);
            Assert.Equal("取引報告書 2026/01/01", saved.Purchase.AcquisitionAmountSource);
            Assert.NotEqual(DateTime.MinValue, saved.Purchase.AcquisitionAmountConfirmedAt);
            Assert.Equal(15000m, saved.Purchase.ReferenceAcquisitionAmountJpy);
            var snapshot = new InvestmentCalculator().CreateSnapshot(saved);
            Assert.Equal(15200m, snapshot.PurchaseTotalJpy);
            Assert.False(snapshot.IsReferencePurchaseCostJpy);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
