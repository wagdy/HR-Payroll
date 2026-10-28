using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Common;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Leaves;

namespace HrPayroll.Domain.Tests;

// Shift rostering (جداول الشفتات): a rostered day is measured against its shift, or is a day off,
// whatever the employee's own work schedule says; a day the roster says nothing of is as before.
public class RosterTests
{
    // A Monday; Hassan works 10:00 to 19:00, off on Fridays.
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly AttendancePolicy NoGrace = new(0, 3);
    private static readonly DateTime Later = new(2026, 12, 1);

    private static Employee Hassan() =>
        new(Guid.NewGuid(), "Hassan", "Waiter", 5_400, new TimeOnly(10, 0), WeekDays.Friday, new DateOnly(2026, 1, 1));

    private static readonly Shift Morning = new(Guid.NewGuid(), " Morning ", new TimeOnly(7, 0), new TimeOnly(15, 0), ShiftTone.Amber);
    private static readonly Shift Night = new(Guid.NewGuid(), "Night", new TimeOnly(22, 0), new TimeOnly(6, 0), ShiftTone.Violet);

    private static WorkSchedule Rostered(Employee employee, params (DateOnly Day, Shift? Shift)[] days) =>
        new(employee, days.Select(d => new RosterDay(employee.Id, d.Day, d.Shift?.Id, "Omar")), new[] { Morning, Night }.ToDictionary(s => s.Id));

    private static DateTime At(DateOnly day, int hour, int minute = 0) => day.ToDateTime(new TimeOnly(hour, minute));

    [Fact]
    public void A_shift_runs_from_its_start_to_its_end_past_midnight_too()
    {
        Assert.Equal(("Morning", TimeSpan.FromHours(8)), (Morning.Name, Morning.Length));
        Assert.Equal(TimeSpan.FromHours(8), Night.Length);

        Assert.Throws<DomainException>(() => new Shift(Guid.NewGuid(), "Nothing", new TimeOnly(9, 0), new TimeOnly(9, 0), ShiftTone.Sky));
        Assert.Throws<DomainException>(() => new Shift(Guid.NewGuid(), "Too long", new TimeOnly(6, 0), new TimeOnly(23, 0), ShiftTone.Sky));
        Assert.Throws<DomainException>(() => new Shift(Guid.NewGuid(), " ", new TimeOnly(6, 0), new TimeOnly(14, 0), ShiftTone.Sky));
        Assert.Throws<DomainException>(() => new RosterDay(Guid.NewGuid(), Monday, Guid.Empty, "Omar"));
    }

    [Fact]
    public void A_rostered_shift_is_what_its_day_is_measured_against()
    {
        var hassan = Hassan();
        var schedule = Rostered(hassan, (Monday, Morning));
        Assert.Equal(new DayPlan(new TimeOnly(7, 0), TimeSpan.FromHours(8), false, true, Morning.Id, "Morning"), schedule.Plan(Monday));

        // In at 07:20, out at 16:00 on the 07:00 to 15:00: twenty minutes late, and an hour past its
        // end, forty minutes of it overtime once its eight hours are worked.
        var day = Timesheet.Evaluate(schedule, Monday, [At(Monday, 7, 20), At(Monday, 16)], null, NoGrace, Later);
        Assert.Equal((DayStatus.Worked, 520, 20, 0, 40), (day.Status, day.WorkedMinutes, day.LateMinutes, day.EarlyLeaveMinutes, day.OvertimeMinutes));

        // The day after, not rostered: their own 10:00 to 19:00, as always.
        var tuesday = Timesheet.Evaluate(schedule, Monday.AddDays(1), [At(Monday.AddDays(1), 10, 30), At(Monday.AddDays(1), 19)], null, NoGrace, Later);
        Assert.Equal((30, 0), (tuesday.LateMinutes, tuesday.OvertimeMinutes));
    }

    [Fact]
    public void A_rostered_day_off_is_never_an_absence_and_a_rostered_weekly_day_off_is_worked()
    {
        var hassan = Hassan();
        var friday = Monday.AddDays(4);
        var schedule = Rostered(hassan, (Monday, null), (friday, Morning));

        Assert.Equal(DayStatus.DayOff, Timesheet.Evaluate(schedule, Monday, [], null, NoGrace, Later).Status);
        Assert.True(schedule.Plan(Monday) is { Off: true, Rostered: true });
        // Their weekly day off, rostered on a shift: no punch, absent.
        Assert.Equal(DayStatus.Absent, Timesheet.Evaluate(schedule, friday, [], null, NoGrace, Later).Status);
        // Leave marked on a rostered day stays leave.
        Assert.Equal(DayStatus.Leave, Timesheet.Evaluate(schedule, friday, [], DayMarkKind.Leave, NoGrace, Later).Status);
        // The rostered day off counts no leave: a Monday to Friday leave is the four days between.
        Assert.Equal([Monday.AddDays(1), Monday.AddDays(2), Monday.AddDays(3), friday], LeaveDays.Of(schedule, Monday, friday, []));
    }

    [Fact]
    public void A_night_shifts_punch_out_is_its_own_day_not_the_next_mornings()
    {
        var hassan = Hassan();
        var tuesday = Monday.AddDays(1);
        // Monday night 22:00 to 06:00, then Tuesday's 07:00 to 15:00.
        var schedule = Rostered(hassan, (Monday, Night), (tuesday, Morning));

        // Tuesday's day opens halfway between the night's end and the morning's start.
        Assert.Equal(At(tuesday, 6, 30), schedule.Window(Monday).To);
        Assert.Equal(Monday, schedule.DayOf(At(tuesday, 6, 5)));
        Assert.Equal(tuesday, schedule.DayOf(At(tuesday, 6, 55)));

        var punches = new[] { At(Monday, 21, 55), At(tuesday, 6, 5), At(tuesday, 6, 58), At(tuesday, 15) };
        var night = Timesheet.Evaluate(schedule, Monday, punches, null, NoGrace, Later);
        var morning = Timesheet.Evaluate(schedule, tuesday, punches, null, NoGrace, Later);
        Assert.Equal((DayStatus.Worked, 490, 0, 5), (night.Status, night.WorkedMinutes, night.LateMinutes, night.OvertimeMinutes));
        Assert.Equal((DayStatus.Worked, 482, 0, 0), (morning.Status, morning.WorkedMinutes, morning.LateMinutes, morning.OvertimeMinutes));

        // With no roster, the days are as they always were: from 04:00, six hours before 10:00.
        var own = WorkSchedule.Of(hassan);
        Assert.Equal((At(Monday, 4), At(tuesday, 4)), own.Window(Monday));
        Assert.Equal(Monday, own.DayOf(At(tuesday, 3, 59)));
        Assert.Equal(Timesheet.Window(hassan, Monday), own.Window(Monday));
    }
}
