using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InvestmentStory.App.ViewModels;
using InvestmentStory.App.Views;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Data;

namespace InvestmentStory.Tests;

[CollectionDefinition("WPF visual acceptance", DisableParallelization = true)]
public sealed class WpfVisualAcceptanceCollection;

[Collection("WPF visual acceptance")]
public sealed class StockListVisualTests
{
    [Fact]
    public void FixedIdentityColumnsRemainAvailableAt125And150PercentScale()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            InvestmentStory.App.App? createdApp = null;
            try
            {
                if (Application.Current is null)
                {
                    createdApp = new InvestmentStory.App.App();
                    createdApp.InitializeComponent();
                }
                foreach (var (scale, density) in new[]
                         {
                             (1.25, "Standard"), (1.5, "Standard"), (1.5, "LargeText")
                         })
                {
                    UiDensityManager.Apply(density);
                    var vm = new StockListViewModel(() => { }, _ => { }, _ => { }, _ => { },
                        _ => { }, () => { }, () => { });
                    var view = new StockListView
                    {
                        DataContext = vm,
                        LayoutTransform = new ScaleTransform(scale, scale)
                    };
                    view.Measure(new Size(1100, 720));
                    view.Arrange(new Rect(0, 0, 1100, 720));
                    view.UpdateLayout();
                    var grid = Assert.IsType<DataGrid>(view.FindName("StockGrid"));
                    Assert.Equal(3, grid.FrozenColumnCount);
                    Assert.Equal(new[] { "コード", "銘柄名", "口座" },
                        grid.Columns.Take(3).Select(x => x.Header?.ToString()).ToArray());
                    Assert.True(grid.ActualHeight >= 120, $"The table is too short at {scale:P0}, {density}.");

                    var outputFolder = Environment.GetEnvironmentVariable("INVESTMENTSTORY_UI_QA_OUTPUT");
                    if (string.IsNullOrWhiteSpace(outputFolder)) continue;
                    Directory.CreateDirectory(outputFolder);
                    var bitmap = new RenderTargetBitmap(1100, 720, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(outputFolder,
                        $"stocklist_{scale * 100:0}_{density.ToLowerInvariant()}.png"));
                    encoder.Save(output);
                }
                UiDensityManager.Apply("Standard");

                string? savedLayout = null;
                var originalVm = new StockListViewModel(() => { }, _ => { }, _ => { }, _ => { },
                    _ => { }, () => { }, () => { }, saveFilterState: value => savedLayout = value);
                var original = new StockListView { DataContext = originalVm };
                original.Measure(new Size(1100, 720));
                original.Arrange(new Rect(0, 0, 1100, 720));
                original.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var originalGrid = Assert.IsType<DataGrid>(original.FindName("StockGrid"));
                originalGrid.Columns[3].Width = new DataGridLength(175);
                originalGrid.Columns[4].DisplayIndex = 3;
                original.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.False(string.IsNullOrEmpty(savedLayout));

                var restoredVm = new StockListViewModel(() => { }, _ => { }, _ => { }, _ => { },
                    _ => { }, () => { }, () => { }, savedFilterJson: savedLayout);
                var restored = new StockListView { DataContext = restoredVm };
                restored.Measure(new Size(1100, 720));
                restored.Arrange(new Rect(0, 0, 1100, 720));
                restored.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var restoredGrid = Assert.IsType<DataGrid>(restored.FindName("StockGrid"));
                Assert.Equal(175, restoredGrid.Columns[3].Width.Value);
                Assert.Equal(3, restoredGrid.Columns[4].DisplayIndex);

                var databaseFolder = Path.Combine(Path.GetTempPath(),
                    "InvestmentStoryPrivacyUi_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(databaseFolder);
                try
                {
                    var databasePath = Path.Combine(databaseFolder, "test.db");
                    var vm = new MainViewModel(new InvestmentStoryRepository(databasePath));
                    var window = new InvestmentStory.App.MainWindow(vm);
                    var root = Assert.IsType<Grid>(window.Content);
                    root.DataContext = vm;
                    root.Measure(new Size(1100, 720));
                    root.Arrange(new Rect(0, 0, 1100, 720));
                    root.UpdateLayout();
                    var shell = Assert.IsType<Grid>(window.FindName("AppShell"));
                    var overlay = Assert.IsType<Grid>(window.FindName("PrivacyOverlay"));
                    Assert.True(shell.IsEnabled);
                    Assert.Equal(Visibility.Collapsed, overlay.Visibility);

                    vm.TogglePrivacyModeCommand.Execute(null);
                    Assert.False(shell.IsEnabled);
                    Assert.Equal(Visibility.Visible, overlay.Visibility);
                    Assert.True(new InvestmentStoryRepository(databasePath).GetSettings().IsPrivacyMode);
                    Assert.True(new MainViewModel(new InvestmentStoryRepository(databasePath)).IsPrivacyMode);

                    var outputFolder = Environment.GetEnvironmentVariable("INVESTMENTSTORY_UI_QA_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(outputFolder))
                    {
                        root.UpdateLayout();
                        var bitmap = new RenderTargetBitmap(1100, 720, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(root);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(outputFolder, "privacy_shield.png"));
                        encoder.Save(output);
                    }
                    vm.TogglePrivacyModeCommand.Execute(null);
                    Assert.True(shell.IsEnabled);
                    Assert.Equal(Visibility.Collapsed, overlay.Visibility);
                }
                finally { Directory.Delete(databaseFolder, true); }
            }
            catch (Exception ex) { error = ex; }
            finally { createdApp?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The WPF layout thread did not finish.");
        if (error is not null) throw error;
    }
}
