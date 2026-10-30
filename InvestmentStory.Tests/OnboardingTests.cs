using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using System.Threading;
using System.Windows;

namespace InvestmentStory.Tests;

[Collection("WPF visual acceptance")]
public sealed class OnboardingTests
{
    [Fact]
    public void FirstRunRequiresAnExplicitPathAndExplainsNextAction()
    {
        var sample = 0; var csv = 0; var manual = 0;
        var vm = new OnboardingViewModel(() => sample++, () => csv++, () => manual++);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.False(vm.HasSelection);

        vm.SelectCsvCommand.Execute(null);
        Assert.True(vm.HasSelection);
        Assert.Contains("プレビュー", vm.NextAction);
        vm.StartCommand.Execute(null);
        Assert.Equal(1, csv); Assert.Equal(0, sample); Assert.Equal(0, manual);

        vm.BackCommand.Execute(null);
        Assert.False(vm.StartCommand.CanExecute(null));
        vm.SelectSampleCommand.Execute(null);
        Assert.Contains("別", vm.NextAction);
        vm.StartCommand.Execute(null);
        vm.SelectManualCommand.Execute(null);
        Assert.Contains("取得額", vm.NextAction);
        vm.StartCommand.Execute(null);
        Assert.Equal(1, sample); Assert.Equal(1, manual);
    }

    [Fact]
    public void EmptyNormalDatabaseStartsWizardAndCsvPathOpensPreviewScreen()
    {
        Exception? failure = null;
        var path = Path.Combine(Path.GetTempPath(), $"investment_story_first_run_{Guid.NewGuid():N}.db");
        var thread = new Thread(() =>
        {
            InvestmentStory.App.App? app = null;
            try
            {
                if (Application.Current is null)
                {
                    app = new InvestmentStory.App.App();
                    app.InitializeComponent();
                }
                var vm = new MainViewModel(new InvestmentStoryRepository(path), new UnavailableRate());
                Assert.Same(vm.Onboarding, vm.CurrentPage);
                vm.Onboarding.SelectCsvCommand.Execute(null);
                vm.Onboarding.StartCommand.Execute(null);
                Assert.Same(vm.CsvImport, vm.CurrentPage);
            }
            catch (Exception ex) { failure = ex; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        try { if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString()); }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private sealed class UnavailableRate : IExchangeRateService
    {
        public ExchangeRateQuote GetUsdJpyRate() => new()
        {
            Rate = 0m, AcquiredAt = DateTime.MinValue,
            InputType = "Unavailable", Source = "固定テスト"
        };
    }
}
