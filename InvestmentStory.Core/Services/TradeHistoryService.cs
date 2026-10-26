using System.Globalization;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class TradeHistoryService
{
    public static IReadOnlyList<BrokerTradeRecord> Merge(
        IEnumerable<BrokerTrade> existing,
        IEnumerable<BrokerTradeRecord> incoming,
        string ticker)
    {
        var imported = existing
            .Where(x => !(x.TradeType == "InitialPosition" && x.Source == "HoldingsSnapshot"))
            .Select(x => new BrokerTradeRecord
            {
                TradeDate = x.TradeDate,
                SettlementDate = x.SettlementDate,
                Broker = x.Broker,
                Account = x.CustodyType,
                Ticker = ticker,
                TradeType = x.TradeType,
                Quantity = x.Quantity,
                SignedQuantity = x.SignedQuantity,
                UnitPrice = x.UnitPrice,
                Currency = x.Currency,
                ExchangeRate = x.ExchangeRate,
                SettlementAmountJpy = x.SettlementAmountJpy,
                FeeJpy = x.FeeJpy,
                Source = x.Source,
                SourceFile = x.SourceFile,
                SourceRowNumber = x.SourceRowNumber
            });

        var previous = imported.GroupBy(GetIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var latest = incoming.GroupBy(GetIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        return previous.Keys.Union(latest.Keys, StringComparer.OrdinalIgnoreCase)
            .SelectMany(key => MergeEquivalentRows(
                previous.GetValueOrDefault(key) ?? new List<BrokerTradeRecord>(),
                latest.GetValueOrDefault(key) ?? new List<BrokerTradeRecord>()))
            .OrderBy(x => x.TradeDate)
            .ThenBy(x => x.SettlementDate)
            .ToList();
    }

    public static TradeReconciliationResult Reconcile(
        IEnumerable<BrokerTrade> existing,
        IEnumerable<BrokerTradeRecord> incoming,
        string ticker)
    {
        var previous = Merge(existing, Array.Empty<BrokerTradeRecord>(), ticker)
            .GroupBy(GetIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var previousRows = previous.Values.SelectMany(x => x).ToList();
        var accepted = new List<BrokerTradeRecord>();
        var pending = new List<BrokerTradeRecord>();
        foreach (var group in incoming.GroupBy(GetIdentityKey, StringComparer.OrdinalIgnoreCase))
        {
            var prior = previous.GetValueOrDefault(group.Key) ?? new List<BrokerTradeRecord>();
            var sourceFiles = group.Where(HasSourceRow).Select(x => x.SourceFile.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var row in group)
            {
                string? reason = null;
                if (previousRows.Any(x => SameSourceRow(x, row) &&
                    !string.Equals(GetIdentityKey(x), group.Key, StringComparison.OrdinalIgnoreCase)))
                    reason = "同じCSV行の取引内容が既存記録と異なります。修正理由を確認してください。";
                else if (sourceFiles.Count > 1 && !prior.Any(x => SameSourceRow(x, row)))
                    reason = "別CSVに同条件の取引があり、取引番号がないため同一取引か別取引か確定できません。";
                else if (HasSourceRow(row) && prior.Count > 0 &&
                         !prior.Any(x => SameSourceRow(x, row)) &&
                         prior.Any(x => !HasSourceRow(x) ||
                             !string.Equals(x.SourceFile, row.SourceFile, StringComparison.OrdinalIgnoreCase)))
                    reason = "既存の同条件取引と元CSV行を照合できません。別取引の可能性があるため保留します。";

                if (reason is not null && row.ApprovedAsDistinctTrade && HasSourceRow(row) &&
                    !reason.StartsWith("同じCSV行", StringComparison.Ordinal))
                    reason = null;

                if (reason is null) accepted.Add(row);
                else
                {
                    row.ReviewReason = reason;
                    pending.Add(row);
                }
            }
        }
        return new TradeReconciliationResult(accepted, pending);
    }

    private static IReadOnlyList<BrokerTradeRecord> MergeEquivalentRows(
        List<BrokerTradeRecord> prior, List<BrokerTradeRecord> current)
    {
        if (prior.Count == 0) return current;
        if (current.Count == 0) return prior;
        if (prior.All(HasSourceRow) && current.All(HasSourceRow) &&
            current.All(row => row.ApprovedAsDistinctTrade ||
                prior.Any(old => SameSourceRow(old, row)) ||
                prior.All(old => string.Equals(old.SourceFile, row.SourceFile,
                    StringComparison.OrdinalIgnoreCase))))
        {
            var seenRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return current.Concat(prior).Where(x => seenRows.Add(SourceRowKey(x))).ToList();
        }
        if (current.All(x => x.ApprovedAsDistinctTrade && HasSourceRow(x)))
        {
            var seenRows = prior.Where(HasSourceRow).Select(SourceRowKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return prior.Concat(current.Where(x => seenRows.Add(SourceRowKey(x)))).ToList();
        }

        // Legacy rows lack line provenance. Preserve the larger multiplicity until
        // an operator resolves a cross-file ambiguity; never manufacture duplicates.
        return current.Count >= prior.Count ? current : prior;
    }

    private static bool HasSourceRow(BrokerTradeRecord row) =>
        row.SourceRowNumber > 0 && !string.IsNullOrWhiteSpace(row.SourceFile);

    private static bool SameSourceRow(BrokerTradeRecord left, BrokerTradeRecord right) =>
        HasSourceRow(left) && HasSourceRow(right) &&
        left.SourceRowNumber == right.SourceRowNumber &&
        string.Equals(left.SourceFile, right.SourceFile, StringComparison.OrdinalIgnoreCase);

    private static string SourceRowKey(BrokerTradeRecord row) =>
        $"{row.SourceFile.Trim()}|{row.SourceRowNumber}";

    public static string GetIdentityKey(BrokerTradeRecord x) => string.Join("|",
        x.TradeDate.Date.ToString("yyyyMMdd"),
        (x.SettlementDate == DateTime.MinValue ? x.TradeDate : x.SettlementDate).Date.ToString("yyyyMMdd"),
        PositionIdentityService.NormalizeBroker(x.Broker),
        AccountTypeNormalizer.Normalize(x.Account),
        NormalizeType(x.TradeType),
        NormalizeDecimal(x.SignedQuantity),
        NormalizeDecimal(x.Quantity),
        NormalizeDecimal(decimal.Round(x.UnitPrice, 8, MidpointRounding.AwayFromZero)),
        NormalizeDecimal(x.SettlementAmountJpy),
        PositionIdentityService.NormalizeCurrency(x.Currency));

    public static string GetSourceContentKey(BrokerTradeRecord trade) => string.Join("|",
        trade.Ticker.Trim().ToUpperInvariant(), GetIdentityKey(trade),
        NormalizeDecimal(trade.FeeJpy), NormalizeDecimal(trade.ExchangeRate),
        NormalizeDecimal(trade.ProfitLossJpy), trade.Source.Trim());

    private static string NormalizeType(string type) =>
        type.Contains("買", StringComparison.Ordinal) || type.Equals("Buy", StringComparison.OrdinalIgnoreCase) ? "Buy" :
        type.Contains("売", StringComparison.Ordinal) || type.Equals("Sell", StringComparison.OrdinalIgnoreCase) ? "Sell" :
        type.Contains("入庫", StringComparison.Ordinal) ||
        type.Equals("TransferIn", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("StockSplit", StringComparison.OrdinalIgnoreCase) ? "TransferIn" :
        type;

    private static string NormalizeDecimal(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}

public sealed record TradeReconciliationResult(
    IReadOnlyList<BrokerTradeRecord> Accepted,
    IReadOnlyList<BrokerTradeRecord> NeedsReview);
