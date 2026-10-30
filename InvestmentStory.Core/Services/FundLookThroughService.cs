using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class FundLookThroughService
{
    public static void Validate(FundDisclosure disclosure)
    {
        if (string.IsNullOrWhiteSpace(disclosure.FundTicker) || disclosure.PublishedDate.Date > DateTime.Today ||
            disclosure.PublishedDate == DateTime.MinValue || string.IsNullOrWhiteSpace(disclosure.Source))
            throw new ArgumentException("ファンド名、実際の公表日、出所が必要です。");
        if (!disclosure.IsFundConfirmed)
            throw new ArgumentException("対象銘柄がETFまたは投資信託であることを確認してください。");
        if (!disclosure.UsageRightsConfirmed || string.IsNullOrWhiteSpace(disclosure.UsageRightsNote))
            throw new ArgumentException("公表資料の利用条件を確認してから内訳を保存してください。");
        if (disclosure.Constituents.Count == 0 || disclosure.Constituents.Any(x =>
                string.IsNullOrWhiteSpace(x.Ticker) || string.IsNullOrWhiteSpace(x.Name) ||
                x.WeightPercent <= 0m || x.WeightPercent > 100m) ||
            disclosure.Constituents.Select(x => x.Ticker.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != disclosure.Constituents.Count)
            throw new ArgumentException("内訳の銘柄・名前・0%超の比率を重複なく入力してください。");
        if (disclosure.CoveragePercent > 100m)
            throw new ArgumentException("内訳のカバー率は100%を超えられません。");
    }

    public FundLookThroughResult Build(IReadOnlyList<StockSnapshot> snapshots,
        IReadOnlyList<FundDisclosure> disclosures, DateTime asOf)
    {
        var active = snapshots.Where(x => PositionEvidenceService.IsActiveHolding(x.Position)).ToArray();
        var notes = new List<string>();
        var rows = new Dictionary<string, (string Name, decimal Direct, decimal Indirect, bool Undisclosed)>(StringComparer.OrdinalIgnoreCase);
        void Add(string ticker, string name, decimal direct, decimal indirect, bool undisclosed)
        {
            rows.TryGetValue(ticker, out var old);
            rows[ticker] = (string.IsNullOrWhiteSpace(old.Name) ? name : old.Name,
                old.Direct + direct, old.Indirect + indirect, old.Undisclosed || undisclosed);
        }

        foreach (var snapshot in active)
        {
            var stock = snapshot.Position.Stock;
            if (!snapshot.HasKnownCurrentMarketValueJpy)
            {
                notes.Add($"{stock.Ticker}: 円評価額が未確認のため全体比率を算出できません。");
                continue;
            }
            var value = snapshot.CurrentMarketValueJpy;
            var disclosure = disclosures.Where(x => x.FundTicker.Equals(stock.Ticker, StringComparison.OrdinalIgnoreCase) &&
                    x.PublishedDate.Date <= asOf.Date && x.UsageRightsConfirmed)
                .OrderByDescending(x => x.PublishedDate).ThenByDescending(x => x.RecordedAtUtc).FirstOrDefault();
            if (disclosure is null)
            {
                Add(stock.Ticker, stock.Name, value, 0m, false);
                if (snapshot.Position.IsMutualFund)
                    notes.Add($"{stock.Ticker}: 公表内訳が未登録です。ファンドそのものとして表示します。");
                continue;
            }
            Validate(disclosure);
            decimal allocated = 0m;
            for (var i = 0; i < disclosure.Constituents.Count; i++)
            {
                var constituent = disclosure.Constituents[i];
                var portion = disclosure.CoveragePercent == 100m && i == disclosure.Constituents.Count - 1
                    ? value - allocated : value * constituent.WeightPercent / 100m;
                allocated += portion;
                Add(constituent.Ticker, constituent.Name, 0m, portion, false);
            }
            var residual = value - allocated;
            if (residual != 0m)
                Add("未公表:" + stock.Ticker, stock.Name + "（内訳未公表・端数を含む）", 0m,
                    residual, true);
            notes.Add($"{stock.Ticker}: {disclosure.PublishedDate:yyyy/MM/dd}公表、出所 {disclosure.Source}、カバー率 {disclosure.CoveragePercent:0.##}%。直接保有とは別の参考内訳です。");
        }

        var resultRows = rows.Select(x => new FundExposureRow(x.Key, x.Value.Name,
                x.Value.Direct, x.Value.Indirect, x.Value.Undisclosed))
            .OrderByDescending(x => x.TotalJpy).ToArray();
        var known = active.Where(x => x.HasKnownCurrentMarketValueJpy).Sum(x => x.CurrentMarketValueJpy);
        if (resultRows.Sum(x => x.TotalJpy) != known)
            throw new InvalidOperationException("直接保有とファンド内訳の合計が元の保有評価額と一致しません。");
        return new(active.All(x => x.HasKnownCurrentMarketValueJpy) ? known : null,
            known, resultRows, notes);
    }
}
