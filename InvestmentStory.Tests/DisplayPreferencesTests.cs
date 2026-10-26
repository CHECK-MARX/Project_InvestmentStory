using InvestmentStory.App.Infrastructure;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Tests;

public sealed class DisplayPreferencesTests
{
    [Fact]
    public void SettingsSaveChangesDensityWithoutResettingPrivacyOrSimulationPlan()
    {
        var stored = new AppSettings
        {
            IsPrivacyMode = true,
            DividendSimulationPlanJson = "{\"name\":\"saved plan\"}",
            CsvWatchFolder = "watch-folder",
            DisplayDensity = "Compact"
        };
        var vm = new SettingsViewModel(() => stored, value => stored = value, () => []);
        Assert.Equal("コンパクト", vm.DisplayDensity);
        vm.DisplayDensity = "大きな文字";
        vm.SaveCommand.Execute(null);

        Assert.Equal("LargeText", stored.DisplayDensity);
        Assert.True(stored.IsPrivacyMode);
        Assert.Equal("{\"name\":\"saved plan\"}", stored.DividendSimulationPlanJson);
        Assert.Equal("watch-folder", stored.CsvWatchFolder);
    }

    [Fact]
    public void DensityValuesAreBoundedAndLargeTextIncreasesGridHeight()
    {
        Assert.Equal("Standard", UiDensityManager.Normalize("unexpected"));
        Assert.True(UiDensityManager.FontSize("LargeText") > UiDensityManager.FontSize("Standard"));
        Assert.True(UiDensityManager.FontSize("Compact") < UiDensityManager.FontSize("Standard"));
    }
}
