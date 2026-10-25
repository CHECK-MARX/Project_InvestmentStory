using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class CashCsvParser
{
    public CashCsvPlan Parse(string filePath, string cashAccount, string emptyCurrency = "", string mappingReason = "")
    {
        if (string.IsNullOrWhiteSpace(cashAccount)) throw new ArgumentException("入出金先の現金口座名を指定してください。");
        if (emptyCurrency.Length > 0 && (!CashLedgerService.IsCurrency(emptyCurrency) || string.IsNullOrWhiteSpace(mappingReason)))
            throw new ArgumentException("空欄通貨を指定する場合は、通貨コードと確認根拠を入力してください。");
        var hash = FileHash(filePath);
        var records = CsvFileEncoding.ReadAllRecords(filePath);
        var parsed = records.Select(Split).ToArray();
        var headerIndex = Array.FindIndex(parsed, x =>
            x.Contains("受渡日") && x.Contains("取引区分") && x.Contains("決済通貨") ||
            x.Contains("受渡日") && x.Contains("取引") && x.Contains("手数料/諸経費等") ||
            x.Contains("国内受渡日") && x.Contains("取引"));
        if (headerIndex < 0) throw new InvalidOperationException("対応する現金明細ヘッダーがありません。野村取引履歴・SBI国内約定・SBI米国株約定CSVに対応しています。配当の円換算表示だけでは現金の決済通貨を確定できません。");
        var headers = parsed[headerIndex];
        var nomura = headers.Contains("取引区分");
        var foreign = headers.Contains("国内受渡日");
        var broker = nomura ? "野村証券" : "SBI証券";
        var format = nomura ? "NomuraCash-v1" : foreign ? "SbiForeignCash-v1" : "SbiDomesticCash-v1";
        var dateColumn = foreign ? "国内受渡日" : "受渡日";
        var kindColumn = nomura ? "取引区分" : "取引";
        var amountColumn = headers.Contains("受渡金額") ? "受渡金額" : "受渡金額/決済損益";
        CsvRowValidation.ValidateHeaders(headers, dateColumn, kindColumn, amountColumn,
            foreign ? "国内約定日" : "約定日");
        var rows = new List<CashCsvCandidate>();
        for (var index = headerIndex + 1; index < records.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(records[index])) continue;
            var row = CsvRowValidation.ToDictionary(headers, parsed[index], index + 1);
            string Get(string name) => row.TryGetValue(name, out var value) ? value.Trim() : string.Empty;
            var semantic = Hash(broker + JsonSerializer.Serialize(row.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new[] { x.Key, x.Value.Trim() })));
            var sourceKey = Hash(hash + ":" + (index + 1).ToString(CultureInfo.InvariantCulture));
            CashOperation? operation = null;
            var problem = string.Empty;
            try
            {
                var kind = ParseKind(Get(kindColumn), nomura);
                var dateText = Get(dateColumn);
                if (Regex.IsMatch(dateText, @"^\d{2}/\d{1,2}/\d{1,2}$")) dateText = "20" + dateText;
                if (!DateTime.TryParseExact(dateText, ["yyyy/MM/dd", "yyyy/M/d", "yyyy年MM月dd日", "yyyy年M月d日", "yyyy-MM-dd"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new ArgumentException("受渡日を確定できません。");
                var amountText = Get(amountColumn);
                var suffix = Regex.Match(amountText, @"\s*([A-Z]{3})$");
                var currency = nomura ? NormalizeCurrency(Get("決済通貨")) : foreign
                    ? NormalizeCurrency(Get("通貨")) : "JPY";
                if (suffix.Success)
                {
                    if (currency.Length > 0 && currency != suffix.Groups[1].Value)
                        throw new ArgumentException("金額の通貨と決済通貨が一致しません。");
                    currency = suffix.Groups[1].Value;
                    amountText = amountText[..suffix.Index];
                }
                var overrideUsed = currency.Length == 0 && emptyCurrency.Length > 0;
                if (overrideUsed) currency = emptyCurrency;
                if (!CashLedgerService.IsCurrency(currency)) throw new ArgumentException("決済通貨が未確認です。発行通貨から推定しません。");
                if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0m)
                    throw new ArgumentException("正の受渡金額を確定できません。取消・訂正や0円の明細は手動確認が必要です。");
                var incoming = kind is CashOperationKind.Deposit or CashOperationKind.SellSettlement or CashOperationKind.Dividend;
                var note = overrideUsed ? $" / 空欄決済通貨を{currency}と指定: {mappingReason.Trim()}" : string.Empty;
                operation = new CashOperation
                {
                    Id = "cashcsv_" + sourceKey, Date = date, Kind = kind,
                    Evidence = $"{Path.GetFileName(filePath)}:{index + 1} / {broker} / {Get(kindColumn)} / 受渡金額{note}",
                    Legs = [new(cashAccount.Trim(), currency, incoming ? amount : -amount)]
                };
                CashLedgerService.Validate(operation);
            }
            catch (ArgumentException ex) { operation = null; problem = ex.Message; }
            rows.Add(new(sourceKey, semantic, index + 1, broker, format, records[index], operation, problem));
        }
        if (hash != FileHash(filePath)) throw new IOException("読込中にCSVが変更されました。プレビューをやり直してください。");
        if (rows.Count == 0) throw new InvalidOperationException("現金台帳に確認できる明細がありません。");
        return new(Path.GetFullPath(filePath), hash, rows, cashAccount.Trim(), emptyCurrency, mappingReason);
    }

    public static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string NormalizeCurrency(string value) => value switch
    {
        "日本円" or "円" or "JPY" => "JPY", "米国ドル" or "米ドル" or "USD" => "USD", _ => value.ToUpperInvariant()
    };
    private static CashOperationKind ParseKind(string value, bool nomura) => value switch
    {
        "現物買付" when nomura => CashOperationKind.BuySettlement,
        "現物売却" when nomura => CashOperationKind.SellSettlement,
        "入金（配当金）" when nomura => CashOperationKind.Dividend,
        "入金（振込）" when nomura => CashOperationKind.Deposit,
        "出金（振込）" when nomura => CashOperationKind.Withdrawal,
        "株式現物買" or "投信金額買付" or "買付" or "現買" when !nomura => CashOperationKind.BuySettlement,
        "株式現物売" or "投信金額売却" or "投信解約" or "売却" or "現売" when !nomura => CashOperationKind.SellSettlement,
        _ => throw new ArgumentException($"取引区分「{value}」は自動登録対象外です。振替・両替・権利処理は相手口座と分類を確認して手動登録してください。")
    };
    private static string[] Split(string text)
    {
        var values = new List<string>(); var value = new StringBuilder(); var quoted = false; var closed = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                else if (quoted) { quoted = false; closed = true; }
                else
                {
                    if (closed || value.ToString().Any(c => !char.IsWhiteSpace(c)))
                        throw new InvalidOperationException("CSVの引用符の位置が不正です。");
                    value.Clear(); quoted = true;
                }
            }
            else if (text[i] == ',' && !quoted) { values.Add(value.ToString().Trim()); value.Clear(); closed = false; }
            else
            {
                if (closed && !char.IsWhiteSpace(text[i])) throw new InvalidOperationException("CSVの閉じ引用符の後に不正な文字があります。");
                value.Append(text[i]);
            }
        }
        if (quoted) throw new InvalidOperationException("CSVの引用符が閉じられていません。");
        values.Add(value.ToString().Trim()); return values.ToArray();
    }
}
