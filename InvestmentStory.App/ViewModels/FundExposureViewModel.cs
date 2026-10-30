using System.Collections.ObjectModel;
using System.Globalization;
using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;

namespace InvestmentStory.App.ViewModels;

public sealed class FundConstituentEdit : ObservableObject
{
    private string _ticker = "", _name = "", _weight = "";
    public string Ticker { get => _ticker; set => SetProperty(ref _ticker, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Weight { get => _weight; set => SetProperty(ref _weight, value); }
}

public sealed record FundDisclosureDisplay(FundDisclosure Disclosure)
{
    public string FundTicker => Disclosure.FundTicker;
    public string PublishedDate => Disclosure.PublishedDate.ToString("yyyy/MM/dd");
    public string Coverage => Disclosure.CoveragePercent.ToString("0.##", CultureInfo.CurrentCulture) + "%";
    public string Source => Disclosure.Source;
    public string Rights => Disclosure.UsageRightsNote;
}

public sealed record FundExposureDisplay(FundExposureRow Row)
{
    public string Name => Row.Name;
    public string Ticker => Row.Ticker;
    public string Direct => Row.DirectJpy.ToString("#,0", CultureInfo.CurrentCulture) + "円";
    public string Indirect => Row.IndirectJpy.ToString("#,0", CultureInfo.CurrentCulture) + "円";
    public string Total => Row.TotalJpy.ToString("#,0", CultureInfo.CurrentCulture) + "円";
    public string Kind => Row.IsUndisclosed ? "未公表" : Row.DirectJpy > 0m && Row.IndirectJpy > 0m ? "直接＋間接" :
        Row.IndirectJpy > 0m ? "ファンド内訳" : "直接保有";
}

public sealed class FundExposureViewModel : ObservableObject
{
    private readonly Func<FundDisclosureRepository> _repository;
    private readonly FundLookThroughService _service = new();
    private IReadOnlyList<StockSnapshot> _snapshots = [];
    private string _fundTicker = "", _source = "", _rightsNote = "", _message = "ETF・投信の公表内訳を手入力できます。自動取得・外部送信は行いません。";
    private DateTime? _publishedDate = DateTime.Today;
    private bool _rightsConfirmed, _isFundConfirmed;
    private FundConstituentEdit? _selectedConstituent;
    private FundDisclosureDisplay? _selectedDisclosure;
    private decimal? _portfolioValue;

    public FundExposureViewModel(Func<FundDisclosureRepository> repository)
    {
        _repository = repository;
        AddConstituentCommand = new RelayCommand(() => Constituents.Add(new FundConstituentEdit()));
        RemoveConstituentCommand = new RelayCommand(() =>
        {
            if (SelectedConstituent is not null) Constituents.Remove(SelectedConstituent);
        });
        SaveCommand = new RelayCommand(Save);
        UseSelectedCommand = new RelayCommand(UseSelected);
    }

    public RelayCommand AddConstituentCommand { get; }
    public RelayCommand RemoveConstituentCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand UseSelectedCommand { get; }
    public ObservableCollection<FundConstituentEdit> Constituents { get; } = new();
    public ObservableCollection<FundDisclosureDisplay> Disclosures { get; } = new();
    public ObservableCollection<FundExposureDisplay> Exposures { get; } = new();
    public ObservableCollection<string> Notes { get; } = new();
    public IReadOnlyList<string> FundOptions => _snapshots.Where(x => PositionEvidenceService.IsActiveHolding(x.Position))
        .Select(x => x.Position.Stock.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
    public string FundTicker { get => _fundTicker; set => SetProperty(ref _fundTicker, value); }
    public DateTime? PublishedDate { get => _publishedDate; set => SetProperty(ref _publishedDate, value); }
    public string Source { get => _source; set => SetProperty(ref _source, value); }
    public string RightsNote { get => _rightsNote; set => SetProperty(ref _rightsNote, value); }
    public bool RightsConfirmed { get => _rightsConfirmed; set => SetProperty(ref _rightsConfirmed, value); }
    public bool IsFundConfirmed { get => _isFundConfirmed; set => SetProperty(ref _isFundConfirmed, value); }
    public FundConstituentEdit? SelectedConstituent { get => _selectedConstituent; set => SetProperty(ref _selectedConstituent, value); }
    public FundDisclosureDisplay? SelectedDisclosure { get => _selectedDisclosure; set => SetProperty(ref _selectedDisclosure, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string PortfolioValue => _portfolioValue is decimal value
        ? $"保有評価額との照合: {value:#,0}円（直接＋間接＋未公表内訳の合計）"
        : "評価額未確認の銘柄があり、全体比率は算出不可です。表示値は確認できた分だけです。";

    public void Load(IReadOnlyList<StockSnapshot> snapshots)
    {
        _snapshots = snapshots;
        OnPropertyChanged(nameof(FundOptions));
        Refresh();
    }

    private void Refresh()
    {
        var history = _repository().GetHistory();
        Disclosures.Clear();
        foreach (var disclosure in history) Disclosures.Add(new FundDisclosureDisplay(disclosure));
        var result = _service.Build(_snapshots, history, DateTime.Today);
        _portfolioValue = result.PortfolioValueJpy;
        OnPropertyChanged(nameof(PortfolioValue));
        Exposures.Clear();
        foreach (var row in result.Rows) Exposures.Add(new FundExposureDisplay(row));
        Notes.Clear();
        foreach (var note in result.Notes) Notes.Add(note);
    }

    private void Save()
    {
        try
        {
            if (!FundOptions.Contains(FundTicker.Trim(), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("保有中のETF・投信のティッカーを選んでください。");
            var constituents = Constituents.Select(x => new FundConstituent(x.Ticker.Trim(), x.Name.Trim(),
                decimal.TryParse(x.Weight, NumberStyles.Number, CultureInfo.CurrentCulture, out var weight)
                    ? weight : throw new ArgumentException($"{x.Ticker}の比率を数値で入力してください。"))).ToArray();
            var disclosure = new FundDisclosure("", FundTicker, PublishedDate ?? DateTime.MinValue, Source,
                RightsConfirmed, DateTime.UtcNow, constituents)
            {
                UsageRightsNote = RightsNote,
                IsFundConfirmed = IsFundConfirmed
            };
            var saved = _repository().Save(disclosure);
            Refresh();
            Message = $"{saved.FundTicker}の{saved.PublishedDate:yyyy/MM/dd}公表内訳（{saved.CoveragePercent:0.##}%）を保存しました。過去版は残します。";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            Message = ex.Message;
        }
    }

    private void UseSelected()
    {
        if (SelectedDisclosure is null) return;
        var value = SelectedDisclosure.Disclosure;
        FundTicker = value.FundTicker; PublishedDate = value.PublishedDate; Source = value.Source;
        RightsNote = value.UsageRightsNote; RightsConfirmed = value.UsageRightsConfirmed; IsFundConfirmed = value.IsFundConfirmed;
        Constituents.Clear();
        foreach (var item in value.Constituents)
            Constituents.Add(new FundConstituentEdit
            {
                Ticker = item.Ticker, Name = item.Name,
                Weight = item.WeightPercent.ToString(CultureInfo.CurrentCulture)
            });
        Message = "保存済み内訳を編集欄に読み込みました。保存すると新しい版を追加し、旧版は残します。";
    }
}
