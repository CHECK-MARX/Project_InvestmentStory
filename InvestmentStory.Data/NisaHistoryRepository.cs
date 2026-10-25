using System.Globalization;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class NisaHistoryRepository(string databasePath)
{
    public static void EnsureSchema(SqliteConnection connection, string databasePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('NisaTransactions','NisaHistoryState');";
        if (Convert.ToInt32(command.ExecuteScalar()) == 2) return;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Stocks';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS NisaTransactions (
                Id INTEGER PRIMARY KEY,
                EffectiveDate TEXT NOT NULL,
                Frame TEXT NOT NULL CHECK (Frame IN ('NisaGrowth','NisaAccumulation')),
                Kind TEXT NOT NULL CHECK (Kind IN ('Buy','Sell')),
                BookCostJpy TEXT NOT NULL,
                Source TEXT NOT NULL,
                Evidence TEXT NOT NULL,
                RuleVersion TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS NisaHistoryState (
                Id INTEGER PRIMARY KEY CHECK (Id=1),
                CompleteThroughDate TEXT NULL,
                ConfirmedAtUtc TEXT NULL,
                Evidence TEXT NOT NULL DEFAULT ''
            );
            INSERT OR IGNORE INTO NisaHistoryState (Id) VALUES (1);
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<NisaTransaction> GetTransactions()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,EffectiveDate,Frame,Kind,BookCostJpy,Source,Evidence,RuleVersion FROM NisaTransactions ORDER BY EffectiveDate,Id;";
        using var reader = command.ExecuteReader();
        var result = new List<NisaTransaction>();
        while (reader.Read())
            result.Add(new NisaTransaction(reader.GetInt64(0), DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                reader.GetString(2), reader.GetString(3), decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return result;
    }

    public NisaHistoryState GetHistoryState()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT CompleteThroughDate,ConfirmedAtUtc,Evidence FROM NisaHistoryState WHERE Id=1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new NisaHistoryState(null, null, string.Empty);
        return new NisaHistoryState(reader.IsDBNull(0) ? null : DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            reader.IsDBNull(1) ? null : DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind), reader.GetString(2));
    }

    public long Save(NisaTransaction item)
    {
        Validate(item);
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NisaTransactions
              (EffectiveDate,Frame,Kind,BookCostJpy,Source,Evidence,RuleVersion,CreatedAtUtc)
            VALUES ($date,$frame,$kind,$cost,$source,$evidence,$version,$created);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$date", item.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$frame", item.Frame);
        command.Parameters.AddWithValue("$kind", item.Kind);
        command.Parameters.AddWithValue("$cost", item.BookCostJpy.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$source", item.Source.Trim());
        command.Parameters.AddWithValue("$evidence", item.Evidence.Trim());
        command.Parameters.AddWithValue("$version", NisaCapacityService.RuleVersion);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        var id = Convert.ToInt64(command.ExecuteScalar());
        InvalidateHistory(connection, transaction);
        transaction.Commit();
        return id;
    }

    public void Delete(long id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM NisaTransactions WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        InvalidateHistory(connection, transaction);
        transaction.Commit();
    }

    public void ConfirmCompleteThrough(DateTime date, string evidence)
    {
        if (date.Year < 2024 || date.Date > DateTime.Today || string.IsNullOrWhiteSpace(evidence))
            throw new ArgumentException("確認日と根拠を入力してください。");
        using var connection = Open();
        new InvestmentStoryRepository(databasePath).CreatePreImportBackup(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE NisaHistoryState SET CompleteThroughDate=$date,ConfirmedAtUtc=$at,Evidence=$evidence WHERE Id=1;";
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$evidence", evidence.Trim());
        command.ExecuteNonQuery();
    }

    private static void InvalidateHistory(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE NisaHistoryState SET CompleteThroughDate=NULL,ConfirmedAtUtc=NULL,Evidence='' WHERE Id=1;";
        command.ExecuteNonQuery();
    }

    private static void Validate(NisaTransaction item)
    {
        if (item.EffectiveDate.Year < 2024 || item.EffectiveDate.Date > DateTime.Today ||
            item.Frame is not (AccountTypes.NisaGrowth or AccountTypes.NisaAccumulation) ||
            item.Kind is not (NisaTransactionKinds.Buy or NisaTransactionKinds.Sell) ||
            item.BookCostJpy <= 0m || string.IsNullOrWhiteSpace(item.Source) ||
            string.IsNullOrWhiteSpace(item.Evidence))
            throw new ArgumentException("新NISAの取引日、枠、売買区分、取得簿価、出所と根拠を確認してください。");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(databasePath));
        connection.Open(); return connection;
    }
}
