using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class DividendSimulationSelectionOptionsTests
{
    [Fact]
    public void BrokerOptions_ContainsRequiredDefaults()
    {
        var options = DividendSimulationSelectionOptions.BrokerOptions;

        Assert.Contains(options, x => x.Value == "SBI証券" && x.DisplayName == "SBI証券");
        Assert.Contains(options, x => x.Value == "野村證券" && x.DisplayName == "野村證券");
        Assert.Contains(options, x => x.Value == "その他" && x.DisplayName == "その他");
    }

    [Fact]
    public void BuildBrokerOptions_AddsCurrentBrokerAndRemovesDuplicates()
    {
        var options = DividendSimulationSelectionOptions.BuildBrokerOptions("SBI証券", "SBI証券", "野村証券");

        Assert.Equal(options.Count, options.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Single(options, x => x.DisplayName == "SBI証券");
        Assert.Single(options, x => x.DisplayName == "野村證券");
    }

    [Fact]
    public void CountryOptions_UseJapaneseDisplayAndStableValues()
    {
        var options = DividendSimulationSelectionOptions.CountryOptions;

        Assert.Contains(options, x => x.Value == "Japan" && x.DisplayName == "日本");
        Assert.Contains(options, x => x.Value == "United States" && x.DisplayName == "米国");
        Assert.Contains(options, x => x.Value == "Other" && x.DisplayName == "その他");
    }

    [Theory]
    [InlineData("日本", "JPY", "8151", "Japan")]
    [InlineData("Japan", "JPY", "8151", "Japan")]
    [InlineData("米国", "USD", "PG", "United States")]
    [InlineData("UnitedStates", "USD", "PG", "United States")]
    public void NormalizeCountry_ReturnsExpectedValue(string country, string currency, string ticker, string expected)
    {
        var actual = DividendSimulationSelectionOptions.NormalizeCountry(country, currency, ticker);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AccountOptions_ContainRequiredAccountTypes()
    {
        var values = DividendSimulationSelectionOptions.AccountOptions.Select(x => x.Value).ToArray();

        Assert.Contains(AccountTypes.Specific, values);
        Assert.Contains(AccountTypes.General, values);
        Assert.Contains(AccountTypes.NisaGrowth, values);
        Assert.Contains(AccountTypes.NisaAccumulation, values);
        Assert.Contains(AccountTypes.NisaLegacy, values);
        Assert.Contains(AccountTypes.Unknown, values);
    }

    [Fact]
    public void PurchaseModeOptions_UseDisplayNameInsteadOfTypeName()
    {
        var options = DividendSimulationSelectionOptions.PurchaseModeOptions;

        Assert.DoesNotContain(options, x => x.ToString().Contains("SelectionOption", StringComparison.Ordinal));
        Assert.Contains(options, x => x.Value == DividendGrowthPurchaseModes.OneTime && x.DisplayName == "今回だけ購入");
    }
}
