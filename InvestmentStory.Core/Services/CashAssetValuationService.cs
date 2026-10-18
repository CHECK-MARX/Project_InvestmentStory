using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class CashAssetValuationService
{
    public CashAssetSummary Build(CashLedgerState ledger, DateTime asOf,
        ExchangeRateQuote? usdJpyQuote = null, bool allowSampleQuotes = false)
    {
        var operations = ledger.Operations.Where(x => x.Date.Date <= asOf.Date).ToArray();
        var periods = ledger.Reconciliations.Where(x => x.StartDate.Date <= asOf.Date).ToArray();
        var scopes = operations.SelectMany(x => x.Legs).Select(x => (x.Account, x.Currency))
            .Concat(periods.Select(x => (x.Account, x.Currency))).Distinct()
            .OrderBy(x => x.Account, StringComparer.Ordinal).ThenBy(x => x.Currency, StringComparer.Ordinal);
        var balances = new List<CashAssetBalance>();
        foreach (var (account, currency) in scopes)
        {
            var candidates = periods.Where(x => x.Account == account && x.Currency == currency && x.EndDate.Date <= asOf.Date)
                .OrderByDescending(x => x.EndDate.Date).ThenByDescending(x => x.ReviewedAtUtc)
                .ThenByDescending(x => x.StartDate.Date).ToArray();
            var latest = candidates.FirstOrDefault();
            if (latest is null)
            {
                balances.Add(new(account, currency, null, null, null, "基準日以前の残高照合なし", string.Empty));
                continue;
            }
            CashAssetBalance Row(decimal? included, string status) => new(account, currency, latest.EndDate.Date,
                latest.ClosingBalance, included, status,
                $"照合期間 {latest.StartDate:yyyy/MM/dd}～{latest.EndDate:yyyy/MM/dd} / 根拠: {latest.Evidence}");
            if (candidates.Where(x => x.EndDate.Date == latest.EndDate.Date && x.ClosingBalance.HasValue)
                .Select(x => x.ClosingBalance!.Value).Distinct().Skip(1).Any())
            {
                balances.Add(Row(null, "同じ基準日の期末残高が不一致。各照合記録を確認してください"));
                continue;
            }
            // A newer failed review must not silently fall back to an older successful one.
            var result = CashLedgerService.Reconcile(ledger.Operations, latest);
            if (!result.IsComplete)
            {
                balances.Add(Row(null, result.Status));
                continue;
            }
            if (latest.EndDate.Date != asOf.Date)
            {
                balances.Add(Row(null, $"{latest.EndDate:yyyy/MM/dd}まで照合済み。{asOf:yyyy/MM/dd}の残高は未確認"));
                continue;
            }
            var amount = latest.ClosingBalance!.Value;
            if (currency == "JPY" || amount == 0m)
            {
                balances.Add(Row(amount, amount == 0m ? "照合済み・残高0（換算不要）" : "照合済み"));
                continue;
            }
            if (currency != "USD" || !CanUseQuote(usdJpyQuote, asOf, allowSampleQuotes))
            {
                balances.Add(Row(null, "残高は照合済み・円換算用の為替と取得日が未確認"));
                continue;
            }
            var quote = usdJpyQuote!;
            var freshness = quote.AcquiredAt.Date < asOf.Date.AddDays(-7) ? " / 為替更新遅れ" : string.Empty;
            balances.Add(Row(amount * quote.Rate,
                $"参考換算 USD/JPY {quote.Rate:#,0.####} / {quote.AcquiredAt:yyyy/MM/dd} / {quote.Source}{freshness}"));
        }
        return new() { AsOfDate = asOf.Date, Balances = balances };
    }

    public static bool CanUseQuote(ExchangeRateQuote? quote, DateTime asOf, bool allowSampleQuotes = false) =>
        quote is not null && quote.BaseCurrency == "USD" && quote.QuoteCurrency == "JPY" &&
        PositionEvidenceService.HasUsableForeignExchangeRate(quote.Rate) &&
        quote.AcquiredAt != DateTime.MinValue && quote.AcquiredAt.Date <= asOf.Date &&
        !string.IsNullOrWhiteSpace(quote.Source) &&
        !quote.InputType.Contains("Mock", StringComparison.OrdinalIgnoreCase) &&
        !quote.Source.Contains("Mock", StringComparison.OrdinalIgnoreCase) &&
        (allowSampleQuotes || (!quote.InputType.Contains("Sample", StringComparison.OrdinalIgnoreCase) &&
                             !quote.Source.Contains("Sample", StringComparison.OrdinalIgnoreCase)));
}
