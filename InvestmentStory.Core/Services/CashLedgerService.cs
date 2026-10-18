using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public static class CashLedgerService
{
    public static string KindLabel(CashOperationKind kind) => kind switch
    {
        CashOperationKind.Deposit => "外部入金", CashOperationKind.Withdrawal => "外部出金",
        CashOperationKind.BuySettlement => "買付決済", CashOperationKind.SellSettlement => "売却決済",
        CashOperationKind.Dividend => "配当入金", CashOperationKind.Tax => "税金",
        CashOperationKind.Fee => "手数料", CashOperationKind.Transfer => "口座間振替",
        CashOperationKind.Exchange => "両替", _ => "不明"
    };

    public static void Validate(CashOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.Id) || operation.Date == DateTime.MinValue ||
            operation.Date.Date > DateTime.Today || !Enum.IsDefined(operation.Kind))
            throw new ArgumentException("実績の取引日・取引種類を確認してください。未来の予定は登録できません。");
        if (string.IsNullOrWhiteSpace(operation.Evidence))
            throw new ArgumentException("根拠（明細名・行番号など）を入力してください。");
        if (operation.Legs.Any(x => string.IsNullOrWhiteSpace(x.Account) || x.Account != x.Account.Trim() ||
            !IsCurrency(x.Currency) || x.Amount == 0m))
            throw new ArgumentException("口座名・通貨・0以外の金額を指定してください。");
        var paired = operation.Kind is CashOperationKind.Transfer or CashOperationKind.Exchange;
        if (operation.Legs.Count != (paired ? 2 : 1))
            throw new ArgumentException("振替・両替は出金側と入金側の2明細、それ以外は1明細が必要です。");
        var first = operation.Legs[0];
        if (paired)
        {
            var second = operation.Legs[1];
            if (first.Amount >= 0m || second.Amount <= 0m)
                throw new ArgumentException("振替・両替は出金側を負、入金側を正にしてください。");
            if (operation.Kind == CashOperationKind.Transfer &&
                (first.Account == second.Account || first.Currency != second.Currency || first.Amount + second.Amount != 0m))
                throw new ArgumentException("口座間振替は別口座・同一通貨・同額で記録してください。手数料は別明細です。");
            if (operation.Kind == CashOperationKind.Exchange && first.Currency == second.Currency)
                throw new ArgumentException("両替には異なる通貨を指定してください。");
        }
        else
        {
            var incoming = operation.Kind is CashOperationKind.Deposit or CashOperationKind.SellSettlement or CashOperationKind.Dividend;
            if (incoming != (first.Amount > 0m)) throw new ArgumentException("取引種類と金額の符号が一致しません。");
        }
    }

    public static bool IsCurrency(string currency) => currency.Length == 3 && currency.All(x => x is >= 'A' and <= 'Z');

    public static void ValidatePeriod(CashReconciliation period)
    {
        if (string.IsNullOrWhiteSpace(period.Account) || period.Account != period.Account.Trim() || !IsCurrency(period.Currency) ||
            period.StartDate == DateTime.MinValue || period.EndDate.Date < period.StartDate.Date || period.EndDate.Date > DateTime.Today)
            throw new ArgumentException("照合対象の口座・通貨・期間を確認してください。終了日は今日以前にしてください。");
        if (string.IsNullOrWhiteSpace(period.Evidence)) throw new ArgumentException("照合根拠を入力してください。");
    }

    private static IEnumerable<CashOperation> InPeriod(IEnumerable<CashOperation> operations, CashReconciliation period) =>
        operations.Where(x => x.Date.Date >= period.StartDate.Date && x.Date.Date <= period.EndDate.Date &&
            x.Legs.Any(l => l.Account == period.Account && l.Currency == period.Currency));

    public static string Fingerprint(IEnumerable<CashOperation> operations, CashReconciliation period)
    {
        var data = InPeriod(operations, period).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    }

    public static CashReconciliationResult Reconcile(IEnumerable<CashOperation> operations, CashReconciliation period)
    {
        var list = operations.ToArray();
        var active = InPeriod(list, period).Where(x => x.CancelledAtUtc is null).ToArray();
        decimal Amount(CashOperation x) => x.Legs.Where(l => l.Account == period.Account && l.Currency == period.Currency).Sum(l => l.Amount);
        var movement = active.Sum(Amount);
        var external = active.Where(x => x.Kind is CashOperationKind.Deposit or CashOperationKind.Withdrawal).Sum(Amount);
        var expected = period.OpeningBalance + movement;
        var difference = period.ClosingBalance - expected;
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(period.Account) || !IsCurrency(period.Currency) ||
            period.StartDate == DateTime.MinValue || period.EndDate.Date < period.StartDate.Date || period.EndDate.Date > DateTime.Today)
            reasons.Add("口座・通貨・対象期間が不正");
        if (period.OpeningBalance is null) reasons.Add("期首残高未確認");
        if (period.ClosingBalance is null) reasons.Add("期末残高未確認");
        if (!period.AllEntriesConfirmed) reasons.Add("期間内の全明細未確認");
        if (!period.ExternalFlowsConfirmed) reasons.Add("外部入出金の網羅性未確認");
        if (string.IsNullOrWhiteSpace(period.Evidence)) reasons.Add("照合根拠未記入");
        if (period.LedgerFingerprint != Fingerprint(list, period)) reasons.Add("台帳を再読込して照合を保存してください");
        if (difference is not null && difference != 0m) reasons.Add("期末残高に差異あり");
        return new(movement, external, expected, difference, reasons.Count == 0,
            reasons.Count == 0 ? "照合済み（この口座・通貨・期間のみ）" : string.Join(" / ", reasons));
    }
}
