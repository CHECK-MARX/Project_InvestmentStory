using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class DividendPurchasePlanDatePolicyTests
{
    private static readonly DateTime Today = new(2026, 8, 2);

    [Theory]
    [InlineData(2000, 2026)]
    [InlineData(2025, 2026)]
    [InlineData(2047, 2026)]
    [InlineData(2026, 2026)]
    [InlineData(2046, 2046)]
    public void NormalizeYear_RestrictsPurchasePlansToTheCurrentPlanningWindow(int input, int expected)
    {
        Assert.Equal(expected, DividendPurchasePlanDatePolicy.NormalizeYear(input, Today));
    }

    [Fact]
    public void NormalizePurchaseDate_PreservesMonthAndDayWhileRepairingAnInvalidSavedYear()
    {
        var result = DividendPurchasePlanDatePolicy.NormalizePurchaseDate(
            new DateTime(2000, 7, 14),
            2000,
            Today);

        Assert.Equal(new DateTime(2026, 7, 14), result);
    }
}
