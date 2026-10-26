using System.Security.Cryptography;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class ReminderService
{
    public IReadOnlyList<ReminderEvent> Build(
        IReadOnlyList<DividendPayment> payments,
        IReadOnlyList<ApiFetchLogEntry> apiLogs,
        DateTime today)
    {
        var dividendService = new DividendDashboardService();
        var dividendEvents = payments
            .Where(x => x.PaymentDate.Year <= today.Year + 1)
            .Select(x => x.PaymentDate.Year).Distinct()
            .SelectMany(year => dividendService.Build(payments, year, 0m, today).Entries)
            .Where(x => !x.IsActual && !x.IsEstimated && x.Payment.Id > 0)
            .Where(x => x.IsUnmatched && x.Payment.PaymentDate.Date < today.Date ||
                x.Payment.PaymentDate.Date >= today.Date &&
                x.Payment.PaymentDate.Date <= today.Date.AddYears(1))
            .Select(x =>
            {
                var overdue = x.IsUnmatched && x.Payment.PaymentDate.Date < today.Date;
                var title = $"{x.Payment.Ticker} 配当{(overdue ? "未照合" : "入金予定")}";
                return new ReminderEvent($"dividend:{x.Payment.Id}:{(overdue ? "overdue" : "upcoming")}",
                    overdue ? ReminderKind.DividendOverdue : ReminderKind.DividendUpcoming,
                    x.Payment.PaymentDate.Date, title,
                    $"{x.Payment.PaymentDate:yyyy/MM/dd} / {x.Payment.Broker} / {x.Payment.AccountType}。" +
                    (overdue ? "予定日を過ぎています。入金実績と照合してください。" : "予定です。実際の入金額とは異なる場合があります。"),
                    $"配当予定 #{x.Payment.Id}", !overdue);
            });

        var latestApi = apiLogs.OrderByDescending(x => x.FetchedAt).ThenByDescending(x => x.Id)
            .GroupBy(x => (x.ApiType.Trim().ToUpperInvariant(), x.Provider.Trim().ToUpperInvariant(), x.Symbol.Trim().ToUpperInvariant()))
            .Select(x => x.First())
            .Where(x => !x.IsSuccess && x.Id > 0)
            .Select(x => new ReminderEvent($"api:{x.Id}", ReminderKind.UpdateFailure,
                x.FetchedAt.Date, $"{x.Symbol} 更新失敗",
                $"{x.Provider} / {x.ApiType} / {x.ErrorMessage}".Trim(' ', '/'),
                $"取得履歴 #{x.Id}", false));
        return dividendEvents.Concat(latestApi)
            .OrderBy(x => x.Kind == ReminderKind.DividendOverdue ? 0 :
                x.Kind == ReminderKind.UpdateFailure ? 1 : 2)
            .ThenBy(x => x.EventDate).ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ReminderEntry> ApplyStates(
        IReadOnlyList<ReminderEvent> events,
        IReadOnlyDictionary<string, ReminderState> states,
        DateTime nowUtc) => events.Select(item =>
        {
            states.TryGetValue(item.Key, out var state);
            var snoozed = state?.SnoozedUntilUtc > nowUtc;
            return new ReminderEntry(item, state, snoozed,
                !snoozed && state?.ReadAtUtc is null);
        }).ToArray();

    public IReadOnlyList<ReminderEntry> SelectToNotify(
        IReadOnlyList<ReminderEntry> entries,
        DateTime nowUtc,
        int maxCount = 3) => entries
            .Where(x => x.IsUnread && !x.IsSnoozed &&
                (x.Event.Kind != ReminderKind.DividendUpcoming ||
                 x.Event.EventDate.Date <= nowUtc.ToLocalTime().Date.AddDays(7)) &&
                (x.State?.LastNotifiedAtUtc is null || x.State.LastNotifiedAtUtc <= nowUtc.AddDays(-7)))
            .Take(Math.Clamp(maxCount, 0, 10)).ToArray();

    public string ExportUpcomingIcs(IReadOnlyList<ReminderEvent> events, DateTime today, DateTime generatedAtUtc)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0",
            "PRODID:-//InvestmentStory//Dividend Calendar//JA", "CALSCALE:GREGORIAN" };
        foreach (var item in events.Where(x => x.Kind == ReminderKind.DividendUpcoming && x.IsCalendarEligible &&
                     x.EventDate.Date >= today.Date && x.EventDate.Date <= today.Date.AddYears(1))
                 .DistinctBy(x => x.Key).OrderBy(x => x.EventDate).ThenBy(x => x.Key))
        {
            var uid = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Key))).ToLowerInvariant();
            lines.Add("BEGIN:VEVENT");
            lines.Add($"UID:{uid}@investmentstory.local");
            lines.Add($"DTSTAMP:{generatedAtUtc.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
            lines.Add($"DTSTART;VALUE=DATE:{item.EventDate:yyyyMMdd}");
            lines.Add($"DTEND;VALUE=DATE:{item.EventDate.AddDays(1):yyyyMMdd}");
            lines.Add("SUMMARY:" + EscapeText(item.Title));
            lines.Add("DESCRIPTION:" + EscapeText(item.Detail + " 予定情報。実績はアプリで確認してください。"));
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }

    private static string EscapeText(string text) => text.Replace("\\", "\\\\")
        .Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\\n")
        .Replace(";", "\\;").Replace(",", "\\,");

    // RFC 5545 content lines: fold at <=75 UTF-8 octets without splitting a Unicode scalar.
    private static string Fold(string line)
    {
        var result = new StringBuilder();
        var bytesOnLine = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var width = rune.Utf8SequenceLength;
            if (bytesOnLine + width > 75)
            {
                result.Append("\r\n ");
                bytesOnLine = 1;
            }
            result.Append(rune.ToString());
            bytesOnLine += width;
        }
        return result.ToString();
    }
}
