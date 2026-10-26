using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed class AllocationTargetRow : ObservableObject
{
    private string _key = "", _label = "", _percent = "0", _evidence = "任意の目標分類", _currentValue = "—";
    public string Key { get => _key; set => SetProperty(ref _key, value); }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public string Percent { get => _percent; set => SetProperty(ref _percent, value); }
    public string Evidence { get => _evidence; set => SetProperty(ref _evidence, value); }
    public string CurrentValue { get => _currentValue; set => SetProperty(ref _currentValue, value); }
}

public sealed record AllocationDimensionOption(AllocationDimension Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class AllocationViewModel : ObservableObject
{
    private readonly Func<AllocationTargetRepository> _repository;
    private readonly AllocationTargetService _service = new();
    private readonly Dictionary<AllocationDimension, List<AllocationTargetRow>> _drafts = new();
    private readonly Dictionary<AllocationDimension, int> _revisions = new();
    private IReadOnlyList<StockSnapshot> _snapshots = Array.Empty<StockSnapshot>();
    private AllocationDimensionOption _selectedDimension;
    private AllocationTargetRow? _selectedTarget;
    private AllocationAnalysis? _analysis;
    private string _additionalFunding = "0", _message = "分類別の目標比率を合計100%に設定してください。";
    private bool _loaded;

    public AllocationViewModel(Func<AllocationTargetRepository> repository)
    {
        _repository = repository;
        _selectedDimension = Dimensions[0];
        CalculateCommand = new RelayCommand(Calculate);
        SaveCommand = new RelayCommand(Save);
        AddCommand = new RelayCommand(() => Targets.Add(new AllocationTargetRow { Percent = "0" }));
        RemoveCommand = new RelayCommand(() => { if (SelectedTarget is not null) Targets.Remove(SelectedTarget); });
        ReloadCommand = new RelayCommand(() => { _drafts.Remove(SelectedDimension.Value); LoadDimension(force: true); });
    }

    public IReadOnlyList<AllocationDimensionOption> Dimensions { get; } =
    [
        new(AllocationDimension.Country, "国別（登録国）"),
        new(AllocationDimension.Sector, "分野別"),
        new(AllocationDimension.Security, "銘柄別"),
        new(AllocationDimension.Currency, "通貨別（取引通貨）"),
        new(AllocationDimension.Broker, "証券会社別"),
        new(AllocationDimension.AssetType, "資産種別別")
    ];
    public RelayCommand CalculateCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public ObservableCollection<AllocationTargetRow> Targets { get; } = new();
    public ObservableCollection<AllocationCategoryResult> Results { get; } = new();
    public AllocationTargetRow? SelectedTarget { get => _selectedTarget; set => SetProperty(ref _selectedTarget, value); }
    public AllocationDimensionOption SelectedDimension
    {
        get => _selectedDimension;
        set
        {
            if (value is null || value.Value == _selectedDimension.Value) return;
            if (_loaded) _drafts[_selectedDimension.Value] = Targets.ToList();
            if (!SetProperty(ref _selectedDimension, value)) return;
            if (_loaded) LoadDimension();
        }
    }
    public string AdditionalFunding { get => _additionalFunding; set => SetProperty(ref _additionalFunding, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string ValuationStatus => _analysis?.TotalValueJpy is decimal total
        ? $"対象保有評価額 {Money(total)}（株・投信のみ、現金除外）"
        : $"把握済み評価額 {Money(_analysis?.KnownValueJpy ?? 0m)}。不明分があるため総額・比率は未確定。";
    public string MinimumFunding => _analysis?.MinimumAdditionalFundingJpy is decimal amount
        ? $"追加資金だけで目標達成する場合の最低額: {Money(Math.Ceiling(amount))}"
        : "追加資金だけでは目標に到達できません（または評価額未確定）。";
    public string FundingStatus => _analysis?.CanReachTargetsWithAdditionalFunding == true
        ? $"追加資金案: {Money(_analysis.AdditionalFundingJpy ?? 0m)}の配分額を表示中。"
        : "追加資金案: 条件がそろうまで金額は表示しません。";
    public string AnalysisNotes => _analysis is null ? "" : string.Join("\n", _analysis.Problems);
    public int Revision => _revisions.GetValueOrDefault(SelectedDimension.Value);

    public void Load(IReadOnlyList<StockSnapshot> snapshots)
    {
        _snapshots = snapshots;
        if (!_loaded)
        {
            _loaded = true;
            LoadDimension();
        }
        else
        {
            // Keep edits across navigation; refresh only the source valuations.
            RefreshRows();
            _analysis = null;
            Results.Clear();
            RefreshStatus();
            Message = "保有評価額を更新しました。目標案を再計算してください（編集中の目標は保持）。";
        }
    }

    public void Reset()
    {
        _loaded = false;
        _drafts.Clear(); _revisions.Clear(); Targets.Clear(); Results.Clear();
        _analysis = null;
        Message = "分類別の目標比率を合計100%に設定してください。";
        RefreshStatus();
    }

    private void LoadDimension(bool force = false)
    {
        var dimension = SelectedDimension.Value;
        if (!force && _drafts.TryGetValue(dimension, out var draft))
        {
            Targets.Clear(); foreach (var row in draft) Targets.Add(row);
            RefreshRows();
        }
        else
        {
            var plan = _repository().GetLatest(dimension);
            _revisions[dimension] = plan?.Revision ?? 0;
            var holdings = _service.CategorizeHoldings(_snapshots, dimension);
            var groups = holdings.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
            Targets.Clear();
            foreach (var key in groups.Keys.Union(plan?.Targets.Select(x => x.Key) ?? [], StringComparer.OrdinalIgnoreCase))
            {
                var group = groups.GetValueOrDefault(key);
                var target = plan?.Targets.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                Targets.Add(new AllocationTargetRow
                {
                    Key = key,
                    Label = target?.Label ?? group?.First().Label ?? key,
                    Percent = (target?.Percent ?? 0m).ToString(CultureInfo.CurrentCulture),
                    Evidence = group is { Length: > 0 }
                        ? string.Join(" / ", group.Select(x => x.ClassificationEvidence).Distinct())
                        : "保有なし（目標のみ）",
                    CurrentValue = group is { Length: > 0 } && group.All(x => x.MarketValueJpy is not null)
                        ? Money(group.Sum(x => x.MarketValueJpy ?? 0m)) : "未確定"
                });
            }
            _drafts[dimension] = Targets.ToList();
        }
        _analysis = null; Results.Clear(); RefreshStatus();
        Message = $"{SelectedDimension.Label}の保存版: 第{Revision}版。目標合計100%で計算・保存できます。";
    }

    private void RefreshRows()
    {
        var groups = _service.CategorizeHoldings(_snapshots, SelectedDimension.Value)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in Targets)
        {
            if (!groups.TryGetValue(row.Key, out var group))
            {
                row.CurrentValue = "保有なし";
                row.Evidence = "保有なし（目標のみ）";
                continue;
            }
            row.CurrentValue = group.All(x => x.MarketValueJpy is not null)
                ? Money(group.Sum(x => x.MarketValueJpy ?? 0m)) : "未確定";
            row.Evidence = string.Join(" / ", group.Select(x => x.ClassificationEvidence).Distinct());
        }
        foreach (var (key, group) in groups)
        {
            if (Targets.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
            Targets.Add(new AllocationTargetRow
            {
                Key = key, Label = group[0].Label, Percent = "0",
                CurrentValue = group.All(x => x.MarketValueJpy is not null)
                    ? Money(group.Sum(x => x.MarketValueJpy ?? 0m)) : "未確定",
                Evidence = string.Join(" / ", group.Select(x => x.ClassificationEvidence).Distinct())
            });
        }
    }

    private IReadOnlyList<AllocationTarget> ReadTargets() => Targets.Select(row =>
    {
        if (!decimal.TryParse(row.Percent, NumberStyles.Number, CultureInfo.CurrentCulture, out var percent))
            throw new ArgumentException($"{row.Label}の目標比率を数値で入力してください。");
        return new AllocationTarget(row.Key.Trim(), row.Label.Trim(), percent);
    }).ToArray();

    private void Calculate()
    {
        try
        {
            if (!decimal.TryParse(AdditionalFunding, NumberStyles.Number, CultureInfo.CurrentCulture, out var funding))
                throw new ArgumentException("追加資金を円額で入力してください。");
            _analysis = _service.Analyze(_snapshots, SelectedDimension.Value, ReadTargets(), funding);
            Results.Clear(); foreach (var row in _analysis.Categories) Results.Add(row);
            RefreshStatus();
            Message = _analysis.Problems.Count == 0
                ? "計算しました。売買案は保有額との差額です。実際の注文は行いません。"
                : "計算条件を確認してください。";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            _analysis = null; Results.Clear(); RefreshStatus(); Message = ex.Message;
        }
    }

    private void Save()
    {
        try
        {
            var evidence = _service.CategorizeHoldings(_snapshots, SelectedDimension.Value)
                .Select(x => new AllocationClassificationEvidence(x.StockId, x.Ticker, x.Key, x.ClassificationEvidence))
                .ToArray();
            var plan = _repository().Save(SelectedDimension.Value, ReadTargets(), Revision, evidence);
            _revisions[SelectedDimension.Value] = plan.Revision;
            Message = $"{SelectedDimension.Label}の目標を第{plan.Revision}版として保存しました。自動発注は行いません。";
            OnPropertyChanged(nameof(Revision));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            Message = ex.Message;
        }
    }

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(ValuationStatus)); OnPropertyChanged(nameof(MinimumFunding));
        OnPropertyChanged(nameof(FundingStatus)); OnPropertyChanged(nameof(AnalysisNotes));
        OnPropertyChanged(nameof(Revision));
    }
    public static string Money(decimal amount) => amount.ToString("#,0.##", CultureInfo.CurrentCulture) + "円";
}
