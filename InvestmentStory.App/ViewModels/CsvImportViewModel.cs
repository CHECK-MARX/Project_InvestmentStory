using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Win32;

namespace InvestmentStory.App.ViewModels;

public sealed class CsvImportViewModel : ObservableObject
{
    private readonly BrokerDataProviderFactory _factory = new();
    private readonly SbiDistributionCsvParser _sbiDistributionParser = new();
    private readonly SbiStatementCsvParser _sbiStatementParser = new();
    private readonly NomuraHoldingsCsvParser _nomuraHoldingsParser = new();
    private readonly NomuraTransactionCsvParser _nomuraTransactionParser = new();
    private readonly BrokerHoldingMergeService _holdingMergeService = new();
    private readonly Func<IReadOnlyList<StockPosition>>? _getPositions;
    private readonly Func<StockPosition, int>? _savePosition;
    private readonly Func<DividendPayment, int>? _saveDividend;
    private readonly Func<IReadOnlyList<DividendPayment>>? _getDividendPayments;
    private readonly Action<IReadOnlyList<BrokerTradeRecord>>? _saveBrokerTrades;
    private readonly Action? _afterImport;
    private readonly List<string> _selectedFilePaths = new();
    private string _message = string.Empty;
    private string _filePath = string.Empty;
    private bool _updatingFilePathFromDialog;

    public CsvImportViewModel(
        Func<IReadOnlyList<StockPosition>>? getPositions = null,
        Func<StockPosition, int>? savePosition = null,
        Func<DividendPayment, int>? saveDividend = null,
        Func<IReadOnlyList<DividendPayment>>? getDividendPayments = null,
        Action<IReadOnlyList<BrokerTradeRecord>>? saveBrokerTrades = null,
        Action? afterImport = null)
    {
        _getPositions = getPositions;
        _savePosition = savePosition;
        _saveDividend = saveDividend;
        _getDividendPayments = getDividendPayments;
        _saveBrokerTrades = saveBrokerTrades;
        _afterImport = afterImport;
        BrowseCommand = new RelayCommand(Browse);
        PreviewCommand = new RelayCommand(Preview);
        ImportCommand = new RelayCommand(Import);
    }

    public ICommand BrowseCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ImportCommand { get; }
    public ObservableCollection<CsvPreviewRowViewModel> PreviewRows { get; } = new();
    public ObservableCollection<string> ImportLogs { get; } = new();

    public string[] Brokers { get; } = { "SBI証券", "野村証券", "その他" };
    public string[] CsvTypes { get; } = { "保有銘柄", "取引履歴", "配当履歴" };

    public string Broker { get; set; } = "SBI証券";
    public string CsvType { get; set; } = "保有銘柄";
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value) && !_updatingFilePathFromDialog)
            {
                _selectedFilePaths.Clear();
            }
        }
    }
    public string TickerColumn { get; set; } = "銘柄コード";
    public string DateColumn { get; set; } = "取引日";
    public string QuantityColumn { get; set; } = "数量";
    public string AmountColumn { get; set; } = "金額";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedFilePaths.Clear();
            _selectedFilePaths.AddRange(dialog.FileNames);
            _updatingFilePathFromDialog = true;
            FilePath = FormatSelectedFilePath(_selectedFilePaths);
            _updatingFilePathFromDialog = false;
            Preview();
        }
    }

    private void Preview()
    {
        PreviewRows.Clear();
        ImportLogs.Clear();
        var filePaths = GetTargetFilePaths();
        if (filePaths.Count == 0)
        {
            Message = "CSVファイルを選択してください。";
            return;
        }

        if (filePaths.Count > 1)
        {
            PreviewMultipleFiles(filePaths);
            return;
        }

        var filePath = filePaths[0];
        if (NomuraHoldingsCsvParser.LooksLikeNomuraHoldingsCsv(filePath))
        {
            PreviewBrokerStatement(() => _nomuraHoldingsParser.Parse(filePath), "野村保有残高CSV");
            return;
        }

        if (NomuraTransactionCsvParser.LooksLikeNomuraTransactionCsv(filePath))
        {
            PreviewBrokerStatement(() => _nomuraTransactionParser.Parse(filePath), "野村取引履歴CSV");
            return;
        }

        if (SbiStatementCsvParser.LooksLikeSbiStatementCsv(filePath))
        {
            PreviewBrokerStatement(() => _sbiStatementParser.ParseFromSeedFile(filePath), "SBI一括CSV");
            return;
        }

        if (SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(filePath))
        {
            PreviewDividendRecords(() => _sbiDistributionParser.Parse(filePath), "SBI配当CSV");
            return;
        }

        var service = _factory.CreateCsvImportService(Broker);
        var preview = service.Preview(filePath, CsvType);
        var index = 1;
        foreach (var row in preview.Rows)
        {
            PreviewRows.Add(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = string.Join(" / ", row.Select(x => $"{x.Key}: {x.Value}"))
            });
        }

        foreach (var log in preview.Logs)
        {
            ImportLogs.Add(log);
        }

        Message = preview.Rows.Count == 0 ? "プレビューできる行がありません。" : "CSVプレビューを更新しました。";
    }

    private void PreviewDividendRecords(Func<IReadOnlyList<BrokerDividendRecord>> parseRecords, string sourceLabel)
    {
        try
        {
            var records = parseRecords();
            AddDividendPreviewRows(records, 1);

            foreach (var byTicker in records.GroupBy(x => x.Ticker).OrderBy(x => x.Key))
            {
                ImportLogs.Add($"{byTicker.Key}: {byTicker.Count()}件 / {byTicker.Sum(x => x.NetAmountJpy):N2}円");
            }

            Message = $"{sourceLabel} を読み込みました。配当明細: {records.Count}件。";
        }
        catch (Exception ex)
        {
            Message = $"{sourceLabel} のプレビューに失敗しました: {ex.Message}";
        }
    }

    private void PreviewBrokerStatement(Func<BrokerStatementImport> parseStatement, string sourceLabel)
    {
        try
        {
            var statement = parseStatement();
            AddStatementPreviewRows(statement, 1);

            AddStatementSummaryLogs(statement);
            Message = statement.CanUpdateHoldings
                ? $"{sourceLabel} を読み込みました。保有: {statement.Holdings.Count}件、配当: {statement.Dividends.Count}件、保有反映対象取引: {statement.Trades.Count}件。取込実行でまとめて反映します。"
                : $"{sourceLabel} を読み込みました。配当: {statement.Dividends.Count}件、売買/入庫履歴: {statement.Trades.Count}件。現在保有は更新せず、配当だけ登録します。";
        }
        catch (Exception ex)
        {
            Message = $"{sourceLabel} のプレビューに失敗しました: {ex.Message}";
        }
    }

    private void PreviewMultipleFiles(IReadOnlyList<string> filePaths)
    {
        try
        {
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 1;
            var statementCount = 0;
            var dividendOnlyCount = 0;

            foreach (var filePath in filePaths.Where(NomuraHoldingsCsvParser.LooksLikeNomuraHoldingsCsv))
            {
                var statement = _nomuraHoldingsParser.Parse(filePath);
                ImportLogs.Add($"[{Path.GetFileName(filePath)}] 野村保有残高CSV");
                AddStatementSummaryLogs(statement);
                index = AddStatementPreviewRows(statement, index);
                handled.Add(filePath);
                statementCount++;
            }

            foreach (var filePath in filePaths.Where(NomuraTransactionCsvParser.LooksLikeNomuraTransactionCsv))
            {
                var statement = _nomuraTransactionParser.Parse(filePath);
                ImportLogs.Add($"[{Path.GetFileName(filePath)}] 野村取引履歴CSV");
                AddStatementSummaryLogs(statement);
                index = AddStatementPreviewRows(statement, index);
                handled.Add(filePath);
                statementCount++;
            }

            var sbiFiles = filePaths
                .Where(filePath => !handled.Contains(filePath) && SbiStatementCsvParser.LooksLikeSbiStatementCsv(filePath))
                .ToList();
            if (sbiFiles.Count > 0)
            {
                var statement = _sbiStatementParser.ParseFiles(sbiFiles);
                ImportLogs.Add($"[SBI選択CSV {sbiFiles.Count}件] SBI一括CSV");
                AddStatementSummaryLogs(statement);
                index = AddStatementPreviewRows(statement, index);
                foreach (var filePath in sbiFiles)
                {
                    handled.Add(filePath);
                }

                statementCount++;
            }

            foreach (var filePath in filePaths.Where(filePath => !handled.Contains(filePath)))
            {
                if (SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(filePath))
                {
                    var records = _sbiDistributionParser.Parse(filePath);
                    ImportLogs.Add($"[{Path.GetFileName(filePath)}] SBI配当CSV: {records.Count}件 / {records.Sum(x => x.NetAmountJpy):N2}円");
                    index = AddDividendPreviewRows(records, index);
                    handled.Add(filePath);
                    dividendOnlyCount++;
                    continue;
                }

                ImportLogs.Add($"[{Path.GetFileName(filePath)}] 未対応CSVのため汎用プレビュー対象です。");
            }

            Message = $"{filePaths.Count}件のCSVを読み込みました。取引/配当CSV: {statementCount}件、配当CSV: {dividendOnlyCount}件。未対応CSVはログを確認してください。";
        }
        catch (Exception ex)
        {
            Message = $"複数CSVのプレビューに失敗しました: {ex.Message}";
        }
    }

    private int AddStatementPreviewRows(BrokerStatementImport statement, int startIndex)
    {
        var index = startIndex;
        foreach (var holding in statement.Holdings.Take(20))
        {
            PreviewRows.Add(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = $"保有 / {holding.Ticker} / {holding.Name} / {holding.Shares:N2}株 / 取得 {holding.AverageAcquisitionPrice:N4} {holding.Currency} / 現在 {holding.CurrentPrice:N4} {holding.Currency}"
            });
        }

        foreach (var trade in statement.Trades.OrderByDescending(x => x.SettlementDate).Take(10))
        {
            PreviewRows.Add(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = $"取引 / {trade.SettlementDate:yyyy/MM/dd} / {trade.TradeType} / {trade.Ticker} / {trade.Name} / {trade.Quantity:N2}株 / {trade.UnitPrice:N4} {trade.Currency}"
            });
        }

        foreach (var dividend in statement.Dividends.Take(10))
        {
            PreviewRows.Add(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = $"配当 / {dividend.PaymentDate:yyyy/MM/dd} / {dividend.Ticker} / {dividend.Name} / {dividend.Currency} / {dividend.NetAmount:N6} / {dividend.NetAmountJpy:N2}円"
            });
        }

        return index;
    }

    private int AddDividendPreviewRows(IReadOnlyList<BrokerDividendRecord> records, int startIndex)
    {
        var index = startIndex;
        foreach (var record in records.Take(20))
        {
            PreviewRows.Add(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = $"{record.PaymentDate:yyyy/MM/dd} / {record.Ticker} / {record.Name} / {record.Currency} / {record.NetAmount:N6} / {record.NetAmountJpy:N2}円"
            });
        }

        return index;
    }

    private void Import()
    {
        ImportLogs.Clear();
        var filePaths = GetTargetFilePaths();
        if (filePaths.Count == 0)
        {
            Message = "CSVファイルを選択してください。";
            return;
        }

        if (filePaths.Count > 1)
        {
            ImportMultipleFiles(filePaths);
            return;
        }

        var filePath = filePaths[0];
        if (NomuraHoldingsCsvParser.LooksLikeNomuraHoldingsCsv(filePath))
        {
            ImportBrokerStatement(() => _nomuraHoldingsParser.Parse(filePath), "野村保有残高CSV");
            return;
        }

        if (NomuraTransactionCsvParser.LooksLikeNomuraTransactionCsv(filePath))
        {
            ImportBrokerStatement(() => _nomuraTransactionParser.Parse(filePath), "野村取引履歴CSV");
            return;
        }

        if (SbiStatementCsvParser.LooksLikeSbiStatementCsv(filePath))
        {
            ImportBrokerStatement(() => _sbiStatementParser.ParseFromSeedFile(filePath), "SBI一括CSV");
            return;
        }

        if (SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(filePath))
        {
            ImportDividendRecords(() => _sbiDistributionParser.Parse(filePath), "SBI配当CSV");
            return;
        }

        var mappings = new Dictionary<string, string>
        {
            ["銘柄コード"] = TickerColumn,
            [CsvType == "配当履歴" ? "入金日" : "取引日"] = DateColumn,
            ["数量"] = QuantityColumn,
            ["金額"] = AmountColumn
        };
        var service = _factory.CreateCsvImportService(Broker);
        var result = service.Import(filePath, CsvType, mappings);
        foreach (var log in result.Logs)
        {
            ImportLogs.Add(log);
        }

        Message = result.IsSuccess
            ? $"取込検証が完了しました。検証件数: {result.ImportedCount}、重複候補: {result.DuplicateCount}"
            : "取込に失敗しました。ログを確認してください。";
    }

    private void ImportMultipleFiles(IReadOnlyList<string> filePaths)
    {
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;

        foreach (var filePath in filePaths.Where(NomuraHoldingsCsvParser.LooksLikeNomuraHoldingsCsv))
        {
            ImportLogs.Add($"[{Path.GetFileName(filePath)}] 野村保有残高CSV");
            ImportBrokerStatement(() => _nomuraHoldingsParser.Parse(filePath), "野村保有残高CSV");
            handled.Add(filePath);
            processed++;
        }

        foreach (var filePath in filePaths.Where(NomuraTransactionCsvParser.LooksLikeNomuraTransactionCsv))
        {
            ImportLogs.Add($"[{Path.GetFileName(filePath)}] 野村取引履歴CSV");
            ImportBrokerStatement(() => _nomuraTransactionParser.Parse(filePath), "野村取引履歴CSV");
            handled.Add(filePath);
            processed++;
        }

        var sbiFiles = filePaths
            .Where(filePath => !handled.Contains(filePath) && SbiStatementCsvParser.LooksLikeSbiStatementCsv(filePath))
            .ToList();
        if (sbiFiles.Count > 0)
        {
            ImportLogs.Add($"[SBI選択CSV {sbiFiles.Count}件] SBI一括CSV");
            ImportBrokerStatement(() => _sbiStatementParser.ParseFiles(sbiFiles), "SBI一括CSV");
            foreach (var filePath in sbiFiles)
            {
                handled.Add(filePath);
            }

            processed++;
        }

        foreach (var filePath in filePaths.Where(filePath => !handled.Contains(filePath)))
        {
            if (SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(filePath))
            {
                ImportLogs.Add($"[{Path.GetFileName(filePath)}] SBI配当CSV");
                ImportDividendRecords(() => _sbiDistributionParser.Parse(filePath), "SBI配当CSV");
                handled.Add(filePath);
                processed++;
                continue;
            }

            ImportLogs.Add($"[{Path.GetFileName(filePath)}] 未対応CSVのためDB反映しませんでした。");
        }

        Message = $"{filePaths.Count}件のCSVを処理しました。DB反映したグループ: {processed}件。詳細は取込ログを確認してください。";
    }

    private void ImportDividendRecords(Func<IReadOnlyList<BrokerDividendRecord>> parseRecords, string sourceLabel)
    {
        if (_getPositions is null || _savePosition is null || _saveDividend is null || _getDividendPayments is null)
        {
            Message = "この画面はDB取込用に初期化されていません。";
            return;
        }

        try
        {
            var records = parseRecords();
            if (records.Count == 0)
            {
                Message = $"{sourceLabel} に取込可能な配当明細がありません。";
                return;
            }

            var positions = _getPositions().ToList();
            var positionByKey = positions
                .GroupBy(x => BuildPositionKey(x.Stock.Broker, x.Stock.Ticker, x.Stock.AccountType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var existingDividends = _getDividendPayments().ToList();
            var reconciliationService = new DividendReconciliationService();

            var imported = 0;
            var updated = 0;
            var replaced = 0;
            var skipped = 0;
            var createdReferenceStocks = 0;

            foreach (var record in records)
            {
                var positionKey = BuildPositionKey(record.Broker, record.Ticker, record.Account);
                if (!positionByKey.TryGetValue(positionKey, out var position))
                {
                    var relatedRecords = records
                        .Where(x => string.Equals(BuildPositionKey(x.Broker, x.Ticker, x.Account), positionKey, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    position = CreateDividendOnlyPosition(record, relatedRecords);
                    position.Stock.Id = _savePosition(position);
                    positionByKey[positionKey] = position;
                    createdReferenceStocks++;
                }

                var payment = CreateDividendPayment(position.Stock.Id, record);
                switch (SaveBrokerDividend(existingDividends, payment, reconciliationService))
                {
                    case DividendImportAction.Created:
                        imported++;
                        break;
                    case DividendImportAction.UpdatedExistingActual:
                        updated++;
                        break;
                    case DividendImportAction.ReplacedSchedule:
                        imported++;
                        replaced++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }

            _afterImport?.Invoke();
            foreach (var byTicker in records.GroupBy(x => x.Ticker).OrderBy(x => x.Key))
            {
                ImportLogs.Add($"{byTicker.Key}: {byTicker.Count()}件 / {byTicker.Sum(x => x.NetAmountJpy):N2}円");
            }

            ImportLogs.Add($"配当専用仮銘柄: {createdReferenceStocks}件（銘柄一覧の「現在保有のみ」には表示しません）");
            Message = $"{sourceLabel} を取り込みました。配当登録: {imported}件、既存実績更新: {updated}件、予定置換: {replaced}件、スキップ: {skipped}件、配当専用仮銘柄: {createdReferenceStocks}件。";
        }
        catch (Exception ex)
        {
            Message = $"{sourceLabel} の取込に失敗しました: {ex.Message}";
        }
    }

    private void ImportBrokerStatement(Func<BrokerStatementImport> parseStatement, string sourceLabel)
    {
        if (_getPositions is null || _savePosition is null || _saveDividend is null || _getDividendPayments is null)
        {
            Message = "この画面はDB取込用に初期化されていません。";
            return;
        }

        try
        {
            var statement = parseStatement();
            if (statement.Holdings.Count == 0 && statement.Trades.Count == 0 && statement.Dividends.Count == 0)
            {
                Message = $"{sourceLabel} に取込可能な保有・配当データがありません。";
                return;
            }

            var positions = _getPositions().ToList();
            var positionByKey = positions
                .GroupBy(x => BuildPositionKey(x.Stock.Broker, x.Stock.Ticker, x.Stock.AccountType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var dividendsByKey = statement.Dividends
                .GroupBy(x => BuildPositionKey(x.Broker, x.Ticker, x.Account), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<BrokerDividendRecord>)x.ToList(), StringComparer.OrdinalIgnoreCase);

            var createdStocks = 0;
            var updatedStocks = 0;
            var reviewHoldings = 0;
            var skippedTradeGroups = 0;
            var createdReferenceStocks = 0;
            var holdingKeys = statement.Holdings
                .Select(x => BuildPositionKey(x.Broker, x.Ticker, x.Account))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (statement.Holdings.Count > 0)
            {
                var mergeResult = _holdingMergeService.MergeHoldings(positions, statement.Holdings, DateTime.Now);
                foreach (var decision in mergeResult.Decisions)
                {
                    var positionKey = BuildPositionKey(decision.Source.Broker, decision.Source.Ticker, decision.Source.Account);
                    if (decision.Merged is null)
                    {
                        reviewHoldings++;
                        ImportLogs.Add($"要確認: {decision.Source.Broker} {decision.Source.Ticker} {decision.Source.Name} / {decision.Reason}");
                        continue;
                    }

                    decision.Merged.Stock.Id = _savePosition(decision.Merged);
                    positionByKey[positionKey] = decision.Merged;
                    if (decision.Action == BrokerMergeAction.Create)
                    {
                        createdStocks++;
                    }
                    else
                    {
                        updatedStocks++;
                    }
                }
            }

            if (statement.CanUpdateHoldings)
            {
                foreach (var tradeGroup in statement.Trades
                             .GroupBy(x => BuildPositionKey(x.Broker, x.Ticker, x.Account), StringComparer.OrdinalIgnoreCase)
                             .OrderBy(x => x.Key))
                {
                    if (holdingKeys.Contains(tradeGroup.Key) ||
                        (positionByKey.TryGetValue(tradeGroup.Key, out var holdingPosition) && HasHoldingSnapshotSource(holdingPosition)))
                    {
                        skippedTradeGroups++;
                        continue;
                    }

                    dividendsByKey.TryGetValue(tradeGroup.Key, out var relatedDividends);
                    relatedDividends ??= Array.Empty<BrokerDividendRecord>();
                    if (!positionByKey.TryGetValue(tradeGroup.Key, out var position))
                    {
                        position = CreatePositionFromTrades(tradeGroup.ToList(), relatedDividends);
                        if (position is null)
                        {
                            skippedTradeGroups++;
                            continue;
                        }

                        position.Stock.Id = _savePosition(position);
                        positionByKey[tradeGroup.Key] = position;
                        createdStocks++;
                    }
                    else
                    {
                        ApplyTradeSummary(position, tradeGroup.ToList(), relatedDividends);
                        position.Stock.Id = _savePosition(position);
                        updatedStocks++;
                    }
                }
            }

            var existingDividends = _getDividendPayments().ToList();
            var reconciliationService = new DividendReconciliationService();
            var importedDividends = 0;
            var updatedDividends = 0;
            var replacedDividends = 0;
            var skippedDividends = 0;

            foreach (var record in statement.Dividends)
            {
                var positionKey = BuildPositionKey(record.Broker, record.Ticker, record.Account);
                if (!positionByKey.TryGetValue(positionKey, out var position))
                {
                    var relatedRecords = dividendsByKey.TryGetValue(positionKey, out var group)
                        ? group
                        : new[] { record };
                    position = CreateDividendOnlyPosition(record, relatedRecords, useDividendQuantityAsHolding: false);
                    position.Stock.Id = _savePosition(position);
                    positionByKey[positionKey] = position;
                    createdReferenceStocks++;
                }
                else if (!IsDividendOnlyPosition(position))
                {
                    var relatedRecords = dividendsByKey.TryGetValue(positionKey, out var group)
                        ? group
                        : new[] { record };
                    ApplyDividendSummary(position, relatedRecords);
                    position.Stock.Id = _savePosition(position);
                }

                var payment = CreateDividendPayment(position.Stock.Id, record);
                switch (SaveBrokerDividend(existingDividends, payment, reconciliationService))
                {
                    case DividendImportAction.Created:
                        importedDividends++;
                        break;
                    case DividendImportAction.UpdatedExistingActual:
                        updatedDividends++;
                        break;
                    case DividendImportAction.ReplacedSchedule:
                        importedDividends++;
                        replacedDividends++;
                        break;
                    default:
                        skippedDividends++;
                        break;
                }
            }

            _saveBrokerTrades?.Invoke(statement.Trades);
            _afterImport?.Invoke();
            AddStatementSummaryLogs(statement);
            ImportLogs.Add($"保有反映: 新規 {createdStocks}件 / 更新 {updatedStocks}件 / 要確認 {reviewHoldings}件 / 取引復元見送り {skippedTradeGroups}件");
            ImportLogs.Add($"配当専用仮銘柄: {createdReferenceStocks}件（銘柄一覧の「現在保有のみ」には表示しません）");
            ImportLogs.Add($"配当登録: {importedDividends}件 / 既存実績更新 {updatedDividends}件 / 予定置換 {replacedDividends}件 / スキップ {skippedDividends}件");

            Message = statement.CanUpdateHoldings
                ? $"{sourceLabel} を一括取込しました。保有反映: 新規 {createdStocks}件、更新 {updatedStocks}件、要確認 {reviewHoldings}件。配当登録: {importedDividends}件、既存実績更新: {updatedDividends}件、予定置換: {replacedDividends}件。"
                : $"{sourceLabel} を取り込みました。現在保有は更新せず、配当登録: {importedDividends}件、既存実績更新: {updatedDividends}件、予定置換: {replacedDividends}件。";
        }
        catch (Exception ex)
        {
            Message = $"{sourceLabel} の一括取込に失敗しました: {ex.Message}";
        }
    }

    private void AddStatementSummaryLogs(BrokerStatementImport statement)
    {
        if (statement.Holdings.Count > 0)
        {
            ImportLogs.Add($"保有残高: {statement.Holdings.Count}件");
            foreach (var bySource in statement.Holdings.GroupBy(x => x.Source).OrderBy(x => x.Key))
            {
                ImportLogs.Add($"{bySource.Key}: 保有 {bySource.Count()}件 / 評価額 {bySource.Sum(x => x.MarketValueJpy):N0}円");
            }
        }

        ImportLogs.Add($"配当明細: {statement.Dividends.Count}件 / {statement.Dividends.Sum(x => x.NetAmountJpy):N2}円");
        foreach (var byType in statement.Trades.GroupBy(x => x.TradeType).OrderBy(x => x.Key))
        {
            ImportLogs.Add($"{byType.Key}: {byType.Count()}件");
        }

        foreach (var byTicker in statement.Dividends.GroupBy(x => x.Ticker).OrderBy(x => x.Key))
        {
            ImportLogs.Add($"{byTicker.Key}: 配当 {byTicker.Count()}件 / {byTicker.Sum(x => x.NetAmountJpy):N2}円");
        }

        if (statement.IgnoredRowCount > 0)
        {
            ImportLogs.Add($"投資成績計算外として読み飛ばし: {statement.IgnoredRowCount}行");
        }
    }

    private static StockPosition? CreatePositionFromTrades(
        IReadOnlyList<BrokerTradeRecord> trades,
        IReadOnlyList<BrokerDividendRecord> relatedDividends)
    {
        if (trades.Count == 0)
        {
            return null;
        }

        var identity = trades
            .OrderByDescending(x => x.SettlementDate)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Ticker));
        if (identity is null)
        {
            return null;
        }

        var reconstruction = TradeReconstructionService.Reconstruct(trades);
        var currentShares = reconstruction.CurrentShares;

        var currency = ResolveCurrency(trades, relatedDividends);
        var purchaseDate = trades
            .Where(x => string.Equals(x.TradeType, "現物買付", StringComparison.Ordinal))
            .OrderBy(x => x.TradeDate)
            .Select(x => x.TradeDate)
            .FirstOrDefault();
        if (purchaseDate == default)
        {
            purchaseDate = trades.Min(x => x.TradeDate);
        }

        var exchangeRate = ResolveExchangeRate(currency, trades, relatedDividends, 1m);
        var averagePurchasePrice = reconstruction.AverageAcquisitionPrice;
        var latestSettlementDate = trades.Max(x => x.SettlementDate);
        var annualDividendPerShare = EstimateAnnualDividendPerShare(FilterDividendsByCurrency(relatedDividends, currency));
        var hasDividend = annualDividendPerShare > 0m || relatedDividends.Count > 0;
        var sourceLabel = trades.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Source))?.Source ?? "取引履歴CSV";

        return new StockPosition
        {
            Stock = new Stock
            {
                Ticker = NormalizeTicker(identity.Ticker),
                Name = ResolveName(trades, relatedDividends),
                Country = currency == "JPY" ? "日本" : "米国",
                Currency = currency,
                Broker = identity.Broker,
                AccountType = AccountTypeNormalizer.Normalize(identity.Account),
                CustodyType = identity.Account,
                DataSource = sourceLabel,
                Memo = BuildTradeSummaryMemo(trades, reconstruction)
            },
            Purchase = new Purchase
            {
                PurchaseDate = purchaseDate,
                Shares = currentShares,
                UnitPrice = averagePurchasePrice,
                ExchangeRate = exchangeRate,
                ExchangeRateAcquiredAt = purchaseDate,
                ExchangeRateSource = sourceLabel,
                ExchangeRateInputType = "CSV",
                Fee = trades
                    .Where(x => string.Equals(x.TradeType, "現物買付", StringComparison.Ordinal))
                    .Sum(x => x.FeeJpy),
                Memo = $"{sourceLabel}から時系列集計"
            },
            Split = new StockSplit
            {
                SplitDate = latestSettlementDate,
                SplitRatio = 1m,
                Memo = $"{sourceLabel}から自動作成"
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = currentShares,
                CurrentPrice = 0m,
                CurrentExchangeRate = exchangeRate,
                ExchangeRateAcquiredAt = latestSettlementDate,
                ExchangeRateSource = sourceLabel,
                ExchangeRateInputType = "CSV",
                AnnualDividendPerShare = annualDividendPerShare,
                DividendStatus = hasDividend ? "配当あり" : "配当未入力",
                DividendFrequency = EstimateDividendFrequency(relatedDividends),
                UpdatedAt = latestSettlementDate
            }
        };
    }

    private static void ApplyTradeSummary(
        StockPosition position,
        IReadOnlyList<BrokerTradeRecord> trades,
        IReadOnlyList<BrokerDividendRecord> relatedDividends)
    {
        if (trades.Count == 0)
        {
            return;
        }

        var currency = ResolveCurrency(trades, relatedDividends);
        var reconstruction = TradeReconstructionService.Reconstruct(trades);
        var currentShares = reconstruction.CurrentShares;
        var averagePurchasePrice = reconstruction.AverageAcquisitionPrice;
        var exchangeRate = ResolveExchangeRate(currency, trades, relatedDividends, position.CurrentHolding.CurrentExchangeRate);
        var latestSettlementDate = trades.Max(x => x.SettlementDate);
        var annualDividendPerShare = EstimateAnnualDividendPerShare(FilterDividendsByCurrency(relatedDividends, currency));
        var sourceLabel = trades.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Source))?.Source ?? "取引履歴CSV";

        position.Stock.Name = ResolveName(trades, relatedDividends);
        position.Stock.Country = currency == "JPY" ? "日本" : "米国";
        position.Stock.Currency = currency;
        position.Stock.Broker = trades[0].Broker;
        position.Stock.AccountType = AccountTypeNormalizer.Normalize(trades[0].Account);
        position.Stock.CustodyType = trades[0].Account;
        position.Stock.DataSource = sourceLabel;
        position.Stock.Memo = BuildTradeSummaryMemo(trades, reconstruction);

        position.Purchase.PurchaseDate = trades
            .Where(x => string.Equals(x.TradeType, "現物買付", StringComparison.Ordinal))
            .OrderBy(x => x.TradeDate)
            .Select(x => x.TradeDate)
            .FirstOrDefault(position.Purchase.PurchaseDate);
        position.Purchase.Shares = currentShares;
        if (averagePurchasePrice > 0m)
        {
            position.Purchase.UnitPrice = averagePurchasePrice;
        }

        position.Purchase.ExchangeRate = exchangeRate;
        position.Purchase.ExchangeRateAcquiredAt = position.Purchase.PurchaseDate;
        position.Purchase.ExchangeRateSource = sourceLabel;
        position.Purchase.ExchangeRateInputType = "CSV";
        position.Purchase.Fee = trades
            .Where(x => string.Equals(x.TradeType, "現物買付", StringComparison.Ordinal))
            .Sum(x => x.FeeJpy);
        position.Purchase.Memo = $"{sourceLabel}から時系列集計";

        position.CurrentHolding.CurrentShares = currentShares;
        position.CurrentHolding.CurrentExchangeRate = exchangeRate;
        position.CurrentHolding.ExchangeRateAcquiredAt = latestSettlementDate;
        position.CurrentHolding.ExchangeRateSource = sourceLabel;
        position.CurrentHolding.ExchangeRateInputType = "CSV";
        if (annualDividendPerShare > 0m)
        {
            position.CurrentHolding.AnnualDividendPerShare = annualDividendPerShare;
            position.CurrentHolding.DividendStatus = "配当あり";
            position.CurrentHolding.DividendFrequency = EstimateDividendFrequency(relatedDividends);
        }

        position.CurrentHolding.UpdatedAt = latestSettlementDate;
    }

    private static void ApplyDividendSummary(StockPosition position, IReadOnlyList<BrokerDividendRecord> relatedDividends)
    {
        if (relatedDividends.Count == 0)
        {
            return;
        }

        var latestDividend = relatedDividends.OrderByDescending(x => x.PaymentDate).First();
        var currency = string.IsNullOrWhiteSpace(latestDividend.Currency) ? position.Stock.Currency : latestDividend.Currency;
        var exchangeRate = currency == "JPY"
            ? 1m
            : latestDividend.ExchangeRate > 0m ? latestDividend.ExchangeRate : position.CurrentHolding.CurrentExchangeRate;
        var annualDividendPerShare = EstimateAnnualDividendPerShare(relatedDividends);

        position.Stock.Name = latestDividend.Name;
        position.Stock.Country = currency == "JPY" ? "日本" : "米国";
        position.Stock.Currency = currency;
        position.Stock.Broker = latestDividend.Broker;
        position.Stock.AccountType = AccountTypeNormalizer.Normalize(latestDividend.Account);
        position.Stock.CustodyType = latestDividend.Account;
        position.Stock.DataSource = string.IsNullOrWhiteSpace(latestDividend.Source) ? "配当CSV" : latestDividend.Source;

        position.CurrentHolding.CurrentExchangeRate = exchangeRate;
        position.CurrentHolding.ExchangeRateAcquiredAt = latestDividend.PaymentDate;
        position.CurrentHolding.ExchangeRateSource = string.IsNullOrWhiteSpace(latestDividend.Source) ? "配当CSV" : latestDividend.Source;
        position.CurrentHolding.ExchangeRateInputType = "CSV";
        if (annualDividendPerShare > 0m)
        {
            position.CurrentHolding.AnnualDividendPerShare = annualDividendPerShare;
        }

        position.CurrentHolding.DividendStatus = "配当あり";
        position.CurrentHolding.DividendFrequency = EstimateDividendFrequency(relatedDividends);
        position.CurrentHolding.UpdatedAt = latestDividend.PaymentDate;
    }

    private static StockPosition CreateDividendOnlyPosition(
        BrokerDividendRecord record,
        IReadOnlyList<BrokerDividendRecord>? relatedRecords = null,
        bool useDividendQuantityAsHolding = false)
    {
        var currency = record.Currency == "USD" ||
                       record.Product.Contains("米国", StringComparison.Ordinal) ||
                       record.Product.Contains("外株", StringComparison.Ordinal)
            ? "USD"
            : "JPY";
        var exchangeRate = currency == "JPY" ? 1m : record.ExchangeRate;
        relatedRecords ??= new[] { record };
        var annualDividendPerShare = EstimateAnnualDividendPerShare(relatedRecords);
        return new StockPosition
        {
            Stock = new Stock
            {
                Ticker = NormalizeTicker(record.Ticker),
                Name = record.Name,
                Country = currency == "JPY" ? "日本" : "米国",
                Currency = currency,
                Broker = record.Broker,
                AccountType = AccountTypeNormalizer.Normalize(record.Account),
                CustodyType = record.Account,
                DataSource = string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source,
                Memo = "配当CSV取込時に自動作成"
            },
            Purchase = new Purchase
            {
                PurchaseDate = record.PaymentDate,
                Shares = useDividendQuantityAsHolding ? record.Quantity : 0m,
                UnitPrice = 0m,
                ExchangeRate = exchangeRate,
                ExchangeRateAcquiredAt = record.PaymentDate,
                ExchangeRateSource = string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source,
                ExchangeRateInputType = "CSV"
            },
            Split = new StockSplit
            {
                SplitDate = record.PaymentDate,
                SplitRatio = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = useDividendQuantityAsHolding ? record.Quantity : 0m,
                CurrentPrice = 0m,
                CurrentExchangeRate = exchangeRate,
                ExchangeRateAcquiredAt = record.PaymentDate,
                ExchangeRateSource = string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source,
                ExchangeRateInputType = "CSV",
                AnnualDividendPerShare = annualDividendPerShare,
                DividendStatus = "配当あり",
                DividendFrequency = EstimateDividendFrequency(relatedRecords),
                UpdatedAt = record.PaymentDate
            }
        };
    }

    private static string ResolveCurrency(
        IReadOnlyList<BrokerTradeRecord> trades,
        IReadOnlyList<BrokerDividendRecord> relatedDividends)
    {
        var tradeCurrency = trades.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Currency))?.Currency;
        if (!string.IsNullOrWhiteSpace(tradeCurrency))
        {
            return tradeCurrency;
        }

        return relatedDividends.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Currency))?.Currency ?? "JPY";
    }

    private static decimal ResolveExchangeRate(
        string currency,
        IReadOnlyList<BrokerTradeRecord> trades,
        IReadOnlyList<BrokerDividendRecord> relatedDividends,
        decimal fallbackRate)
    {
        if (currency == "JPY")
        {
            return 1m;
        }

        var dividendRate = relatedDividends
            .OrderByDescending(x => x.PaymentDate)
            .FirstOrDefault(x => x.ExchangeRate > 0m)
            ?.ExchangeRate;
        if (dividendRate is > 0m)
        {
            return dividendRate.Value;
        }

        var tradeRate = trades
            .OrderByDescending(x => x.SettlementDate)
            .FirstOrDefault(x => x.ExchangeRate > 0m)
            ?.ExchangeRate;
        if (tradeRate is > 0m)
        {
            return tradeRate.Value;
        }

        return fallbackRate > 0m ? fallbackRate : 1m;
    }

    private static string ResolveName(
        IReadOnlyList<BrokerTradeRecord> trades,
        IReadOnlyList<BrokerDividendRecord> relatedDividends)
    {
        var dividendName = relatedDividends
            .OrderByDescending(x => x.PaymentDate)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Name))
            ?.Name;
        if (!string.IsNullOrWhiteSpace(dividendName))
        {
            return dividendName;
        }

        return trades
            .OrderByDescending(x => x.SettlementDate)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Name))
            ?.Name ?? string.Empty;
    }

    private static string BuildTradeSummaryMemo(IReadOnlyList<BrokerTradeRecord> trades, TradeReconstructionResult reconstruction)
    {
        var buys = trades.Count(x => string.Equals(x.TradeType, "現物買付", StringComparison.Ordinal));
        var sells = trades.Count(x => string.Equals(x.TradeType, "現物売却", StringComparison.Ordinal));
        var stockChanges = trades.Count(x => string.Equals(x.TradeType, "入庫（増減資）", StringComparison.Ordinal));
        var source = trades.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Source))?.Source ?? "取引履歴CSV";
        var warning = reconstruction.IncompleteSellCount > 0
            ? $" / 売却前数量不足 {reconstruction.IncompleteSellCount}件"
            : string.Empty;
        var realizedGainJpy = reconstruction.RealizedGainJpy != 0m
            ? $" / 推定実現損益JPY {reconstruction.RealizedGainJpy:N0}"
            : string.Empty;
        return $"{source}一括取込 / 買付 {buys}件 / 売却 {sells}件 / 入庫・増減資 {stockChanges}件 / 推定現在株数 {reconstruction.CurrentShares:N2} / 推定平均取得単価 {reconstruction.AverageAcquisitionPrice:N4} / 推定実現損益 {reconstruction.RealizedGain:N2}{realizedGainJpy}{warning}";
    }

    private static decimal EstimateAnnualDividendPerShare(IReadOnlyList<BrokerDividendRecord> records)
    {
        if (records.Count == 0)
        {
            return 0m;
        }

        var latestDate = records.Max(x => x.PaymentDate);
        var latestQuantity = records
            .Where(x => x.Quantity > 0m)
            .OrderByDescending(x => x.PaymentDate)
            .FirstOrDefault()
            ?.Quantity ?? 0m;
        if (latestQuantity <= 0m)
        {
            return 0m;
        }

        var from = latestDate.AddDays(-370);
        var annualGross = records
            .Where(x => x.PaymentDate >= from && x.PaymentDate <= latestDate)
            .Sum(x => x.GrossAmount);

        return annualGross > 0m ? annualGross / latestQuantity : 0m;
    }

    private static IReadOnlyList<BrokerDividendRecord> FilterDividendsByCurrency(
        IReadOnlyList<BrokerDividendRecord> records,
        string currency)
    {
        var matching = records
            .Where(x => string.Equals(x.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matching.Count > 0 ? matching : Array.Empty<BrokerDividendRecord>();
    }

    private static string EstimateDividendFrequency(IReadOnlyList<BrokerDividendRecord> records)
    {
        if (records.Count == 0)
        {
            return string.Empty;
        }

        var latestDate = records.Max(x => x.PaymentDate);
        var from = latestDate.AddDays(-370);
        var count = records.Count(x => x.PaymentDate >= from && x.PaymentDate <= latestDate);
        if (count >= 4)
        {
            return "年4回";
        }

        if (count >= 2)
        {
            return "年2回";
        }

        return "年1回";
    }

    private DividendImportAction SaveBrokerDividend(
        List<DividendPayment> existingDividends,
        DividendPayment payment,
        DividendReconciliationService reconciliationService)
    {
        if (_saveDividend is null)
        {
            return DividendImportAction.Skipped;
        }

        var decision = reconciliationService.ReconcileActual(existingDividends, payment);
        if (decision.DuplicateActual is not null)
        {
            payment.Id = decision.DuplicateActual.Id;
            payment.CreatedAt = decision.DuplicateActual.CreatedAt;
            payment.UpdatedAt = DateTime.Now;
            var updatedId = _saveDividend(payment);
            payment.Id = updatedId;
            ReplaceInMemoryDividend(existingDividends, payment);
            return DividendImportAction.UpdatedExistingActual;
        }

        var newId = _saveDividend(payment);
        payment.Id = newId;
        existingDividends.Add(payment);

        foreach (var target in decision.ReplaceTargets)
        {
            target.DividendStatus = DividendConstants.Replaced;
            target.ReplacedByDividendId = newId;
            target.MatchedActualDividendId = newId;
            target.UpdatedAt = DateTime.Now;
            _saveDividend(target);
            ReplaceInMemoryDividend(existingDividends, target);
        }

        return decision.ReplaceTargets.Count > 0
            ? DividendImportAction.ReplacedSchedule
            : DividendImportAction.Created;
    }

    private static void ReplaceInMemoryDividend(List<DividendPayment> dividends, DividendPayment payment)
    {
        var index = dividends.FindIndex(x => x.Id == payment.Id);
        if (index >= 0)
        {
            dividends[index] = payment;
        }
    }

    private static DividendPayment CreateDividendPayment(int stockId, BrokerDividendRecord record)
    {
        var currency = NormalizeCurrency(record.Currency);
        var exchangeRate = currency == "JPY"
            ? 1m
            : record.ExchangeRate > 0m ? record.ExchangeRate : 1m;
        var accountType = DividendConstants.NormalizeAccountType(record.Account);
        var grossAmount = record.GrossAmount > 0m
            ? record.GrossAmount
            : record.NetAmount > 0m ? record.NetAmount + record.TaxAmount : record.NetAmountJpy / exchangeRate + record.TaxAmount;
        var netAmount = record.NetAmount > 0m
            ? record.NetAmount
            : record.NetAmountJpy > 0m ? record.NetAmountJpy / exchangeRate : Math.Max(0m, grossAmount - record.TaxAmount);
        var netAmountJpy = record.NetAmountJpy > 0m ? record.NetAmountJpy : netAmount * exchangeRate;
        var grossAmountJpy = grossAmount * exchangeRate;
        var totalTaxAmountJpy = record.TaxAmount * exchangeRate;

        return new DividendPayment
        {
            StockId = stockId,
            AccountType = accountType,
            TaxAccountType = accountType,
            PaymentDate = record.PaymentDate,
            FiscalYear = record.PaymentDate.Year,
            FiscalQuarter = $"Q{((record.PaymentDate.Month - 1) / 3) + 1}",
            Broker = record.Broker,
            DividendStatus = DividendConstants.Actual,
            Source = DividendConstants.SourceCsv,
            SourceFile = string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source,
            SourcePriority = 100,
            Quantity = record.Quantity,
            DividendPerShare = record.Quantity > 0m && grossAmount > 0m ? grossAmount / record.Quantity : 0m,
            GrossAmount = grossAmount,
            TotalTaxAmount = record.TaxAmount,
            TaxAmount = record.TaxAmount,
            NetAmount = netAmount,
            Currency = currency,
            ExchangeRate = exchangeRate,
            ExchangeRateAcquiredAt = record.PaymentDate,
            ExchangeRateSource = string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source,
            ExchangeRateInputType = "CSV",
            GrossAmountJpy = grossAmountJpy,
            TotalTaxAmountJpy = totalTaxAmountJpy,
            NetAmountJpy = netAmountJpy,
            JpyAmount = netAmountJpy,
            IsTaxEstimated = false,
            IsNisa = DividendConstants.IsNisaAccount(accountType),
            IsForeignStock = currency != "JPY",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            Memo = $"{(string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source)} / 口座: {record.Account} / 商品: {record.Product} / 数量: {record.Quantity:N2}"
        };
    }

    private static bool HasHoldingSnapshotSource(StockPosition position)
    {
        var source = position.Stock.DataSource ?? string.Empty;
        return source.Contains("保有残高CSV", StringComparison.Ordinal) ||
               source.Contains("保有CSV", StringComparison.Ordinal) ||
               source.Contains("日本株保有CSV", StringComparison.Ordinal) ||
               source.Contains("証券会社データ", StringComparison.Ordinal);
    }

    private static bool IsDividendOnlyPosition(StockPosition position)
    {
        var source = position.Stock.DataSource ?? string.Empty;
        var memo = position.Stock.Memo ?? string.Empty;
        return position.CurrentHolding.CurrentShares <= 0m &&
               position.Purchase.UnitPrice <= 0m &&
               position.CurrentHolding.CurrentPrice <= 0m &&
               (source.Contains("配当CSV", StringComparison.Ordinal) ||
                memo.Contains("配当CSV取込時", StringComparison.Ordinal));
    }

    private static string BuildDividendKey(DividendPayment payment) =>
        $"{payment.StockId}|{payment.PaymentDate:yyyyMMdd}|{payment.Broker}|{payment.JpyAmount:0.######}|{payment.Memo}";

    private enum DividendImportAction
    {
        Skipped,
        Created,
        UpdatedExistingActual,
        ReplacedSchedule
    }

    private static string BuildPositionKey(string broker, string ticker, string account = "")
    {
        var normalizedBroker = SecuritySymbolNormalizer.NormalizeBroker(broker);
        var normalizedTicker = NormalizeTicker(ticker);
        var normalizedAccount = AccountTypeNormalizer.Normalize(account);
        return $"{normalizedBroker}|{normalizedTicker}|{normalizedAccount}";
    }

    private IReadOnlyList<string> GetTargetFilePaths()
    {
        if (_selectedFilePaths.Count > 0)
        {
            return _selectedFilePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return string.IsNullOrWhiteSpace(FilePath)
            ? Array.Empty<string>()
            : new[] { FilePath.Trim().Trim('"') };
    }

    private static string FormatSelectedFilePath(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
        {
            return string.Empty;
        }

        if (filePaths.Count == 1)
        {
            return filePaths[0];
        }

        return $"{filePaths.Count}件選択: {string.Join("; ", filePaths.Select(Path.GetFileName))}";
    }

    private static string NormalizeTicker(string ticker)
    {
        return SecuritySymbolNormalizer.NormalizeTicker(ticker);
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "円" ? "JPY" : normalized;
    }
}
