using InvestmentStory.App.Infrastructure;

namespace InvestmentStory.App.ViewModels;

public sealed class OnboardingViewModel : ObservableObject
{
    private readonly Action _openSample;
    private readonly Action _openCsv;
    private readonly Action _openManual;
    private string _selectedPath = string.Empty;

    public OnboardingViewModel(Action openSample, Action openCsv, Action openManual)
    {
        _openSample = openSample;
        _openCsv = openCsv;
        _openManual = openManual;
        SelectSampleCommand = new RelayCommand(() => SelectedPath = "Sample");
        SelectCsvCommand = new RelayCommand(() => SelectedPath = "Csv");
        SelectManualCommand = new RelayCommand(() => SelectedPath = "Manual");
        StartCommand = new RelayCommand(Start, () => SelectedPath.Length > 0);
        BackCommand = new RelayCommand(() => SelectedPath = string.Empty);
    }

    public RelayCommand SelectSampleCommand { get; }
    public RelayCommand SelectCsvCommand { get; }
    public RelayCommand SelectManualCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand BackCommand { get; }
    public bool HasSelection => SelectedPath.Length > 0;
    public string SelectedPath
    {
        get => _selectedPath;
        private set
        {
            if (!SetProperty(ref _selectedPath, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(NextAction));
            OnPropertyChanged(nameof(StartButtonText));
            StartCommand.RaiseCanExecuteChanged();
        }
    }
    public string SelectedTitle => SelectedPath switch
    {
        "Sample" => "サンプルで画面を見る",
        "Csv" => "CSVを確認して取り込む",
        "Manual" => "保有銘柄を手動で登録する",
        _ => "始め方を選んでください"
    };
    public string NextAction => SelectedPath switch
    {
        "Sample" => "別のサンプル用DBで操作を試します。通常DBには銘柄を追加しません。後から通常データに戻せます。",
        "Csv" => "証券会社のCSVを選び、形式・行数・差分をプレビューします。確認して適用するまで通常DBは変更しません。",
        "Manual" => "銘柄、口座、数量、取得額を入力します。不明な取得額は空欄にでき、損益を推定値で確定しません。",
        _ => ""
    };
    public string StartButtonText => SelectedPath switch
    {
        "Sample" => "サンプルを開く", "Csv" => "CSV取込へ進む", "Manual" => "手動登録へ進む", _ => "次へ"
    };

    private void Start()
    {
        switch (SelectedPath)
        {
            case "Sample": _openSample(); break;
            case "Csv": _openCsv(); break;
            case "Manual": _openManual(); break;
        }
    }
}
