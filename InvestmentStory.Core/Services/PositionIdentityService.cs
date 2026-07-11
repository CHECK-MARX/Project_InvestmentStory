using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class PositionIdentityService
{
    public static string BuildKey(StockPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        return BuildKey(
            position.Stock.Broker,
            ResolveSecurityId(position),
            position.Stock.AssetType,
            position.Stock.AccountType,
            position.Stock.CustodyType,
            position.Stock.Currency);
    }

    public static string BuildKey(BrokerHoldingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return BuildKey(
            record.Broker,
            ResolveSecurityId(record),
            record.AssetType,
            record.Account,
            record.Account,
            record.Currency);
    }

    public static string BuildKey(
        string broker,
        string securityId,
        string assetType,
        string accountType,
        string custodyType,
        string currency)
    {
        return string.Join(
            "|",
            NormalizeBroker(broker),
            NormalizeSecurityId(securityId, assetType),
            NormalizeAssetType(assetType),
            AccountTypeNormalizer.Normalize(accountType),
            NormalizeCustodyType(custodyType, accountType),
            NormalizeCurrency(currency));
    }

    public static string ResolveSecurityId(StockPosition position)
    {
        if (position.IsMutualFund)
        {
            return FirstNonBlank(
                position.MutualFund.FundCode,
                position.MutualFund.AssociationCode,
                position.Stock.Ticker,
                position.MutualFund.FundName,
                position.Stock.Name);
        }

        return FirstNonBlank(position.Stock.Ticker, position.Stock.Name);
    }

    public static string ResolveSecurityId(BrokerHoldingRecord record)
    {
        if (record.IsMutualFund)
        {
            return FirstNonBlank(
                record.FundCode,
                record.AssociationCode,
                record.Ticker,
                record.FundName,
                record.Name);
        }

        return FirstNonBlank(record.Ticker, record.Name);
    }

    public static void NormalizeForPersistence(Stock stock)
    {
        stock.Broker = NormalizeBroker(stock.Broker);
        stock.AssetType = NormalizeAssetType(stock.AssetType);
        stock.Ticker = NormalizeSecurityId(stock.Ticker, stock.AssetType);
        stock.AccountType = AccountTypeNormalizer.Normalize(stock.AccountType);
        stock.CustodyType = NormalizeCustodyType(stock.CustodyType, stock.AccountType);
        stock.Currency = NormalizeCurrency(stock.Currency);
    }

    public static string NormalizeBroker(string broker)
    {
        var normalized = SecuritySymbolNormalizer.NormalizeBroker(broker ?? string.Empty);
        return NormalizeText(normalized);
    }

    public static string NormalizeSecurityId(string securityId, string assetType)
    {
        var normalized = (securityId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (AssetTypes.IsMutualFund(assetType))
        {
            return NormalizeText(normalized).ToUpperInvariant();
        }

        return SecuritySymbolNormalizer.NormalizeTicker(normalized);
    }

    public static string NormalizeAssetType(string assetType)
    {
        return AssetTypes.IsMutualFund(assetType) ? AssetTypes.MutualFund : AssetTypes.Stock;
    }

    public static string NormalizeCustodyType(string custodyType, string accountType)
    {
        var normalized = NormalizeText(custodyType ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return AccountTypeNormalizer.Normalize(accountType);
    }

    public static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant();
        return normalized is "YEN" ? "JPY" : normalized;
    }

    public static string NormalizeText(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal);
    }

    private static string FirstNonBlank(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
