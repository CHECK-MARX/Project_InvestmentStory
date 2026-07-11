using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DataQualityService
{
    public IReadOnlyList<DataQualityInfo> BuildForPosition(StockPosition position, DateTime asOf)
    {
        var stockId = position.Stock.Id;
        var items = new List<DataQualityInfo>
        {
            Create(
                stockId,
                "保有数量",
                ResolveSourceType(position.Stock.DataSource),
                position.Stock.DataSource,
                position.CurrentHolding.UpdatedAt,
                CurrentQuantity(position) > 0m ? DataQualityStates.Confirmed : DataQualityStates.Missing,
                memo: CurrentQuantity(position) > 0m ? string.Empty : "保有数量が未取得です。"),
            Create(
                stockId,
                "平均取得単価",
                ResolveSourceType(position.Purchase.ExchangeRateInputType == "CSV" ? position.Purchase.ExchangeRateSource : position.Stock.DataSource),
                position.Purchase.ExchangeRateSource,
                position.Purchase.PurchaseDate,
                position.Purchase.UnitPrice > 0m ? DataQualityStates.Confirmed : DataQualityStates.Missing,
                memo: position.Purchase.UnitPrice > 0m ? string.Empty : "平均取得単価が未取得です。"),
            Create(
                stockId,
                position.IsMutualFund ? "基準価額" : "現在株価",
                ResolveSourceType(position.IsMutualFund ? position.MutualFund.NavSource : position.CurrentHolding.CurrentPriceSource),
                position.IsMutualFund ? position.MutualFund.NavSource : position.CurrentHolding.CurrentPriceSource,
                position.IsMutualFund ? position.MutualFund.NavDate : position.CurrentHolding.CurrentPriceAcquiredAt,
                HasCurrentPrice(position) ? ResolveFreshness(position.IsMutualFund ? position.MutualFund.NavDate : position.CurrentHolding.CurrentPriceAcquiredAt, asOf) : DataQualityStates.Missing,
                isStale: IsStale(position.IsMutualFund ? position.MutualFund.NavDate : position.CurrentHolding.CurrentPriceAcquiredAt, asOf),
                memo: HasCurrentPrice(position) ? string.Empty : "現在値が未取得です。"),
            Create(
                stockId,
                "現在為替",
                ResolveSourceType(position.CurrentHolding.ExchangeRateInputType),
                position.CurrentHolding.ExchangeRateSource,
                position.CurrentHolding.ExchangeRateAcquiredAt,
                position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) || position.CurrentHolding.CurrentExchangeRate > 0m
                    ? ResolveFreshness(position.CurrentHolding.ExchangeRateAcquiredAt, asOf)
                    : DataQualityStates.Missing),
            Create(
                stockId,
                "年間配当",
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
        string sourceType,
        string sourceName,
        DateTime retrievedAt,
        string state,
        bool isEstimated = false,
        bool isStale = false,
        string memo = "") =>
        new()
        {
            StockId = stockId,
            FieldName = fieldName,
            SourceType = sourceType,
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "未取得" : sourceName,
            RetrievedAt = retrievedAt,
            ConfidenceLevel = state,
            IsEstimated = isEstimated,
            IsStale = isStale,
            Memo = memo
        };

    private static decimal CurrentQuantity(StockPosition position) =>
        position.IsMutualFund ? position.MutualFund.UnitsHeld : position.CurrentHolding.CurrentShares;

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

    private static string ResolveFreshness(DateTime retrievedAt, DateTime asOf)
    {
        if (retrievedAt == DateTime.MinValue || retrievedAt == default)
        {
            return DataQualityStates.Missing;
        }

        return IsStale(retrievedAt, asOf) ? DataQualityStates.Stale : DataQualityStates.Reliable;
    }

    private static bool IsStale(DateTime retrievedAt, DateTime asOf) =>
        retrievedAt != DateTime.MinValue && retrievedAt.Date < asOf.Date.AddDays(-7);
}
