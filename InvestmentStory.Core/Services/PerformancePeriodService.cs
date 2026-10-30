using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class PerformancePeriodService
{
    public static CashLedgerState RelevantLedger(CashLedgerState ledger, DateTime end) => new(
        ledger.Operations.Where(x => x.Date.Date <= end.Date).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(),
        ledger.Reconciliations.Where(x => x.StartDate.Date <= end.Date).OrderBy(x => x.Account, StringComparer.Ordinal)
            .ThenBy(x => x.Currency, StringComparer.Ordinal).ThenBy(x => x.StartDate).ThenBy(x => x.EndDate).ToArray());

    public static string Fingerprint(CashLedgerState ledger, DateTime end) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(RelevantLedger(ledger, end)))));

    public PerformancePeriodResult Calculate(PerformancePeriodInput input, CashLedgerState ledger)
    {
        try { return CalculateCore(input, RelevantLedger(ledger, input.EndDate)); }
        catch (OverflowException)
        {
            return new() { Problems = ["金額が計算可能な範囲を超えています。入力値を確認してください。"] };
        }
    }

    private static PerformancePeriodResult CalculateCore(PerformancePeriodInput input, CashLedgerState ledger)
    {
        var problems = new List<string>();
        var start = input.StartDate.Date; var end = input.EndDate.Date;
        if (start == DateTime.MinValue || end <= start || end > DateTime.Today)
            return new() { Problems = ["開始日末より後、今日以前の終了日末を指定してください。"] };
        if (input.OpeningSecuritiesJpy is null or < 0m || input.ClosingSecuritiesJpy is null or < 0m)
            problems.Add("期首・期末の株・投信等の評価額が未確認または負の値です。残高なしは0を入力してください。");
        if (!input.SecuritiesConfirmed || string.IsNullOrWhiteSpace(input.OpeningEvidence) || string.IsNullOrWhiteSpace(input.ClosingEvidence))
            problems.Add("両時点の評価根拠と、同じ範囲の株・投信等を含むことを確認してください。");
        if (!input.AllAccountsConfirmed) problems.Add("管理対象の現金口座・通貨がすべて台帳にあることを確認してください。");
        if (!input.NoInKindTransfersConfirmed) problems.Add("期間中の管理対象外との現物入出庫が未確認です。現物入出庫を含む期間はこの計算の対象外です。");

        decimal? ConvertJpy(decimal? value, string currency, DateTime date)
        {
            if (value is null) return null;
            if (currency == "JPY" || value == 0m) return value;
            var quotes = input.Fx.Where(x => x.Currency == currency && x.Date.Date == date).ToArray();
            if (quotes.Length != 1 || quotes[0].JpyPerUnit <= 0m || quotes[0].QuoteDate == DateTime.MinValue ||
                quotes[0].QuoteDate.Date > date || (date - quotes[0].QuoteDate.Date).TotalDays > 7 || string.IsNullOrWhiteSpace(quotes[0].Evidence))
            {
                problems.Add($"{date:yyyy/MM/dd} {currency}: 換算根拠不足・重複・不正な為替です（公表日は当日または直前7日以内）。");
                return null;
            }
            return value * quotes[0].JpyPerUnit;
        }

        var balances = new List<PerformanceCashBalance>();
        var scopes = ledger.Operations.SelectMany(x => x.Legs).Select(x => (x.Account, x.Currency))
            .Concat(ledger.Reconciliations.Select(x => (x.Account, x.Currency))).Distinct()
            .OrderBy(x => x.Account, StringComparer.Ordinal).ThenBy(x => x.Currency, StringComparer.Ordinal).ToArray();
        if (scopes.Length == 0) problems.Add("現金口座の残高照合がありません。現金が0でも口座・通貨と残高0の照合を登録してください。");
        foreach (var (account, currency) in scopes)
        {
            var candidates = ledger.Reconciliations.Where(x => x.Account == account && x.Currency == currency &&
                x.StartDate.Date == start.AddDays(1) && x.EndDate.Date == end).ToArray();
            if (candidates.Length != 1)
            {
                var reason = $"{account} / {currency}: {start.AddDays(1):yyyy/MM/dd}～{end:yyyy/MM/dd}の現金残高照合が必要です。";
                problems.Add(reason); balances.Add(new(account, currency, null, null, null, null, reason)); continue;
            }
            var review = candidates[0]; var result = CashLedgerService.Reconcile(ledger.Operations, review);
            if (!result.IsComplete) problems.Add($"{account} / {currency}: {result.Status}");
            var opening = result.IsComplete ? ConvertJpy(review.OpeningBalance, currency, start) : null;
            var closing = result.IsComplete ? ConvertJpy(review.ClosingBalance, currency, end) : null;
            balances.Add(new(account, currency, review.OpeningBalance, review.ClosingBalance, opening, closing,
                result.IsComplete && (opening is null || closing is null) ? "為替根拠を確認してください" : result.Status));
        }
        var flows = new List<PerformanceFlow>();
        foreach (var operation in ledger.Operations.Where(x => x.CancelledAtUtc is null && x.Date.Date > start &&
            x.Date.Date <= end && x.Kind is CashOperationKind.Deposit or CashOperationKind.Withdrawal).OrderBy(x => x.Date))
        {
            foreach (var leg in operation.Legs)
                flows.Add(new(operation.Date.Date, leg.Account, leg.Currency, leg.Amount,
                    ConvertJpy(leg.Amount, leg.Currency, operation.Date.Date), operation.Evidence, operation.Id));
        }
        if (problems.Count > 0) return new()
        {
            Problems = problems.Distinct().ToArray(), Balances = balances, Flows = flows,
            TimeWeighted = new(null, "不足条件を確認してください"), MoneyWeighted = new(null, "不足条件を確認してください"),
            ModifiedDietz = new(null, "不足条件を確認してください"),
            Benchmark = new(null, null, null, input.Benchmark is null ? "比較指数未指定" :
                "運用側の残高・外部入出金・為替に不足があるため比較できません。")
        };
        var openingTotal = input.OpeningSecuritiesJpy!.Value + balances.Sum(x => x.OpeningJpy!.Value);
        var closingTotal = input.ClosingSecuritiesJpy!.Value + balances.Sum(x => x.ClosingJpy!.Value);
        var externalNet = flows.Sum(x => x.JpyAmount!.Value);
        if (openingTotal < 0m || closingTotal < 0m)
            return new() { Problems = ["総資産が負になる期間は計算対象外です。借入・決済残高を確認してください。"], Balances = balances, Flows = flows };
        var profit = closingTotal - openingTotal - externalNet;
        var twr = TimeWeighted(openingTotal, closingTotal, flows, input.FlowValuations);
        var weightedBase = openingTotal + flows.Sum(x => x.JpyAmount!.Value * (end - x.Date).Days / (end - start).Days);
        var dietz = weightedBase <= 0m ? new ReturnMeasure(null, "時間調整後の投資額が0以下のため算出不可")
            : new((double)(profit / weightedBase), "概算・期間収益率。外部入出金は各日の末尾と仮定。年率換算なし。");
        var cashFlows = flows.Select(x => (x.Date, Amount: -x.JpyAmount!.Value)).ToList();
        cashFlows.Add((start, -openingTotal)); cashFlows.Add((end, closingTotal));
        return new()
        {
            OpeningTotalJpy = openingTotal, ClosingTotalJpy = closingTotal, ExternalNetFlowJpy = externalNet, ProfitJpy = profit,
            Balances = balances, Flows = flows, TimeWeighted = twr, ModifiedDietz = dietz, MoneyWeighted = Xirr(cashFlows),
            Benchmark = CompareBenchmark(input, twr)
        };
    }

    public static PerformanceBenchmarkComparison CompareBenchmark(PerformancePeriodInput input, ReturnMeasure portfolioTwr)
    {
        var benchmark = input.Benchmark;
        if (benchmark is null) return new(null, portfolioTwr.Value, null, "比較指数未指定");
        if (benchmark.StartDate.Date != input.StartDate.Date || benchmark.EndDate.Date != input.EndDate.Date)
            return new(null, portfolioTwr.Value, null, "指数と運用成績の期首・期末が異なります。期間を揃えてください。");
        if (benchmark.IsHistoricalEstimate)
            return new(null, portfolioTwr.Value, null, "遡及推計の指数値は実測と区別し、比較差を算出しません。");
        if (!benchmark.SourceConfirmed || string.IsNullOrWhiteSpace(benchmark.Name) || string.IsNullOrWhiteSpace(benchmark.Evidence))
            return new(null, portfolioTwr.Value, null, "指数名、両時点の公表値・出所と確認状態が必要です。");
        if (!benchmark.IncludesNetDividends)
            return new(null, portfolioTwr.Value, null, "保有成績は税引後配当を含みます。配当条件の異なる価格指数・税引前指数とは比較しません。");
        if (!CashLedgerService.IsCurrency(benchmark.Currency) || benchmark.OpeningLevel is null or <= 0m ||
            benchmark.ClosingLevel is null or <= 0m)
            return new(null, portfolioTwr.Value, null, "指数の通貨と両時点の正の指数値を確認してください。");

        double? YenRate(DateTime date)
        {
            if (benchmark.Currency == "JPY") return 1d;
            var quotes = input.Fx.Where(x => x.Currency == benchmark.Currency && x.Date.Date == date.Date).ToArray();
            if (quotes.Length != 1 || quotes[0].JpyPerUnit <= 0m || quotes[0].QuoteDate.Date > date.Date ||
                (date.Date - quotes[0].QuoteDate.Date).TotalDays > 7 || string.IsNullOrWhiteSpace(quotes[0].Evidence))
                return null;
            return (double)quotes[0].JpyPerUnit;
        }
        var openingRate = YenRate(input.StartDate);
        var closingRate = YenRate(input.EndDate);
        if (openingRate is null || closingRate is null)
            return new(null, portfolioTwr.Value, null, "指数の通貨を円に揃える期首・期末の為替根拠が不足しています。");
        var openingJpy = (double)benchmark.OpeningLevel.Value * openingRate.Value;
        var closingJpy = (double)benchmark.ClosingLevel.Value * closingRate.Value;
        var benchmarkReturn = closingJpy / openingJpy - 1d;
        if (!double.IsFinite(benchmarkReturn))
            return new(null, portfolioTwr.Value, null, "指数値が計算可能な範囲を超えています。");
        if (portfolioTwr.Value is not double twr)
            return new(benchmarkReturn, null, null, "運用側の同期間TWRが算出不可です。XIRR年率・概算率との比較差は表示しません。");
        return new(benchmarkReturn, twr, twr - benchmarkReturn,
            "同一期間・円建て・税引後配当込み指数との期間率比較。指数と実際の税・手数料条件の差は根拠で確認してください。");
    }

    private static ReturnMeasure TimeWeighted(decimal opening, decimal closing,
        IReadOnlyList<PerformanceFlow> flows, IReadOnlyList<PerformanceFlowValuation> valuations)
    {
        if (opening <= 0m) return new(null, "期首総資産が0以下のため算出不可。投資開始後の期間を指定してください。");
        if (valuations.Any(x => !flows.Any(f => f.OperationId == x.OperationId)))
            return new(null, "入出金時点の評価に対象外・取消済みの取引が含まれています。台帳から欄を作成し直してください。");
        if (flows.Count == 0) return new((double)(closing / opening - 1m), "外部入出金なし・期間収益率（年率換算なし）");
        var records = new List<(PerformanceFlow Flow, PerformanceFlowValuation Valuation)>();
        foreach (var flow in flows)
        {
            var matching = valuations.Where(x => x.OperationId == flow.OperationId).ToArray();
            if (matching.Length != 1 || !matching[0].Confirmed || string.IsNullOrWhiteSpace(matching[0].Evidence) ||
                matching[0].BeforeJpy is null or < 0m || matching[0].AfterJpy is null or < 0m || matching[0].Sequence <= 0)
                return new(null, $"{flow.Date:yyyy/MM/dd} {flow.Account}: 入出金の直前・直後の総資産、当日の順序と根拠を確認してください。概算は修正ディーツ法を参照できます。");
            var value = matching[0];
            if (value.AfterJpy - value.BeforeJpy != flow.JpyAmount)
                return new(null, $"{flow.Date:yyyy/MM/dd} {flow.Account}: 直後−直前の評価差と外部入出金の円額が一致しません。同じ評価・換算条件で確認してください。");
            records.Add((flow, value));
        }
        if (records.GroupBy(x => (x.Flow.Date, x.Valuation.Sequence)).Any(x => x.Count() != 1))
            return new(null, "同じ日の入出金の順序が重複しています。実際に発生した順を指定してください。");
        var ordered = records.OrderBy(x => x.Flow.Date).ThenBy(x => x.Valuation.Sequence).ToArray();
        var previous = opening; double factor = 1d;
        for (var i = 0; i < ordered.Length; i++)
        {
            var value = ordered[i].Valuation;
            if (previous <= 0m) return new(null, "途中に投資残高0の区間があるため連結できません。投資残高がある期間に分けて確認してください。");
            factor *= (double)(value.BeforeJpy!.Value / previous);
            previous = value.AfterJpy!.Value;
        }
        if (previous == 0m && closing == 0m)
            return new(double.IsFinite(factor) ? factor - 1d : null, "最終出金までの期間収益率（年率換算なし）。出金後は投資残高0。");
        if (previous <= 0m) return new(null, "最終入出金後の残高が0のため期末まで連結できません。");
        factor *= (double)(closing / previous);
        return double.IsFinite(factor) ? new(factor - 1d, "全外部入出金の直前・直後評価を連結した期間収益率。年率換算なし。")
            : new(null, "計算可能な範囲を超えています。");
    }

    // A single negative-to-positive sign transition guarantees a unique root. We deliberately
    // withhold a number for non-conventional flows instead of choosing one arbitrary root.
    public static ReturnMeasure Xirr(IEnumerable<(DateTime Date, decimal Amount)> values)
    {
        var groups = values.GroupBy(x => x.Date.Date).Select(g => (Date: g.Key, Amount: g.Sum(x => x.Amount)))
            .Where(x => x.Amount != 0m).OrderBy(x => x.Date).ToArray();
        if (groups.Length < 2 || groups[0].Amount >= 0m || groups[^1].Amount <= 0m)
            return new(null, "異なる日の投資（負）と回収（正）が必要です。XIRRを確定できません。");
        var changes = groups.Zip(groups.Skip(1)).Count(pair => Math.Sign(pair.First.Amount) != Math.Sign(pair.Second.Amount));
        if (changes != 1) return new(null, "資金の符号が複数回変わります。複数解・解なしの可能性があり、一意性を確認できないため算出しません。");
        var origin = groups[0].Date;
        var terms = groups.Select(x => (Years: (x.Date - origin).TotalDays / 365d,
            LogAmount: Math.Log(Math.Abs((double)x.Amount)), Positive: x.Amount > 0m)).ToArray();
        double LogSum(bool positive, double q)
        {
            var logs = terms.Where(x => x.Positive == positive).Select(x => x.LogAmount - q * x.Years).ToArray();
            var max = logs.Max(); return max + Math.Log(logs.Sum(x => Math.Exp(x - max)));
        }
        double Difference(double q) => LogSum(true, q) - LogSum(false, q);
        var low = Math.Log(1e-10); var high = Math.Log(1e6);
        if (Difference(low) < 0d || Difference(high) > 0d) return new(null, "年率が数値計算の対応範囲外です。期間と金額を確認してください。");
        for (var i = 0; i < 160; i++)
        {
            var middle = (low + high) / 2d;
            if (Difference(middle) > 0d) low = middle; else high = middle;
        }
        var rate = Math.Exp((low + high) / 2d) - 1d;
        if (Math.Abs(rate) < 1e-12) rate = 0d;
        return new(rate, "円ベース・年率（実日数 / 365日）。入力した評価額・為替と照合済み台帳に基づきます。");
    }
}
