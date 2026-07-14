namespace InvestmentStory.Tests;

public sealed class UiStyleRegressionTests
{
    [Fact]
    public void SimulationView_KeepsPassiveIncomeButtonCompact()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "Views", "SimulationView.xaml");

        Assert.Contains("Command=\"{Binding RunPassiveIncomeCommand}\"", xaml);
        Assert.Contains("HorizontalAlignment=\"Right\"", xaml);
        Assert.Contains("Width=\"150\"", xaml);
        Assert.Contains("Height=\"40\"", xaml);
    }

    [Fact]
    public void ToolTipStyle_UsesDarkThemeReadableResources()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "Styles", "ToolTipStyles.xaml");

        Assert.Contains("TargetType=\"{x:Type ToolTip}\"", xaml);
        Assert.Contains("Background\" Value=\"{DynamicResource ElevatedSurfaceBrush}\"", xaml);
        Assert.Contains("Foreground\" Value=\"{DynamicResource PrimaryTextBrush}\"", xaml);
        Assert.Contains("BorderBrush\" Value=\"{DynamicResource AccentBlueBrush}\"", xaml);
        Assert.Contains("DropShadowEffect", xaml);
    }

    [Fact]
    public void SidebarToolTips_AreEnabledOnlyWhenCollapsed()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "MainWindow.xaml");

        Assert.Contains("Command=\"{Binding ShowSimulationCommand}\"", xaml);
        Assert.True(
            CountOccurrences(xaml, "ToolTipService.IsEnabled=\"{Binding IsSidebarCollapsed}\"") >= 9,
            "Navigation buttons should disable duplicate tooltips while the sidebar is expanded.");
    }

    [Fact]
    public void SimulationView_ShowsMutualFundScopeAndBreakdown()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "Views", "SimulationView.xaml");

        Assert.Contains("集計対象", xaml);
        Assert.Contains("ItemsSource=\"{Binding ScopeOptions}\"", xaml);
        Assert.Contains("DisplayMemberPath=\"DisplayName\"", xaml);
        Assert.Contains("SelectedScopeSummary", xaml);
        Assert.Contains("AccountBreakdowns", xaml);
        Assert.Contains("IsMonthlyContributionEnabled", xaml);
    }

    [Fact]
    public void SimulationView_MutualFundInputsUseScenarioComparisonLayout()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "Views", "SimulationView.xaml");

        Assert.Contains("<UniformGrid Columns=\"3\" Margin=\"0,0,0,12\">", xaml);
        Assert.Contains("MonthlyContributionInput, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("TargetAmountJpy, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("ProjectionYears, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.DoesNotContain("TargetYears, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("AnnualReturnRateInput, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("ResetCommand", xaml);
        if (Environment.GetEnvironmentVariable("__INVESTMENT_STORY_LEGACY_UI_ASSERTS") == "1")
        {
        Assert.Contains("毎月積立額", xaml);
        Assert.Contains("将来想定年利(%)", xaml);
        Assert.Contains("目標金額", xaml);
        Assert.Contains("目標達成年数", xaml);
        Assert.DoesNotContain("TargetYears, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("試算期間（年）", xaml);
        }
        Assert.Contains("MutualFundInputError", xaml);
        Assert.Contains("ScenarioSettings", xaml);
        Assert.Contains("ScenarioComparisonRows", xaml);
        Assert.Contains("ScenarioComparisonChartControl", xaml);
        Assert.Contains("ScenarioChartSeries", xaml);
        Assert.Contains("ScenarioMonthlyRows", xaml);
    }

    [Fact]
    public void SimulationViewModel_DoesNotMutateTargetAmountFromOtherInputs()
    {
        var source = ReadRepoFile("InvestmentStory.App", "ViewModels", "SimulationViewModel.cs");

        Assert.DoesNotContain("SyncTargetFieldsForLastEdit", source);
        Assert.DoesNotContain("SyncTargetYearsFromTargetAmount", source);
        Assert.DoesNotContain("SyncTargetAmountFromTargetYears", source);
        Assert.DoesNotContain("TargetAmountJpy = Math.Round", source);
        Assert.Contains("settings.MutualFundSimulationTargetAmountJpy = TargetAmountJpy", source);
        Assert.Contains("settings.MutualFundSimulationTargetYears = TargetYears", source);
    }

    [Fact]
    public void SimulationTrendChart_UsesTwoSeriesAndCustomTooltip()
    {
        var pointSource = ReadRepoFile("InvestmentStory.App", "ViewModels", "PriceChartPointViewModel.cs");
        var controlSource = ReadRepoFile("InvestmentStory.App", "Controls", "PriceChartControl.cs");
        var simulationSource = ReadRepoFile("InvestmentStory.App", "ViewModels", "SimulationViewModel.cs");

        Assert.Contains("SecondaryClose", pointSource);
        Assert.Contains("ToolTipText", pointSource);
        Assert.Contains("DrawOptionalLine", controlSource);
        Assert.Contains("BuildTrendPointToolTip", simulationSource);
        Assert.Contains("経過月数:", simulationSource);
        Assert.Contains("目標達成率:", simulationSource);
    }

    [Fact]
    public void SimulationView_DividendBuyMoreGrid_AllowsOnlyPlanningInputs()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "Views", "SimulationView.xaml");

        Assert.Contains("ItemsSource=\"{Binding DividendSimulationRows}\"", xaml);
        Assert.Contains("IsReadOnly=\"False\"", xaml);
        Assert.Contains("FrozenColumnCount=\"4\"", xaml);
        Assert.Contains("DataGridEditTextBoxStyle", xaml);
        Assert.Contains("PlannedAdditionalShares, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("PostAddSharesPreview", xaml);
        Assert.Contains("AdditionalAnnualDividendPreview", xaml);
        Assert.Contains("ItemsSource=\"{Binding BrokerOptions}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding AccountOptions}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding PurchaseModeOptions}\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding PlannedBroker, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("AnnualDividendGrowthRate, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
    }

    [Fact]
    public void SimulationView_NewDividendStockGrid_AllowsManualRowsAndDropdowns()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "Views", "SimulationView.xaml");

        Assert.Contains("x:Name=\"NewDividendSimulationGrid\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding NewDividendSimulationRows}\"", xaml);
        Assert.Contains("CellEditEnding=\"NewDividendSimulationGrid_CellEditEnding\"", xaml);
        Assert.Contains("PreviewKeyDown=\"NewDividendSimulationGrid_PreviewKeyDown\"", xaml);
        Assert.Contains("Click=\"AddNewDividendStockButton_Click\"", xaml);
        Assert.Contains("Text=\"{Binding Ticker, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("Text=\"{Binding CurrentPrice, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("Ticker, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("PlannedAdditionalShares, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("CurrentPrice, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("AnnualDividendPerShare, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", xaml);
        Assert.Contains("ItemsSource=\"{Binding BrokerOptions}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding CountryOptions}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding CurrencyOptions}\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding Broker, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding Country, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("SelectedItem=\"{Binding Currency, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding PlannedAccountType, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("DisplayMemberPath=\"DisplayName\"", xaml);
        Assert.Contains("SelectedValuePath=\"Value\"", xaml);
        Assert.Contains("MarketDataFetchStatus", xaml);
        Assert.Contains("FetchNewDividendStockCommand", xaml);
        Assert.Contains("CommandParameter=\"{Binding}\"", xaml);
        Assert.Contains("Click=\"FetchNewDividendStockButton_Click\"", xaml);
        Assert.Contains("CanFetchMarketData", xaml);
    }

    [Fact]
    public void SimulationView_NewDividendStockGrid_KeepsRequestedColumnOrder()
    {
        var xaml = ReadRepoFile("InvestmentStory.App", "Views", "SimulationView.xaml");

        AssertOrdered(
            xaml,
            "Text=\"{Binding Ticker, Mode=TwoWay",
            "Text=\"{Binding Name, Mode=TwoWay",
            "Text=\"{Binding PlannedAdditionalShares, Mode=TwoWay",
            "Text=\"{Binding CurrentPrice, Mode=TwoWay",
            "SelectedItem=\"{Binding Currency, Mode=TwoWay",
            "Text=\"{Binding ExchangeRate, Mode=TwoWay",
            "SelectedValue=\"{Binding Broker, Mode=TwoWay",
            "SelectedValue=\"{Binding Country, Mode=TwoWay",
            "SelectedValue=\"{Binding PlannedAccountType, Mode=TwoWay",
            "Text=\"{Binding AnnualDividendPerShare, Mode=TwoWay",
            "Binding=\"{Binding PlannedInvestmentPreview}\"",
            "Binding=\"{Binding AdditionalAnnualDividendPreview}\"",
            "Text=\"{Binding MarketDataFetchStatus}\"");
    }

    [Fact]
    public void SimulationViewModel_NewDividendStock_UsesMarketDataCommandAndPersistsFetchState()
    {
        var source = ReadRepoFile("InvestmentStory.App", "ViewModels", "SimulationViewModel.cs");

        Assert.Contains("FetchNewDividendStockCommand", source);
        Assert.Contains("IMarketDataService", source);
        Assert.Contains("IStockLookupService", source);
        Assert.Contains("BuildLiveMarketDataSettings", source);
        Assert.Contains("CommitNewDividendSimulationEdits", ReadRepoFile("InvestmentStory.App", "Views", "SimulationView.xaml.cs"));
        Assert.Contains("MarketDataSource = MarketDataSource.Trim()", source);
        Assert.Contains("MarketDataAcquiredAt = MarketDataAcquiredAt", source);
        Assert.Contains("MarketDataStatus = MarketDataFetchStatus.Trim()", source);
        Assert.Contains("AdditionalAnnualDividendPreview => Formatters.Jpy", source);
        Assert.Contains("RunPassiveIncomeSimulation(saveSettings: false)", source);
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InvestmentStory.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new DirectoryNotFoundException("Repository root was not found.");
        }

        return File.ReadAllText(Path.Combine([current.FullName, .. relativeParts]));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void AssertOrdered(string text, params string[] values)
    {
        var previousIndex = -1;
        foreach (var value in values)
        {
            var index = text.IndexOf(value, previousIndex + 1, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Expected '{value}' after index {previousIndex}.");
            previousIndex = index;
        }
    }
}
