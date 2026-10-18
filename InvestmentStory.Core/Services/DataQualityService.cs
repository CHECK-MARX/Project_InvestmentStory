using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DataQualityService
{
    public IReadOnlyList<DataQualityInfo> BuildForPosition(
        StockPosition position, DateTime asOf, IReadOnlyList<BrokerTrade>? trades = null)
    {
        var stockId = position.Stock.Id;
        var snapshot = new InvestmentCalculator().CreateSnapshot(position);
        var quickRegistrationDate = !position.IsMutualFund &&
            position.Purchase.Memo.Contains("かんたん登録", StringComparison.Ordinal);
        var firstDocumentedBuy = PositionEvidenceService.EarliestDocumentedBuyDate(
            stockId, trades ?? Array.Empty<BrokerTrade>());
        var purchaseDateAfterBuy = !position.IsMutualFund && firstDocumentedBuy != DateTime.MinValue &&
            position.Purchase.PurchaseDate != DateTime.MinValue &&
            firstDocumentedBuy < position.Purchase.PurchaseDate.Date;
        var items = new List<DataQualityInfo>
        {
            Create(
                stockId,
                "円建て取得額",
                snapshot.HasKnownPurchaseCostJpy ? $"{snapshot.PurchaseTotalJpy:N0}円" : "未確認",
                PositionEvidenceService.CostQuality(snapshot) == AcquisitionCostQuality.Estimated ? DataSourceTypes.Estimated :
                    snapshot.HasKnownPurchaseCostJpy ? ResolveSourceType(position.Stock.DataSource) : DataSourceTypes.Unknown,
                position.IsMutualFund ? position.Stock.DataSource :
                    snapshot.IsReferencePurchaseCostJpy ? position.Purchase.ReferenceAcquisitionSource :
                    position.Purchase.AcquisitionAmountJpy > 0m ? position.Purchase.AcquisitionAmountSource : position.Purchase.ExchangeRateSource,
                position.IsMutualFund ? position.CurrentHolding.UpdatedAt : position.Purchase.PurchaseDate,
                PositionEvidenceService.CostQuality(snapshot) switch
                {
                    AcquisitionCostQuality.Estimated => DataQualityStates.Estimated,
                    AcquisitionCostQuality.Unconfirmed => DataQualityStates.Missing,
                    _ => DataQualityStates.Confirmed
                },
                isEstimated: snapshot.IsReferencePurchaseCostJpy,
                memo: !snapshot.HasKnownPurchaseCostJpy ? position.IsMutualFund
                    ? "取得額と平均取得価額が不明なため損益集計から除外します。"
                    : position.Purchase.Shares <= 0m || position.Purchase.UnitPrice <= 0m
                        ? "取得数量または取得単価が不明なため損益集計から除外します。"
                        : position.Purchase.PurchaseDate == DateTime.MinValue
                            ? "取得日が不明です。取得時の円決済額または取得日の参考為替を確認するまで損益集計から除外します。"
                        : "取得時の円決済額・為替が不明なため損益集計から除外します。外貨の初期値1は換算に使いません。" :
                    snapshot.IsReferencePurchaseCostJpy ? "参考為替による換算です。実際の円決済額ではありません。適用日・出所を確認してください。" : string.Empty),
            Create(
                stockId,
                "取得日",
                position.Purchase.PurchaseDate == DateTime.MinValue ? "未確認" :
                    position.Purchase.PurchaseDate.ToString("yyyy/MM/dd"),
                purchaseDateAfterBuy ? DataSourceTypes.TransactionCsv :
                    quickRegistrationDate ? DataSourceTypes.Manual : ResolveSourceType(position.Stock.DataSource),
                purchaseDateAfterBuy ? "取引履歴との照合" :
                    quickRegistrationDate ? "かんたん登録" : position.Stock.DataSource,
                position.Purchase.PurchaseDate,
                position.Purchase.PurchaseDate == DateTime.MinValue ? DataQualityStates.Missing :
                    purchaseDateAfterBuy ? DataQualityStates.Conflict :
                    quickRegistrationDate ? DataQualityStates.Estimated : DataQualityStates.Reliable,
                memo: position.Purchase.PurchaseDate == DateTime.MinValue
                    ? "取得日が不明です。取引報告書や取引履歴で確認してください。"
                    : purchaseDateAfterBuy
                        ? $"記録された取得日より前の買付履歴があります（最初の買付 {firstDocumentedBuy:yyyy/MM/dd}）。売却・再購入や移管の可能性もあるため、取得日と現在の原価の対応を確認してください。日付は自動変更しません。"
                    : quickRegistrationDate
                        ? "かんたん登録の取得日です。以前の登録では保存日が自動設定された可能性があるため、取引履歴で照合してください。"
                        : string.Empty,
                hasConflict: purchaseDateAfterBuy,
                conflictDescription: purchaseDateAfterBuy ? "取得日より前に買付履歴あり。現保有分との対応は未確定。" : string.Empty),
            Create(
                stockId,
                "保有数量",
                FormatQuantity(position),
                ResolveSourceType(position.Stock.DataSource),
                position.Stock.DataSource,
                position.CurrentHolding.UpdatedAt,
                CurrentQuantity(position) > 0m ? DataQualityStates.Confirmed : DataQualityStates.Missing,
                memo: CurrentQuantity(position) > 0m ? string.Empty : "保有数量が未取得です。"),
            Create(
                stockId,
                "平均取得単価",
                (position.IsMutualFund ? position.MutualFund.AverageCostNav : position.Purchase.UnitPrice) <= 0m
                    ? "未取得" : position.IsMutualFund
                        ? $"{position.MutualFund.AverageCostNav:N0}" : $"{position.Purchase.UnitPrice:N2}",
                ResolveSourceType(position.IsMutualFund ? position.Stock.DataSource :
                    position.Purchase.ExchangeRateInputType == "CSV" ? position.Purchase.ExchangeRateSource : position.Stock.DataSource),
                position.IsMutualFund ? position.Stock.DataSource : position.Purchase.ExchangeRateSource,
                position.IsMutualFund ? position.CurrentHolding.UpdatedAt : position.Purchase.PurchaseDate,
                (position.IsMutualFund ? position.MutualFund.AverageCostNav : position.Purchase.UnitPrice) > 0m
                    ? DataQualityStates.Confirmed : DataQualityStates.Missing,
                memo: (position.IsMutualFund ? position.MutualFund.AverageCostNav : position.Purchase.UnitPrice) > 0m
                    ? string.Empty : "平均取得単価が未取得です。"),
            Create(
                stockId,
                position.IsMutualFund ? "基準価額" : "現在株価",
                !HasCurrentPrice(position) ? "未取得" :
                    position.IsMutualFund ? $"{position.MutualFund.CurrentNav:N0}" : $"{position.CurrentHolding.CurrentPrice:N2}",
                ResolveSourceType(position.IsMutualFund ? position.MutualFund.NavSource : position.CurrentHolding.CurrentPriceSource),
                position.IsMutualFund ? position.MutualFund.NavSource : position.CurrentHolding.CurrentPriceSource,
                position.IsMutualFund ? position.MutualFund.NavDate : position.CurrentHolding.CurrentPriceAcquiredAt,
                HasCurrentPrice(position) ? DataQualityStates.Reliable : DataQualityStates.Missing,
                isStale: PositionEvidenceService.Freshness(position.IsMutualFund ? position.MutualFund.NavDate : position.CurrentHolding.CurrentPriceAcquiredAt, asOf) == DataFreshness.Delayed,
                memo: HasCurrentPrice(position) ? string.Empty : "現在値が未取得です。"),
            Create(
                stockId,
                "現在為替",
                position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? "対象外" :
                    PositionEvidenceService.HasUsableForeignExchangeRate(position.CurrentHolding.CurrentExchangeRate)
                        ? $"{position.CurrentHolding.CurrentExchangeRate:N2}" : "未確認",
                ResolveSourceType(position.CurrentHolding.ExchangeRateInputType),
                position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
                    ? "円建て換算不要" : position.CurrentHolding.ExchangeRateSource,
                position.CurrentHolding.ExchangeRateAcquiredAt,
                position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
                    ? DataQualityStates.Confirmed
                    : PositionEvidenceService.HasUsableForeignExchangeRate(position.CurrentHolding.CurrentExchangeRate)
                        ? DataQualityStates.Reliable : DataQualityStates.Missing,
                isStale: !position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) &&
                    PositionEvidenceService.Freshness(position.CurrentHolding.ExchangeRateAcquiredAt, asOf) == DataFreshness.Delayed,
                memo: !position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) &&
                    !PositionEvidenceService.HasUsableForeignExchangeRate(position.CurrentHolding.CurrentExchangeRate)
                    ? "現在為替が不明なため円建て評価額・損益を算出しません。初期値1は換算に使いません。" : string.Empty),
            Create(
                stockId,
                "年間配当",
                $"{position.CurrentHolding.AnnualDividendPerShare:N2}",
                ResolveSourceType(position.CurrentHolding.DividendInfoSource),
                position.CurrentHolding.DividendInfoSource,
                position.CurrentHolding.DividendInfoAcquiredAt,
                position.CurrentHolding.DividendStatus == "配当未入力"
                    ? DataQualityStates.Missing
                    : string.IsNullOrWhiteSpace(position.CurrentHolding.DividendInfoSource) ? DataQualityStates.Estimated : DataQualityStates.Reliable,
                isEstimated: string.IsNullOrWhiteSpace(position.CurrentHolding.DividendInfoSource)),
            Create(
                stockId,
                "口座区分",
                position.Stock.AccountType,
                ResolveSourceType(position.Stock.DataSource),
                position.Stock.DataSource,
                position.CurrentHolding.UpdatedAt,
                AccountTypeNormalizer.Normalize(position.Stock.AccountType) == AccountTypes.Unknown ? DataQualityStates.Missing : DataQualityStates.Confirmed,
                memo: AccountTypeNormalizer.Normalize(position.Stock.AccountType) == AccountTypes.Unknown ? "口座区分未確認。NISA非課税として扱いません。" : string.Empty)
        };

        return items;
    }

    private static DataQualityInfo Create(
        int stockId,
        string fieldName,
        string value,
        string sourceType,
        string sourceName,
        DateTime retrievedAt,
        string state,
        bool isEstimated = false,
        bool isStale = false,
        string memo = "",
        bool hasConflict = false,
        string conflictDescription = "") =>
        new()
        {
            StockId = stockId,
            FieldName = fieldName,
            Value = value,
            SourceType = sourceType,
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "未取得" : sourceName,
            RetrievedAt = retrievedAt,
            ConfidenceLevel = state,
            IsEstimated = isEstimated,
            IsStale = isStale,
            Memo = memo,
            HasConflict = hasConflict,
            ConflictDescription = conflictDescription
        };

    private static decimal CurrentQuantity(StockPosition position) =>
        position.IsMutualFund ? position.MutualFund.UnitsHeld : position.CurrentHolding.CurrentShares;

    private static string FormatQuantity(StockPosition position) =>
        position.IsMutualFund
            ? $"{position.MutualFund.UnitsHeld:N0}口"
            : $"{position.CurrentHolding.CurrentShares:N2}株";

    private static bool HasCurrentPrice(StockPosition position) =>
        position.IsMutualFund ? position.MutualFund.CurrentNav > 0m : position.CurrentHolding.CurrentPrice > 0m;

    private static string ResolveSourceType(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return DataSourceTypes.Unknown;
        }

        if (source.Contains("CSV", StringComparison.OrdinalIgnoreCase))
        {
            return DataSourceTypes.BrokerCsv;
        }

        if (source.Contains("API", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Yahoo", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Alpha", StringComparison.OrdinalIgnoreCase))
        {
            return DataSourceTypes.Api;
        }

        if (source.Contains("手入力", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            return DataSourceTypes.Manual;
        }

        return DataSourceTypes.Calculated;
    }

}
