using System.Globalization;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class SecurityIdentityService
{
    public static string BuildCanonicalKey(StockPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return BuildCanonicalKey(position.Stock, position.MutualFund);
    }

    public static string BuildCanonicalKey(Stock stock, MutualFundHolding? fund = null)
    {
        ArgumentNullException.ThrowIfNull(stock);

        var assetType = PositionIdentityService.NormalizeAssetType(stock.AssetType);
        if (AssetTypes.IsMutualFund(assetType))
        {
            var fundCode = FirstNonBlank(
                fund?.FundCode,
                fund?.AssociationCode,
                stock.Ticker,
                fund?.FundName,
                stock.Name);
            return $"FUND:{ResolveCountrySegment(stock)}:{NormalizeKeyPart(fundCode)}";
        }

        var ticker = FirstNonBlank(stock.Ticker, stock.Name);
        var market = ResolveMarketSegment(stock);
        return $"EQUITY:{ResolveCountrySegment(stock)}:{market}:{NormalizeTickerForKey(ticker)}";
    }

    public static string BuildCanonicalKey(BrokerHoldingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.IsMutualFund)
        {
            var fundCode = FirstNonBlank(
                record.FundCode,
                record.AssociationCode,
                record.Ticker,
                record.FundName,
                record.Name);
            return $"FUND:{ResolveCountrySegment(record.Currency, record.Market)}:{NormalizeKeyPart(fundCode)}";
        }

        return $"EQUITY:{ResolveCountrySegment(record.Currency, record.Market)}:{ResolveMarketSegment(record.Market, record.Currency)}:{NormalizeTickerForKey(FirstNonBlank(record.Ticker, record.Name))}";
    }

    public static string BuildPositionKey(StockPosition position)
    {
        return string.Join(
            "|",
            PositionIdentityService.NormalizeBroker(position.Stock.Broker),
            BuildCanonicalKey(position),
            PositionIdentityService.NormalizeAssetType(position.Stock.AssetType),
            AccountTypeNormalizer.Normalize(position.Stock.AccountType),
            PositionIdentityService.NormalizeCustodyType(position.Stock.CustodyType, position.Stock.AccountType),
            PositionIdentityService.NormalizeCurrency(position.Stock.Currency));
    }

    public static string NormalizeKeyPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var normalized = value.Normalize(NormalizationForm.FormKC)
            .Trim()
            .ToUpperInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.SpaceSeparator or UnicodeCategory.DashPunctuation or UnicodeCategory.ConnectorPunctuation)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch) || ch is ':' or '&' or '+' or '.')
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "UNKNOWN" : builder.ToString();
    }

    private static string NormalizeTickerForKey(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return "UNKNOWN";
        }

        var normalized = SecuritySymbolNormalizer.NormalizeTicker(ticker);
        return NormalizeKeyPart(normalized);
    }

    private static string ResolveCountrySegment(Stock stock)
    {
        if (IsJapan(stock.Country, stock.Currency, stock.Market))
        {
            return "JP";
        }

        if (IsUs(stock.Country, stock.Currency, stock.Market))
        {
            return "US";
        }

        return NormalizeKeyPart(FirstNonBlank(stock.Country, stock.Currency, "UNKNOWN"));
    }

    private static string ResolveCountrySegment(string currency, string market)
    {
        if (IsJapan(string.Empty, currency, market))
        {
            return "JP";
        }

        if (IsUs(string.Empty, currency, market))
        {
            return "US";
        }

        return NormalizeKeyPart(FirstNonBlank(currency, market, "UNKNOWN"));
    }

    private static string ResolveMarketSegment(Stock stock) =>
        ResolveMarketSegment(stock.Market, stock.Currency);

    private static string ResolveMarketSegment(string market, string currency)
    {
        if (!string.IsNullOrWhiteSpace(market))
        {
            return NormalizeKeyPart(market);
        }

        var normalizedCurrency = PositionIdentityService.NormalizeCurrency(currency);
        return normalizedCurrency == "JPY" ? "TSE" : normalizedCurrency == "USD" ? "US" : "UNKNOWN";
    }

    private static bool IsJapan(string country, string currency, string market)
    {
        var key = NormalizeKeyPart($"{country}{currency}{market}");
        return key.Contains("JP", StringComparison.Ordinal) ||
               key.Contains("JAPAN", StringComparison.Ordinal) ||
               key.Contains("JPY", StringComparison.Ordinal) ||
               key.Contains("TSE", StringComparison.Ordinal);
    }

    private static bool IsUs(string country, string currency, string market)
    {
        var key = NormalizeKeyPart($"{country}{currency}{market}");
        return key.Contains("US", StringComparison.Ordinal) ||
               key.Contains("USA", StringComparison.Ordinal) ||
               key.Contains("USD", StringComparison.Ordinal) ||
               key.Contains("NASDAQ", StringComparison.Ordinal) ||
               key.Contains("NYSE", StringComparison.Ordinal);
    }

    private static string FirstNonBlank(params string?[] values)
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
