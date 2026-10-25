using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

/// <summary>Read-only action summary. A source record remains open until its own workflow resolves it.</summary>
public sealed class TodayReviewService
{
    public TodayReviewOverview Build(
        IReadOnlyList<StockSnapshot> snapshots,
        IReadOnlyList<DividendPayment> payments,
        IReadOnlyList<ApiFetchLogEntry> recentApiLogs,
        CsvReviewSummary csv,
        DateTime asOf,
        IReadOnlyList<CsvReviewRecord>? csvRecords = null,
        IReadOnlyDictionary<int, DividendReviewDecision>? dividendReviews = null)
    {
        var active = snapshots.Where(x => PositionEvidenceService.IsActiveHolding(x.Position)).ToArray();
        var missingCost = active.Where(x => !x.HasKnownPurchaseCostJpy).ToArray();
        var missingKnownValue = missingCost.Any(x => !x.HasKnownCurrentMarketValueJpy);
        var costImpact = missingKnownValue ? "損益集計から除外した評価額: 算出不可" :
            $"損益集計から除外した評価額: {missingCost.Sum(x => x.CurrentMarketValueJpy):#,0}円";

        var reminderEvents = new ReminderService().Build(payments, recentApiLogs, asOf);
        var overdueIds = reminderEvents.Where(x => x.Kind == ReminderKind.DividendOverdue)
            .Select(x => int.Parse(x.Key.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();
        var failedIds = reminderEvents.Where(x => x.Kind == ReminderKind.UpdateFailure)
            .Select(x => int.Parse(x.Key.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();
        var dashboard = new DividendDashboardService();
        var overdue = payments.Where(x => x.PaymentDate.Year <= asOf.Year)
            .Select(x => x.PaymentDate.Year).Distinct()
            .SelectMany(year => dashboard.Build(payments, year, 0m, asOf).Entries)
            .Where(x => overdueIds.Contains(x.Payment.Id))
            .ToArray();
        var overdueAmount = overdue.Sum(x => x.NetJpy);
        var heldDividends = overdue.Count(x => dividendReviews is not null &&
            dividendReviews.TryGetValue(x.Payment.Id, out var decision) && decision.Action == "Held");

        var failed = recentApiLogs.Where(x => failedIds.Contains(x.Id)).ToArray();
        var csvCount = csv.UnresolvedTrades + csv.HoldingHistory + csv.DividendHistory;
        var csvOnHold = csvRecords?.Count(x => x.Status == "保留") ?? 0;

        var cards = new[]
        {
            new TodayReviewCard(TodayReviewKind.Csv, "CSV要確認", csvCount,
                $"取引の未解決 {csv.UnresolvedTrades}件 / 保有の要確認履歴 {csv.HoldingHistory}件 / 配当の要確認履歴 {csv.DividendHistory}件。保留 {csvOnHold}件を含み、確認済みは除外します。",
                "影響額: 元CSVの通貨・換算根拠が混在するため算出不可", "保存済み要確認記録を開く")
            {
                Items = (csvRecords ?? []).Select(x => new TodayReviewItem(TodayReviewKind.Csv,
                    x.SourceKey, $"{x.Category} {x.EventDate} {x.Security} / {x.Broker}",
                    x.Reason)).ToArray()
            },
            new TodayReviewCard(TodayReviewKind.Cost, "取得原価の不足", missingCost.Length,
                missingCost.Length == 0 ? "現在保有の原価未確認はありません。" :
                    string.Join("、", missingCost.Take(4).Select(x => x.Position.Stock.Ticker)) +
                    (missingCost.Length > 4 ? $" ほか{missingCost.Length - 4}件" : "") + " の円取得額を確認してください。",
                costImpact, "銘柄一覧から取得額の根拠を確認")
            {
                Items = missingCost.Select(x => new TodayReviewItem(TodayReviewKind.Cost,
                    SecurityIdentityService.BuildPositionKey(x.Position),
                    $"{x.Position.Stock.Ticker} / {x.Position.Stock.Broker} / {x.Position.Stock.AccountType}",
                    x.HasKnownCurrentMarketValueJpy ? $"評価額 {x.CurrentMarketValueJpy:#,0}円" : "評価額も未確認"))
                    .ToArray()
            },
            new TodayReviewCard(TodayReviewKind.Dividend, "期限経過の配当", overdue.Length,
                overdue.Length == 0 ? "予定日経過で未照合の配当はありません。" :
                    $"{overdue.Min(x => x.Payment.PaymentDate):yyyy/MM/dd}以降の未照合予定。保留 {heldDividends}件を含みます。実績と一致している予定は除外しています。",
                $"予定税引後額（参考）: {overdueAmount:#,0}円。未入金額の確定値ではありません。", "配当実績で予定と入金を照合")
            {
                Items = overdue.OrderBy(x => x.Payment.PaymentDate)
                    .Select(x => new TodayReviewItem(TodayReviewKind.Dividend,
                        x.Payment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        $"{x.Payment.PaymentDate:yyyy/MM/dd} {x.Payment.Ticker} / {x.Payment.Broker}",
                        $"予定税引後 {x.NetJpy:#,0}円（参考）" +
                        (dividendReviews is not null && dividendReviews.TryGetValue(x.Payment.Id, out var review) &&
                         review.Action == "Held" ? $" / 保留: {review.Reason}" : "")))
                    .ToArray()
            },
            new TodayReviewCard(TodayReviewKind.Update, "データ更新の失敗", failed.Length,
                failed.Length == 0 ? "直近の取得履歴に継続中の失敗はありません。" :
                    string.Join("、", failed.Take(4).Select(x => $"{x.Provider}/{x.Symbol}")) +
                    (failed.Length > 4 ? $" ほか{failed.Length - 4}件" : "") + "。後続の成功がある対象は除外しています。",
                "影響額: 更新できない銘柄の評価差を確定できません", "設定のAPI取得履歴と銘柄の鮮度を確認")
            {
                Items = failed.Select(x => new TodayReviewItem(TodayReviewKind.Update,
                    x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{x.FetchedAt:yyyy/MM/dd HH:mm} {x.Provider} / {x.Symbol}",
                    $"{x.ApiType}: {x.ErrorMessage}"))
                    .ToArray()
            }
        };
        return new TodayReviewOverview(asOf.Date, cards,
            "CSVは未解決取引と、解決状態を持たない保有・配当の要確認履歴を区別。配当未照合・更新失敗は通知一覧と同じ対象判定を使用します。");
    }
}
