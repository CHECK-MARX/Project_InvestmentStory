using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Threading;
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
    private readonly NomuraDividendCsvParser _nomuraDividendParser = new();
    private readonly BrokerHoldingMergeService _holdingMergeService = new();
    private readonly Func<IReadOnlyList<StockPosition>>? _getPositions;
    private readonly Func<StockPosition, int>? _savePosition;
    private readonly Func<DividendPayment, int>? _saveDividend;
    private readonly Func<IReadOnlyList<DividendPayment>>? _getDividendPayments;
    private readonly Action<IReadOnlyList<BrokerTradeRecord>>? _saveBrokerTrades;
    private readonly Func<int, IReadOnlyList<BrokerTrade>>? _getBrokerTrades;
    private readonly Action<Action>? _runImportAtomically;
    private readonly Func<string>? _getMergePolicy;
    private readonly Action<BrokerTradeRecord, string>? _saveReviewTrade;
    private readonly Action<BrokerUnresolvedDividend>? _saveReviewDividend;
    private readonly Action<BrokerHoldingRecord, string>? _saveReviewHolding;
    private readonly Action<CsvImportLogEntry>? _saveImportLog;
    private readonly Action? _afterImport;
    private readonly Func<Task>? _afterImportAsync;
    private readonly bool _useAsyncCommands;
    private readonly List<string> _selectedFilePaths = new();
    private readonly Dictionary<string, string> _previewFileHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CsvKind> _previewKinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BrokerStatementImport> _previewStatements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<BrokerDividendRecord>> _previewDividends = new(StringComparer.OrdinalIgnoreCase);
    private BrokerStatementImport? _previewSbiBatch;
    private string _message = string.Empty;
    private string _filePath = string.Empty;
    private bool _updatingFilePathFromDialog;
    private bool _atomicImportInProgress;
    private bool _lastImportApplied;
    private int _lastImportRowCount;
    private int _lastImportInsertedCount;
    private int _lastImportUpdatedCount;
    private int _lastImportSkippedCount;
    private int _lastImportConflictCount;
    private BrokerStatementImport? _lastImportedStatement;
    private readonly Dictionary<string, FileImportOutcome> _fileImportOutcomes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _ambiguousDividendIds = new();
    private CancellationTokenSource? _operationCancellation;
    private List<string>? _operationLogs;
    private List<CsvPreviewRowViewModel>? _operationPreviewRows;
    private string? _operationMessage;
    private Dispatcher? _uiDispatcher;
    private bool _backgroundOperation;
    private bool _refreshAfterImport;
    private bool _isBusy;
    private string _progressText = string.Empty;
    private int _progressValue;
    private int _progressMaximum;

    public CsvImportViewModel(
        Func<IReadOnlyList<StockPosition>>? getPositions = null,
        Func<StockPosition, int>? savePosition = null,
        Func<DividendPayment, int>? saveDividend = null,
        Func<IReadOnlyList<DividendPayment>>? getDividendPayments = null,
        Action<IReadOnlyList<BrokerTradeRecord>>? saveBrokerTrades = null,
        Action? afterImport = null,
        Func<int, IReadOnlyList<BrokerTrade>>? getBrokerTrades = null,
        Action<Action>? runImportAtomically = null,
        Func<string>? getMergePolicy = null,
        Action<BrokerTradeRecord, string>? saveReviewTrade = null,
        Action<BrokerUnresolvedDividend>? saveReviewDividend = null,
        Action<BrokerHoldingRecord, string>? saveReviewHolding = null,
        Action<CsvImportLogEntry>? saveImportLog = null,
        bool useAsyncCommands = false,
        Func<Task>? afterImportAsync = null)
    {
        _getPositions = getPositions;
        _savePosition = savePosition;
        _saveDividend = saveDividend;
        _getDividendPayments = getDividendPayments;
        _saveBrokerTrades = saveBrokerTrades;
        _getBrokerTrades = getBrokerTrades;
        _runImportAtomically = runImportAtomically;
        _getMergePolicy = getMergePolicy;
        _saveReviewTrade = saveReviewTrade;
        _saveReviewDividend = saveReviewDividend;
        _saveReviewHolding = saveReviewHolding;
        _saveImportLog = saveImportLog;
        _afterImport = afterImport;
        _afterImportAsync = afterImportAsync;
        _useAsyncCommands = useAsyncCommands;
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        PreviewCommand = new RelayCommand(() => StartOperation(Preview, false), () => !IsBusy);
        ImportCommand = new RelayCommand(() => StartOperation(Import, true), () => !IsBusy);
        CancelCommand = new RelayCommand(() => _operationCancellation?.Cancel(), () => IsBusy && !_refreshAfterImport);
    }

    public ICommand BrowseCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand CancelCommand { get; }
    public ObservableCollection<CsvPreviewRowViewModel> PreviewRows { get; } = new();
    public ObservableCollection<string> ImportLogs { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            ((RelayCommand)BrowseCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PreviewCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ImportCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
        }
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public int ProgressMaximum
    {
        get => _progressMaximum;
        private set => SetProperty(ref _progressMaximum, value);
    }

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
                ResetPreviewPlan();
            }
        }
    }
    public string TickerColumn { get; set; } = "銘柄コード";
    public string DateColumn { get; set; } = "取引日";
    public string QuantityColumn { get; set; } = "数量";
    public string AmountColumn { get; set; } = "金額";

    public string Message
    {
        get => _backgroundOperation ? _operationMessage ?? _message : _message;
        private set
        {
            if (_backgroundOperation) _operationMessage = value;
            else SetProperty(ref _message, value);
        }
    }

    private void StartOperation(Action operation, bool isImport)
    {
        if (!_useAsyncCommands)
        {
            operation();
            return;
        }

        if (IsBusy) return;
        _ = RunOperationAsync(operation, isImport);
    }

    private async Task RunOperationAsync(Action operation, bool isImport)
    {
        _uiDispatcher = Dispatcher.CurrentDispatcher;
        _operationCancellation = new CancellationTokenSource();
        _operationLogs = new List<string>();
        _operationPreviewRows = new List<CsvPreviewRowViewModel>();
        _operationMessage = null;
        _refreshAfterImport = false;
        _backgroundOperation = true;
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, GetTargetFilePaths().Count);
        ProgressText = isImport ? "CSVを取り込んでいます…" : "CSVを解析しています…";
        ImportLogs.Clear();
        if (!isImport) PreviewRows.Clear();
        IsBusy = true;
        var workWatch = Stopwatch.StartNew();
        var refreshTime = TimeSpan.Zero;
        try
        {
            await Task.Run(operation);
            if (_operationCancellation.IsCancellationRequested && !_refreshAfterImport)
                _operationMessage = "操作を中止しました。";
        }
        catch (OperationCanceledException)
        {
            if (!isImport) ResetPreviewPlan();
            _operationMessage = "操作を中止しました。";
        }
        catch (Exception ex)
        {
            if (!isImport) ResetPreviewPlan();
            _operationMessage = $"CSVの処理に失敗しました: {ex.Message}";
        }
        finally
        {
            workWatch.Stop();
            _backgroundOperation = false;
            if (!isImport && _operationCancellation?.IsCancellationRequested == true)
            {
                ResetPreviewPlan();
                _operationPreviewRows!.Clear();
            }
            foreach (var row in _operationPreviewRows!) PreviewRows.Add(row);
            foreach (var log in _operationLogs!) ImportLogs.Add(log);
            if (_operationMessage is not null) Message = _operationMessage;
            if (_refreshAfterImport)
            {
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
                ProgressText = "画面を更新しています…";
                var refreshWatch = Stopwatch.StartNew();
                try
                {
                    if (_afterImportAsync is not null) await _afterImportAsync();
                    else _afterImport?.Invoke();
                }
                catch (Exception ex)
                {
                    AddImportLog($"DB保存済み。画面更新に失敗しました: {ex.Message}");
                    Message = $"DB保存は完了しました。画面更新に失敗しました: {ex.Message}";
                }
                finally
                {
                    refreshWatch.Stop();
                    refreshTime = refreshWatch.Elapsed;
                }
            }
            AddImportLog($"計測: {GetTargetFilePaths().Count}ファイル / " +
                (isImport ? $"入力明細 {_fileImportOutcomes.Values.Sum(x => x.RowCount)}行 / " : string.Empty) +
                $"CSV処理・DB適用 {workWatch.Elapsed.TotalSeconds:N2}秒 / 画面更新 {refreshTime.TotalSeconds:N2}秒");
            ProgressValue = ProgressMaximum;
            ProgressText = _operationCancellation?.IsCancellationRequested == true && !_refreshAfterImport
                ? "中止しました" : "処理が完了しました";
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            _operationLogs = null;
            _operationPreviewRows = null;
            _uiDispatcher = null;
            IsBusy = false;
        }
    }

    private void AddImportLog(string log)
    {
        if (_backgroundOperation) _operationLogs!.Add(log);
        else ImportLogs.Add(log);
    }

    private void ClearImportLogs()
    {
        if (_backgroundOperation) _operationLogs!.Clear();
        else ImportLogs.Clear();
    }

    private IReadOnlyList<string> CurrentImportLogs => _backgroundOperation ? _operationLogs! : ImportLogs;

    private void AddPreviewRow(CsvPreviewRowViewModel row)
    {
        if (_backgroundOperation) _operationPreviewRows!.Add(row);
        else PreviewRows.Add(row);
    }

    private void ClearPreviewRows()
    {
        if (_backgroundOperation) _operationPreviewRows!.Clear();
        else PreviewRows.Clear();
    }

    private void CheckCancellation() => _operationCancellation?.Token.ThrowIfCancellationRequested();

    private void ReportProgress(int completed, string fileName)
    {
        if (!_backgroundOperation || _uiDispatcher is null) return;
        _ = _uiDispatcher.BeginInvoke(() =>
        {
            if (!IsBusy || !_backgroundOperation) return;
            ProgressValue = Math.Min(completed, ProgressMaximum);
            ProgressText = $"{ProgressValue}/{ProgressMaximum}件: {fileName}";
        });
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
            ResetPreviewPlan();
            _selectedFilePaths.AddRange(dialog.FileNames);
            _updatingFilePathFromDialog = true;
            FilePath = FormatSelectedFilePath(_selectedFilePaths);
            _updatingFilePathFromDialog = false;
            Preview();
        }
    }

    private void Preview()
    {
        CheckCancellation();
        ClearPreviewRows();
        ClearImportLogs();
        ResetPreviewPlan();
        var filePaths = GetTargetFilePaths();
        if (filePaths.Count == 0)
        {
            Message = "CSVファイルを選択してください。";
            return;
        }

        try
        {
            foreach (var path in filePaths)
            {
                CheckCancellation();
                _previewFileHashes[path] = ComputeFileHash(path);
            }
        }
        catch (Exception ex)
        {
            ResetPreviewPlan();
            Message = $"CSVの読み取りに失敗しました: {ex.Message}";
            return;
        }

        if (filePaths.Count > 1)
        {
            PreviewMultipleFiles(filePaths);
            return;
        }

        var filePath = filePaths[0];
        if (GetCsvKind(filePath) == CsvKind.NomuraHoldings)
        {
            PreviewBrokerStatement(() => _nomuraHoldingsParser.Parse(filePath), "野村保有残高CSV", filePath);
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.NomuraTransaction)
        {
            PreviewBrokerStatement(() => _nomuraTransactionParser.Parse(filePath), "野村取引履歴CSV", filePath);
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.NomuraDividend)
        {
            PreviewBrokerStatement(() => _nomuraDividendParser.Parse(filePath), "野村配当履歴CSV", filePath);
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.SbiStatement)
        {
            PreviewBrokerStatement(() => _sbiStatementParser.ParseFiles(new[] { filePath }), "SBI一括CSV", filePath);
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.SbiDistribution)
        {
            PreviewBrokerStatement(() => _sbiDistributionParser.ParseStatement(filePath), "SBI配当CSV", filePath);
            return;
        }

        try
        {
            var service = _factory.CreateCsvImportService(Broker);
            var preview = service.Preview(filePath, CsvType);
            var index = 1;
            foreach (var row in preview.Rows)
            {
                AddPreviewRow(new CsvPreviewRowViewModel
                {
                    RowNumber = index++,
                    Values = string.Join(" / ", row.Select(x => $"{x.Key}: {x.Value}"))
                });
            }

            foreach (var log in preview.Logs)
            {
                AddImportLog(log);
            }

            ReportProgress(1, Path.GetFileName(filePath));
            Message = "未対応のCSV形式です。内容の確認のみ可能で、DBには反映できません。";
        }
        catch (Exception ex)
        {
            ResetPreviewPlan();
            Message = $"CSVのプレビューに失敗しました: {ex.Message}";
        }
    }

    private void PreviewDividendRecords(Func<IReadOnlyList<BrokerDividendRecord>> parseRecords, string sourceLabel, string filePath)
    {
        try
        {
            var records = parseRecords();
            _previewDividends[filePath] = records;
            AddDividendPreviewRows(records, 1);

            foreach (var byTicker in records.GroupBy(x => x.Ticker).OrderBy(x => x.Key))
            {
                AddImportLog($"{byTicker.Key}: {byTicker.Count()}件 / {byTicker.Sum(x => x.NetAmountJpy):N2}円");
            }

            ReportProgress(1, Path.GetFileName(filePath));
            Message = $"{sourceLabel} を読み込みました。配当明細: {records.Count}件。";
        }
        catch (Exception ex)
        {
            ResetPreviewPlan();
            Message = $"{sourceLabel} のプレビューに失敗しました: {ex.Message}";
        }
    }

    private void PreviewBrokerStatement(Func<BrokerStatementImport> parseStatement, string sourceLabel, string filePath)
    {
        try
        {
            var statement = parseStatement();
            _previewStatements[filePath] = statement;
            AddStatementPreviewRows(statement, 1);
            foreach (var review in statement.ReviewMessages) AddImportLog($"要確認: {review}");

            AddStatementSummaryLogs(statement);
            ReportProgress(1, Path.GetFileName(filePath));
            Message = statement.CanUpdateHoldings
                ? $"{sourceLabel} を読み込みました。保有: {statement.Holdings.Count}件、配当: {statement.Dividends.Count}件、保有反映対象取引: {statement.Trades.Count}件。取込実行でまとめて反映します。"
                : $"{sourceLabel} を読み込みました。配当: {statement.Dividends.Count}件、売買/入庫履歴: {statement.Trades.Count}件。現在保有は更新せず、配当だけ登録します。";
        }
        catch (Exception ex)
        {
            ResetPreviewPlan();
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

            foreach (var filePath in filePaths.Where(filePath => GetCsvKind(filePath) == CsvKind.NomuraHoldings))
            {
                CheckCancellation();
                var statement = _nomuraHoldingsParser.Parse(filePath);
                _previewStatements[filePath] = statement;
                AddImportLog($"[{Path.GetFileName(filePath)}] 野村保有残高CSV");
                AddStatementSummaryLogs(statement);
                foreach (var review in statement.ReviewMessages) AddImportLog($"要確認: {review}");
                index = AddStatementPreviewRows(statement, index);
                handled.Add(filePath);
                statementCount++;
                ReportProgress(handled.Count, Path.GetFileName(filePath));
            }

            foreach (var filePath in filePaths.Where(filePath => GetCsvKind(filePath) == CsvKind.NomuraTransaction))
            {
                CheckCancellation();
                var statement = _nomuraTransactionParser.Parse(filePath);
                _previewStatements[filePath] = statement;
                AddImportLog($"[{Path.GetFileName(filePath)}] 野村取引履歴CSV");
                AddStatementSummaryLogs(statement);
                index = AddStatementPreviewRows(statement, index);
                handled.Add(filePath);
                statementCount++;
                ReportProgress(handled.Count, Path.GetFileName(filePath));
            }

            foreach (var filePath in filePaths.Where(filePath =>
                         !handled.Contains(filePath) && GetCsvKind(filePath) == CsvKind.NomuraDividend))
            {
                CheckCancellation();
                var statement = _nomuraDividendParser.Parse(filePath);
                _previewStatements[filePath] = statement;
                AddImportLog($"[{Path.GetFileName(filePath)}] 野村配当履歴CSV");
                AddStatementSummaryLogs(statement);
                index = AddStatementPreviewRows(statement, index);
                handled.Add(filePath);
                statementCount++;
                ReportProgress(handled.Count, Path.GetFileName(filePath));
            }

            var sbiFiles = filePaths
                .Where(filePath => !handled.Contains(filePath) && GetCsvKind(filePath) == CsvKind.SbiStatement)
                .ToList();
            if (sbiFiles.Count > 0)
            {
                CheckCancellation();
                var statement = _sbiStatementParser.ParseFiles(sbiFiles);
                _previewSbiBatch = statement;
                AddImportLog($"[SBI選択CSV {sbiFiles.Count}件] SBI一括CSV");
                AddStatementSummaryLogs(statement);
                index = AddStatementPreviewRows(statement, index);
                foreach (var filePath in sbiFiles)
                {
                    handled.Add(filePath);
                }

                statementCount += sbiFiles.Count;
                ReportProgress(handled.Count, "SBI一括CSV");
            }

            foreach (var filePath in filePaths.Where(filePath => !handled.Contains(filePath)))
            {
                CheckCancellation();
                if (GetCsvKind(filePath) == CsvKind.SbiDistribution)
                {
                    var statement = _sbiDistributionParser.ParseStatement(filePath);
                    _previewStatements[filePath] = statement;
                    AddImportLog($"[{Path.GetFileName(filePath)}] SBI配当CSV: {statement.Dividends.Count}件 / {statement.Dividends.Sum(x => x.NetAmountJpy):N2}円");
                    AddStatementSummaryLogs(statement);
                    index = AddStatementPreviewRows(statement, index);
                    handled.Add(filePath);
                    dividendOnlyCount++;
                    ReportProgress(handled.Count, Path.GetFileName(filePath));
                    continue;
                }

                AddImportLog($"[{Path.GetFileName(filePath)}] 未対応CSVのため汎用プレビュー対象です。");
                handled.Add(filePath);
                ReportProgress(handled.Count, Path.GetFileName(filePath));
            }

            var supported = statementCount + dividendOnlyCount;
            Message = $"{filePaths.Count}件のCSVを読み込みました。対応形式: {supported}件、未対応形式: {filePaths.Count - supported}件。詳細はログを確認してください。";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ResetPreviewPlan();
            Message = $"複数CSVのプレビューに失敗しました: {ex.Message}";
        }
    }

    private int AddStatementPreviewRows(BrokerStatementImport statement, int startIndex)
    {
        var index = startIndex;
        foreach (var holding in statement.Holdings.Take(20))
        {
            AddPreviewRow(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = $"保有 / {holding.Ticker} / {holding.Name} / {holding.Shares:N2}株 / 取得 {holding.AverageAcquisitionPrice:N4} {holding.Currency} / 現在 {holding.CurrentPrice:N4} {holding.Currency}"
            });
        }

        foreach (var trade in statement.Trades.OrderByDescending(x => x.SettlementDate).Take(10))
        {
            AddPreviewRow(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = $"取引 / {trade.SettlementDate:yyyy/MM/dd} / {trade.TradeType} / {trade.Ticker} / {trade.Name} / {trade.Quantity:N2}株 / {trade.UnitPrice:N4} {trade.Currency}"
            });
        }

        foreach (var dividend in statement.Dividends.Take(10))
        {
            AddPreviewRow(new CsvPreviewRowViewModel
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
            AddPreviewRow(new CsvPreviewRowViewModel
            {
                RowNumber = index++,
                Values = $"{record.PaymentDate:yyyy/MM/dd} / {record.Ticker} / {record.Name} / {record.Currency} / {record.NetAmount:N6} / {record.NetAmountJpy:N2}円"
            });
        }

        return index;
    }

    private void Import()
    {
        CheckCancellation();
        ClearImportLogs();
        _fileImportOutcomes.Clear();
        _ambiguousDividendIds.Clear();
        var filePaths = GetTargetFilePaths();
        if (filePaths.Count == 0)
        {
            Message = "CSVファイルを選択してください。";
            return;
        }

        if (_previewFileHashes.Count == 0)
        {
            Preview();
            if (Message.Contains("失敗", StringComparison.Ordinal)) return;
            ClearImportLogs();
        }
        bool previewMatches;
        try
        {
            previewMatches = _previewFileHashes.Count == filePaths.Count && filePaths.All(path =>
                _previewFileHashes.TryGetValue(path, out var hash) &&
                string.Equals(hash, ComputeFileHash(path), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Message = $"CSVの再確認に失敗しました: {ex.Message}";
            return;
        }
        if (!previewMatches)
        {
            Message = "プレビュー後にCSVの内容または選択ファイルが変わりました。再プレビューしてください。";
            return;
        }

        if (filePaths.Count > 1)
        {
            ImportMultipleFiles(filePaths);
            return;
        }

        var filePath = filePaths[0];
        if (GetCsvKind(filePath) == CsvKind.NomuraHoldings)
        {
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _nomuraHoldingsParser.Parse(filePath)), "野村保有残高CSV");
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.NomuraTransaction)
        {
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _nomuraTransactionParser.Parse(filePath)), "野村取引履歴CSV");
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.NomuraDividend)
        {
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _nomuraDividendParser.Parse(filePath)), "野村配当履歴CSV");
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.SbiStatement)
        {
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _sbiStatementParser.ParseFiles(new[] { filePath })), "SBI一括CSV");
            return;
        }

        if (GetCsvKind(filePath) == CsvKind.SbiDistribution)
        {
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _sbiDistributionParser.ParseStatement(filePath)), "SBI配当CSV");
            return;
        }

        Message = "未対応のCSV形式です。DBには反映していません。対応形式のCSVを選択してください。";
        AddImportLog($"[{Path.GetFileName(filePath)}] 未対応CSV: DB反映なし");
        _saveImportLog?.Invoke(BuildLogEntry(Guid.NewGuid().ToString("N"), filePath,
            new FileImportOutcome("未対応CSV", "Skipped", 0, 0, 0, 0, 0, false)));
    }

    private void ImportMultipleFiles(IReadOnlyList<string> filePaths)
    {
        if (_runImportAtomically is not null && !_atomicImportInProgress)
        {
            _fileImportOutcomes.Clear();
            _ambiguousDividendIds.Clear();
            RunAtomicImport(() => ImportMultipleFiles(filePaths), "選択CSV一括取込", filePaths);
            return;
        }

        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var skipped = 0;

        foreach (var filePath in filePaths.Where(filePath => GetCsvKind(filePath) == CsvKind.NomuraHoldings))
        {
            CheckCancellation();
            AddImportLog($"[{Path.GetFileName(filePath)}] 野村保有残高CSV");
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _nomuraHoldingsParser.Parse(filePath)), "野村保有残高CSV");
            handled.Add(filePath);
            RecordFileOutcome(filePath, "野村保有残高CSV");
            if (_lastImportApplied) processed++; else skipped++;
        }

        foreach (var filePath in filePaths.Where(filePath => GetCsvKind(filePath) == CsvKind.NomuraTransaction))
        {
            CheckCancellation();
            AddImportLog($"[{Path.GetFileName(filePath)}] 野村取引履歴CSV");
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _nomuraTransactionParser.Parse(filePath)), "野村取引履歴CSV");
            handled.Add(filePath);
            RecordFileOutcome(filePath, "野村取引履歴CSV");
            if (_lastImportApplied) processed++; else skipped++;
        }

        foreach (var filePath in filePaths.Where(filePath =>
                     !handled.Contains(filePath) && GetCsvKind(filePath) == CsvKind.NomuraDividend))
        {
            CheckCancellation();
            AddImportLog($"[{Path.GetFileName(filePath)}] 野村配当履歴CSV");
            ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _nomuraDividendParser.Parse(filePath)), "野村配当履歴CSV");
            handled.Add(filePath);
            RecordFileOutcome(filePath, "野村配当履歴CSV");
            if (_lastImportApplied) processed++; else skipped++;
        }

        var sbiFiles = filePaths
            .Where(filePath => !handled.Contains(filePath) && GetCsvKind(filePath) == CsvKind.SbiStatement)
            .ToList();
        if (sbiFiles.Count > 0)
        {
            CheckCancellation();
            AddImportLog($"[SBI選択CSV {sbiFiles.Count}件] SBI一括CSV");
            ImportBrokerStatement(() => _previewSbiBatch ?? _sbiStatementParser.ParseFiles(sbiFiles), "SBI一括CSV");
            foreach (var filePath in sbiFiles)
            {
                handled.Add(filePath);
                RecordFileOutcome(filePath, "SBI一括CSV", batchCounts: true);
            }

            if (_lastImportApplied) processed += sbiFiles.Count; else skipped += sbiFiles.Count;
        }

        foreach (var filePath in filePaths.Where(filePath => !handled.Contains(filePath)))
        {
            CheckCancellation();
            if (GetCsvKind(filePath) == CsvKind.SbiDistribution)
            {
                AddImportLog($"[{Path.GetFileName(filePath)}] SBI配当CSV");
                ImportBrokerStatement(() => GetPreviewStatement(filePath, () => _sbiDistributionParser.ParseStatement(filePath)), "SBI配当CSV");
                handled.Add(filePath);
                RecordFileOutcome(filePath, "SBI配当CSV");
                if (_lastImportApplied) processed++; else skipped++;
                continue;
            }

            AddImportLog($"[{Path.GetFileName(filePath)}] 未対応CSVのためDB反映しませんでした。");
            _fileImportOutcomes[filePath] = new FileImportOutcome("未対応CSV", "Skipped", 0, 0, 0, 0, 0, false);
            skipped++;
        }

        var reviewRows = CurrentImportLogs.Count(x => x.StartsWith("要確認:", StringComparison.Ordinal));
        Message = $"{filePaths.Count}件のCSVを処理しました。適用対象: {processed}件、未対応ファイル: {skipped}件、要確認ログ: {reviewRows}行。詳細はログを確認してください。";
    }

    private void ImportDividendRecords(Func<IReadOnlyList<BrokerDividendRecord>> parseRecords, string sourceLabel)
    {
        if (_runImportAtomically is not null && !_atomicImportInProgress)
        {
            RunAtomicImport(() => ImportDividendRecords(parseRecords, sourceLabel), sourceLabel);
            return;
        }

        _lastImportApplied = false;
        ResetLastImportCounts();

        if (_getPositions is null || _savePosition is null || _saveDividend is null || _getDividendPayments is null)
        {
            Message = "この画面はDB取込用に初期化されていません。";
            return;
        }

        try
        {
            var records = parseRecords();
            _lastImportRowCount = records.Count;
            if (records.Count == 0)
            {
                Message = $"{sourceLabel} に取込可能な配当明細がありません。";
                return;
            }

            var positions = _getPositions().ToList();
            var positionByKey = positions
                .GroupBy(x => BuildPositionKey(x.Stock.Broker, x.Stock.Ticker, x.Stock.AccountType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var existingDividends = new DividendPaymentIndex(_getDividendPayments());
            var reconciliationService = new DividendReconciliationService();

            var imported = 0;
            var updated = 0;
            var replaced = 0;
            var skipped = 0;
            var createdReferenceStocks = 0;
            var reviewCount = 0;

            var processedDividendRows = 0;
            foreach (var record in records)
            {
                if ((processedDividendRows++ & 127) == 0) CheckCancellation();
                var positionKey = BuildPositionKey(record.Broker, record.Ticker, record.Account);
                if (!positionByKey.TryGetValue(positionKey, out var position))
                {
                    var candidates = FindDividendPositionCandidates(positionByKey.Values, record);
                    if (string.IsNullOrWhiteSpace(record.Account) && candidates.Count > 1)
                    {
                        skipped++;
                        reviewCount++;
                        AddImportLog($"要確認: {record.Ticker} の配当は口座区分がなく既存保有が複数あります。");
                        continue;
                    }
                    if (candidates.Count == 1) position = candidates[0];
                }
                if (position is null)
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
                if (string.IsNullOrWhiteSpace(record.Account))
                {
                    payment.AccountType = position.Stock.AccountType;
                    payment.TaxAccountType = position.Stock.AccountType;
                }
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
                    case DividendImportAction.CreatedNeedsReview:
                        imported++;
                        reviewCount++;
                        break;
                    case DividendImportAction.NeedsReview:
                        skipped++;
                        reviewCount++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }

            if (!_atomicImportInProgress) _afterImport?.Invoke();
            foreach (var byTicker in records.GroupBy(x => x.Ticker).OrderBy(x => x.Key))
            {
                AddImportLog($"{byTicker.Key}: {byTicker.Count()}件 / {byTicker.Sum(x => x.NetAmountJpy):N2}円");
            }

            AddImportLog($"配当専用仮銘柄: {createdReferenceStocks}件（銘柄一覧の「現在保有のみ」には表示しません）");
            Message = $"{sourceLabel} を取り込みました。配当登録: {imported}件、既存実績更新: {updated}件、予定置換: {replaced}件、スキップ: {skipped}件、配当専用仮銘柄: {createdReferenceStocks}件。";
            _lastImportInsertedCount = imported + createdReferenceStocks;
            _lastImportUpdatedCount = updated;
            _lastImportSkippedCount = skipped;
            _lastImportConflictCount = reviewCount;
            _lastImportApplied = true;
        }
        catch (Exception ex)
        {
            if (_atomicImportInProgress) throw;
            Message = $"{sourceLabel} の取込に失敗しました: {ex.Message}";
        }
    }

    private void ImportBrokerStatement(Func<BrokerStatementImport> parseStatement, string sourceLabel)
    {
        if (_runImportAtomically is not null && !_atomicImportInProgress)
        {
            RunAtomicImport(() => ImportBrokerStatement(parseStatement, sourceLabel), sourceLabel);
            return;
        }

        _lastImportApplied = false;
        ResetLastImportCounts();

        if (_getPositions is null || _savePosition is null || _saveDividend is null || _getDividendPayments is null)
        {
            Message = "この画面はDB取込用に初期化されていません。";
            return;
        }

        try
        {
            var statement = parseStatement();
            _lastImportedStatement = statement;
            _lastImportRowCount = statement.SourceRowAudits.Count > 0
                ? statement.SourceRowAudits.Values.Sum(x => x.InputRows)
                : statement.Holdings.Count + statement.UnresolvedHoldings.Count +
                  statement.Trades.Count + statement.Dividends.Count + statement.IgnoredRowCount;
            foreach (var review in statement.ReviewMessages) AddImportLog($"要確認: {review}");
            var mergePolicy = _getMergePolicy?.Invoke();
            if (mergePolicy == "すべてプレビューして手動選択")
            {
                Message = "手動選択方針のため、プレビューのみ行いDBへ反映していません。";
                AddImportLog($"{sourceLabel}: 手動選択が必要です。");
                return;
            }
            if (statement.Holdings.Count == 0 && statement.CompleteHoldingScopes.Count == 0 &&
                statement.Trades.Count == 0 &&
                statement.Dividends.Count == 0 && statement.UnresolvedHoldings.Count == 0 &&
                statement.UnresolvedDividends.Count == 0 &&
                statement.UnresolvedTrades.Count == 0)
            {
                Message = $"{sourceLabel} に取込可能な保有・配当データがありません。";
                return;
            }

            foreach (var unresolved in statement.UnresolvedHoldings)
            {
                const string reason = "外国株式のティッカーを確定できません。";
                _saveReviewHolding?.Invoke(unresolved, reason);
                AddImportLog($"要確認: {unresolved.Name} / {reason}");
            }
            foreach (var unresolved in statement.UnresolvedDividends)
            {
                _saveReviewDividend?.Invoke(unresolved);
                AddImportLog($"要確認: {unresolved.PaymentDate:yyyy/MM/dd} {unresolved.Name} / {unresolved.Reason}");
            }
            foreach (var unresolved in statement.UnresolvedTrades)
            {
                const string reason = "銘柄コードがなく、名称からティッカーを確定できません。";
                _saveReviewTrade?.Invoke(unresolved, reason);
                AddImportLog($"要確認: {unresolved.TradeDate:yyyy/MM/dd} {unresolved.Name} / {reason}");
            }

            var positions = _getPositions().ToList();
            var originalPositionIds = positions.Select(x => x.Stock.Id).ToHashSet();
            var tradesToPersist = statement.CanUpdateHoldings
                ? statement.Trades.ToList() : new List<BrokerTradeRecord>();
            var positionByKey = positions
                .GroupBy(x => BuildPositionKey(x.Stock.Broker, x.Stock.Ticker, x.Stock.AccountType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var dividendsByKey = statement.Dividends
                .GroupBy(x => BuildPositionKey(x.Broker, x.Ticker, x.Account), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<BrokerDividendRecord>)x.ToList(), StringComparer.OrdinalIgnoreCase);

            var createdStocks = 0;
            var updatedStocks = 0;
            var unchangedStocks = 0;
            var reviewHoldings = 0;
            var skippedTradeGroups = 0;
            var createdReferenceStocks = 0;
            var holdingKeys = statement.Holdings
                .Select(x => BuildPositionKey(x.Broker, x.Ticker, x.Account))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (statement.Holdings.Count > 0 || statement.CompleteHoldingScopes.Count > 0)
            {
                var mergeResult = _holdingMergeService.MergeHoldings(positions, statement.Holdings, DateTime.Now,
                    mergePolicy, statement.CompleteHoldingScopes);
                foreach (var decision in mergeResult.Decisions)
                {
                    var positionKey = BuildPositionKey(decision.Source.Broker, decision.Source.Ticker, decision.Source.Account);
                    if (decision.Merged?.CurrentHolding.CurrentShares == 0m &&
                        decision.Source.Shares == 0m && decision.Existing is not null)
                        holdingKeys.Add(positionKey);
                    if (decision.Merged is null)
                    {
                        reviewHoldings++;
                        _saveReviewHolding?.Invoke(decision.Source, decision.Reason);
                        AddImportLog($"要確認: {decision.Source.Broker} {decision.Source.Ticker} {decision.Source.Name} / {decision.Reason}");
                        continue;
                    }

                    if (decision.Action == BrokerMergeAction.Overwrite && decision.ChangedFields.Count == 0 &&
                        decision.Existing is not null &&
                        decision.Merged.CurrentHolding.UpdatedAt == decision.Existing.CurrentHolding.UpdatedAt)
                    {
                        unchangedStocks++;
                        continue;
                    }

                    if (decision.Existing is not null && decision.Existing.Stock.Id > 0)
                    {
                        decision.Merged.Stock.Id = decision.Existing.Stock.Id;
                    }
                    decision.Merged.Stock.Id = _savePosition(decision.Merged);
                    positionByKey[positionKey] = decision.Merged;
                    if (decision.Reason.StartsWith("確認済みの完全な口座残高スナップショット", StringComparison.Ordinal))
                        AddImportLog($"残高0反映: {decision.Source.Broker} {decision.Source.Account} {decision.Source.Ticker} / {decision.Source.SnapshotDate:yyyy/MM/dd}");
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
                    positionByKey.TryGetValue(tradeGroup.Key, out var positionForHistory);
                    var fullHistory = positionForHistory is null || _getBrokerTrades is null
                        ? tradeGroup.ToList()
                        : TradeHistoryService.Merge(GetAccountTrades(positionForHistory.Stock.Id, tradeGroup.First()), tradeGroup,
                            positionForHistory.Stock.Ticker);
                    if (TradeReconstructionService.Reconstruct(fullHistory).IncompleteSellCount > 0)
                    {
                        const string reason = "開始残高が不明な売却を含むため数量を確定できません。";
                        skippedTradeGroups++;
                        tradesToPersist.RemoveAll(x => tradeGroup.Contains(x));
                        foreach (var trade in tradeGroup) _saveReviewTrade?.Invoke(trade, reason);
                        AddImportLog($"要確認: {tradeGroup.Key} / {reason}");
                        continue;
                    }

                    if (mergePolicy is "新規銘柄だけ追加" or "手入力を保護して不足分だけ補完" or
                        "現在株数・現在株価だけ証券会社データで更新" &&
                        positionByKey.TryGetValue(tradeGroup.Key, out var existingForPolicy) &&
                        originalPositionIds.Contains(existingForPolicy.Stock.Id))
                    {
                        skippedTradeGroups++;
                        tradesToPersist.RemoveAll(x => tradeGroup.Contains(x));
                        continue;
                    }

                    if (IsMutualFundTradeGroup(tradeGroup))
                    {
                        if (positionByKey.TryGetValue(tradeGroup.Key, out var fundPosition) &&
                            fundPosition.CurrentHolding.CurrentShares > 0m)
                        {
                            skippedTradeGroups++;
                            continue;
                        }

                        var reconstructedFund = CreateFundPositionFromTrades(fullHistory);
                        if (reconstructedFund is null)
                        {
                            skippedTradeGroups++;
                            AddImportLog($"要確認: 投信約定 {tradeGroup.Key} の数量を確定できません。");
                            continue;
                        }

                        if (fundPosition is not null)
                        {
                            reconstructedFund.Stock.Id = fundPosition.Stock.Id;
                            reconstructedFund.Purchase.Id = fundPosition.Purchase.Id;
                            reconstructedFund.Split.Id = fundPosition.Split.Id;
                            reconstructedFund.CurrentHolding.Id = fundPosition.CurrentHolding.Id;
                            reconstructedFund.MutualFund.Id = fundPosition.MutualFund.Id;
                        }

                        reconstructedFund.Stock.Id = _savePosition(reconstructedFund);
                        positionByKey[tradeGroup.Key] = reconstructedFund;
                        if (fundPosition is null) createdStocks++; else updatedStocks++;
                        continue;
                    }

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
                        var completeHistory = _getBrokerTrades is null
                            ? tradeGroup.ToList()
                            : TradeHistoryService.Merge(GetAccountTrades(position.Stock.Id, tradeGroup.First()), tradeGroup, position.Stock.Ticker);
                        ApplyTradeSummary(position, completeHistory, relatedDividends);
                        position.Stock.Id = _savePosition(position);
                        updatedStocks++;
                    }
                }
            }

            var existingDividends = new DividendPaymentIndex(_getDividendPayments());
            var reconciliationService = new DividendReconciliationService();
            var importedDividends = 0;
            var updatedDividends = 0;
            var replacedDividends = 0;
            var skippedDividends = 0;
            var reviewDividends = 0;

            var processedDividendRows = 0;
            foreach (var record in statement.Dividends)
            {
                if ((processedDividendRows++ & 127) == 0) CheckCancellation();
                var positionKey = BuildPositionKey(record.Broker, record.Ticker, record.Account);
                if (!positionByKey.TryGetValue(positionKey, out var position))
                {
                    var candidates = FindDividendPositionCandidates(positionByKey.Values, record);
                    if (string.IsNullOrWhiteSpace(record.Account) && candidates.Count > 1)
                    {
                        skippedDividends++;
                        AddImportLog($"要確認: {record.Ticker} の配当は口座区分がなく既存保有が複数あります。");
                        continue;
                    }
                    if (candidates.Count == 1) position = candidates[0];
                }
                if (position is null)
                {
                    var relatedRecords = dividendsByKey.TryGetValue(positionKey, out var group)
                        ? group
                        : new[] { record };
                    position = CreateDividendOnlyPosition(record, relatedRecords, useDividendQuantityAsHolding: false);
                    position.Stock.Id = _savePosition(position);
                    positionByKey[positionKey] = position;
                    createdReferenceStocks++;
                }
                // Dividend payment currency and FX must never change the security holding.

                var payment = CreateDividendPayment(position.Stock.Id, record);
                if (string.IsNullOrWhiteSpace(record.Account))
                {
                    payment.AccountType = position.Stock.AccountType;
                    payment.TaxAccountType = position.Stock.AccountType;
                }
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
                    case DividendImportAction.CreatedNeedsReview:
                        importedDividends++;
                        reviewDividends++;
                        break;
                    case DividendImportAction.NeedsReview:
                        skippedDividends++;
                        reviewDividends++;
                        break;
                    default:
                        skippedDividends++;
                        break;
                }
            }

            _saveBrokerTrades?.Invoke(tradesToPersist);
            if (!_atomicImportInProgress) _afterImport?.Invoke();
            AddStatementSummaryLogs(statement);
            AddImportLog($"保有反映: 新規 {createdStocks}件 / 更新 {updatedStocks}件 / 変更なし {unchangedStocks}件 / 要確認 {reviewHoldings}件 / 取引復元見送り {skippedTradeGroups}件");
            AddImportLog($"配当専用仮銘柄: {createdReferenceStocks}件（銘柄一覧の「現在保有のみ」には表示しません）");
            AddImportLog($"配当登録: {importedDividends}件 / 既存実績更新 {updatedDividends}件 / 予定置換 {replacedDividends}件 / スキップ {skippedDividends}件 / 要確認 {reviewDividends}件");

            Message = statement.CanUpdateHoldings
                ? BuildImportResultMessage(sourceLabel, createdStocks, updatedStocks, unchangedStocks, reviewHoldings + reviewDividends, importedDividends, updatedDividends, replacedDividends, skippedDividends)
                : $"{sourceLabel} を取り込みました。現在保有は更新せず、配当登録: {importedDividends}件、既存実績更新: {updatedDividends}件、予定置換: {replacedDividends}件。";
            _lastImportInsertedCount = createdStocks + createdReferenceStocks + importedDividends;
            _lastImportUpdatedCount = updatedStocks + updatedDividends;
            _lastImportSkippedCount = unchangedStocks + skippedDividends + skippedTradeGroups;
            _lastImportConflictCount = reviewHoldings + reviewDividends + statement.UnresolvedHoldings.Count +
                                       statement.UnresolvedDividends.Count + statement.UnresolvedTrades.Count;
            _lastImportApplied = true;
        }
        catch (Exception ex)
        {
            if (_atomicImportInProgress) throw;
            Message = $"{sourceLabel} の一括取込に失敗しました: {ex.Message}";
        }
    }

    private void RunAtomicImport(Action apply, string sourceLabel, IReadOnlyList<string>? selectedFiles = null)
    {
        var batchId = Guid.NewGuid().ToString("N");
        try
        {
            _runImportAtomically!(() =>
            {
                _atomicImportInProgress = true;
                try
                {
                    apply();
                    CheckCancellation();
                    WriteSuccessLogs(batchId, sourceLabel);
                    CheckCancellation();
                }
                finally { _atomicImportInProgress = false; }
            });
        }
        catch (Exception ex)
        {
            var canceled = ex is OperationCanceledException;
            try { WriteFailureLogs(batchId, sourceLabel, ex.Message, selectedFiles, canceled ? "Canceled" : "Failed"); }
            catch (Exception logEx) { AddImportLog($"失敗記録の保存に失敗しました: {logEx.Message}"); }
            AddImportLog(canceled ? $"{sourceLabel}: 取込を中止し、変更を元に戻しました。" :
                $"{sourceLabel}: ロールバックしました: {ex.Message}");
            Message = canceled ? $"{sourceLabel} の取込を中止しました。変更は保存されていません。" :
                $"{sourceLabel} の取込に失敗しました。変更は保存されていません: {ex.Message}";
            return;
        }

        if (_backgroundOperation)
        {
            _refreshAfterImport = true;
            return;
        }
        try
        {
            _afterImport?.Invoke();
        }
        catch (Exception ex)
        {
            AddImportLog($"{sourceLabel}: DB保存済み。画面更新に失敗しました: {ex.Message}");
            Message = $"{sourceLabel} のDB保存は完了しました。画面更新に失敗しました: {ex.Message}";
        }
    }

    private void ResetLastImportCounts()
    {
        _lastImportRowCount = 0;
        _lastImportInsertedCount = 0;
        _lastImportUpdatedCount = 0;
        _lastImportSkippedCount = 0;
        _lastImportConflictCount = 0;
        _lastImportedStatement = null;
    }

    private void RecordFileOutcome(string filePath, string csvType, bool batchCounts = false)
    {
        var audit = _lastImportedStatement?.SourceRowAudits.TryGetValue(filePath, out var sourceAudit) == true
            ? sourceAudit : null;
        var rowCount = audit?.InputRows ?? (batchCounts && _lastImportedStatement is not null &&
                       _lastImportedStatement.SourceRowCounts.TryGetValue(filePath, out var count)
            ? count : batchCounts ? 0 : _lastImportRowCount);
        _fileImportOutcomes[filePath] = new FileImportOutcome(
            csvType,
            !_lastImportApplied ? "Skipped" : batchCounts ? audit?.PendingRows > 0 ? "PartialBatch" : "SucceededBatch" :
                _lastImportConflictCount > 0 || audit?.PendingRows > 0 ? "Partial" : "Succeeded",
            rowCount,
            batchCounts ? 0 : _lastImportInsertedCount,
            batchCounts ? 0 : _lastImportUpdatedCount,
            batchCounts ? 0 : _lastImportSkippedCount,
            batchCounts ? 0 : _lastImportConflictCount,
            batchCounts, audit);
        ReportProgress(_fileImportOutcomes.Count, Path.GetFileName(filePath));
    }

    private void WriteSuccessLogs(string batchId, string sourceLabel)
    {
        if (_saveImportLog is null) return;
        if (_fileImportOutcomes.Count == 0)
        {
            var selected = GetTargetFilePaths();
            if (selected.Count == 1) RecordFileOutcome(selected[0], sourceLabel);
        }
        foreach (var (filePath, outcome) in _fileImportOutcomes)
        {
            _saveImportLog(BuildLogEntry(batchId, filePath, outcome));
        }
    }

    private void WriteFailureLogs(string batchId, string sourceLabel, string error,
        IReadOnlyList<string>? selectedFiles, string result = "Failed")
    {
        if (_saveImportLog is null) return;
        var selected = selectedFiles ?? GetTargetFilePaths();
        var paths = selected.Count > 0 ? selected : _fileImportOutcomes.Keys.ToList();
        foreach (var filePath in paths)
        {
            var outcome = _fileImportOutcomes.TryGetValue(filePath, out var recorded)
                ? recorded : new FileImportOutcome(sourceLabel, "Failed", 0, 0, 0, 0, 0, false);
            _saveImportLog(BuildLogEntry(batchId, filePath, outcome, result, error));
        }
    }

    private CsvImportLogEntry BuildLogEntry(
        string batchId, string filePath, FileImportOutcome outcome,
        string? overrideResult = null, string? error = null)
    {
        var csvType = outcome.CsvType;
        var note = outcome.Result == "Skipped"
            ? "未対応形式、取込対象明細なし、または手動選択方針のためDB適用なし。"
            : outcome.BatchCounts
            ? "一括適用のため追加・更新件数はファイル別に配分していません。"
            : "追加・更新件数は保有と配当を対象とし、再構築した取引台帳の行数は含みません。";
        if (outcome.Audit is { } audit)
            note += $" 入力明細 {audit.InputRows}行 = 解析 {audit.ParsedRows}行 + 要確認 {audit.PendingRows}行 + 対象外 {audit.ExcludedRows}行。";
        return new CsvImportLogEntry
        {
            BatchId = batchId,
            FileName = Path.GetFileName(filePath),
            FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
            FileHash = _previewFileHashes.TryGetValue(filePath, out var hash) ? hash :
                File.Exists(filePath) ? ComputeFileHash(filePath) : string.Empty,
            Broker = csvType.StartsWith("野村", StringComparison.Ordinal) ? "野村証券" :
                csvType.StartsWith("SBI", StringComparison.Ordinal) ? "SBI証券" : string.Empty,
            CsvType = csvType,
            RowCount = outcome.RowCount,
            ParsedRowCount = outcome.Audit?.ParsedRows ?? 0,
            PendingRowCount = outcome.Audit?.PendingRows ?? 0,
            ExcludedRowCount = outcome.Audit?.ExcludedRows ?? 0,
            InsertedCount = outcome.InsertedCount,
            UpdatedCount = outcome.UpdatedCount,
            SkippedCount = outcome.SkippedCount,
            ConflictCount = outcome.ConflictCount,
            Result = overrideResult ?? outcome.Result,
            ErrorSummary = error ?? note
        };
    }

    private sealed record FileImportOutcome(
        string CsvType, string Result, int RowCount, int InsertedCount, int UpdatedCount,
        int SkippedCount, int ConflictCount, bool BatchCounts, CsvSourceRowAudit? Audit = null);

    private void AddStatementSummaryLogs(BrokerStatementImport statement)
    {
        foreach (var (file, audit) in statement.SourceRowAudits.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddImportLog($"[{Path.GetFileName(file)}] 入力明細 {audit.InputRows}行 = 解析 {audit.ParsedRows}行 + 要確認 {audit.PendingRows}行 + 対象外 {audit.ExcludedRows}行");
            foreach (var issue in audit.Issues)
                AddImportLog($"[{Path.GetFileName(file)}:{issue.RowNumber}] {issue.Category}: {issue.Reason}");
        }
        if (statement.Holdings.Count > 0)
        {
            AddImportLog($"保有残高: {statement.Holdings.Count}件");
            foreach (var bySource in statement.Holdings.GroupBy(x => x.Source).OrderBy(x => x.Key))
            {
                AddImportLog($"{bySource.Key}: 保有 {bySource.Count()}件 / 評価額 {bySource.Sum(x => x.MarketValueJpy):N0}円");
            }
        }

        AddImportLog($"配当明細: {statement.Dividends.Count}件 / {statement.Dividends.Sum(x => x.NetAmountJpy):N2}円");
        foreach (var byType in statement.Trades.GroupBy(x => x.TradeType).OrderBy(x => x.Key))
        {
            AddImportLog($"{byType.Key}: {byType.Count()}件");
        }

        foreach (var byTicker in statement.Dividends.GroupBy(x => x.Ticker).OrderBy(x => x.Key))
        {
            AddImportLog($"{byTicker.Key}: 配当 {byTicker.Count()}件 / {byTicker.Sum(x => x.NetAmountJpy):N2}円");
        }

        var otherIgnored = Math.Max(0, statement.IgnoredRowCount - statement.UnresolvedDividends.Count - statement.UnresolvedTrades.Count);
        if (otherIgnored > 0)
        {
            AddImportLog($"投資成績計算外として読み飛ばし: {otherIgnored}行");
        }
        if (statement.UnresolvedDividends.Count > 0)
        {
            AddImportLog($"銘柄未確定の配当: {statement.UnresolvedDividends.Count}行（要確認一覧に保存）");
        }
        if (statement.UnresolvedHoldings.Count > 0)
        {
            AddImportLog($"銘柄未確定の保有: {statement.UnresolvedHoldings.Count}行（要確認一覧に保存）");
        }
        if (statement.UnresolvedTrades.Count > 0)
        {
            AddImportLog($"銘柄未確定の取引: {statement.UnresolvedTrades.Count}行（要確認一覧に保存）");
        }
    }

    private static string BuildImportResultMessage(
        string sourceLabel,
        int createdStocks,
        int updatedStocks,
        int unchangedStocks,
        int reviewHoldings,
        int importedDividends,
        int updatedDividends,
        int replacedDividends,
        int skippedDividends)
    {
        if (createdStocks == 0 &&
            updatedStocks == 0 &&
            reviewHoldings == 0 &&
            importedDividends == 0 &&
            updatedDividends == 0 &&
            replacedDividends == 0)
        {
            return $"{sourceLabel} を再取込しました。新規追加0件、更新0件、重複スキップ{skippedDividends}件、変更なし{unchangedStocks}件。資産合計に変更はありません。";
        }

        return $"{sourceLabel} を一括取込しました。保有反映: 新規 {createdStocks}件、更新 {updatedStocks}件、変更なし {unchangedStocks}件、要確認 {reviewHoldings}件。配当登録: {importedDividends}件、既存実績更新: {updatedDividends}件、予定置換: {replacedDividends}件、重複スキップ: {skippedDividends}件。";
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

        var exchangeRate = ResolveExchangeRate(currency, trades, relatedDividends, 0m);
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

    private static bool IsMutualFundTradeGroup(IEnumerable<BrokerTradeRecord> trades) =>
        trades.Any(IsMutualFundTrade);

    private static bool IsMutualFundTrade(BrokerTradeRecord trade)
    {
        return trade.Ticker.StartsWith("FUND:", StringComparison.OrdinalIgnoreCase) ||
               trade.Product.Contains("投資信託", StringComparison.Ordinal) ||
               trade.Product.Contains("FUND", StringComparison.OrdinalIgnoreCase);
    }

    private static StockPosition? CreateFundPositionFromTrades(IReadOnlyList<BrokerTradeRecord> trades)
    {
        if (trades.Count == 0) return null;
        var reconstruction = TradeReconstructionService.Reconstruct(trades);
        if (reconstruction.IncompleteSellCount > 0) return null;
        var latest = trades.OrderByDescending(x => x.TradeDate).First();
        var units = reconstruction.CurrentShares;
        var averageNav = reconstruction.AverageAcquisitionPrice * 10000m;
        var acquisitionAmount = units * reconstruction.AverageAcquisitionPrice;
        return new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund, Broker = latest.Broker,
                AccountType = AccountTypeNormalizer.NormalizeForMutualFund(latest.Account, latest.Account),
                CustodyType = latest.Account, Ticker = latest.Ticker, Name = latest.Name,
                Currency = "JPY", Country = "日本", DataSource = latest.Source,
                Sector = "投資信託", Memo = "積立約定から再構築。基準価額・評価額は未取得。"
            },
            Purchase = new Purchase
            {
                PurchaseDate = trades.Min(x => x.TradeDate), Shares = units, UnitPrice = averageNav,
                ExchangeRate = 1m, ExchangeRateInputType = "CSV", ExchangeRateSource = latest.Source
            },
            Split = new StockSplit { SplitDate = latest.TradeDate, SplitRatio = 1m },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = units, CurrentPrice = 0m, CurrentExchangeRate = 1m,
                UpdatedAt = latest.TradeDate
            },
            MutualFund = new MutualFundHolding
            {
                FundName = latest.Name, FundCode = latest.Ticker, UnitsHeld = units,
                UnitBase = 10000m, AverageCostNav = averageNav,
                AcquisitionAmount = acquisitionAmount, TotalPurchaseAmount = acquisitionAmount,
                AccountType = latest.Account
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
        var exchangeRate = currency == "JPY" ? 1m :
            record.Currency == "USD" && record.ExchangeRate > 0m ? record.ExchangeRate : 0m;
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

        return fallbackRate;
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
        var covered = records
            .Where(x => x.PaymentDate >= from && x.PaymentDate <= latestDate && x.GrossAmount > 0m)
            .ToList();
        if (covered.Count < 2 ||
            (latestDate - covered.Min(x => x.PaymentDate)).TotalDays < 330 ||
            covered.Where(x => x.Quantity > 0m).Select(x => x.Quantity).Distinct().Count() > 1)
        {
            return 0m;
        }
        var annualGross = covered.Sum(x => x.GrossAmount);

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
        DividendPaymentIndex existingDividends,
        DividendPayment payment,
        DividendReconciliationService reconciliationService)
    {
        if (_saveDividend is null)
        {
            return DividendImportAction.Skipped;
        }

        if (payment.SourceRowNumber > 0 && !string.IsNullOrWhiteSpace(payment.SourceFile))
        {
            var previousRow = existingDividends.FindSourceRow(payment);
            if (previousRow is not null && !SameImportedDividendValues(previousRow, payment))
            {
                var reason = $"同じCSV明細行の内容が既存配当ID {previousRow.Id} と異なります（既存受取 {previousRow.NetAmountJpy:N2}円、CSV受取 {payment.NetAmountJpy:N2}円）。元データを残して改訂候補として確認してください。";
                _saveReviewDividend?.Invoke(new BrokerUnresolvedDividend
                {
                    PaymentDate = payment.PaymentDate,
                    Broker = payment.Broker,
                    Name = string.IsNullOrWhiteSpace(payment.StockName) ? payment.Ticker : payment.StockName,
                    Product = payment.Source,
                    Quantity = payment.Quantity,
                    GrossAmount = payment.GrossAmount,
                    TaxAmount = payment.TaxAmount,
                    Source = $"{payment.SourceFile}:{payment.SourceRowNumber}",
                    Reason = reason
                });
                AddImportLog($"要確認: {payment.PaymentDate:yyyy/MM/dd} {payment.Ticker} / {reason}");
                return DividendImportAction.NeedsReview;
            }
        }

        var decision = reconciliationService.ReconcileActual(existingDividends.Candidates(payment), payment);
        if (decision.DuplicateActual is not null)
        {
            // Keep the CSV row with traceable provenance when an older import is replayed.
            if (decision.DuplicateActual.SourceRowNumber > 0 && payment.SourceRowNumber == 0)
                return DividendImportAction.Skipped;

            if (payment.SourceRowNumber > 0 && decision.DuplicateActual.SourceRowNumber > 0 &&
                !string.Equals(payment.SourceFile, decision.DuplicateActual.SourceFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!_ambiguousDividendIds.Add(decision.DuplicateActual.Id))
                    return DividendImportAction.Skipped;
                var reason = $"別CSVの同額・同日配当（既存ID {decision.DuplicateActual.Id}）と区別できません。元明細を確認してください。";
                _saveReviewDividend?.Invoke(new BrokerUnresolvedDividend
                {
                    PaymentDate = payment.PaymentDate, Broker = payment.Broker,
                    Name = string.IsNullOrWhiteSpace(payment.StockName) ? payment.Ticker : payment.StockName,
                    Product = payment.Source, Quantity = payment.Quantity,
                    GrossAmount = payment.GrossAmount, TaxAmount = payment.TaxAmount,
                    Source = payment.ExchangeRateSource, Reason = reason
                });
                AddImportLog($"要確認: {payment.PaymentDate:yyyy/MM/dd} {payment.Ticker} / {reason}");
                return DividendImportAction.NeedsReview;
            }
            if (SameImportedDividendValues(decision.DuplicateActual, payment) &&
                decision.DuplicateActual.StockId == payment.StockId &&
                decision.DuplicateActual.SourceRowNumber == payment.SourceRowNumber &&
                string.Equals(decision.DuplicateActual.SourceFile, payment.SourceFile,
                    StringComparison.OrdinalIgnoreCase))
                return DividendImportAction.Skipped;
            payment.Id = decision.DuplicateActual.Id;
            payment.CreatedAt = decision.DuplicateActual.CreatedAt;
            payment.UpdatedAt = DateTime.Now;
            var updatedId = _saveDividend(payment);
            payment.Id = updatedId;
            existingDividends.Replace(payment);
            return DividendImportAction.UpdatedExistingActual;
        }

        var newId = _saveDividend(payment);
        payment.Id = newId;
        existingDividends.Add(payment);

        if (decision.ReviewCandidates.Count > 0)
        {
            var candidateIds = string.Join(", ", decision.ReviewCandidates.Select(x => x.Id));
            var reason = $"予定・手入力の配当候補ID {candidateIds} と近似一致します。CSV実績ID {newId} は登録済みです。候補の置換は明細を確認して判断してください。";
            _saveReviewDividend?.Invoke(new BrokerUnresolvedDividend
            {
                PaymentDate = payment.PaymentDate,
                Broker = payment.Broker,
                Name = string.IsNullOrWhiteSpace(payment.StockName) ? payment.Ticker : payment.StockName,
                Product = payment.Source,
                Quantity = payment.Quantity,
                GrossAmount = payment.GrossAmount,
                TaxAmount = payment.TaxAmount,
                Source = $"{payment.SourceFile}:{payment.SourceRowNumber}",
                Reason = reason
            });
            AddImportLog($"要確認: {payment.PaymentDate:yyyy/MM/dd} {payment.Ticker} / {reason}");
            return DividendImportAction.CreatedNeedsReview;
        }

        foreach (var target in decision.ReplaceTargets)
        {
            target.DividendStatus = DividendConstants.Replaced;
            target.ReplacedByDividendId = newId;
            target.MatchedActualDividendId = newId;
            target.UpdatedAt = DateTime.Now;
            _saveDividend(target);
            existingDividends.Replace(target);
        }

        return decision.ReplaceTargets.Count > 0
            ? DividendImportAction.ReplacedSchedule
            : DividendImportAction.Created;
    }

    private static bool SameImportedDividendValues(DividendPayment left, DividendPayment right) =>
        left.PaymentDate.Date == right.PaymentDate.Date &&
        left.Quantity == right.Quantity &&
        left.GrossAmount == right.GrossAmount &&
        left.TaxAmount == right.TaxAmount &&
        left.NetAmount == right.NetAmount &&
        left.IsNetOnly == right.IsNetOnly &&
        string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase) &&
        left.ExchangeRate == right.ExchangeRate &&
        left.NetAmountJpy == right.NetAmountJpy &&
        left.GrossAmountJpy == right.GrossAmountJpy;

    private sealed class DividendPaymentIndex
    {
        private readonly List<DividendPayment> _payments;
        private readonly Dictionary<(int StockId, int Day), List<DividendPayment>> _byDate = new();
        private readonly Dictionary<(string Ticker, int Day), List<DividendPayment>> _byTickerDate = new();
        private readonly Dictionary<(string Ticker, string Broker, string File, int Row), DividendPayment> _bySourceRow = new();
        private readonly Dictionary<int, int> _positionById = new();

        public DividendPaymentIndex(IReadOnlyList<DividendPayment> payments)
        {
            _payments = new List<DividendPayment>(payments.Count);
            foreach (var payment in payments) Add(payment);
        }

        public IReadOnlyList<DividendPayment> Candidates(DividendPayment payment)
        {
            var day = DateOnly.FromDateTime(payment.PaymentDate).DayNumber;
            var candidates = new List<DividendPayment>();
            for (var offset = -10; offset <= 10; offset++)
            {
                if (_byDate.TryGetValue((payment.StockId, day + offset), out var bucket))
                    candidates.AddRange(bucket);
                if (_byTickerDate.TryGetValue((TickerKey(payment), day + offset), out var tickerBucket))
                    candidates.AddRange(tickerBucket);
            }

            candidates = candidates.DistinctBy(x => x.Id).ToList();
            candidates.Sort((left, right) => _positionById[left.Id].CompareTo(_positionById[right.Id]));
            return candidates;
        }

        public DividendPayment? FindSourceRow(DividendPayment payment) =>
            _bySourceRow.TryGetValue(SourceKey(payment), out var found) ? found : null;

        public void Add(DividendPayment payment)
        {
            if (payment.Id > 0) _positionById[payment.Id] = _payments.Count;
            _payments.Add(payment);
            Index(payment);
        }

        public void Replace(DividendPayment payment)
        {
            if (!_positionById.TryGetValue(payment.Id, out var position))
            {
                Add(payment);
                return;
            }

            var previous = _payments[position];
            Unindex(previous);
            _payments[position] = payment;
            Index(payment);
        }

        private void Index(DividendPayment payment)
        {
            var key = (payment.StockId, DateOnly.FromDateTime(payment.PaymentDate).DayNumber);
            if (!_byDate.TryGetValue(key, out var bucket))
                _byDate[key] = bucket = new List<DividendPayment>();
            bucket.Add(payment);
            var tickerKey = (TickerKey(payment), key.Item2);
            if (!_byTickerDate.TryGetValue(tickerKey, out var tickerBucket))
                _byTickerDate[tickerKey] = tickerBucket = new List<DividendPayment>();
            tickerBucket.Add(payment);
            if (payment.SourceRowNumber > 0 && !string.IsNullOrWhiteSpace(payment.SourceFile))
                _bySourceRow.TryAdd(SourceKey(payment), payment);
        }

        private void Unindex(DividendPayment payment)
        {
            var key = (payment.StockId, DateOnly.FromDateTime(payment.PaymentDate).DayNumber);
            if (_byDate.TryGetValue(key, out var bucket))
            {
                bucket.Remove(payment);
                if (bucket.Count == 0) _byDate.Remove(key);
            }
            var tickerKey = (TickerKey(payment), key.Item2);
            if (_byTickerDate.TryGetValue(tickerKey, out var tickerBucket))
            {
                tickerBucket.Remove(payment);
                if (tickerBucket.Count == 0) _byTickerDate.Remove(tickerKey);
            }
            if (payment.SourceRowNumber > 0 && !string.IsNullOrWhiteSpace(payment.SourceFile))
                _bySourceRow.Remove(SourceKey(payment));
        }

        private static string TickerKey(DividendPayment payment) =>
            string.IsNullOrWhiteSpace(payment.Ticker)
                ? $"STOCK:{payment.StockId}"
                : SecuritySymbolNormalizer.NormalizeTicker(payment.Ticker);

        private static (string, string, string, int) SourceKey(DividendPayment payment) =>
            (TickerKey(payment), SecuritySymbolNormalizer.NormalizeBroker(payment.Broker).ToUpperInvariant(),
                payment.SourceFile.ToUpperInvariant(),
                payment.SourceRowNumber);
    }

    private static DividendPayment CreateDividendPayment(int stockId, BrokerDividendRecord record)
    {
        var currency = NormalizeCurrency(record.Currency);
        var exchangeRate = currency == "JPY"
            ? 1m
            : record.ExchangeRate > 0m ? record.ExchangeRate : 0m;
        var accountType = DividendConstants.NormalizeAccountType(record.Account);
        var netOnly = record.GrossAmount <= 0m && record.TaxAmount <= 0m;
        var grossAmount = netOnly ? 0m : record.GrossAmount > 0m
            ? record.GrossAmount : record.NetAmount + record.TaxAmount;
        var netAmount = record.NetAmount > 0m
            ? record.NetAmount
            : record.NetAmountJpy > 0m && exchangeRate > 0m
                ? record.NetAmountJpy / exchangeRate
                : record.NetAmountJpy > 0m ? 0m : Math.Max(0m, grossAmount - record.TaxAmount);
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
            Ticker = record.Ticker,
            StockName = record.Name,
            Broker = record.Broker,
            DividendStatus = DividendConstants.Actual,
            Source = DividendConstants.SourceCsv,
            SourceFile = string.IsNullOrWhiteSpace(record.SourceFile)
                ? string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source
                : record.SourceFile,
            SourceRowNumber = record.SourceRowNumber,
            SourcePriority = 100,
            Quantity = record.Quantity,
            DividendPerShare = record.Quantity > 0m && grossAmount > 0m ? grossAmount / record.Quantity : 0m,
            GrossAmount = grossAmount,
            TotalTaxAmount = record.TaxAmount,
            TaxAmount = record.TaxAmount,
            NetAmount = netAmount,
            Currency = currency,
            ExchangeRate = exchangeRate,
            ExchangeRateAcquiredAt = exchangeRate > 0m ? record.PaymentDate : DateTime.MinValue,
            ExchangeRateSource = exchangeRate > 0m ? (string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source) : string.Empty,
            ExchangeRateInputType = exchangeRate > 0m ? "CSV" : string.Empty,
            GrossAmountJpy = grossAmountJpy,
            TotalTaxAmountJpy = totalTaxAmountJpy,
            NetAmountJpy = netAmountJpy,
            JpyAmount = netAmountJpy,
            IsTaxEstimated = false,
            IsNetOnly = netOnly,
            IsNisa = DividendConstants.IsNisaAccount(accountType),
            IsForeignStock = currency != "JPY" ||
                             record.Product.Contains("米国", StringComparison.Ordinal) ||
                             record.Product.Contains("外株", StringComparison.Ordinal),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            Memo = $"{(string.IsNullOrWhiteSpace(record.Source) ? "配当CSV" : record.Source)} / 口座: {record.Account} / 商品: {record.Product} / 数量: {record.Quantity:N2}" +
                   (netOnly ? " / 税引後入金のみ判明（税引前・税額は不明）" : string.Empty)
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
        NeedsReview,
        Created,
        CreatedNeedsReview,
        UpdatedExistingActual,
        ReplacedSchedule
    }

    private static string BuildPositionKey(string broker, string ticker, string account = "")
    {
        var normalizedBroker = SecuritySymbolNormalizer.NormalizeBroker(broker);
        var normalizedTicker = NormalizeTicker(ticker);
        if (normalizedTicker.StartsWith("FUND:", StringComparison.OrdinalIgnoreCase))
            normalizedTicker = normalizedTicker[5..];
        var normalizedAccount = AccountTypeNormalizer.Normalize(account);
        return $"{normalizedBroker}|{normalizedTicker}|{normalizedAccount}";
    }

    private IReadOnlyList<BrokerTrade> GetAccountTrades(int stockId, BrokerTradeRecord incoming)
    {
        if (_getBrokerTrades is null) return Array.Empty<BrokerTrade>();
        var broker = SecuritySymbolNormalizer.NormalizeBroker(incoming.Broker);
        var account = AccountTypeNormalizer.Normalize(incoming.Account);
        return _getBrokerTrades(stockId)
            .Where(x => string.Equals(SecuritySymbolNormalizer.NormalizeBroker(x.Broker), broker,
                StringComparison.OrdinalIgnoreCase))
            .Where(x => AccountTypeNormalizer.Normalize(string.IsNullOrWhiteSpace(x.CustodyType)
                ? x.AccountType : x.CustodyType) == account)
            .ToList();
    }

    private static IReadOnlyList<StockPosition> FindDividendPositionCandidates(
        IEnumerable<StockPosition> positions, BrokerDividendRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Account)) return Array.Empty<StockPosition>();
        return positions
            .Where(x => string.Equals(PositionIdentityService.NormalizeBroker(x.Stock.Broker),
                PositionIdentityService.NormalizeBroker(record.Broker), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeTicker(x.Stock.Ticker), NormalizeTicker(record.Ticker),
                    StringComparison.OrdinalIgnoreCase))
            .DistinctBy(x => x.Stock.Id)
            .ToList();
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

    private void ResetPreviewPlan()
    {
        _previewFileHashes.Clear();
        _previewKinds.Clear();
        _previewStatements.Clear();
        _previewDividends.Clear();
        _previewSbiBatch = null;
    }

    private BrokerStatementImport GetPreviewStatement(string filePath, Func<BrokerStatementImport> parse) =>
        _previewStatements.TryGetValue(filePath, out var statement) ? statement : parse();

    private CsvKind GetCsvKind(string filePath)
    {
        CheckCancellation();
        if (_previewKinds.TryGetValue(filePath, out var kind)) return kind;
        kind = NomuraHoldingsCsvParser.LooksLikeNomuraHoldingsCsv(filePath) ? CsvKind.NomuraHoldings :
            NomuraTransactionCsvParser.LooksLikeNomuraTransactionCsv(filePath) ? CsvKind.NomuraTransaction :
            NomuraDividendCsvParser.LooksLikeNomuraDividendCsv(filePath) ? CsvKind.NomuraDividend :
            SbiStatementCsvParser.LooksLikeSbiStatementCsv(filePath) ? CsvKind.SbiStatement :
            SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(filePath) ? CsvKind.SbiDistribution :
            CsvKind.Unsupported;
        _previewKinds[filePath] = kind;
        return kind;
    }

    private enum CsvKind
    {
        Unsupported,
        NomuraHoldings,
        NomuraTransaction,
        NomuraDividend,
        SbiStatement,
        SbiDistribution
    }

    private IReadOnlyList<BrokerDividendRecord> GetPreviewDividends(
        string filePath, Func<IReadOnlyList<BrokerDividendRecord>> parse) =>
        _previewDividends.TryGetValue(filePath, out var records) ? records : parse();

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
