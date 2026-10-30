using System.Text.Json;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Data;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class PositionChangeAuditTests
{
    [Fact]
    public void ManualEdit_SavesBeforeAfterReasonAndValueImpactInSameTransaction()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_manual_audit_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var position = Position();
            position.ManualChangeReason = "取引報告書による登録";
            var stockId = repository.SavePosition(position);
            position = repository.GetPosition(stockId)!;
            position.CurrentHolding.CurrentShares = 12m;
            position.Purchase.AcquisitionAmountJpy = 12_000m;
            position.ManualChangeReason = "残高証明書に合わせて数量と原価を修正";
            repository.SavePosition(position);

            var edits = repository.GetPositionChangeAudits(stockId);
            Assert.Equal(2, edits.Count);
            var edit = edits[0];
            Assert.Equal("残高証明書に合わせて数量と原価を修正", edit.Reason);
            Assert.Equal(4_000m, edit.MarketValueChangeJpy);
            using var before = JsonDocument.Parse(edit.BeforeJson!);
            using var after = JsonDocument.Parse(edit.AfterJson);
            Assert.Equal(10m, before.RootElement.GetProperty("CurrentShares").GetDecimal());
            Assert.Equal(12m, after.RootElement.GetProperty("CurrentShares").GetDecimal());
            Assert.Equal(12_000m, after.RootElement.GetProperty("ConfirmedCostJpy").GetDecimal());

            position = repository.GetPosition(stockId)!;
            position.CurrentHolding.CurrentShares = 15m;
            position.ManualChangeReason = "失敗する修正";
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DROP TABLE PositionChangeAudits;";
                command.ExecuteNonQuery();
            }
            Assert.Throws<SqliteException>(() => repository.SavePosition(position));
            Assert.Equal(12m, repository.GetPosition(stockId)!.CurrentHolding.CurrentShares);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Editor_RequiresReasonAndConfirmationBeforeExistingPositionSave()
    {
        var saved = 0;
        var prompt = string.Empty;
        var editor = new StockEditorViewModel(_ => saved++, _ => { },
            text => { prompt = text; return false; });
        var position = Position();
        position.Stock.Id = 1;
        editor.Load(position);
        editor.CurrentShares = 12m;
        editor.SaveCommand.Execute(null);
        Assert.Contains("理由", editor.ErrorMessage);
        Assert.Equal(0, saved);

        editor.ChangeReason = "残高証明書";
        editor.SaveCommand.Execute(null);
        Assert.Equal(0, saved);
        Assert.Contains("10.0000 → 12.0000", prompt);
        Assert.Contains("残高証明書", prompt);
    }

    [Fact]
    public void RevertChange_RestoresOnlyTouchedFieldsAndKeepsNewerMarketPrice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_revert_audit_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new InvestmentStoryRepository(path);
            repository.Initialize();
            var position = Position();
            var stockId = repository.SavePosition(position);
            position = repository.GetPosition(stockId)!;
            position.CurrentHolding.CurrentShares = 12m;
            position.Purchase.AcquisitionAmountJpy = 12_000m;
            position.ManualChangeReason = "数量と原価を更新";
            repository.SavePosition(position);
            var auditId = Assert.Single(repository.GetPositionChangeAudits(stockId)).Id;

            position = repository.GetPosition(stockId)!;
            position.CurrentHolding.CurrentPrice = 2_100m;
            repository.SavePosition(position); // API update, unrelated to the manual correction.
            repository.RevertPositionChange(auditId, "元資料を再確認");

            position = repository.GetPosition(stockId)!;
            Assert.Equal(10m, position.CurrentHolding.CurrentShares);
            Assert.Equal(10_000m, position.Purchase.AcquisitionAmountJpy);
            Assert.Equal(2_100m, position.CurrentHolding.CurrentPrice);
            var changes = repository.GetPositionChangeAudits(stockId);
            Assert.Equal(2, changes.Count);
            Assert.Contains($"履歴ID {auditId} の再調整", changes[0].Reason);

            position.CurrentHolding.CurrentShares = 11m;
            repository.SavePosition(position);
            Assert.Throws<InvalidOperationException>(() =>
                repository.RevertPositionChange(auditId, "古い変更を再適用"));
            Assert.Equal(11m, repository.GetPosition(stockId)!.CurrentHolding.CurrentShares);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static StockPosition Position() => new()
    {
        Stock = new Stock
        {
            Ticker = "EX", Name = "Example", Broker = "SBI",
            AccountType = AccountTypes.Specific, Currency = "JPY"
        },
        Purchase = new Purchase
        {
            PurchaseDate = new DateTime(2026, 1, 1), Shares = 10m,
            UnitPrice = 1_000m, AcquisitionAmountJpy = 10_000m,
            AcquisitionAmountSource = "取引報告書"
        },
        CurrentHolding = new CurrentHolding
        { CurrentShares = 10m, CurrentPrice = 2_000m, CurrentExchangeRate = 1m }
    };

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
