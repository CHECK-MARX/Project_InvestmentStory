using InvestmentStory.App.Controls;
using InvestmentStory.Core.Models;
using System.Windows.Media;

namespace InvestmentStory.Tests;

public sealed class DividendChartInteractionTests
{
    [Fact]
    public void StatusRegistry_DefinesEveryScheduleStatusWithDistinctMeaning()
    {
        var visuals = DividendChartColorRegistry.Legend;

        Assert.Equal(Enum.GetValues<DividendScheduleStatus>().Length, visuals.Count);
        Assert.Equal(Enum.GetValues<DividendScheduleStatus>().Order(), visuals.Select(x => x.Status).Order());
        Assert.All(visuals, visual => Assert.False(string.IsNullOrWhiteSpace(visual.DisplayName)));
        Assert.Equal(Color.FromRgb(56, 189, 248),
            DividendChartColorRegistry.ForStatus(DividendScheduleStatus.Paid).Color);
        Assert.Equal(Color.FromRgb(34, 197, 94),
            DividendChartColorRegistry.ForStatus(DividendScheduleStatus.Expected).Color);
        Assert.Equal(Color.FromRgb(245, 158, 11),
            DividendChartColorRegistry.ForStatus(DividendScheduleStatus.Estimated).Color);
        Assert.Equal(Color.FromRgb(100, 116, 139),
            DividendChartColorRegistry.ForStatus(DividendScheduleStatus.MissedEligibility).Color);
        Assert.Equal(Color.FromRgb(239, 68, 68),
            DividendChartColorRegistry.ForStatus(DividendScheduleStatus.NotAvailable).Color);
    }

    [Fact]
    public void SecurityColor_IsDeterministicAndCanonicalized()
    {
        var first = DividendChartColorRegistry.ColorForSecurity(" nvda ");
        var second = DividendChartColorRegistry.ColorForSecurity("NVDA");
        var afterOtherLookups = Enumerable.Range(0, 50)
            .Select(index => DividendChartColorRegistry.ColorForSecurity($"TEST-{index}"))
            .ToList();

        Assert.Equal(first, second);
        Assert.Equal(first, DividendChartColorRegistry.ColorForSecurity("NvDa"));
        Assert.Equal(50, afterOtherLookups.Count);
    }

    [Fact]
    public void Heatmap_DistinguishesNoDividendFromUnavailableData()
    {
        var noDividend = DividendChartColorRegistry.HeatmapBrush(0m, 100m, null).Color;
        var unavailable = DividendChartColorRegistry.HeatmapBrush(
            0m, 100m, DividendScheduleStatus.NotAvailable).Color;

        Assert.NotEqual(noDividend, unavailable);
        Assert.True(unavailable.R > unavailable.G);
        Assert.True(noDividend.A < unavailable.A);
    }

    [Fact]
    public void Tooltip_OmitsMissingValuesButKeepsActualZero()
    {
        var text = new DividendChartTooltipModel
        {
            Month = 6,
            Ticker = "NVDA",
            SecurityName = "NVIDIA",
            NetJpy = 0m,
            GrossJpy = null,
            ForeignTaxJpy = null,
            DomesticTaxJpy = null,
            Status = DividendScheduleStatus.Paid,
            Source = "CSV"
        }.ToDisplayText();

        Assert.Contains("NVDA", text);
        Assert.Contains("NVIDIA", text);
        Assert.Contains("0", text);
        Assert.Contains("CSV", text);
        Assert.DoesNotContain("外国税", text);
        Assert.DoesNotContain("国内税", text);
        Assert.DoesNotContain("税引前", text);
    }

    [Fact]
    public void SharedSelection_DimsOnlyUnrelatedTickerMonthOrStatus()
    {
        var state = new DividendChartInteractionState();

        state.Select("KO", 6, DividendScheduleStatus.Expected, "selected");

        Assert.Equal(1d, state.OpacityFor("KO", 6, DividendScheduleStatus.Expected));
        Assert.Equal(.18d, state.OpacityFor("JNJ", 6, DividendScheduleStatus.Expected));
        Assert.Equal(.18d, state.OpacityFor("KO", 9, DividendScheduleStatus.Expected));
        Assert.Equal(.18d, state.OpacityFor("KO", 6, DividendScheduleStatus.Estimated));
        Assert.Equal(1d, state.OpacityFor(null, 6, null));

        state.ClearCommand.Execute(null);

        Assert.False(state.HasSelection);
        Assert.Equal(1d, state.OpacityFor("JNJ", 9, DividendScheduleStatus.Estimated));
    }

    [Fact]
    public void LegendToggle_HidesAndRestoresOnlyRequestedSeries()
    {
        var state = new DividendChartInteractionState();

        state.ToggleSeries("status:Estimated");

        Assert.False(state.IsSeriesVisible("status:Estimated"));
        Assert.True(state.IsSeriesVisible("status:Paid"));

        state.ToggleSeries("status:Estimated");

        Assert.True(state.IsSeriesVisible("status:Estimated"));
    }

    [Theory]
    [InlineData(DividendPlanEligibility.Eligible, DividendPlanDataQuality.Acquired, DividendScheduleStatus.Expected)]
    [InlineData(DividendPlanEligibility.Estimated, DividendPlanDataQuality.Estimated, DividendScheduleStatus.Estimated)]
    [InlineData(DividendPlanEligibility.Ineligible, DividendPlanDataQuality.Acquired, DividendScheduleStatus.MissedEligibility)]
    [InlineData(DividendPlanEligibility.Missing, DividendPlanDataQuality.Missing, DividendScheduleStatus.NotAvailable)]
    public void ScheduleStatusResolver_UsesTypedStatusInsteadOfDisplayText(
        string eligibility,
        string quality,
        DividendScheduleStatus expected)
    {
        Assert.Equal(expected, DividendScheduleStatusResolver.FromPlan(eligibility, quality));
    }

    [Fact]
    public void DividendViews_BindEveryChartToOneSharedInteractionState()
    {
        var dividends = ReadRepoFile("InvestmentStory.App", "Views", "DividendsView.xaml");
        var purchasePlan = ReadRepoFile("InvestmentStory.App", "Views", "DividendPurchasePlanView.xaml");

        Assert.Equal(2, CountOccurrences(dividends, "InteractionState=\"{Binding DividendChartInteraction}\""));
        Assert.True(
            CountOccurrences(purchasePlan, "InteractionState=\"{Binding DividendChartInteraction}\"") >= 9,
            "All purchase-plan charts must share ticker/month/status selection state.");
        Assert.Contains("ClearCommand", dividends);
        Assert.Contains("ClearCommand", purchasePlan);
    }

    [Fact]
    public void MonthMap_UsesTypedStatusesAndNeverComparesJapaneseLabels()
    {
        var source = ReadRepoFile("InvestmentStory.App", "Controls", "DividendMonthMapControl.cs");

        Assert.Contains("ScheduleStatuses", source);
        Assert.Contains("DividendChartColorRegistry.ForStatus", source);
        Assert.DoesNotContain("Contains(\"推定\")", source);
        Assert.DoesNotContain("Contains(\"受取\")", source);
        Assert.DoesNotContain("Contains(\"未取得\")", source);
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string ReadRepoFile(params string[] segments)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InvestmentStory.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("InvestmentStory.sln was not found.");
    }
}
