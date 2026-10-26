using System.Text;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using InvestmentStory.Data;
using InvestmentStory.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Tests;

public sealed class ReminderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InvestmentStoryReminder_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly ReminderService _service = new();
    private static readonly DateTime Today = new(2026, 10, 24);
    private static readonly DateTime NowUtc = new(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc);
    public ReminderTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "test.db");
        new DatabaseInitializer().Initialize(_path);
    }
    private static DividendPayment Payment(int id, string status, DateTime date, int stockId = 1) => new()
    {
        Id = id, StockId = stockId, Ticker = "ABC", StockName = "Alpha", Broker = "証券A",
        AccountType = "Specific", PaymentDate = date, DividendStatus = status,
        Currency = "JPY", NetAmountJpy = 100m, GrossAmountJpy = 100m,
        NetAmount = 100m, GrossAmount = 100m
    };

    [Fact]
    public void UpcomingAndOverdueAreSeparateAndMatchedActualRemovesPlan()
    {
        var plan = Payment(1, DividendConstants.Planned, Today.AddDays(2));
        var actual = Payment(2, DividendConstants.Actual, Today.AddDays(3));
        plan.MatchedActualDividendId = actual.Id;
        var overdue = Payment(3, DividendConstants.Planned, Today.AddDays(-3), stockId: 2);
        var upcoming = Payment(4, DividendConstants.Planned, Today.AddDays(5), stockId: 3);
        var estimated = Payment(5, DividendConstants.Estimated, Today.AddDays(4), stockId: 4);
        var events = _service.Build([plan, actual, overdue, upcoming, estimated], [], Today);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, x => x.Key == "dividend:3:overdue" && !x.IsCalendarEligible);
        Assert.Contains(events, x => x.Key == "dividend:4:upcoming" && x.IsCalendarEligible);
    }

    [Fact]
    public void LaterSuccessfulFetchRemovesFailureAndKeepsOtherTarget()
    {
        var logs = new[]
        {
            new ApiFetchLogEntry { Id = 1, ApiType = "Price", Provider = "P", Symbol = "AAA", IsSuccess = false, FetchedAt = Today.AddHours(-2) },
            new ApiFetchLogEntry { Id = 2, ApiType = "Price", Provider = "P", Symbol = "AAA", IsSuccess = true, FetchedAt = Today.AddHours(-1) },
            new ApiFetchLogEntry { Id = 3, ApiType = "FX", Provider = "P", Symbol = "USDJPY", IsSuccess = false, FetchedAt = Today }
        };
        var events = _service.Build([], logs, Today);
        Assert.Single(events);
        Assert.Equal("api:3", events[0].Key);
    }

    [Fact]
    public void ReadSnoozeAndWeeklySuppressionDoNotChangeSourceEvents()
    {
        var eventItem = new ReminderEvent("api:7", ReminderKind.UpdateFailure, Today, "失敗", "根拠", "履歴", false);
        var repository = new ReminderRepository(_path);
        repository.MarkRead(eventItem.Key, NowUtc);
        Assert.False(_service.ApplyStates([eventItem], repository.GetStates(), NowUtc).Single().IsUnread);
        repository.MarkUnread(eventItem.Key);
        repository.SnoozeUntil(eventItem.Key, NowUtc.AddDays(2), NowUtc);
        var snoozed = _service.ApplyStates([eventItem], repository.GetStates(), NowUtc);
        Assert.True(snoozed.Single().IsSnoozed);
        Assert.Empty(_service.SelectToNotify(snoozed, NowUtc));
        repository.ClearSnooze(eventItem.Key);
        Assert.Single(_service.SelectToNotify(_service.ApplyStates([eventItem], repository.GetStates(), NowUtc), NowUtc));
        repository.MarkNotified([eventItem.Key], NowUtc);
        var after = _service.ApplyStates([eventItem], repository.GetStates(), NowUtc.AddDays(1));
        Assert.Empty(_service.SelectToNotify(after, NowUtc.AddDays(1)));
        Assert.Single(_service.SelectToNotify(after, NowUtc.AddDays(8)));
    }

    [Fact]
    public void IcsHasStableUidEscapedTextAndUtf8OctetFolding()
    {
        var item = new ReminderEvent("dividend:42:upcoming", ReminderKind.DividendUpcoming,
            Today.AddDays(2), "ABC, DEF; 配当", new string('日', 50) + "\n詳細,追加", "配当", true);
        var ics = _service.ExportUpcomingIcs([item], Today, NowUtc);
        Assert.Contains("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261026\r\n", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261027\r\n", ics);
        Assert.Contains("SUMMARY:ABC\\, DEF\\; 配当", ics);
        Assert.Contains("\\n詳細\\,追加", ics.Replace("\r\n ", ""));
        Assert.All(ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries),
            line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.Equal(1, ics.Split("BEGIN:VEVENT").Length - 1);
        var second = _service.ExportUpcomingIcs([item], Today, NowUtc.AddDays(1));
        Assert.Equal(ics.Split("UID:")[1].Split("\r\n")[0], second.Split("UID:")[1].Split("\r\n")[0]);
    }

    [Fact]
    public void IcsExcludesOverdueApiAndPastOrDistantEvents()
    {
        var events = new[]
        {
            new ReminderEvent("dividend:1:upcoming", ReminderKind.DividendUpcoming, Today.AddDays(-1), "past", "", "", true),
            new ReminderEvent("dividend:2:overdue", ReminderKind.DividendOverdue, Today.AddDays(-1), "overdue", "", "", false),
            new ReminderEvent("api:3", ReminderKind.UpdateFailure, Today, "api", "", "", false),
            new ReminderEvent("dividend:4:upcoming", ReminderKind.DividendUpcoming, Today.AddYears(2), "far", "", "", true)
        };
        var ics = _service.ExportUpcomingIcs(events, Today, NowUtc);
        Assert.DoesNotContain("BEGIN:VEVENT", ics);
    }

    [Fact]
    public void FutureYearCalendarEventDoesNotNotifyBeforeItsSevenDayWindow()
    {
        var future = Payment(7, DividendConstants.Planned, Today.AddDays(30));
        var events = _service.Build([future], [], Today);
        var eventItem = Assert.Single(events);
        Assert.Empty(_service.SelectToNotify(_service.ApplyStates(events,
            new Dictionary<string, ReminderState>(), NowUtc), NowUtc));
        Assert.Contains("DTSTART;VALUE=DATE:", _service.ExportUpcomingIcs(events, Today, NowUtc));
        Assert.Equal("dividend:7:upcoming", eventItem.Key);
    }

    [Fact]
    public void FuturePaymentDueStatusIsUpcomingUntilScheduledDate()
    {
        var future = Payment(8, DividendConstants.PaymentDue, Today.AddDays(5));
        var item = Assert.Single(_service.Build([future], [], Today));
        Assert.Equal(ReminderKind.DividendUpcoming, item.Kind);
        Assert.True(item.IsCalendarEligible);
    }

    [Fact]
    public void NotificationModePersistsAndWindowsDeliveryUsesSameSuppression()
    {
        var log = new ApiFetchLogEntry { Id = 17, ApiType = "Price", Provider = "P", Symbol = "ABC", IsSuccess = false, FetchedAt = DateTime.Now };
        var vm = new ReminderViewModel(() => new ReminderRepository(_path), () => [], () => [log]);
        vm.Load(presentNew: false);
        vm.SelectedMode = vm.ModeOptions[1];
        Assert.Equal("Windows", new ReminderRepository(_path).GetMode());
        vm.Load();
        Assert.Single(vm.GetPendingWindowsNotifications());
        Assert.Empty(new ReminderRepository(_path).GetStates());
        vm.MarkWindowsNotified(vm.GetPendingWindowsNotifications());
        Assert.Empty(vm.GetPendingWindowsNotifications());
        vm.SelectedMode = vm.ModeOptions[0];
        Assert.Equal("InApp", new ReminderRepository(_path).GetMode());
        Assert.Empty(vm.GetPendingWindowsNotifications());
    }

    [Fact]
    public void OpenSourcePassesExactReminderEventToNavigator()
    {
        var log = new ApiFetchLogEntry { Id = 51, ApiType = "Price", Provider = "P", Symbol = "ABC",
            IsSuccess = false, FetchedAt = DateTime.Now };
        ReminderEvent? opened = null;
        var vm = new ReminderViewModel(() => new ReminderRepository(_path), () => [], () => [log],
            item => opened = item);
        vm.Load(presentNew: false);
        vm.Selected = Assert.Single(vm.Rows);
        vm.OpenSourceCommand.Execute(null);
        Assert.Equal("api:51", opened?.Key);
    }

    [Fact]
    public void ViewModelKeepsSampleAndNormalReminderStatesSeparate()
    {
        var samplePath = Path.Combine(_directory, "sample.db");
        new DatabaseInitializer().Initialize(samplePath);
        var activePath = _path;
        // API event IDs are independent of the machine's current date.
        var log = new ApiFetchLogEntry { Id = 11, ApiType = "Price", Provider = "P", Symbol = "ABC", IsSuccess = false, FetchedAt = DateTime.Now };
        var vm = new ReminderViewModel(() => new ReminderRepository(activePath), () => [], () => [log]);
        vm.Load(presentNew: false);
        vm.Selected = Assert.Single(vm.Rows);
        vm.MarkReadCommand.Execute(null);
        Assert.NotNull(new ReminderRepository(_path).GetStates()["api:11"].ReadAtUtc);
        activePath = samplePath; vm.Reset(); vm.Load(presentNew: false);
        Assert.True(Assert.Single(vm.Rows).Entry.IsUnread);
        Assert.Empty(new ReminderRepository(samplePath).GetStates());
    }

    [Fact]
    public void MigrationBacksUpExistingDatabaseAndRestorePreviewIncludesStates()
    {
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE ReminderStates;"; command.ExecuteNonQuery();
        }
        new DatabaseInitializer().Initialize(_path);
        var backup = Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        new DatabaseInitializer().Initialize(_path);
        Assert.Single(new InvestmentStoryRepository(_path).GetPreImportBackupPaths());
        new ReminderRepository(_path).MarkRead("api:1", NowUtc);
        Assert.Contains("通知の既読・延期状態 1件", new DatabaseRestoreService(_path).GetRestoreImpact(Path.GetFileName(backup)));
    }

    [Fact]
    public void AddingNotificationPreferenceToExistingReminderDatabasePreservesStates()
    {
        var repo = new ReminderRepository(_path);
        repo.MarkRead("api:old", NowUtc);
        var existingBackups = new InvestmentStoryRepository(_path).GetPreImportBackupPaths().Count;
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE ReminderPreferences;"; command.ExecuteNonQuery();
        }
        new DatabaseInitializer().Initialize(_path);
        Assert.Equal(existingBackups + 1, new InvestmentStoryRepository(_path).GetPreImportBackupPaths().Count);
        Assert.NotNull(repo.GetStates()["api:old"].ReadAtUtc);
        Assert.Equal("InApp", repo.GetMode());
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
