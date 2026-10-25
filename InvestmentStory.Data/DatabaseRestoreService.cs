using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InvestmentStory.Data;

public sealed record DatabaseBackupInfo(
    string FileName, DateTime CreatedAtLocal, long SizeBytes, string Status,
    long ImportLogCount, long LastImportLogId, DateTime? LastImportAtUtc, string AppVersion, string Reason)
{
    public string SizeDisplay => $"{SizeBytes / 1024.0 / 1024.0:N1} MB";
    public string CreatedAtDisplay => CreatedAtLocal.ToString("yyyy/MM/dd HH:mm:ss");
    public string LastImportDisplay => LastImportAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "記録なし";
    public DateTime? LastDrillAtUtc { get; init; }
    public string LastDrillDisplay => LastDrillAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "未実施";
    public string SchemaId { get; init; } = "不明";
    public string BackupKey { get; init; } = FileName;
    public string SourceKind { get; init; } = "取込前";
}

public sealed record DatabaseRestoreDrillResult(
    string FileName, DateTime CompletedAtUtc, long StockCount, long HoldingCount,
    long TradeCount, long DividendCount, decimal HoldingShares,
    decimal AcquisitionAmountJpy, decimal MarketValueJpy, decimal NetDividendJpy)
{
    public string Summary => $"復元演習に成功しました（{CompletedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}）。" +
        $"銘柄 {StockCount:N0}件、保有 {HoldingCount:N0}件、取引 {TradeCount:N0}件、" +
        $"配当 {DividendCount:N0}件。取得額 {AcquisitionAmountJpy:N0}円、" +
        $"評価額 {MarketValueJpy:N0}円、税引後配当 {NetDividendJpy:N0}円を照合しました。" +
        "現在のDBは変更していません。";
}

public sealed record DatabaseRestoreReceipt(
    DateTime CompletedAtUtc, string BackupFileName, string ArchiveFileName,
    string BackupSha256, string ArchiveSha256, string RestoredSha256,
    string BackupSchemaId, string ArchiveSchemaId,
    long StockCount, long HoldingCount, long TradeCount, long DividendCount,
    decimal AcquisitionAmountJpy, decimal MarketValueJpy, decimal NetDividendJpy);

/// <summary>Restores a verified, application-created SQLite snapshot on the next process start.</summary>
public sealed class DatabaseRestoreService
{
    private readonly string _databasePath;
    private readonly string _backupDirectory;
    private readonly string _pendingPath;
    private readonly string _archiveDirectory;

    public DatabaseRestoreService(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        _backupDirectory = _databasePath + ".pre_import_backups";
        _pendingPath = _databasePath + ".pending_restore";
        _archiveDirectory = _databasePath + ".restore_archives";
    }

    public IReadOnlyList<DatabaseBackupInfo> GetBackups()
    {
        var imported = Directory.Exists(_backupDirectory)
            ? Directory.GetFiles(_backupDirectory, "*.db") : Array.Empty<string>();
        var parent = Path.GetDirectoryName(_databasePath)!;
        var migrated = Directory.Exists(parent)
            ? Directory.GetFiles(parent, "*.db")
                .Where(path => IsMigrationBackupName(Path.GetFileName(path)))
            : Enumerable.Empty<string>();
        return imported.Concat(migrated)
            .Select(path => Inspect(path))
            .OrderByDescending(info => info.CreatedAtLocal)
            .ToArray();
    }

    public DatabaseBackupInfo Inspect(string path)
    {
        var backupKey = GetBackupKey(path);
        var fullPath = ResolveBackupPath(backupKey);
        var isMigration = backupKey.StartsWith("migration:", StringComparison.Ordinal);
        var file = new FileInfo(fullPath);
        try
        {
            var metadata = ReadMetadata(fullPath);
            if (!string.IsNullOrWhiteSpace(metadata?.Sha256))
            {
                using var backupFile = File.OpenRead(fullPath);
                var actualHash = Convert.ToHexString(SHA256.HashData(backupFile));
                if (!actualHash.Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("バックアップのハッシュが作成時と一致しません。");
            }
            using var connection = OpenReadOnly(fullPath);
            ValidateDatabase(connection);
            var schemaId = DatabaseSchemaIdentity.Compute(connection);
            if (!string.IsNullOrWhiteSpace(metadata?.SchemaId) &&
                !string.Equals(metadata.SchemaId, schemaId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("バックアップのDB形式IDが作成時と一致しません。");
            return new DatabaseBackupInfo(file.Name, metadata?.CreatedAtUtc.ToLocalTime() ?? file.CreationTime,
                file.Length, "正常",
                CountImportLogs(connection), GetLastImportLogId(connection), GetLastImportAt(connection),
                metadata?.AppVersion ?? "記録なし", metadata?.Reason ?? "CSV取込前（旧形式）")
            {
                LastDrillAtUtc = ReadValidDrillDate(fullPath, metadata?.Sha256),
                SchemaId = schemaId,
                BackupKey = backupKey,
                SourceKind = isMigration ? "移行前" : "取込前"
            };
        }
        catch (Exception ex) when (ex is SqliteException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new DatabaseBackupInfo(file.Name, file.CreationTime, file.Length,
                "復元不可: " + ex.Message, 0, 0, null, "不明", "不明")
            {
                BackupKey = backupKey,
                SourceKind = isMigration ? "移行前" : "取込前"
            };
        }
    }

    public DatabaseRestoreDrillResult RunRestoreDrill(string backupFileName)
    {
        var backupPath = ResolveBackupPath(backupFileName);
        var inspection = Inspect(backupPath);
        if (inspection.Status != "正常") throw new InvalidDataException(inspection.Status);
        var temporaryPath = Path.Combine(Path.GetTempPath(),
            $"InvestmentStory_restore_drill_{Guid.NewGuid():N}.db");
        try
        {
            using var source = OpenReadOnly(backupPath);
            ValidateDatabase(source);
            var expected = ReadDrillMetrics(source);
            CopySqlite(backupPath, temporaryPath);
            using var restored = OpenReadOnly(temporaryPath);
            ValidateDatabase(restored);
            var actual = ReadDrillMetrics(restored);
            if (actual != expected)
                throw new InvalidDataException("復元演習の主要件数・金額が元のバックアップと一致しません。");

            using var stream = File.OpenRead(backupPath);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var completed = DateTime.UtcNow;
            var result = new DatabaseRestoreDrillResult(inspection.FileName, completed,
                actual.StockCount, actual.HoldingCount, actual.TradeCount, actual.DividendCount,
                actual.HoldingShares, actual.AcquisitionAmountJpy, actual.MarketValueJpy,
                actual.NetDividendJpy);
            var recordPath = backupPath + ".drill.json";
            var pendingRecordPath = recordPath + ".tmp";
            try
            {
                File.WriteAllText(pendingRecordPath,
                    JsonSerializer.Serialize(new RestoreDrillMetadata(completed, hash, result)));
                File.Move(pendingRecordPath, recordPath, true);
            }
            finally { if (File.Exists(pendingRecordPath)) File.Delete(pendingRecordPath); }
            return result;
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = temporaryPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static DatabaseRestoreDrillResult ReadDrillMetrics(SqliteConnection connection)
    {
        static long Count(SqliteConnection db, string table)
        {
            using var command = db.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
            return (long)command.ExecuteScalar()!;
        }
        static decimal Sum(SqliteConnection db, string sql)
        {
            using var command = db.CreateCommand();
            command.CommandText = sql;
            return Convert.ToDecimal(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        var hasFunds = GetColumns(connection, "MutualFundHoldings").Count > 0 &&
            GetColumns(connection, "Stocks").Any(column => column.Name == "AssetType");
        var hasExchangeRate = GetColumns(connection, "CurrentHoldings")
            .Any(column => column.Name == "CurrentExchangeRate");
        var exchangeRate = hasExchangeRate ? "ch.CurrentExchangeRate" : "1";
        var acquisitionSql = hasFunds
            ? "SELECT COALESCE(SUM(CASE WHEN s.AssetType = 'MutualFund' " +
              "THEN COALESCE(mf.AcquisitionAmount, 0) ELSE COALESCE(p.AcquisitionAmountJpy, 0) END), 0) " +
              "FROM Stocks s LEFT JOIN Purchases p ON p.StockId = s.Id " +
              "LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id;"
            : "SELECT COALESCE(SUM(AcquisitionAmountJpy), 0) FROM Purchases;";
        var marketSql = hasFunds
            ? "SELECT COALESCE(SUM(CASE WHEN s.AssetType = 'MutualFund' " +
              $"THEN COALESCE(mf.MarketValue, 0) ELSE COALESCE(ch.CurrentShares * ch.CurrentPrice * {exchangeRate}, 0) END), 0) " +
              "FROM Stocks s LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id " +
              "LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id;"
            : $"SELECT COALESCE(SUM(CurrentShares * CurrentPrice * " +
              (hasExchangeRate ? "CurrentExchangeRate" : "1") + "), 0) FROM CurrentHoldings;";
        return new DatabaseRestoreDrillResult(string.Empty, default,
            Count(connection, "Stocks"), Count(connection, "CurrentHoldings"),
            GetColumns(connection, "BrokerTrades").Count == 0 ? 0 : Count(connection, "BrokerTrades"),
            Count(connection, "DividendPayments"),
            Sum(connection, "SELECT COALESCE(SUM(CurrentShares), 0) FROM CurrentHoldings;"),
            Sum(connection, acquisitionSql),
            Sum(connection, marketSql),
            Sum(connection, "SELECT COALESCE(SUM(NetAmountJpy), 0) FROM DividendPayments;"));
    }

    private static DateTime? ReadValidDrillDate(string backupPath, string? expectedHash)
    {
        var recordPath = backupPath + ".drill.json";
        if (!File.Exists(recordPath)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<RestoreDrillMetadata>(File.ReadAllText(recordPath));
            if (record is null) return null;
            var hash = expectedHash;
            if (string.IsNullOrWhiteSpace(hash))
            {
                using var stream = File.OpenRead(backupPath);
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }
            return hash.Equals(record.BackupSha256, StringComparison.OrdinalIgnoreCase)
                ? record.CompletedAtUtc : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed record RestoreDrillMetadata(DateTime CompletedAtUtc, string BackupSha256,
        DatabaseRestoreDrillResult Result);

    public string GetRestoreImpact(string backupFileName)
    {
        var backupPath = ResolveBackupPath(backupFileName);
        var selected = Inspect(backupPath);
        if (selected.Status != "正常") throw new InvalidDataException(selected.Status);
        using var current = OpenReadOnly(_databasePath);
        using var backup = OpenReadOnly(backupPath);
        ValidateDatabase(current);
        ValidateDatabase(backup);
        var currentCount = CountImportLogs(current);
        var importDifference = GetTableDifference(current, backup, "CsvImportLogs");
        var differences = RestoreDifferenceTables
            .Select(item => (item.Table, item.Label, Difference: GetTableDifference(current, backup, item.Table)))
            .ToArray();
        var differenceCounts = differences
            .Where(item => item.Difference.Count > 0)
            .Select(item => $"{item.Label} {item.Difference.Count}件")
            .ToArray();
        var differenceText = differenceCounts.Length == 0 ? "主要データの行差分はありません。" :
            "復元で現在の内容と入れ替わる行: " + string.Join("、", differenceCounts) + "。";
        var changedImports = importDifference.ChangedCurrentRows;
        var importFiles = SummarizeTargets(changedImports, row => Path.GetFileName(row.Get("FileName")));
        var importDates = SummarizeDates(changedImports, "ImportedAt");
        var importDetail = changedImports.Count == 0 ? "現在側で失う取込記録はありません。" :
            $"復元先にない、または内容が異なる取込記録: {changedImports.Count}件。" +
            $"対象CSV: {importFiles}。記録日時: {importDates}。";
        var stockLabels = ReadStockLabels(current);
        var positionDetails = differences
            .Where(item => item.Table is "Stocks" or "CurrentHoldings" or "Purchases")
            .Where(item => item.Difference.ChangedCurrentRows.Count > 0)
            .Select(item => $"{item.Label} {item.Difference.ChangedCurrentRows.Count}件" +
                $"（対象: {SummarizeTargets(item.Difference.ChangedCurrentRows, row => GetPositionTarget(row, stockLabels))}" +
                $" / 記録日時: {SummarizeDates(item.Difference.ChangedCurrentRows, "UpdatedAt")}）")
            .ToArray();
        var positionText = positionDetails.Length == 0 ? string.Empty :
            "銘柄・保有・買付の現在側の差分: " + string.Join("、", positionDetails) + "。";
        var planDetails = differences
            .Where(item => item.Table is "DividendPurchasePlans" or "DividendPurchasePlanItems")
            .Where(item => item.Difference.ChangedCurrentRows.Count > 0)
            .Select(item => $"{item.Label} {item.Difference.ChangedCurrentRows.Count}件" +
                $"（対象: {SummarizeTargets(item.Difference.ChangedCurrentRows, row => GetPlanTarget(row, current))}" +
                $" / 記録日時: {SummarizeDates(item.Difference.ChangedCurrentRows, "UpdatedAt")}）")
            .ToArray();
        var planText = planDetails.Length == 0 ? string.Empty :
            "保存計画の現在側の差分: " + string.Join("、", planDetails) + "。";
        return $"選択したバックアップ: {selected.CreatedAtDisplay} / {selected.SizeDisplay}。" +
               $"取込履歴は現在 {currentCount} 件、復元先 {selected.ImportLogCount} 件です。" +
               importDetail +
               differenceText +
               positionText + planText +
               "行差分には取込と手入力の両方が含まれ、出所を区別できません。変更日時を記録しない行の編集日時は特定できません。" +
               "現在のDBは復元直前に別途退避します。";
    }

    private static string SummarizeTargets(
        IReadOnlyList<RestoreRow> rows, Func<RestoreRow, string?> getTarget)
    {
        var examples = rows.Select(getTarget)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5).ToArray();
        if (examples.Length == 0) return "対象名の記録なし";
        return string.Join("、", examples) + (rows.Count > 5 ? $" ほか最大{rows.Count - 5}件" : string.Empty);
    }

    private static string SummarizeDates(IReadOnlyList<RestoreRow> rows, string column)
    {
        var dates = rows.Select(row => row.Get(column))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToLocalTime() : DateTime.MinValue)
            .Where(value => value > DateTime.MinValue).OrderBy(value => value).ToArray();
        if (dates.Length == 0) return "記録なし";
        return dates.Length == 1
            ? dates[0].ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)
            : $"{dates[0]:yyyy/MM/dd HH:mm}〜{dates[^1]:yyyy/MM/dd HH:mm}";
    }

    private static IReadOnlyDictionary<string, string> ReadStockLabels(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Ticker, Name FROM Stocks;";
        using var reader = command.ExecuteReader();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture);
            labels[id] = !reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1))
                ? reader.GetString(1) : !reader.IsDBNull(2) ? reader.GetString(2) : $"銘柄ID {id}";
        }
        return labels;
    }

    private static string GetPositionTarget(RestoreRow row, IReadOnlyDictionary<string, string> stockLabels)
    {
        var ticker = row.Get("Ticker");
        if (!string.IsNullOrWhiteSpace(ticker)) return ticker;
        var stockId = row.Get("StockId") ?? row.Get("Id");
        return stockId is not null && stockLabels.TryGetValue(stockId, out var label)
            ? label : stockId is null ? "対象不明" : $"銘柄ID {stockId}";
    }

    private static string GetPlanTarget(RestoreRow row, SqliteConnection current)
    {
        var name = row.Get("Name");
        if (!string.IsNullOrWhiteSpace(name)) return name;
        var planId = row.Get("PlanId");
        if (string.IsNullOrWhiteSpace(planId)) return "計画ID不明";
        using var command = current.CreateCommand();
        command.CommandText = "SELECT Name FROM DividendPurchasePlans WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", planId);
        return command.ExecuteScalar()?.ToString() ?? $"計画ID {planId}";
    }

    private static readonly (string Table, string Label)[] RestoreDifferenceTables =
    [
        ("Stocks", "銘柄"), ("CurrentHoldings", "保有残高"), ("Purchases", "買付"),
        ("BrokerTrades", "取引"), ("DividendPayments", "配当"),
        ("MutualFundHoldings", "投信残高"), ("StockSplits", "分割"),
        ("DividendPurchasePlans", "購入計画"), ("DividendPurchasePlanItems", "計画明細"),
        ("AppSettings", "設定"), ("IncomeGoals", "収入目標"),
        ("PortfolioSnapshots", "資産履歴"),
        ("CashOperations", "現金取引"), ("CashLegs", "現金明細"), ("CashReconciliations", "現金残高照合"),
        ("CashCsvSources", "現金CSV照合履歴"), ("PerformanceReports", "運用成績の保存記録"),
        ("AllocationTargetRevisions", "資産配分目標の改訂履歴"),
        ("ReminderStates", "通知の既読・延期状態"),
        ("ReminderPreferences", "通知方法の設定"),
        ("CsvReviewDispositions", "CSV要確認の解決・保留状態"),
        ("DividendReviewHistory", "配当照合の判断履歴"),
        ("NisaTransactions", "NISA取得・売却履歴"),
        ("NisaHistoryState", "NISA履歴確認状態")
    ];

    private sealed record RestoreRow(string Contents, string[] Columns, string?[] Values)
    {
        public string? Get(string column)
        {
            var index = Array.FindIndex(Columns, name => name.Equals(column, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? null : Values[index];
        }
    }

    private sealed record RestoreTableDifference(long Count, IReadOnlyList<RestoreRow> ChangedCurrentRows);

    private static RestoreTableDifference GetTableDifference(SqliteConnection current, SqliteConnection backup, string table)
    {
        var currentColumns = GetColumns(current, table);
        var backupColumns = GetColumns(backup, table);
        if (currentColumns.Count == 0 && backupColumns.Count == 0) return new(0, []);
        if (currentColumns.Count == 0) return new(CountRows(backup, table), []);
        if (backupColumns.Count == 0)
        {
            var columns = currentColumns.Select(column => column.Name).ToArray();
            var rows = ReadRows(current, table, columns, Array.Empty<string>());
            return new(rows.Count, rows.Values.ToArray());
        }

        var shared = currentColumns.Select(column => column.Name)
            .Where(name => backupColumns.Any(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (shared.Length == 0)
            return new(CountRows(current, table) + CountRows(backup, table), []);
        var keys = currentColumns.Where(column => column.PrimaryKeyOrder > 0)
            .OrderBy(column => column.PrimaryKeyOrder).Select(column => column.Name)
            .Where(name => shared.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
        var currentRows = ReadRows(current, table, shared, keys);
        var backupRows = ReadRows(backup, table, shared, keys);
        var changedCurrent = currentRows
            .Where(item => !backupRows.TryGetValue(item.Key, out var previous) ||
                item.Value.Contents != previous.Contents)
            .Select(item => item.Value).ToArray();
        var backupOnlyCount = backupRows.Keys.LongCount(key => !currentRows.ContainsKey(key));
        return new(changedCurrent.LongLength + backupOnlyCount, changedCurrent);
    }

    private static IReadOnlyList<(string Name, int PrimaryKeyOrder)> GetColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = command.ExecuteReader();
        var columns = new List<(string, int)>();
        while (reader.Read()) columns.Add((reader.GetString(1), reader.GetInt32(5)));
        return columns;
    }

    private static Dictionary<string, RestoreRow> ReadRows(
        SqliteConnection connection, string table, string[] columns, string[] keyColumns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(name => $"\"{name}\""))} FROM \"{table}\";";
        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, RestoreRow>(StringComparer.Ordinal);
        var keyIndexes = keyColumns.Select(name => Array.FindIndex(columns,
            column => column.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        while (reader.Read())
        {
            var values = Enumerable.Range(0, columns.Length)
                .Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index) switch
                {
                    byte[] bytes => Convert.ToBase64String(bytes),
                    var value => Convert.ToString(value, CultureInfo.InvariantCulture)
                }).ToArray();
            var contents = JsonSerializer.Serialize(values);
            var key = keyIndexes.Length == 0 ? contents :
                JsonSerializer.Serialize(keyIndexes.Select(index => values[index]).ToArray());
            // Tables without a declared primary key use the whole row as identity.
            rows[key] = new RestoreRow(contents, columns, values);
        }
        return rows;
    }

    private static long CountRows(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return (long)command.ExecuteScalar()!;
    }

    public void ScheduleRestore(string backupFileName)
    {
        if (Inspect(ResolveBackupPath(backupFileName)).Status != "正常")
            throw new InvalidDataException("破損または非対応のバックアップは復元できません。");
        Directory.CreateDirectory(Path.GetDirectoryName(_pendingPath)!);
        var temporary = _pendingPath + ".tmp";
        File.WriteAllText(temporary, backupFileName);
        File.Move(temporary, _pendingPath, true);
    }

    public string? ApplyPendingRestore()
    {
        if (!File.Exists(_pendingPath)) return null;
        var backupFileName = File.ReadAllText(_pendingPath).Trim();
        string backupPath;
        try { backupPath = ResolveBackupPath(backupFileName); }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
        {
            File.Delete(_pendingPath);
            throw;
        }
        var inspection = Inspect(backupPath);
        if (inspection.Status != "正常")
        {
            File.Delete(_pendingPath);
            throw new InvalidDataException("予約後にバックアップの検証結果が変わりました。復元を中止しました。" + inspection.Status);
        }
        if (!File.Exists(_databasePath)) throw new FileNotFoundException("復元前のDBがありません。", _databasePath);

        using var source = OpenReadOnly(backupPath);
        ValidateDatabase(source);
        var expectedMetrics = ReadDrillMetrics(source);
        var backupSchemaId = DatabaseSchemaIdentity.Compute(source);

        Directory.CreateDirectory(_archiveDirectory);
        var archivePath = Path.Combine(_archiveDirectory,
            $"{DateTime.Now:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.db");
        // Creating and checking the archive must succeed before changing the live database.
        CopySqlite(_databasePath, archivePath);
        var receiptPath = archivePath + ".restore.json";
        try
        {
            using var archived = OpenReadOnly(archivePath);
            ValidateDatabase(archived);
            var archiveSchemaId = DatabaseSchemaIdentity.Compute(archived);
            CopySqlite(backupPath, _databasePath);
            using var restored = OpenReadOnly(_databasePath);
            ValidateDatabase(restored);
            var actualMetrics = ReadDrillMetrics(restored);
            if (actualMetrics != expectedMetrics)
                throw new InvalidDataException("復元後の主要件数・金額がバックアップと一致しません。");
            var receipt = new DatabaseRestoreReceipt(DateTime.UtcNow, Path.GetFileName(backupPath),
                Path.GetFileName(archivePath), ComputeFileHash(backupPath),
                ComputeFileHash(archivePath), ComputeFileHash(_databasePath),
                backupSchemaId, archiveSchemaId,
                actualMetrics.StockCount, actualMetrics.HoldingCount,
                actualMetrics.TradeCount, actualMetrics.DividendCount,
                actualMetrics.AcquisitionAmountJpy, actualMetrics.MarketValueJpy,
                actualMetrics.NetDividendJpy);
            var temporaryReceipt = receiptPath + ".tmp";
            try
            {
                File.WriteAllText(temporaryReceipt, JsonSerializer.Serialize(receipt));
                File.Move(temporaryReceipt, receiptPath, true);
            }
            finally { if (File.Exists(temporaryReceipt)) File.Delete(temporaryReceipt); }
            File.Delete(_pendingPath);
            return archivePath;
        }
        catch
        {
            try { CopySqlite(archivePath, _databasePath); }
            finally
            {
                if (File.Exists(receiptPath)) File.Delete(receiptPath);
                if (File.Exists(_pendingPath)) File.Delete(_pendingPath);
            }
            throw;
        }
    }

    private string ResolveBackupPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("バックアップのファイル名が不正です。");
        var isMigration = fileName.StartsWith("migration:", StringComparison.Ordinal);
        var name = isMigration ? fileName["migration:".Length..] : fileName;
        if (string.IsNullOrWhiteSpace(name) ||
            name != Path.GetFileName(name) ||
            !name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
            isMigration && !IsMigrationBackupName(name))
            throw new InvalidDataException("バックアップのファイル名が不正です。");
        var directory = isMigration ? Path.GetDirectoryName(_databasePath)! : _backupDirectory;
        var path = Path.GetFullPath(Path.Combine(directory, name));
        if (!File.Exists(path)) throw new FileNotFoundException("バックアップが見つかりません。", path);
        return path;
    }

    private string GetBackupKey(string path)
    {
        if (!Path.IsPathRooted(path)) return path;
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (string.Equals(directory, _backupDirectory, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(fullPath);
        if (string.Equals(directory, Path.GetDirectoryName(_databasePath), StringComparison.OrdinalIgnoreCase) &&
            IsMigrationBackupName(Path.GetFileName(fullPath)))
            return "migration:" + Path.GetFileName(fullPath);
        throw new InvalidDataException("指定されたファイルはこのDBのバックアップではありません。");
    }

    private bool IsMigrationBackupName(string name)
    {
        var prefix = Path.GetFileNameWithoutExtension(_databasePath) + "_";
        var extension = Path.GetExtension(_databasePath);
        if (extension.Length == 0 || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false;
        var middle = name[prefix.Length..^extension.Length];
        return Regex.IsMatch(middle,
            @"^\d{8}_\d{6}(?:_\d{7})?(?:_[0-9a-f]{32})?_(?:before_[a-z0-9_]+|backup)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CopySqlite(string sourcePath, string destinationPath)
    {
        using var source = OpenReadOnly(sourcePath);
        ValidateDatabase(source);
        using var destination = new SqliteConnection(DatabaseInitializer.CreateConnectionString(destinationPath));
        destination.Open();
        source.BackupDatabase(destination);
        ValidateDatabase(destination);
    }

    private static void ValidateDatabase(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SQLite整合性検査に失敗しました。");
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        using var reader = schema.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) tables.Add(reader.GetString(0));
        if (!new[] { "Stocks", "Purchases", "CurrentHoldings", "DividendPayments", "CsvImportLogs" }
                .All(tables.Contains))
            throw new InvalidDataException("必要なテーブルがない古い形式のDBです。");
        var expected = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stocks"] = ["Id", "Ticker", "Broker", "AccountType", "CanonicalSecurityKey"],
            ["Purchases"] = ["StockId", "AcquisitionAmountJpy", "ReferenceAcquisitionAmountJpy"],
            ["CurrentHoldings"] = ["StockId", "CurrentShares", "CurrentPrice"],
            ["DividendPayments"] = ["StockId", "PaymentDate", "NetAmountJpy"],
            ["CsvImportLogs"] = ["BatchId", "FileHash", "ImportedAt", "Result"]
        };
        foreach (var (table, columns) in expected)
        {
            using var fields = connection.CreateCommand();
            fields.CommandText = $"PRAGMA table_info({table});";
            using var fieldReader = fields.ExecuteReader();
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (fieldReader.Read()) present.Add(fieldReader.GetString(1));
            if (!columns.All(present.Contains))
                throw new InvalidDataException($"{table} の形式が現行版と互換ではありません。");
        }
    }

    private static BackupMetadata? ReadMetadata(string backupPath)
    {
        var path = backupPath + ".json";
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(path))
            ?? throw new InvalidDataException("バックアップのメタデータが空です。"); }
        catch (JsonException ex) { throw new InvalidDataException("バックアップのメタデータを読めません。", ex); }
    }

    private sealed record BackupMetadata(DateTime CreatedAtUtc, string AppVersion, string Reason,
        string? Sha256 = null, string? SchemaId = null);

    private static long CountImportLogs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM CsvImportLogs;";
        return (long)command.ExecuteScalar()!;
    }

    private static long GetLastImportLogId(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id), 0) FROM CsvImportLogs;";
        return (long)command.ExecuteScalar()!;
    }

    private static DateTime? GetLastImportAt(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(ImportedAt) FROM CsvImportLogs;";
        var value = command.ExecuteScalar()?.ToString();
        return DateTime.TryParse(value, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : null;
    }
}
