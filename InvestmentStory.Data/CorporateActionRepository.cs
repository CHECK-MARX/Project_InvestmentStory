using System.Globalization;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class CorporateActionRepository(string databasePath)
{
    public long Append(CorporateAction action)
    {
        CorporateActionLedgerService.Validate(action);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (action.SupersedesId.HasValue)
        {
            using var correctionCheck = connection.CreateCommand();
            correctionCheck.Transaction = transaction;
            correctionCheck.CommandText = """
                SELECT StockId FROM CorporateActions WHERE Id = $id
                AND NOT EXISTS (SELECT 1 FROM CorporateActions WHERE SupersedesId = $id);
                """;
            correctionCheck.Parameters.AddWithValue("$id", action.SupersedesId.Value);
            if (Convert.ToInt32(correctionCheck.ExecuteScalar() ?? 0) != action.StockId)
                throw new ArgumentException("訂正対象が存在しないか、既に訂正されています。");
        }
        using (var stockCheck = connection.CreateCommand())
        {
            stockCheck.Transaction = transaction;
            stockCheck.CommandText = "SELECT 1 FROM Stocks WHERE Id = $stockId;";
            stockCheck.Parameters.AddWithValue("$stockId", action.StockId);
            if (stockCheck.ExecuteScalar() is null)
                throw new ArgumentException("対象銘柄が見つかりません。");
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CorporateActions
                (StockId, EventDate, Kind, SourceBroker, SourceAccount,
                 DestinationBroker, DestinationAccount, Quantity, SplitRatio,
                 CarriedCostJpy, OldTicker, NewTicker, Evidence, SourceFile,
                 SourceRowNumber, Status, Reason, SupersedesId, RecordedAtUtc)
            VALUES ($stockId, $eventDate, $kind, $sourceBroker, $sourceAccount,
                    $destinationBroker, $destinationAccount, $quantity, $splitRatio,
                    $cost, $oldTicker, $newTicker, $evidence, $sourceFile,
                    $sourceRow, $status, $reason, $supersedesId, $recordedAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$stockId", action.StockId);
        command.Parameters.AddWithValue("$eventDate", action.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", action.Kind);
        command.Parameters.AddWithValue("$sourceBroker", action.SourceBroker);
        command.Parameters.AddWithValue("$sourceAccount", action.SourceAccount);
        command.Parameters.AddWithValue("$destinationBroker", action.DestinationBroker);
        command.Parameters.AddWithValue("$destinationAccount", action.DestinationAccount);
        command.Parameters.AddWithValue("$quantity", action.Quantity.ToString("G29", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$splitRatio", action.SplitRatio.ToString("G29", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cost", action.CarriedCostJpy.HasValue
            ? action.CarriedCostJpy.Value.ToString("G29", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$oldTicker", action.OldTicker);
        command.Parameters.AddWithValue("$newTicker", action.NewTicker);
        command.Parameters.AddWithValue("$evidence", action.Evidence);
        command.Parameters.AddWithValue("$sourceFile", action.SourceFile);
        command.Parameters.AddWithValue("$sourceRow", action.SourceRowNumber);
        command.Parameters.AddWithValue("$status", action.Status);
        command.Parameters.AddWithValue("$reason", action.Reason);
        command.Parameters.AddWithValue("$supersedesId", (object?)action.SupersedesId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recordedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var id = (long)(command.ExecuteScalar() ?? 0L);
        transaction.Commit();
        return id;
    }

    public IReadOnlyList<CorporateAction> GetForStock(int stockId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StockId, EventDate, Kind, SourceBroker, SourceAccount,
                   DestinationBroker, DestinationAccount, Quantity, SplitRatio,
                   CarriedCostJpy, OldTicker, NewTicker, Evidence, SourceFile,
                   SourceRowNumber, Status, Reason, RecordedAtUtc, SupersedesId
            FROM CorporateActions WHERE StockId = $stockId ORDER BY EventDate, Id;
            """;
        command.Parameters.AddWithValue("$stockId", stockId);
        using var reader = command.ExecuteReader();
        var result = new List<CorporateAction>();
        while (reader.Read())
            result.Add(new CorporateAction
            {
                Id = reader.GetInt64(0), StockId = reader.GetInt32(1),
                EventDate = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                Kind = reader.GetString(3), SourceBroker = reader.GetString(4),
                SourceAccount = reader.GetString(5), DestinationBroker = reader.GetString(6),
                DestinationAccount = reader.GetString(7),
                Quantity = Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
                SplitRatio = Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
                CarriedCostJpy = reader.IsDBNull(10) ? null :
                    Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
                OldTicker = reader.GetString(11), NewTicker = reader.GetString(12),
                Evidence = reader.GetString(13), SourceFile = reader.GetString(14),
                SourceRowNumber = reader.GetInt32(15), Status = reader.GetString(16),
                Reason = reader.GetString(17),
                RecordedAtUtc = DateTime.Parse(reader.GetString(18), CultureInfo.InvariantCulture),
                SupersedesId = reader.IsDBNull(19) ? null : reader.GetInt64(19)
            });
        return result;
    }

    public CorporateActionBalance GetBalance(int stockId, DateTime asOf)
    {
        using var connection = Open();
        var ids = ResolveLinkedStockIds(connection, stockId);
        var events = ids.SelectMany(GetForStock).ToList();
        var trades = new InvestmentStoryRepository(databasePath).GetBrokerTrades(ids);
        return CorporateActionLedgerService.Reconstruct(events, trades, asOf);
    }

    private static IReadOnlyList<int> ResolveLinkedStockIds(SqliteConnection connection, int stockId)
    {
        var stocks = new List<(int Id, string Key, string Ticker)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, CanonicalSecurityKey, Ticker FROM Stocks;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) stocks.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        var target = stocks.FirstOrDefault(x => x.Id == stockId);
        if (target.Id == 0) return [stockId];
        var ids = stocks.Where(x => target.Key.Length > 0 && x.Key == target.Key)
            .Select(x => x.Id).ToHashSet();
        ids.Add(stockId);
        var prefixEnd = target.Key.LastIndexOf(':');
        if (prefixEnd < 0) return ids.ToList();
        var keyPrefix = target.Key[..prefixEnd];
        var tickerChanges = new List<(int StockId, string OldTicker, string NewTicker)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ca.StockId, ca.OldTicker, ca.NewTicker
                FROM CorporateActions ca
                WHERE ca.Kind = 'TickerChange' AND ca.Status = 'Confirmed'
                  AND NOT EXISTS (SELECT 1 FROM CorporateActions newer WHERE newer.SupersedesId = ca.Id);
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) tickerChanges.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        var expanded = true;
        while (expanded)
        {
            expanded = false;
            foreach (var change in tickerChanges)
            {
                var owner = stocks.FirstOrDefault(x => x.Id == change.StockId);
                if (string.IsNullOrEmpty(owner.Key) ||
                    !owner.Key.StartsWith(keyPrefix + ":", StringComparison.OrdinalIgnoreCase)) continue;
                var connected = ids.Contains(change.StockId) || stocks.Any(x => ids.Contains(x.Id) &&
                    (x.Ticker.Equals(change.OldTicker, StringComparison.OrdinalIgnoreCase) ||
                     x.Ticker.Equals(change.NewTicker, StringComparison.OrdinalIgnoreCase)));
                if (!connected) continue;
                foreach (var peer in stocks.Where(x =>
                             x.Key.StartsWith(keyPrefix + ":", StringComparison.OrdinalIgnoreCase) &&
                             (x.Ticker.Equals(change.OldTicker, StringComparison.OrdinalIgnoreCase) ||
                              x.Ticker.Equals(change.NewTicker, StringComparison.OrdinalIgnoreCase))))
                    expanded |= ids.Add(peer.Id);
            }
        }
        return ids.ToList();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open();
        return connection;
    }
}
