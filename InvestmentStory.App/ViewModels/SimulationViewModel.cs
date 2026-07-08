using System.Collections.ObjectModel;
using System.Windows.Input;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.App.ViewModels;

public sealed class SimulationViewModel : ObservableObject
{
    private readonly InvestmentCalculator _calculator;
    private string _resultSummary = "-";

    public SimulationViewModel(InvestmentCalculator calculator)
    {
        _calculator = calculator;
        RunCommand = new RelayCommand(Run);
    }

    public ObservableCollection<SimulationProjectionRowViewModel> Projections { get; } = new();
    public ICommand RunCommand { get; }

    public decimal CurrentAnnualPassiveIncome { get; set; }
    public decimal MonthlyAdditionalInvestment { get; set; } = 100_000m;
    public decimal AssumedDividendYieldRate { get; set; } = 3.5m;
    public decimal AnnualDividendGrowthRate { get; set; } = 5m;
    public decimal TargetAnnualPassiveIncome { get; set; } = 1_200_000m;

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    public void UpdateCurrentAnnualIncome(decimal annualIncome)
    {
        if (CurrentAnnualPassiveIncome == 0m)
        {
            CurrentAnnualPassiveIncome = annualIncome;
            OnPropertyChanged(nameof(CurrentAnnualPassiveIncome));
        }
    }

    private void Run()
    {
        if (CurrentAnnualPassiveIncome < 0m || MonthlyAdditionalInvestment < 0m || AssumedDividendYieldRate < 0m ||
            AnnualDividendGrowthRate < 0m || TargetAnnualPassiveIncome < 0m)
        {
            ResultSummary = "入力値を確認してください。";
            return;
        }

        var result = _calculator.SimulatePassiveIncome(new PassiveIncomeSimulationInput
        {
            CurrentAnnualPassiveIncome = CurrentAnnualPassiveIncome,
            MonthlyAdditionalInvestment = MonthlyAdditionalInvestment,
            AssumedDividendYieldRate = AssumedDividendYieldRate,
            AnnualDividendGrowthRate = AnnualDividendGrowthRate,
            TargetAnnualPassiveIncome = TargetAnnualPassiveIncome,
            StartYear = DateTime.Today.Year
        });

        Projections.Clear();
        foreach (var projection in result.Projections)
        {
            Projections.Add(new SimulationProjectionRowViewModel(projection));
        }
        OnPropertyChanged(nameof(Projections));

        ResultSummary = result.TargetAchievementYear is null
            ? "10年以内には目標未達です。"
            : $"{result.TargetAchievementYear}年に目標達成見込みです。目標まであと{result.YearsToTarget}年です。";
    }
}
