using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class SbiStatementCsvParser
{
    private readonly SbiDistributionCsvParser _distributionParser = new();

    public BrokerStatementImport ParseFromSeedFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CSVファイルが見つかりません。", filePath);
        }

        // A file selected by the user is the complete import scope.
        return ParseFiles(new[] { filePath });
    }

    public BrokerStatementImport ParseFiles(IEnumerable<string> filePaths)
    {
        var files = filePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var holdings = new List<BrokerHoldingRecord>();
        var unresolvedHoldings = new List<BrokerHoldingRecord>();
        var completeScopes = new List<CompleteHoldingSnapshotScope>();
        var dividends = new List<BrokerDividendRecord>();
        var unresolvedDividends = new List<BrokerUnresolvedDividend>();
        var trades = new List<BrokerTradeRecord>();
        var unresolvedTrades = new List<BrokerTradeRecord>();
        var ignoredRows = 0;
        var reviewMessages = new List<string>();
        var sourceRowCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sourceRowAudits = new Dictionary<string, CsvSourceRowAudit>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (SbiDistributionCsvParser.LooksLikeSbiDistributionCsv(file))
            {
                var parsed = _distributionParser.ParseStatement(file);
                dividends.AddRange(parsed.Dividends);
                unresolvedDividends.AddRange(parsed.UnresolvedDividends);
                sourceRowCounts[file] = parsed.Dividends.Count;
                sourceRowAudits[file] = parsed.SourceRowAudits[file];
                continue;
            }

            var lines = ReadLines(file);
            var rowAudit = new CsvSourceRowAuditBuilder();
            if (LooksLikeJapanStockHoldings(lines))
            {
                var parsedHoldings = ParseJapanStockAndFundHoldings(lines, rowAudit, unresolvedHoldings);
                var exportDate = TryReadExportDate(file);
                foreach (var holding in parsedHoldings)
                {
                    holding.SnapshotDate = exportDate;
                    if (holding.IsMutualFund) holding.NavDate = exportDate;
                }
                completeScopes.AddRange(FindCompleteHoldingScopes(lines, exportDate));
                holdings.AddRange(parsedHoldings);
                sourceRowCounts[file] = parsedHoldings.Count;
                sourceRowAudits[file] = rowAudit.Build();
                continue;
            }

            if (LooksLikeForeignStockTrades(lines))
            {
                var parsed = ParseForeignStockTrades(lines, rowAudit, unresolvedTrades);
                trades.AddRange(parsed);
                sourceRowCounts[file] = parsed.Count;
                sourceRowAudits[file] = rowAudit.Build();
                continue;
            }

            if (LooksLikePaymentRecords(lines))
            {
                var parsed = ParsePaymentRecords(lines, rowAudit, unresolvedTrades);
                trades.AddRange(parsed);
                sourceRowCounts[file] = parsed.Count;
                sourceRowAudits[file] = rowAudit.Build();
                continue;
            }

            if (LooksLikeAccumulationOrders(lines))
            {
                var parsed = ParseAccumulationOrders(lines, rowAudit);
                trades.AddRange(parsed);
                sourceRowCounts[file] = parsed.Count;
                sourceRowAudits[file] = rowAudit.Build();
                continue;
            }

            if (LooksLikeDomesticOrFundTrades(lines))
            {
                var parsed = ParseDomesticOrFundTrades(lines, rowAudit, unresolvedTrades);
                trades.AddRange(parsed);
                sourceRowCounts[file] = parsed.Count;
                sourceRowAudits[file] = rowAudit.Build();
                continue;
            }

            ignoredRows += Math.Max(0, lines.Count - 1);
            sourceRowCounts[file] = 0;
            sourceRowAudits[file] = rowAudit.Build();
        }

        var distinctHoldings = new List<BrokerHoldingRecord>();
        var conflictingHoldingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in holdings.GroupBy(PositionIdentityService.BuildKey, StringComparer.OrdinalIgnoreCase))
        {
            var newestDate = group.Max(x => x.SnapshotDate);
            var newest = group.Where(x => x.SnapshotDate == newestDate).ToList();
            if (newest.Count > 1 && newest.Any(x =>
                    x.Shares != newest[0].Shares ||
                    x.MarketValue != newest[0].MarketValue ||
                    x.AcquisitionAmount != newest[0].AcquisitionAmount))
            {
                reviewMessages.Add($"同一日時の保有残高が食い違います: {newest[0].Broker} {newest[0].Ticker} {newest[0].Account}");
                conflictingHoldingKeys.Add(group.Key);
                continue;
            }
            distinctHoldings.Add(newest[0]);
        }

        return new BrokerStatementImport
        {
            Broker = "SBI証券",
            Source = "SBI一括CSV",
            CanUpdateHoldings = holdings.Count > 0 || trades.Count > 0 || completeScopes.Count > 0,
            Holdings = distinctHoldings,
            UnresolvedHoldings = unresolvedHoldings,
            CompleteHoldingScopes = completeScopes
                .Where(scope => !holdings.Any(holding => conflictingHoldingKeys.Contains(PositionIdentityService.BuildKey(holding)) &&
                    SameHoldingScope(holding, scope)))
                .ToList(),
            Dividends = dividends,
            UnresolvedDividends = unresolvedDividends,
            Trades = trades,
            UnresolvedTrades = unresolvedTrades,
            IgnoredRowCount = ignoredRows,
            ReviewMessages = reviewMessages,
            SourceRowCounts = sourceRowCounts,
            SourceRowAudits = sourceRowAudits
        };
    }

    public static bool LooksLikeSbiStatementCsv(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var lines = ReadLines(filePath).Take(40).ToList();
        return LooksLikeJapanStockHoldings(lines) ||
               LooksLikeForeignStockTrades(lines) ||
               LooksLikeSbiDomesticOrFundTrades(lines) ||
               LooksLikePaymentRecords(lines) ||
               LooksLikeAccumulationOrders(lines);
    }

    private static DateTime TryReadExportDate(string filePath)
    {
        var match = Regex.Match(Path.GetFileName(filePath), @"(?<!\d)(20\d{6})(?:\d{6})?(?!\d)");
        if (!match.Success) return DateTime.MinValue;
        var timestamp = match.Value;
        var format = timestamp.Length == 14 ? "yyyyMMddHHmmss" : "yyyyMMdd";
        return DateTime.TryParseExact(timestamp, format, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : DateTime.MinValue;
    }

    private static IReadOnlyList<CompleteHoldingSnapshotScope> FindCompleteHoldingScopes(
        IReadOnlyList<string> lines, DateTime snapshotDate)
    {
        if (snapshotDate == DateTime.MinValue ||
            !lines.Any(line => line.Trim().Trim('"') == "保有証券一覧"))
            return Array.Empty<CompleteHoldingSnapshotScope>();

        var scopes = new List<CompleteHoldingSnapshotScope>();
        foreach (var (assetType, requiredColumns, nameColumn, quantityColumn) in new[]
                 {
                     (AssetTypes.Stock, new[] { "銘柄コード", "銘柄名称", "保有株数", "評価額" }, "銘柄名称", "保有株数"),
                     (AssetTypes.MutualFund, new[] { "ファンド名", "保有口数", "基準価額", "評価額" }, "ファンド名", "保有口数")
                 })
        {
            foreach (var headerIndex in FindHeaderIndexes(lines, requiredColumns))
            {
                var account = ResolvePreviousSectionTitle(lines, headerIndex);
                if (string.IsNullOrWhiteSpace(account)) continue;

                // SBI's complete-list export has an account subtotal directly above each detail table.
                var subtotalHeaderIndex = Enumerable.Range(Math.Max(0, headerIndex - 8),
                        headerIndex - Math.Max(0, headerIndex - 8))
                    .LastOrDefault(index => lines[index].Contains("評価額合計", StringComparison.Ordinal), -1);
                if (subtotalHeaderIndex < 0 || subtotalHeaderIndex + 1 >= headerIndex) continue;
                var subtotalCells = ParseCsvLine(lines[subtotalHeaderIndex + 1]);
                if (subtotalCells.Count == 0 || !decimal.TryParse(
                        subtotalCells[0].Replace(",", "", StringComparison.Ordinal),
                        NumberStyles.Number | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var declaredMarketValue)) continue;

                var headers = ParseCsvLine(lines[headerIndex]);
                var rows = 0;
                var total = 0m;
                var valid = true;
                for (var i = headerIndex + 1; i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]); i++)
                {
                    var row = ToDictionary(headers, ParseCsvLine(lines[i]), i + 1);
                    var rowAccount = Get(row, "預り区分");
                    if (string.IsNullOrWhiteSpace(Get(row, nameColumn)) ||
                        (assetType == AssetTypes.Stock && string.IsNullOrWhiteSpace(Get(row, "銘柄コード"))) ||
                        string.IsNullOrWhiteSpace(Get(row, quantityColumn)) ||
                        (!string.IsNullOrWhiteSpace(rowAccount) &&
                         !SameAccount(rowAccount, account)))
                    {
                        valid = false;
                        break;
                    }
                    var quantity = ParseDecimal(Get(row, quantityColumn));
                    var marketValue = ParseDecimal(Get(row, "評価額"));
                    if (quantity < 0m || marketValue < 0m) { valid = false; break; }
                    total += marketValue;
                    rows++;
                }
                if (!valid || (rows == 0 && declaredMarketValue != 0m) ||
                    decimal.Abs(total - declaredMarketValue) > 1m) continue;

                scopes.Add(new CompleteHoldingSnapshotScope
                {
                    Broker = "SBI証券", Account = account, AssetType = assetType,
                    SnapshotDate = snapshotDate, Source = "SBI保有証券一覧CSV"
                });
            }
        }
        return scopes;
    }

    private static bool SameAccount(string left, string right) =>
        string.Equals(PositionIdentityService.NormalizeCustodyForIdentity(left, left),
            PositionIdentityService.NormalizeCustodyForIdentity(right, right), StringComparison.OrdinalIgnoreCase);

    private static bool SameHoldingScope(BrokerHoldingRecord holding, CompleteHoldingSnapshotScope scope) =>
        string.Equals(PositionIdentityService.BuildKey(holding.Broker, "SCOPE", holding.AssetType,
                holding.Account, holding.Account, holding.Currency),
            PositionIdentityService.BuildKey(scope.Broker, "SCOPE", scope.AssetType,
                scope.Account, scope.Account, scope.Currency), StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePaymentRecords(IReadOnlyList<string> lines) =>
        FindHeaderIndex(lines, "国内約定日", "注文種別", "商品区分", "国内受渡日") >= 0;

    private static IReadOnlyList<BrokerTradeRecord> ParsePaymentRecords(
        IReadOnlyList<string> lines, CsvSourceRowAuditBuilder rowAudit, List<BrokerTradeRecord> unresolvedTrades)
    {
        var headerIndex = FindHeaderIndex(lines, "国内約定日", "注文種別", "商品区分", "国内受渡日");
        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers, "国内約定日", "注文種別", "商品区分",
            "国内受渡日", "銘柄コード", "約定数量", "約定単価");
        var records = new List<BrokerTradeRecord>();
        foreach (var (line, index) in lines.Select((line, index) => (line, index))
                     .Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x.line)))
        {
            rowAudit.Seen(index + 1);
            var row = ToDictionary(headers, ParseCsvLine(line), index + 1);
            var ticker = Get(row, "銘柄コード");
            if (string.IsNullOrWhiteSpace(ticker))
            {
                rowAudit.Pending(index + 1, "銘柄コードを確定できません。");
                unresolvedTrades.Add(new BrokerTradeRecord
                {
                    Broker = "SBI証券", Account = Get(row, "預り区分"),
                    Product = Get(row, "商品区分"), Name = Get(row, "銘柄"),
                    TradeDate = ParseDate(Get(row, "国内約定日")),
                    SettlementDate = ParseDate(Get(row, "国内受渡日")),
                    Quantity = ParseDecimal(Get(row, "約定数量")), Source = "SBI米株約定CSV"
                });
                continue;
            }
            var quantity = ParseDecimal(Get(row, "約定数量"));
            var unitPrice = ParseDecimal(Get(row, "約定単価").Replace("USD", string.Empty, StringComparison.OrdinalIgnoreCase));
            if (quantity <= 0m || unitPrice <= 0m)
                throw new InvalidOperationException($"SBI米株約定CSVの数量・単価が不正です: {ticker}");
            var settlementText = Get(row, "受渡金額/決済損益");
            var currency = Get(row, "商品区分").Contains("米国", StringComparison.Ordinal) ? "USD" : "JPY";
            records.Add(new BrokerTradeRecord
            {
                Broker = "SBI証券", Account = Get(row, "預り区分"), Product = Get(row, "商品区分"),
                TradeDate = ParseDate(Get(row, "国内約定日")),
                SettlementDate = ParseDate(Get(row, "国内受渡日")),
                TradeType = NormalizeTradeType(Get(row, "取引")),
                Ticker = SecuritySymbolNormalizer.NormalizeTicker(ticker), Name = Get(row, "銘柄"),
                Quantity = quantity,
                SignedQuantity = Get(row, "取引").Contains("売", StringComparison.Ordinal) ? -quantity : quantity,
                UnitPrice = unitPrice, Currency = currency,
                ExchangeRate = currency == "JPY" ? 1m : 0m,
                SettlementAmountJpy = settlementText.Contains("USD", StringComparison.OrdinalIgnoreCase)
                    ? 0m : ParseDecimal(settlementText),
                Source = "SBI米株約定CSV（円換算・為替が不明な行は0）"
            });
            rowAudit.Parsed();
        }

        return records;
    }

    private static bool LooksLikeAccumulationOrders(IReadOnlyList<string> lines) =>
        FindHeaderIndex(lines, "発注日", "注文状況", "ファンド名", "約定数量") >= 0;

    private static IReadOnlyList<BrokerTradeRecord> ParseAccumulationOrders(IReadOnlyList<string> lines, CsvSourceRowAuditBuilder rowAudit)
    {
        var headerIndex = FindHeaderIndex(lines, "発注日", "注文状況", "ファンド名", "約定数量");
        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers, "発注日", "注文状況", "ファンド名", "約定数量", "約定単価");
        var records = new List<BrokerTradeRecord>();
        foreach (var (line, index) in lines.Select((line, index) => (line, index))
                     .Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x.line)))
        {
            rowAudit.Seen(index + 1);
            var row = ToDictionary(headers, ParseCsvLine(line), index + 1);
            if (Get(row, "注文状況") != "完了")
            {
                rowAudit.Excluded(index + 1, $"未約定の注文: {Get(row, "注文状況")}");
                continue;
            }
            var name = Get(row, "ファンド名");
            var quantity = ParseDecimal(Get(row, "約定数量"));
            var nav = ParseDecimal(Get(row, "約定単価"));
            if (string.IsNullOrWhiteSpace(name) || quantity <= 0m || nav <= 0m)
                throw new InvalidOperationException("SBI積立注文CSVの確定数量・単価が不正です。");
            var orderDate = ParseDate(Get(row, "発注日"));
            records.Add(new BrokerTradeRecord
            {
                Broker = "SBI証券", Account = Get(row, "預り区分"), Product = "投資信託",
                TradeDate = orderDate, SettlementDate = orderDate,
                TradeType = "現物買付", Ticker = SecuritySymbolNormalizer.NormalizeTicker($"FUND:{name}"), Name = name,
                Quantity = quantity, SignedQuantity = quantity, UnitPrice = nav / 10000m,
                SettlementAmountJpy = ParseDecimal(Get(row, "注文金額")),
                FeeJpy = ParseDecimal(Get(row, "手数料等")), Currency = "JPY", ExchangeRate = 1m,
                Source = "SBI積立注文CSV（発注日基準・受渡日不明）"
            });
            rowAudit.Parsed();
        }

        return records;
    }

    private static IReadOnlyList<BrokerTradeRecord> ParseForeignStockTrades(
        IReadOnlyList<string> lines, CsvSourceRowAuditBuilder rowAudit, List<BrokerTradeRecord> unresolvedTrades)
    {
        var headerIndex = FindHeaderIndex(lines, "国内約定日", "銘柄名", "約定数量");
        if (headerIndex < 0)
        {
            return Array.Empty<BrokerTradeRecord>();
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers, "国内約定日", "銘柄名", "約定数量", "約定単価", "国内受渡日", "通貨");
        var records = new List<BrokerTradeRecord>();
        foreach (var (line, index) in lines.Select((line, index) => (line, index))
                     .Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x.line)))
        {
            rowAudit.Seen(index + 1);
            var row = ToDictionary(headers, ParseCsvLine(line), index + 1);
            var rawName = Get(row, "銘柄名");
            var (name, ticker) = SplitSbiForeignName(rawName);
            if (string.IsNullOrWhiteSpace(ticker))
            {
                rowAudit.Pending(index + 1, "銘柄名から外国株ティッカーを確定できません。");
                unresolvedTrades.Add(new BrokerTradeRecord
                {
                    Broker = "SBI証券", Account = Get(row, "預り区分"), Product = "米国株式",
                    Name = rawName, TradeDate = ParseJapaneseDate(Get(row, "国内約定日")),
                    SettlementDate = ParseShortDate(Get(row, "国内受渡日")),
                    Quantity = ParseDecimal(Get(row, "約定数量")),
                    Currency = ResolveCurrency(Get(row, "通貨")), Source = "SBI米国株約定CSV"
                });
                continue;
            }
            ticker = SecuritySymbolNormalizer.NormalizeTicker(ticker);
            name = SecuritySymbolNormalizer.NormalizeName(ticker, name);

            var quantity = ParseDecimal(Get(row, "約定数量"));
            var unitPrice = ParseDecimal(Get(row, "約定単価"));
            var settlementAmount = ParseDecimal(Get(row, "受渡金額"));
            var settlementCurrency = Get(row, "通貨");
            var securityCurrency = rawName.Contains("/ NASDAQ", StringComparison.OrdinalIgnoreCase) ||
                                   rawName.Contains("New York Stock Exchange", StringComparison.OrdinalIgnoreCase)
                ? "USD"
                : ResolveCurrency(settlementCurrency);
            var exchangeRate = securityCurrency == "USD" &&
                               settlementCurrency.Contains("日本円", StringComparison.Ordinal) &&
                               quantity > 0m &&
                               unitPrice > 0m
                ? settlementAmount / (quantity * unitPrice)
                : 0m;
            var tradeType = NormalizeTradeType(Get(row, "取引"));

            records.Add(new BrokerTradeRecord
            {
                Broker = "SBI証券",
                TradeDate = ParseJapaneseDate(Get(row, "国内約定日")),
                SettlementDate = ParseShortDate(Get(row, "国内受渡日")),
                Account = Get(row, "預り区分"),
                Product = "米国株式",
                TradeType = tradeType,
                Ticker = ticker,
                Name = name,
                Quantity = quantity,
                SignedQuantity = IsSellTrade(tradeType) ? -quantity : quantity,
                UnitPrice = unitPrice,
                SettlementAmountJpy = securityCurrency == "JPY" ? settlementAmount : 0m,
                ExchangeRate = exchangeRate,
                Currency = securityCurrency,
                Source = "SBI米国株約定CSV"
            });
            rowAudit.Parsed();
        }

        return records;
    }

    private static IReadOnlyList<BrokerHoldingRecord> ParseJapanStockAndFundHoldings(
        IReadOnlyList<string> lines, CsvSourceRowAuditBuilder rowAudit, List<BrokerHoldingRecord> unresolvedHoldings)
    {
        var records = new List<BrokerHoldingRecord>();
        foreach (var headerIndex in FindHeaderIndexes(lines, "銘柄コード", "銘柄名称", "保有株数", "評価額"))
        {
            var account = ResolvePreviousSectionTitle(lines, headerIndex);
            var headers = ParseCsvLine(lines[headerIndex]);
            CsvRowValidation.ValidateHeaders(headers, "銘柄コード", "銘柄名称", "保有株数", "評価額");
            foreach (var (line, index) in lines.Select((line, index) => (line, index)).Skip(headerIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                rowAudit.Seen(index + 1);
                var row = ToDictionary(headers, ParseCsvLine(line), index + 1);
                var ticker = Get(row, "銘柄コード");
                var name = Get(row, "銘柄名称");
                var shares = ParseDecimal(Get(row, "保有株数"));
                if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(name) || shares < 0m)
                {
                    rowAudit.Pending(index + 1, "株式の銘柄コード・名称・数量を確定できません。");
                    unresolvedHoldings.Add(new BrokerHoldingRecord
                    {
                        Broker = "SBI証券", Account = account, AssetType = AssetTypes.Stock,
                        Ticker = ticker, Name = name, Shares = shares,
                        Currency = "JPY", Source = "SBI日本株保有CSV"
                    });
                    continue;
                }

                records.Add(new BrokerHoldingRecord
                {
                    AssetType = AssetTypes.Stock,
                    Broker = "SBI証券",
                    Account = account,
                    Product = "国内株式",
                    Ticker = SecuritySymbolNormalizer.NormalizeTicker(ticker),
                    Name = name.Trim(),
                    Shares = shares,
                    AverageAcquisitionPrice = ParseDecimal(Get(row, "取得単価")),
                    CurrentPrice = ParseDecimal(Get(row, "現在値")),
                    AcquisitionAmount = ParseDecimal(Get(row, "取得金額")),
                    MarketValue = ParseDecimal(Get(row, "評価額")),
                    MarketValueJpy = ParseDecimal(Get(row, "評価額")),
                    UnrealizedGainLossJpy = ParseDecimal(Get(row, "評価損益")),
                    Currency = "JPY",
                    PurchaseExchangeRate = 1m,
                    CurrentExchangeRate = 1m,
                    SnapshotDate = DateTime.Today,
                    Source = "SBI日本株保有CSV"
                });
                rowAudit.Parsed();
            }
        }

        foreach (var headerIndex in FindHeaderIndexes(lines, "ファンド名", "保有口数", "基準価額", "評価額"))
        {
            var account = ResolvePreviousSectionTitle(lines, headerIndex);
            var headers = ParseCsvLine(lines[headerIndex]);
            CsvRowValidation.ValidateHeaders(headers, "ファンド名");
            foreach (var (line, index) in lines.Select((line, index) => (line, index)).Skip(headerIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                rowAudit.Seen(index + 1);
                var row = ToDictionary(headers, ParseCsvLine(line), index + 1);
                var name = Get(row, "ファンド名");
                var shares = ParseDecimal(GetAny(row, "保有口数", "保有口数 (口)"));
                if (string.IsNullOrWhiteSpace(name) || shares < 0m)
                {
                    rowAudit.Pending(index + 1, "投資信託の名称・数量を確定できません。");
                    unresolvedHoldings.Add(new BrokerHoldingRecord
                    {
                        Broker = "SBI証券", Account = account, AssetType = AssetTypes.MutualFund,
                        Name = name, Shares = shares, UnitsHeld = shares,
                        Currency = "JPY", Source = "SBI投信保有CSV"
                    });
                    continue;
                }

                var acquisitionAmount = ParseDecimal(GetAny(row, "取得金額", "取得金額 (円)"));
                var marketValue = ParseDecimal(GetAny(row, "評価額", "評価額 (円)"));
                var averageCostNav = ParseDecimal(GetAny(row, "取得単価", "取得単価 (円)"));
                var currentNav = ParseDecimal(GetAny(row, "基準価額", "基準価額 (円)"));
                var distributionMethod = Get(row, "分配金受取方法");
                var rowAccount = Get(row, "預り区分");
                records.Add(new BrokerHoldingRecord
                {
                    AssetType = AssetTypes.MutualFund,
                    Broker = "SBI証券",
                    Account = string.IsNullOrWhiteSpace(rowAccount) ? account : rowAccount,
                    Product = "投資信託",
                    Ticker = SecuritySymbolNormalizer.NormalizeTicker(ResolveDomesticTicker(string.Empty, name)),
                    Name = name.Trim(),
                    Shares = shares,
                    AverageAcquisitionPrice = averageCostNav,
                    CurrentPrice = currentNav,
                    AcquisitionAmount = acquisitionAmount,
                    MarketValue = marketValue,
                    MarketValueJpy = marketValue,
                    UnrealizedGainLossJpy = ParseDecimal(GetAny(row, "評価損益", "評価損益 (円)")),
                    Currency = "JPY",
                    PurchaseExchangeRate = 1m,
                    CurrentExchangeRate = 1m,
                    SnapshotDate = DateTime.Today,
                    Source = "SBI投信保有CSV",
                    FundName = name.Trim(),
                    FundCode = SecuritySymbolNormalizer.NormalizeTicker(ResolveDomesticTicker(string.Empty, name)),
                    UnitsHeld = shares,
                    UnitBase = 10000m,
                    AverageCostNav = averageCostNav,
                    CurrentNav = currentNav,
                    NavDate = DateTime.Today,
                    NavSource = "SBI投信保有CSV",
                    DistributionMethod = distributionMethod
                });
                rowAudit.Parsed();
            }
        }

        return AggregateHoldings(records);
    }

    private static IReadOnlyList<BrokerTradeRecord> ParseDomesticOrFundTrades(
        IReadOnlyList<string> lines, CsvSourceRowAuditBuilder rowAudit, List<BrokerTradeRecord> unresolvedTrades)
    {
        var headerIndex = FindHeaderIndex(lines, "約定日", "銘柄", "約定数量");
        if (headerIndex < 0)
        {
            return Array.Empty<BrokerTradeRecord>();
        }

        var headers = ParseCsvLine(lines[headerIndex]);
        CsvRowValidation.ValidateHeaders(headers, "約定日", "受渡日", "銘柄", "約定数量", "約定単価");
        var records = new List<BrokerTradeRecord>();
        foreach (var (line, index) in lines.Select((line, index) => (line, index))
                     .Skip(headerIndex + 1).Where(x => !string.IsNullOrWhiteSpace(x.line)))
        {
            rowAudit.Seen(index + 1);
            var row = ToDictionary(headers, ParseCsvLine(line), index + 1);
            var name = Get(row, "銘柄");
            if (string.IsNullOrWhiteSpace(name))
            {
                rowAudit.Pending(index + 1, "銘柄名を確定できません。");
                unresolvedTrades.Add(new BrokerTradeRecord
                {
                    Broker = "SBI証券", Account = Get(row, "預り"),
                    Ticker = Get(row, "銘柄コード"), TradeDate = ParseDate(Get(row, "約定日")),
                    SettlementDate = ParseDate(Get(row, "受渡日")),
                    Quantity = ParseDecimal(Get(row, "約定数量")),
                    Currency = "JPY", Source = "SBI国内約定CSV"
                });
                continue;
            }

            var quantity = ParseDecimal(Get(row, "約定数量"));
            var amount = ParseDecimal(Get(row, "受渡金額/決済損益"));
            var unitPrice = ParseDecimal(Get(row, "約定単価"));
            if (Get(row, "取引").Contains("投信", StringComparison.Ordinal) && quantity > 0m && amount > 0m)
            {
                unitPrice = amount / quantity;
            }

            var tradeType = NormalizeTradeType(Get(row, "取引"));
            records.Add(new BrokerTradeRecord
            {
                Broker = "SBI証券",
                TradeDate = ParseDate(Get(row, "約定日")),
                SettlementDate = ParseDate(Get(row, "受渡日")),
                Account = Get(row, "預り"),
                Product = Get(row, "取引").Contains("投信", StringComparison.Ordinal) ? "投資信託" : "国内株式",
                TradeType = tradeType,
                Ticker = SecuritySymbolNormalizer.NormalizeTicker(ResolveDomesticTicker(Get(row, "銘柄コード"), name)),
                Name = name,
                Quantity = quantity,
                SignedQuantity = IsSellTrade(tradeType) ? -quantity : quantity,
                UnitPrice = unitPrice,
                SettlementAmountJpy = amount,
                FeeJpy = ParseDecimal(Get(row, "手数料/諸経費等")),
                ExchangeRate = 1m,
                Currency = "JPY",
                Source = "SBI国内約定CSV"
            });
            rowAudit.Parsed();
        }

        return records;
    }

    private static bool LooksLikeForeignStockTrades(IReadOnlyList<string> lines) =>
        lines.Any(line => line.Contains("国内約定日", StringComparison.Ordinal) &&
                          line.Contains("通貨", StringComparison.Ordinal) &&
                          line.Contains("受渡金額", StringComparison.Ordinal));

    private static bool LooksLikeJapanStockHoldings(IReadOnlyList<string> lines)
    {
        var hasHoldingsSection = lines.Any(line => line.Contains("保有証券一覧", StringComparison.Ordinal)) ||
                                 lines.Any(line => line.Contains("保有状況-金額指定-", StringComparison.Ordinal));
        return hasHoldingsSection &&
        (lines.Any(line => line.Contains("銘柄コード", StringComparison.Ordinal) &&
                           line.Contains("銘柄名称", StringComparison.Ordinal) &&
                           line.Contains("保有株数", StringComparison.Ordinal) &&
                           line.Contains("評価額", StringComparison.Ordinal)) ||
         lines.Any(line => line.Contains("ファンド名", StringComparison.Ordinal) &&
                           line.Contains("保有口数", StringComparison.Ordinal) &&
                           line.Contains("基準価額", StringComparison.Ordinal) &&
                           line.Contains("評価額", StringComparison.Ordinal)));
    }

    private static bool LooksLikeDomesticOrFundTrades(IReadOnlyList<string> lines) =>
        lines.Any(line => line.Contains("約定履歴照会", StringComparison.Ordinal)) ||
        lines.Any(line => line.Contains("約定日", StringComparison.Ordinal) &&
                          line.Contains("銘柄コード", StringComparison.Ordinal) &&
                          line.Contains("受渡金額/決済損益", StringComparison.Ordinal));

    private static bool LooksLikeSbiDomesticOrFundTrades(IReadOnlyList<string> lines) =>
        lines.Any(line => line.Contains("約定履歴照会", StringComparison.Ordinal)) &&
        lines.Any(line => line.Contains("約定日", StringComparison.Ordinal) &&
                          line.Contains("銘柄", StringComparison.Ordinal) &&
                          line.Contains("約定数量", StringComparison.Ordinal) &&
                          line.Contains("手数料/諸経費等", StringComparison.Ordinal));

    private static IReadOnlyList<string> ReadLines(string filePath)
    {
        return CsvFileEncoding.ReadAllRecords(filePath);
    }

    private static int FindHeaderIndex(IReadOnlyList<string> lines, params string[] requiredColumns)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (requiredColumns.All(column => lines[i].Contains(column, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<int> FindHeaderIndexes(IReadOnlyList<string> lines, params string[] requiredColumns)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (requiredColumns.All(column => lines[i].Contains(column, StringComparison.Ordinal)))
            {
                yield return i;
            }
        }
    }

    private static IReadOnlyList<BrokerHoldingRecord> AggregateHoldings(IReadOnlyList<BrokerHoldingRecord> records)
    {
        var aggregated = new List<BrokerHoldingRecord>();
        foreach (var group in records.GroupBy(BuildAggregationKey, StringComparer.OrdinalIgnoreCase))
        {
            var holdings = group.ToList();
            if (holdings.Count == 1)
            {
                aggregated.Add(holdings[0]);
                continue;
            }

            var shares = holdings.Sum(x => x.Shares);
            var acquisitionAmount = holdings.Sum(x => x.AcquisitionAmount);
            var marketValue = holdings.Sum(x => x.MarketValue);
            var marketValueJpy = holdings.Sum(x => x.MarketValueJpy);
            var first = holdings[0];
            if (first.IsMutualFund)
            {
                var unitsHeld = holdings.Sum(x => x.UnitsHeld > 0m ? x.UnitsHeld : x.Shares);
                var unitBase = MutualFundCalculator.NormalizeUnitBase(first.UnitBase);
                aggregated.Add(new BrokerHoldingRecord
                {
                    AssetType = AssetTypes.MutualFund,
                    Broker = first.Broker,
                    Account = first.Account,
                    Product = first.Product,
                    Ticker = first.Ticker,
                    Name = first.Name,
                    Shares = unitsHeld,
                    AverageAcquisitionPrice = unitsHeld > 0m && acquisitionAmount > 0m
                        ? acquisitionAmount / unitsHeld * unitBase
                        : first.AverageCostNav,
                    CurrentPrice = unitsHeld > 0m && marketValue > 0m
                        ? marketValue / unitsHeld * unitBase
                        : first.CurrentNav,
                    AcquisitionAmount = acquisitionAmount,
                    MarketValue = marketValue,
                    MarketValueJpy = marketValueJpy,
                    UnrealizedGainLossJpy = holdings.Sum(x => x.UnrealizedGainLossJpy),
                    Currency = "JPY",
                    PurchaseExchangeRate = 1m,
                    CurrentExchangeRate = 1m,
                    SnapshotDate = holdings.Max(x => x.SnapshotDate),
                    Source = first.Source,
                    FundName = first.FundName,
                    FundCode = first.FundCode,
                    AssociationCode = first.AssociationCode,
                    UnitsHeld = unitsHeld,
                    UnitBase = unitBase,
                    AverageCostNav = unitsHeld > 0m && acquisitionAmount > 0m
                        ? acquisitionAmount / unitsHeld * unitBase
                        : first.AverageCostNav,
                    CurrentNav = unitsHeld > 0m && marketValue > 0m
                        ? marketValue / unitsHeld * unitBase
                        : first.CurrentNav,
                    NavDate = holdings.Max(x => x.NavDate),
                    NavSource = first.NavSource,
                    DistributionMethod = first.DistributionMethod
                });
                continue;
            }

            aggregated.Add(new BrokerHoldingRecord
            {
                AssetType = AssetTypes.Stock,
                Broker = first.Broker,
                Account = "複数口座合算",
                Product = first.Product,
                Ticker = first.Ticker,
                Name = first.Name,
                Shares = shares,
                AverageAcquisitionPrice = shares > 0m ? acquisitionAmount / shares : 0m,
                CurrentPrice = shares > 0m ? marketValue / shares : 0m,
                AcquisitionAmount = acquisitionAmount,
                MarketValue = marketValue,
                MarketValueJpy = marketValueJpy,
                UnrealizedGainLossJpy = holdings.Sum(x => x.UnrealizedGainLossJpy),
                Currency = first.Currency,
                PurchaseExchangeRate = 1m,
                CurrentExchangeRate = 1m,
                SnapshotDate = holdings.Max(x => x.SnapshotDate),
                Source = first.Source
            });
        }

        return aggregated;
    }

    private static string BuildAggregationKey(BrokerHoldingRecord record) =>
        PositionIdentityService.BuildKey(record);

    private static string ResolvePreviousSectionTitle(IReadOnlyList<string> lines, int headerIndex)
    {
        for (var i = headerIndex - 1; i >= 0; i--)
        {
            var line = lines[i].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(line) &&
                !line.Contains(",", StringComparison.Ordinal) &&
                !line.EndsWith("合計", StringComparison.Ordinal))
            {
                const string sectionPrefix = "保有状況-金額指定-";
                if (line.StartsWith(sectionPrefix, StringComparison.Ordinal))
                {
                    line = line[sectionPrefix.Length..];
                }

                var countSuffixIndex = line.LastIndexOf(" (", StringComparison.Ordinal);
                if (countSuffixIndex >= 0 && line.EndsWith("件)", StringComparison.Ordinal))
                {
                    line = line[..countSuffixIndex];
                }

                return line.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeTradeType(string value)
    {
        if (value.Contains("売", StringComparison.Ordinal))
        {
            return "現物売却";
        }

        if (value.Contains("買", StringComparison.Ordinal))
        {
            return "現物買付";
        }

        return value;
    }

    private static bool IsSellTrade(string tradeType) =>
        tradeType.Contains("売", StringComparison.Ordinal);

    private static string ResolveCurrency(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Contains("米国ドル", StringComparison.Ordinal)) return "USD";
        if (normalized.Contains("日本円", StringComparison.Ordinal) || normalized is "円" or "YEN") return "JPY";
        if (normalized.Contains("ユーロ", StringComparison.Ordinal)) return "EUR";
        if (normalized.Contains("豪ドル", StringComparison.Ordinal)) return "AUD";
        if (normalized.Contains("英ポンド", StringComparison.Ordinal)) return "GBP";
        if (normalized.Length == 0)
            throw new InvalidOperationException("SBI外国株式CSVの通貨が空欄です。");
        return normalized;
    }

    private static string ResolveDomesticTicker(string code, string name)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code.Trim().ToUpperInvariant();
        }

        return $"FUND:{name.Trim()}";
    }

    private static (string Name, string Ticker) SplitSbiForeignName(string rawName)
    {
        var left = rawName.Split('/')[0].Trim();
        var lastSpace = left.LastIndexOf(' ');
        if (lastSpace < 0 || lastSpace == left.Length - 1)
        {
            return (left, string.Empty);
        }

        var ticker = left[(lastSpace + 1)..].Trim().ToUpperInvariant();
        var name = left[..lastSpace].Trim();
        return ticker.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-')
            ? (name, ticker)
            : (left, string.Empty);
    }

    private static Dictionary<string, string> ToDictionary(
        IReadOnlyList<string> headers, IReadOnlyList<string> values, int recordNumber = 0) =>
        CsvRowValidation.ToDictionary(headers, values, recordNumber);

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim().Trim('"') : string.Empty;

    private static string GetAny(IReadOnlyDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Get(row, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static DateTime ParseJapaneseDate(string value) =>
        DateTime.TryParseExact(value, "yyyy年MM月dd日", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : ParseDate(value);

    private static DateTime ParseShortDate(string value)
    {
        if (DateTime.TryParseExact(value, "yy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return ParseDate(value);
    }

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException($"日付を読み取れません: {value}");

    private static decimal ParseDecimal(string value)
    {
        var normalized = value
            .Replace(",", string.Empty)
            .Replace("+", string.Empty)
            .Replace("口", string.Empty)
            .Replace("--", string.Empty)
            .Trim();
        if (normalized.Length == 0) return 0m;
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result : throw new InvalidOperationException($"数値を読み取れません: {value}");
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString());
        return values;
    }
}
