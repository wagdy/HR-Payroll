using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Attendance;

// A shift's colour on the roster, from a few that read well together.
public enum ShiftTone
{
    Sky = 1,
    Amber = 2,
    Violet = 3,
    Teal = 4,
    Rose = 5,
    Slate = 6,
}

// A shift (شفت) the roster assigns: morning, evening, night; from when to when. One that ends at or
// before the hour it starts runs past midnight: 22:00 to 06:00 is eight hours, into the next
// morning. An employee rostered on it is measured against its hours that day instead of their own
// work schedule: late from its start, leaving early before its end, overtime past its end once its
// hours are worked.
public sealed class Shift : AggregateRoot
{
    public const int MaxNameLength = 50;
    public static readonly TimeSpan MinLength = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(16);

    private Shift() { }

    public Shift(Guid id, string name, TimeOnly startsAt, TimeOnly endsAt, ShiftTone tone)
    {
        Id = id;
        Rename(name);
        SetTimes(startsAt, endsAt);
        SetTone(tone);
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;
    public TimeOnly StartsAt { get; private set; }
    public TimeOnly EndsAt { get; private set; }
    public ShiftTone Tone { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // From its start to its end, past midnight when it ends at or before the hour it starts.
    public TimeSpan Length => EndsAt - StartsAt;

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new DomainException("Give the shift its name.");
        if (trimmed.Length > MaxNameLength)
            throw new DomainException($"Keep the shift's name under {MaxNameLength} characters.");
        Name = trimmed;
    }

    // To the minute. A shift is an hour to sixteen.
    public void SetTimes(TimeOnly startsAt, TimeOnly endsAt)
    {
        var start = new TimeOnly(startsAt.Hour, startsAt.Minute);
        var end = new TimeOnly(endsAt.Hour, endsAt.Minute);
        var length = end - start;
        if (length < MinLength || length > MaxLength)
            throw new DomainException($"A shift is {MinLength.TotalHours:0} to {MaxLength.TotalHours:0} hours long.");
        StartsAt = start;
        EndsAt = end;
    }

    public void SetTone(ShiftTone tone)
    {
        if (!Enum.IsDefined(tone))
            throw new DomainException("Choose the shift's colour.");
        Tone = tone;
    }
}

// A day on the roster (جدول الشفتات): one employee's shift that day, or a day off (no shift). It
// stands for their own work schedule and weekly days off on that day alone; a day with none is as
// their own schedule says. One a day; changed or taken off while the month's pay is open.
public sealed class RosterDay : AggregateRoot
{
    private RosterDay() { }

    public RosterDay(Guid employeeId, DateOnly date, Guid? shiftId, string setBy)
    {
        EmployeeId = employeeId;
        Date = date;
        Set(shiftId, setBy);
    }

    public Guid EmployeeId { get; private set; }
    public DateOnly Date { get; private set; }

    // None: a day off.
    public Guid? ShiftId { get; private set; }

    public string SetBy { get; private set; } = string.Empty;
    public DateTime SetAtUtc { get; private set; }

    public bool IsDayOff => ShiftId is null;

    public void Set(Guid? shiftId, string setBy)
    {
        if (shiftId == Guid.Empty)
            throw new DomainException("Choose the shift, or a day off.");
        ShiftId = shiftId;
        SetBy = setBy;
        SetAtUtc = DateTime.UtcNow;
    }
}
