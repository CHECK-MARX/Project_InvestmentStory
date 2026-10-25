namespace InvestmentStory.Core.Models;

public enum ReminderKind { DividendUpcoming, DividendOverdue, UpdateFailure }

public sealed record ReminderEvent(
    string Key,
    ReminderKind Kind,
    DateTime EventDate,
    string Title,
    string Detail,
    string SourceLabel,
    bool IsCalendarEligible);

public sealed record ReminderState(
    string Key,
    DateTime? ReadAtUtc,
    DateTime? SnoozedUntilUtc,
    DateTime? LastNotifiedAtUtc);

public sealed record ReminderEntry(ReminderEvent Event, ReminderState? State, bool IsSnoozed, bool IsUnread);
