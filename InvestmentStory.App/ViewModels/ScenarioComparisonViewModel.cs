using System.Collections.ObjectModel;
using System.Globalization;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed record ScenarioHistoryDisplay(SavedScenario Value)
{
    public string Label => $"{Value.Inputs.Name}｜{Value.RecordedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}";
    public string Baseline => $"{Value.Baseline.AsOf:yyyy/MM/dd}・{Value.Baseline.KnownPortfolioValueJpy:#,0}円";
    public string Projected => $"{Value.Outcome.ProjectedPortfolioValueJpy:#,0}円";
    public string Dividend => $"{Value.Outcome.GrossAnnualDividendForecastJpy:#,0}円";
    public string Assumptions => $"月{Value.Inputs.MonthlyContributionJpy:#,0}円 / 配当減{Value.Inputs.DividendReductionPercent:0.##}% / USD/JPY {Value.Inputs.UsdJpyChange:+0.##;-0.##;0}円 / {Value.Inputs.Years}年";
    public override string ToString() => Label;
}

public sealed class ScenarioComparisonViewModel : ObservableObject
{
    private readonly Func<ScenarioComparisonRepository> _repository;
    private readonly ScenarioComparisonService _service = new();
    private IReadOnlyList<StockSnapshot> _snapshots = [];
    private ScenarioBaseline? _baseline;
    private ScenarioOutcome? _outcome;
    private ScenarioInputs? _inputs;
    private string _name = "標準案", _monthlyContribution = "100000", _annualReturn = "0",
        _dividendReduction = "0", _usdJpyChange = "0", _years = "10";
    private string _message = "条件を入力して「計算」を押してください。保有実績は変更しません。";
    private string _comparison = "比較する保存案を2つ選んでください。";
    private ScenarioHistoryDisplay? _selectedA, _selectedB;

    public ScenarioComparisonViewModel(Func<ScenarioComparisonRepository> repository)
    {
        _repository = repository;
        CalculateCommand = new RelayCommand(Calculate);
        SaveCommand = new RelayCommand(Save, () => _outcome is not null);
    }

    public RelayCommand CalculateCommand { get; }
    public RelayCommand SaveCommand { get; }
    public ObservableCollection<ScenarioHistoryDisplay> History { get; } = new();
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Invalidate(); } }
    public string MonthlyContribution { get => _monthlyContribution; set { if (SetProperty(ref _monthlyContribution, value)) Invalidate(); } }
    public string AnnualReturn { get => _annualReturn; set { if (SetProperty(ref _annualReturn, value)) Invalidate(); } }
    public string DividendReduction { get => _dividendReduction; set { if (SetProperty(ref _dividendReduction, value)) Invalidate(); } }
    public string UsdJpyChange { get => _usdJpyChange; set { if (SetProperty(ref _usdJpyChange, value)) Invalidate(); } }
    public string Years { get => _years; set { if (SetProperty(ref _years, value)) Invalidate(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public ScenarioHistoryDisplay? SelectedA { get => _selectedA; set { if (SetProperty(ref _selectedA, value)) UpdateComparison(); } }
    public ScenarioHistoryDisplay? SelectedB { get => _selectedB; set { if (SetProperty(ref _selectedB, value)) UpdateComparison(); } }
    public string Comparison { get => _comparison; private set => SetProperty(ref _comparison, value); }
    public string BaselineText => _baseline is null ? "基準データ未読込" :
        $"{_baseline.AsOf:yyyy/MM/dd}時点の保有評価額（確認済み）{_baseline.KnownPortfolioValueJpy:#,0}円、評価額未確認 {_baseline.UnknownValuePositionCount}件、年間税引前配当見込み {_baseline.GrossAnnualDividendForecastJpy:#,0}円";
    public string ResultText => _outcome is null ? "計算結果はまだありません。" :
        $"{_inputs!.Years}年後の参考評価額 {_outcome.ProjectedPortfolioValueJpy:#,0}円（拠出なし・同年率の比較 {_outcome.ReferenceProjectedValueJpy:#,0}円、差 {_outcome.DifferenceFromReferenceJpy:+#,0;-#,0;0}円）\n" +
        $"為替変化直後の参考評価額 {_outcome.FxAdjustedStartingValueJpy:#,0}円、年間税引前配当見込み {_outcome.GrossAnnualDividendForecastJpy:#,0}円";

    public void Load(IReadOnlyList<StockSnapshot> snapshots)
    {
        _snapshots = snapshots;
        _baseline = _service.CaptureBaseline(snapshots, DateTime.Today);
        Invalidate();
        OnPropertyChanged(nameof(BaselineText));
        RefreshHistory();
    }

    private void Invalidate()
    {
        _outcome = null; _inputs = null;
        SaveCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ResultText));
    }

    private ScenarioInputs ReadInputs()
    {
        static decimal Parse(string text, string label) => decimal.TryParse(text, NumberStyles.Number,
            CultureInfo.CurrentCulture, out var value) ? value : throw new ArgumentException($"{label}を数値で入力してください。");
        if (!int.TryParse(Years, NumberStyles.Integer, CultureInfo.CurrentCulture, out var years))
            throw new ArgumentException("年数を整数で入力してください。");
        return new ScenarioInputs(Name, Parse(MonthlyContribution, "毎月拠出額"),
            Parse(AnnualReturn, "年率"), Parse(DividendReduction, "配当減少率"),
            Parse(UsdJpyChange, "USD/JPY変化"), years);
    }

    private void Calculate()
    {
        try
        {
            _inputs = ReadInputs();
            _outcome = _service.Calculate(_baseline ?? throw new InvalidOperationException("基準データを読み込んでください。"),
                _inputs, _snapshots);
            Message = "計算しました。保存すると、この時点の実データ基準と仮定・結果を固定して残します。";
            OnPropertyChanged(nameof(ResultText));
            SaveCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            Invalidate(); Message = ex.Message;
        }
    }

    private void Save()
    {
        if (_inputs is null || _baseline is null || _outcome is null) return;
        try
        {
            var saved = _repository().Save(_inputs, _baseline, _outcome);
            RefreshHistory();
            SelectedA = History.First(x => x.Value.Id == saved.Id);
            Message = $"「{saved.Inputs.Name}」を保存しました。保有・取引・配当実績は更新していません。";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            Message = ex.Message;
        }
    }

    private void RefreshHistory()
    {
        var a = SelectedA?.Value.Id; var b = SelectedB?.Value.Id;
        History.Clear();
        foreach (var value in _repository().GetHistory()) History.Add(new ScenarioHistoryDisplay(value));
        SelectedA = History.FirstOrDefault(x => x.Value.Id == a);
        SelectedB = History.FirstOrDefault(x => x.Value.Id == b);
        UpdateComparison();
    }

    private void UpdateComparison()
    {
        if (SelectedA is null || SelectedB is null)
        {
            Comparison = "比較する保存案を2つ選んでください。"; return;
        }
        if (SelectedA.Value.Id == SelectedB.Value.Id)
        {
            Comparison = "異なる2案を選んでください。"; return;
        }
        var a = SelectedA.Value; var b = SelectedB.Value;
        if (a.Baseline != b.Baseline || a.Inputs.Years != b.Inputs.Years ||
            a.Inputs.AnnualReturnPercent != b.Inputs.AnnualReturnPercent)
        {
            Comparison = "基準日の実データ、予測年数または年率が異なるため、差額を単純比較できません。各案の条件と結果を履歴で確認してください。";
            return;
        }
        Comparison = $"A − B：{a.Inputs.Years}年後の参考評価額 {a.Outcome.ProjectedPortfolioValueJpy - b.Outcome.ProjectedPortfolioValueJpy:+#,0;-#,0;0}円、年間税引前配当見込み {a.Outcome.GrossAnnualDividendForecastJpy - b.Outcome.GrossAnnualDividendForecastJpy:+#,0;-#,0;0}円";
    }
}
