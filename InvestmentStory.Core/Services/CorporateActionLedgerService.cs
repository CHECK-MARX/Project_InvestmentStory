using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class CorporateActionLedgerService
{
    public static void Validate(CorporateAction action)
    {
        if (action.StockId <= 0 || action.EventDate == default)
            throw new ArgumentException("銘柄と発生日を指定してください。");
        if (action.Kind is not ("OpeningBalance" or "Transfer" or "StockSplit" or "TickerChange"))
            throw new ArgumentException("未対応の企業行動です。");
        if (action.Status is not ("Pending" or "Confirmed"))
            throw new ArgumentException("確認状態が不正です。");
        if (action.Quantity < 0m || action.CarriedCostJpy < 0m || action.SourceRowNumber < 0)
            throw new ArgumentException("数量・原価・行番号に負数は指定できません。");
        if (action.SupersedesId.HasValue &&
            (action.SupersedesId <= 0 || string.IsNullOrWhiteSpace(action.Reason)))
            throw new ArgumentException("訂正には対象IDと理由が必要です。");
        if (action.Status == "Pending") return;
        if (string.IsNullOrWhiteSpace(action.Evidence))
            throw new ArgumentException("確認済みイベントには根拠が必要です。");
        if (action.Kind == "TickerChange")
        {
            if (string.IsNullOrWhiteSpace(action.OldTicker) || string.IsNullOrWhiteSpace(action.NewTicker))
                throw new ArgumentException("銘柄変更の前後コードが必要です。");
            return;
        }
        if (action.Kind == "StockSplit")
        {
            if (action.SplitRatio <= 0m || action.SplitRatio == 1m ||
                !KnownAccount(action.SourceBroker, action.SourceAccount))
                throw new ArgumentException("分割比率と対象口座が必要です。");
            return;
        }
        if (action.Quantity <= 0m || !KnownAccount(action.DestinationBroker, action.DestinationAccount))
            throw new ArgumentException("数量と移管先・残高口座が必要です。");
        if (action.Kind == "Transfer" &&
            (!KnownAccount(action.SourceBroker, action.SourceAccount) ||
             Key(action.SourceBroker, action.SourceAccount) == Key(action.DestinationBroker, action.DestinationAccount)))
            throw new ArgumentException("異なる移管元・移管先の口座を明示してください。");
    }

    public static CorporateActionBalance Reconstruct(
        IEnumerable<CorporateAction> events, DateTime asOf) =>
        Reconstruct(events, Array.Empty<BrokerTrade>(), asOf);

    public static CorporateActionBalance Reconstruct(
        IEnumerable<CorporateAction> events, IEnumerable<BrokerTrade> trades, DateTime asOf)
    {
        var balances = new Dictionary<string, AccountBalance>(StringComparer.OrdinalIgnoreCase);
        var hasOpening = false;
        var allEvents = events.ToList();
        var supersededIds = allEvents.Where(x => x.SupersedesId.HasValue)
            .Select(x => x.SupersedesId!.Value).ToHashSet();
        var activeEvents = allEvents.Where(x => !supersededIds.Contains(x.Id) &&
            x.EventDate.Date <= asOf.Date).ToList();
        var openingDates = activeEvents.Where(x => x.Kind == "OpeningBalance" && x.Status == "Confirmed")
            .GroupBy(x => Key(x.DestinationBroker, x.DestinationAccount), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Min(y => y.EventDate.Date), StringComparer.OrdinalIgnoreCase);
        var timeline = activeEvents
            .Select(x => (Date: x.EventDate.Date, Order: 0, Id: x.Id,
                Action: (CorporateAction?)x, Trade: (BrokerTrade?)null))
            .Concat(trades.Where(x => x.SettlementDate.Date <= asOf.Date &&
                    x.TradeType is not ("InitialPosition" or "OpeningBalance"))
                .Select(x => (Date: x.SettlementDate.Date, Order: 1, Id: (long)x.Id,
                    Action: (CorporateAction?)null, Trade: (BrokerTrade?)x)))
            .OrderBy(x => x.Date).ThenBy(x => x.Order).ThenBy(x => x.Id);
        foreach (var item in timeline)
        {
            if (item.Trade is { } trade)
            {
                if (trade.TradeType is "TransferIn" or "StockSplitCandidate" or "StockSplit" or "TransferOut")
                {
                    if (trade.SourceRowNumber > 0 && allEvents.Any(x =>
                            x.StockId == trade.StockId &&
                            x.SourceFile == trade.SourceFile &&
                            x.SourceRowNumber == trade.SourceRowNumber))
                        continue; // The dedicated event (or its correction) accounts for this row.
                    return Incomplete(asOf, "未確認の入出庫または分割取引があります。", balances);
                }
                var accountKey = Key(trade.Broker, trade.AccountType);
                if (openingDates.TryGetValue(accountKey, out var openingDate) && item.Date <= openingDate)
                    continue; // The opening balance already includes this settled trade.
                if (!balances.TryGetValue(accountKey, out var accountBalance) ||
                    !KnownAccount(trade.Broker, trade.AccountType))
                    return Incomplete(asOf, "取引口座の開始残高がありません。", balances);
                var signed = trade.SignedQuantity;
                if (signed > 0m)
                {
                    decimal? purchaseCost = trade.SettlementAmountJpy > 0m
                        ? trade.SettlementAmountJpy : null;
                    balances[accountKey] = accountBalance with
                    {
                        Quantity = accountBalance.Quantity + signed,
                        CostJpy = accountBalance.CostJpy + purchaseCost
                    };
                }
                else if (signed < 0m)
                {
                    var quantity = Math.Abs(signed);
                    if (accountBalance.Quantity < quantity)
                        return Incomplete(asOf, "売却数量が口座の時点残高を超えています。", balances);
                    var removedCost = accountBalance.CostJpy * quantity / accountBalance.Quantity;
                    balances[accountKey] = accountBalance with
                    {
                        Quantity = accountBalance.Quantity - quantity,
                        CostJpy = accountBalance.CostJpy - removedCost
                    };
                }
                continue;
            }
            var action = item.Action!;
            if (action.Status != "Confirmed")
                return Incomplete(asOf, "未確認の移管・企業行動があります。", balances);
            try { Validate(action); }
            catch (ArgumentException ex) { return Incomplete(asOf, ex.Message, balances); }

            var destination = Key(action.DestinationBroker, action.DestinationAccount);
            var source = Key(action.SourceBroker, action.SourceAccount);
            if (action.Kind == "OpeningBalance")
            {
                hasOpening = true;
                if (balances.ContainsKey(destination))
                    return Incomplete(asOf, "同じ口座の開始残高が重複しています。", balances);
                balances[destination] = new AccountBalance(
                    action.DestinationBroker, action.DestinationAccount, action.Quantity, action.CarriedCostJpy);
            }
            else if (action.Kind == "Transfer")
            {
                if (!balances.TryGetValue(source, out var sourceBalance) || sourceBalance.Quantity < action.Quantity)
                    return Incomplete(asOf, "移管元の時点残高が不足しています。", balances);
                var inheritedCost = sourceBalance.CostJpy.HasValue
                    ? sourceBalance.CostJpy.Value * action.Quantity / sourceBalance.Quantity : action.CarriedCostJpy;
                if (sourceBalance.CostJpy.HasValue && action.CarriedCostJpy.HasValue &&
                    Math.Abs(inheritedCost!.Value - action.CarriedCostJpy.Value) > 0.01m)
                    return Incomplete(asOf, "引継原価が移管元の按分原価と一致しません。", balances);
                var to = balances.GetValueOrDefault(destination) ??
                    new AccountBalance(action.DestinationBroker, action.DestinationAccount, 0m, 0m);
                balances[source] = sourceBalance with
                {
                    Quantity = sourceBalance.Quantity - action.Quantity,
                    CostJpy = sourceBalance.CostJpy - inheritedCost
                };
                balances[destination] = to with
                {
                    Quantity = to.Quantity + action.Quantity,
                    CostJpy = to.CostJpy + inheritedCost
                };
            }
            else if (action.Kind == "StockSplit")
            {
                if (!balances.TryGetValue(source, out var sourceBalance))
                    return Incomplete(asOf, "分割対象口座の開始残高がありません。", balances);
                if (action.Quantity > 0m && action.Quantity != sourceBalance.Quantity)
                    return Incomplete(asOf, "分割前数量が時点残高と一致しません。", balances);
                balances[source] = sourceBalance with { Quantity = sourceBalance.Quantity * action.SplitRatio };
            }
            // Ticker changes alter the security identifier, not quantity or cost.
        }
        return hasOpening
            ? new CorporateActionBalance(asOf, true, string.Empty, balances.Values.ToList())
            : Incomplete(asOf, "開始残高とその根拠がありません。", balances);
    }

    private static CorporateActionBalance Incomplete(DateTime asOf, string reason,
        Dictionary<string, AccountBalance> balances) =>
        new(asOf, false, reason, balances.Values.ToList());

    private static bool KnownAccount(string broker, string account) =>
        !string.IsNullOrWhiteSpace(broker) && !string.IsNullOrWhiteSpace(account) &&
        !account.Equals(AccountTypes.Unknown, StringComparison.OrdinalIgnoreCase);

    private static string Key(string broker, string account) =>
        $"{broker.Trim().ToUpperInvariant()}|{account.Trim().ToUpperInvariant()}";
}
