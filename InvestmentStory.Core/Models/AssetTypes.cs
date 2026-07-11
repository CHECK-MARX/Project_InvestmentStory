namespace InvestmentStory.Core.Models;

public static class AssetTypes
{
    public const string Stock = "Stock";
    public const string MutualFund = "MutualFund";

    public static bool IsMutualFund(string assetType) =>
        string.Equals(assetType, MutualFund, StringComparison.OrdinalIgnoreCase);
}
