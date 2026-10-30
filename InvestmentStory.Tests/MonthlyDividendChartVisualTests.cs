using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InvestmentStory.App.Controls;
using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Tests;

[Collection("WPF visual acceptance")]
public sealed class MonthlyDividendChartVisualTests
{
    [Fact]
    public void TwelveMonthChartRendersAtStandardAnd150PercentViewportWidths()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var year = DateTime.Today.Year;
                var rows = Enumerable.Range(1, 12).Select(month =>
                    new MonthlyDividendBreakdownRowViewModel(new MonthlyDividendBreakdown
                    {
                        Year = year,
                        Month = month,
                        ActualJpy = month is 1 or 3 or 6 ? month * 1_500m : 0m,
                        PlannedJpy = month is 9 or 12 ? month * 900m : 0m,
                        PreviousYearActualJpy = month is 1 or 6 or 9 ? month * 1_000m : 0m,
                        MonthlyGoalJpy = 10_000m,
                        HasActualRecord = month is 1 or 3 or 6,
                        HasPlannedRecord = month is 9 or 12,
                        HasPreviousYearRecord = month is 1 or 6 or 9,
                        HasMonthlyGoal = true
                    })).ToArray();

                foreach (var (viewportWidth, scale) in new[]
                         {
                             (1116, 1.0), (1116, 1.5),
                             (1670, 1.0), (1670, 1.5)
                         })
                {
                    // The chart card is now a full-width row with 24 px margins on each side.
                    var chartWidth = (viewportWidth - 48) / scale;
                    var chart = new MonthlyDividendLineChartControl { Items = rows, Height = 340 };
                    chart.Resources["PrimaryTextBrush"] = Brushes.White;
                    chart.Resources["SecondaryTextBrush"] = Brushes.LightGray;
                    chart.Resources["BorderBrush"] = Brushes.DimGray;
                    chart.Resources["AccentBlueBrush"] = Brushes.DeepSkyBlue;
                    chart.Resources["DividendBrush"] = Brushes.MediumPurple;
                    chart.Resources["NeutralBrush"] = Brushes.SlateGray;
                    chart.Resources["WarningBrush"] = Brushes.Orange;
                    chart.Resources["ElevatedSurfaceBrush"] = Brushes.Black;
                    chart.Measure(new Size(chartWidth, 340));
                    chart.Arrange(new Rect(0, 0, chartWidth, 340));
                    Assert.True(chart.ActualWidth >= 300,
                        $"Chart width {chart.ActualWidth:N0} at {viewportWidth} / {scale:P0} is too narrow.");

                    var outputFolder = Environment.GetEnvironmentVariable("INVESTMENTSTORY_UI_QA_OUTPUT");
                    if (string.IsNullOrWhiteSpace(outputFolder))
                        outputFolder = Path.Combine(Path.GetTempPath(), "InvestmentStoryChartQA_20261027");
                    Directory.CreateDirectory(outputFolder);
                    var bitmap = new RenderTargetBitmap(
                        (int)Math.Ceiling(chart.ActualWidth), 340, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(chart);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(outputFolder,
                        $"monthly_dividend_{viewportWidth}_{scale * 100:0}.png"));
                    encoder.Save(output);
                }
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The WPF chart layout thread did not finish.");
        if (error is not null) throw error;
    }
}
