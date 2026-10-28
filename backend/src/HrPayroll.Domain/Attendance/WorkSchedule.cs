using HrPayroll.Domain.Employees;

namespace HrPayroll.Domain.Attendance;

// What a day was planned as (الدوام المقرر): a working day on a shift from StartsAt for Length (none
// for flexible hours), or a day off; Rostered when the roster said so (with the shift's name),
// else the employee's own schedule did. A day off keeps their own shift's hours, for a day off
// marked as a working day after all.
public sealed record DayPlan(TimeOnly? StartsAt, TimeSpan Length, bool Off, bool Rostered, Guid? ShiftId = null, string? ShiftName = null);

// An employee's work schedule day by day: their own (a nine-hour shift from ShiftStartsAt, or
// flexible hours, and their weekly days off), or the roster's where it says otherwise for a day: a
// shift of its own hours, or a day off.
//
// A day's punches are those of its window. On their own schedule throughout, a day's window opens
// six hours before their shift starts (04:00 on flexible hours) and closes when the next day's
// opens, as it always has. A rostered shift opens its day six hours before it starts; and a day
// after one whose shift ends late opens no sooner than halfway between that shift's end and its own
// shift's start (three hours after it, with no shift of its own), so a night shift's punch-out is
// never taken for the next morning's punch-in.
public sealed class WorkSchedule
{
    public static readonly TimeSpan RestAfterShift = TimeSpan.FromHours(3);

    private readonly Dictionary<DateOnly, DayPlan> rostered;

    // `roster`: the employee's roster days, any of them; `shifts`: the shifts they name. One
    // naming a shift not there is taken as not rostered.
    public WorkSchedule(Employee employee, IEnumerable<RosterDay> roster, IReadOnlyDictionary<Guid, Shift> shifts)
    {
        Employee = employee;
        rostered = new Dictionary<DateOnly, DayPlan>();
        foreach (var day in roster.Where(r => r.EmployeeId == employee.Id))
        {
            if (day.ShiftId is not { } shiftId)
                rostered[day.Date] = new DayPlan(employee.ShiftStartsAt, Employee.ShiftLength, Off: true, Rostered: true);
            else if (shifts.TryGetValue(shiftId, out var shift))
                rostered[day.Date] = new DayPlan(shift.StartsAt, shift.Length, Off: false, Rostered: true, shift.Id, shift.Name);
        }
    }

    // Their own schedule alone, no roster.
    public static WorkSchedule Of(Employee employee) => new(employee, [], new Dictionary<Guid, Shift>());

    public Employee Employee { get; }

    public DayPlan Plan(DateOnly day) =>
        rostered.TryGetValue(day, out var plan) ? plan : new DayPlan(Employee.ShiftStartsAt, Employee.ShiftLength, Employee.IsDayOff(day), Rostered: false);

    // The shift worked on `day`, from when to when: the plan's on a working day; on a day off marked
    // as a working day, their own. None on flexible hours, or on a day off.
    public (DateTime Start, DateTime End)? ShiftOn(DateOnly day, bool workingAfterAll = false)
    {
        var plan = Plan(day);
        if (plan.Off && !workingAfterAll)
            return null;
        var (startsAt, length) = plan.Off ? (Employee.ShiftStartsAt, Employee.ShiftLength) : (plan.StartsAt, plan.Length);
        return startsAt is { } s ? (day.ToDateTime(s), day.ToDateTime(s) + length) : null;
    }

    // When the day's window opens: see the class.
    public DateTime Opens(DateOnly day)
    {
        var opens = ShiftOn(day) is { } shift ? shift.Start - Timesheet.DayStartsBeforeShift
            : Employee.ShiftStartsAt is { } own ? day.ToDateTime(own) - Timesheet.DayStartsBeforeShift
            : day.ToDateTime(Timesheet.FlexibleDayStartsAt);
        if (ShiftOn(day.AddDays(-1)) is not { End: var before })
            return opens;
        var floor = ShiftOn(day) is { } today && today.Start > before ? before + (today.Start - before) / 2 : before + RestAfterShift;
        return opens > floor ? opens : floor;
    }

    // The punches of `day` are the ones from From, up to but not including To.
    public (DateTime From, DateTime To) Window(DateOnly day)
    {
        var from = Opens(day);
        var to = Opens(day.AddDays(1));
        return (from, to > from ? to : from);
    }

    // The day a punch at `time` belongs to: the latest whose window has opened by it. A window opens
    // from six hours before its date to the end of it, so it is the time's date, the one before or
    // the one after.
    public DateOnly DayOf(DateTime time)
    {
        var date = DateOnly.FromDateTime(time);
        for (var day = date.AddDays(1); day > date.AddDays(-1); day = day.AddDays(-1))
        {
            if (Opens(day) <= time)
                return day;
        }

        return date.AddDays(-1);
    }
}
