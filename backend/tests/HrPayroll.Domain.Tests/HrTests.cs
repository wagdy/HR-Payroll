using HrPayroll.Domain.Common;
using HrPayroll.Domain.Company;
using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Payroll;

namespace HrPayroll.Domain.Tests;

// HR's rules: who is on the payroll, how their punches become a day's hours, lateness and
// overtime, and how a month's pay is made of it.
public class HrTests
{
    // Monday 12 October 2026, and Friday the 16th.
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Friday = new(2026, 10, 16);
    private static readonly DateTime Later = new(2026, 10, 25, 12, 0, 0);
    private static readonly AttendancePolicy TenMinutesGrace = new(10, 3);

    // 5,400 a month: 180 a day, 20 an hour.
    private static Employee Checker(TimeOnly? shift = null, DateOnly? hiredOn = null) =>
        new(Guid.NewGuid(), " Hassan ", " Checker ", 5_400, shift ?? new TimeOnly(10, 0), WeekDays.Friday, hiredOn ?? new DateOnly(2026, 1, 1), "7");

    private static DateTime At(DateOnly day, int hour, int minute = 0) => day.ToDateTime(new TimeOnly(hour, minute));

    private static DayEvaluation Day(Employee employee, DateOnly day, params DateTime[] punches) =>
        Timesheet.Evaluate(employee, day, punches, null, TenMinutesGrace, Later);

    [Fact]
    public void An_employee_has_a_nine_hour_shift_and_rates_from_a_thirty_day_month()
    {
        var checker = Checker(new TimeOnly(10, 0, 42));

        Assert.Equal(("Hassan", "Checker", "7", new TimeOnly(10, 0)), (checker.Name, checker.JobTitle, checker.DeviceUserId, checker.ShiftStartsAt));
        Assert.Equal((180m, 20m), (checker.DailyRate, checker.HourlyRate));
        Assert.Equal(9, Employee.ShiftHours);
        Assert.True(checker.IsDayOff(Friday));
        Assert.False(checker.IsDayOff(Monday));

        Assert.Throws<DomainException>(() => checker.Rename(" "));
        Assert.Throws<DomainException>(() => checker.SetSalary(-1));
        Assert.Throws<DomainException>(() => checker.SetDaysOff(WeekDaysExtensions.All));
        Assert.Throws<DomainException>(() => checker.LinkDevice("7 b"));
        checker.LinkDevice(" ");
        Assert.Null(checker.DeviceUserId);
    }

    [Fact]
    public void One_who_leaves_is_employed_to_their_last_day_and_again_from_their_return()
    {
        var checker = Checker(hiredOn: new DateOnly(2026, 10, 5));
        Assert.False(checker.IsEmployedOn(new DateOnly(2026, 10, 4)));

        Assert.Throws<DomainException>(() => checker.Leave(new DateOnly(2026, 10, 4)));
        checker.Leave(new DateOnly(2026, 10, 20));
        Assert.False(checker.IsActive);
        Assert.True(checker.IsEmployedOn(new DateOnly(2026, 10, 20)));
        Assert.False(checker.IsEmployedOn(new DateOnly(2026, 10, 21)));

        Assert.Throws<DomainException>(() => checker.Return(new DateOnly(2026, 10, 20)));
        checker.Return(new DateOnly(2026, 11, 1));
        Assert.Equal((true, (DateOnly?)null, new DateOnly(2026, 11, 1)), (checker.IsActive, checker.LeftOn, checker.HiredOn));
    }

    [Fact]
    public void A_day_on_time_within_the_grace_is_worked_with_nothing_short()
    {
        var checker = Checker();

        var early = Day(checker, Monday, At(Monday, 9, 55), At(Monday, 19));
        Assert.Equal(new DayEvaluation(Monday, DayStatus.Worked, At(Monday, 9, 55), At(Monday, 19), 2, 545, 0, 0, 0, 0), early);

        var withinGrace = Day(checker, Monday, At(Monday, 10, 8), At(Monday, 19));
        Assert.Equal((0, 0), (withinGrace.LateMinutes, withinGrace.ShortMinutes));
    }

    [Fact]
    public void Late_past_the_grace_counts_from_the_start_and_only_time_past_nine_hours_worked_is_overtime()
    {
        var day = Day(Checker(), Monday, At(Monday, 10, 25), At(Monday, 19, 30));

        // 25 minutes late; of the half hour after the shift's end, five minutes are past nine hours.
        Assert.Equal((25, 0, 5, 545), (day.LateMinutes, day.EarlyLeaveMinutes, day.OvertimeMinutes, day.WorkedMinutes));
    }

    [Fact]
    public void Coming_in_late_and_staying_to_make_up_is_late_all_the_same_and_no_overtime()
    {
        var early = Checker(new TimeOnly(8, 0));

        // 08:00 shift, in at 10:00, out at 19:00: nine hours worked, two hours late, nothing over.
        var madeUp = Day(early, Monday, At(Monday, 10), At(Monday, 19));
        Assert.Equal((540, 120, 0, 120), (madeUp.WorkedMinutes, madeUp.LateMinutes, madeUp.OvertimeMinutes, madeUp.ShortMinutes));

        // On time and staying to 19:00: two hours past the nine.
        Assert.Equal(120, Day(early, Monday, At(Monday, 8), At(Monday, 19)).OvertimeMinutes);
    }

    [Fact]
    public void Time_out_between_punches_and_leaving_early_are_short_of_the_shift()
    {
        var day = Day(Checker(), Monday, At(Monday, 10), At(Monday, 14), At(Monday, 15), At(Monday, 18, 30));

        Assert.Equal((0, 30, 60, 0, 450), (day.LateMinutes, day.EarlyLeaveMinutes, day.AwayMinutes, day.OvertimeMinutes, day.WorkedMinutes));
        Assert.Equal(90, day.ShortMinutes);
    }

    [Fact]
    public void A_finger_put_on_twice_is_one_scan()
    {
        var day = Day(Checker(), Monday, At(Monday, 10), At(Monday, 10, 2), At(Monday, 19));

        Assert.Equal((DayStatus.Worked, 2, 540), (day.Status, day.Punches, day.WorkedMinutes));
    }

    [Fact]
    public void A_shift_past_midnight_is_one_day()
    {
        var evening = Checker(new TimeOnly(16, 0));
        var next = Monday.AddDays(1);

        var day = Day(evening, Monday, At(Monday, 15, 50), At(next, 1, 30));

        Assert.Equal((DayStatus.Worked, 580, 0, 30), (day.Status, day.WorkedMinutes, day.LateMinutes, day.OvertimeMinutes));
        Assert.Equal((At(Monday, 10), At(next, 10)), Timesheet.Window(evening, Monday));
        // Nor is the punch after midnight the next day's.
        var tuesday = Day(evening, next, At(next, 1, 30));
        Assert.Equal((DayStatus.Absent, 0), (tuesday.Status, tuesday.Punches));
    }

    [Fact]
    public void Coming_after_the_shift_has_ended_is_the_whole_shift_short()
    {
        var day = Day(Checker(), Monday, At(Monday, 20), At(Monday, 22));

        Assert.Equal((540, 0, 0, 540), (day.LateMinutes, day.EarlyLeaveMinutes, day.OvertimeMinutes, day.ShortMinutes));
    }

    [Fact]
    public void A_day_without_punches_is_an_absence_unless_it_is_a_day_off_or_marked()
    {
        var checker = Checker();

        Assert.Equal(DayStatus.Absent, Day(checker, Monday).Status);
        Assert.Equal(DayStatus.DayOff, Day(checker, Friday).Status);
        Assert.Equal(DayStatus.Leave, Timesheet.Evaluate(checker, Monday, [], DayMarkKind.Leave, TenMinutesGrace, Later).Status);
        Assert.Equal(DayStatus.DayOff, Timesheet.Evaluate(checker, Monday, [], DayMarkKind.DayOff, TenMinutesGrace, Later).Status);
        // Not over yet: not absent yet.
        Assert.Equal(DayStatus.Open, Timesheet.Evaluate(checker, Monday, [], null, TenMinutesGrace, At(Monday, 12)).Status);
        // Before they started.
        Assert.Equal(DayStatus.NotEmployed, Day(Checker(hiredOn: Friday), Monday, At(Monday, 10), At(Monday, 19)).Status);
    }

    [Fact]
    public void A_day_off_worked_is_hours_with_no_shift_to_be_late_for()
    {
        var day = Day(Checker(), Friday, At(Friday, 12), At(Friday, 16));

        Assert.Equal(new DayEvaluation(Friday, DayStatus.WorkedOnDayOff, At(Friday, 12), At(Friday, 16), 2, 240, 0, 0, 0, 0), day);
    }

    [Fact]
    public void A_weekly_day_off_marked_a_working_day_is_a_shift_like_any()
    {
        var checker = Checker();
        var swapped = Timesheet.Evaluate(checker, Friday, [At(Friday, 10, 20), At(Friday, 19)], DayMarkKind.WorkingDay, TenMinutesGrace, Later);

        Assert.Equal((DayStatus.Worked, 20), (swapped.Status, swapped.LateMinutes));
        // Not worked after all: absent, as any working day.
        Assert.Equal(DayStatus.Absent, Timesheet.Evaluate(checker, Friday, [], DayMarkKind.WorkingDay, TenMinutesGrace, Later).Status);
    }

    [Fact]
    public void A_day_off_worked_earns_a_days_pay_from_half_a_shift_and_its_hours_before()
    {
        var checker = Checker();
        var october = new DateOnly(2026, 10, 1);
        DayEvaluation OffWorked(DateOnly d, int minutes) => new(d, DayStatus.WorkedOnDayOff, null, null, 2, minutes, 0, 0, 0, 0);
        var days = PayrollMonth.Days(october).Select(d => d.Day switch
        {
            // Fridays: six hours, then three, then half a shift exactly.
            2 => OffWorked(d, 360),
            9 => OffWorked(d, 180),
            16 => OffWorked(d, PayrollCalculator.HalfShiftMinutes),
            _ when checker.IsDayOff(d) => Nothing(d, DayStatus.DayOff),
            _ => new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0),
        }).ToList();

        var figures = PayrollCalculator.Calculate(checker, october, days, [], advances: 0, broughtForward: 0);

        // 180 + 3 hours at 20 + 180.
        Assert.Equal((3, 420m), (figures.DaysOffWorked, figures.DaysOffWorkedPay));
        Assert.Equal(5_400m + 420m, figures.Gross);
        Assert.Equal(5_820m, figures.WagesCost);
        Assert.Equal(270, PayrollCalculator.HalfShiftMinutes);
    }

    [Fact]
    public void A_public_holiday_is_a_paid_day_off_and_worked_is_measured_as_the_day_would_have_been()
    {
        var checker = Checker();
        DayEvaluation Holiday(DateOnly day, DayMarkKind? mark = null, DateTime? now = null, params DateTime[] punches) =>
            Timesheet.Evaluate(checker, day, punches, mark, TenMinutesGrace, now ?? Later, holiday: true);

        // Not worked: a holiday, never an absence, on a working day, a day off or one marked; known
        // before the day is over.
        Assert.Equal(DayStatus.Holiday, Holiday(Monday).Status);
        Assert.Equal(DayStatus.Holiday, Holiday(Friday).Status);
        Assert.Equal(DayStatus.Holiday, Holiday(Monday, DayMarkKind.Leave).Status);
        Assert.Equal(DayStatus.Holiday, Holiday(Monday, now: At(Monday, 9)).Status);

        // Worked on a working day: against the shift, 30 minutes late.
        var worked = Holiday(Monday, punches: [At(Monday, 10, 30), At(Monday, 19)]);
        Assert.Equal((DayStatus.WorkedOnHoliday, 510, 30), (worked.Status, worked.WorkedMinutes, worked.LateMinutes));
        // On their day off: no shift to be late for.
        var off = Holiday(Friday, punches: [At(Friday, 12), At(Friday, 16)]);
        Assert.Equal((DayStatus.WorkedOnHoliday, 240, 0), (off.Status, off.WorkedMinutes, off.ShortMinutes));
    }

    [Fact]
    public void A_holiday_worked_earns_a_days_pay_once_and_never_costs_more_than_it_gives()
    {
        var checker = Checker();
        var october = new DateOnly(2026, 10, 1);
        DayEvaluation Worked(DateOnly d, DayStatus status, int minutes, int late = 0, int early = 0) => new(d, status, null, null, 2, minutes, late, early, 0, 0);
        var days = PayrollMonth.Days(october).Select(d => d.Day switch
        {
            // Holidays: not worked; a full shift; two hours late; three hours, then gone; four hours
            // on their Friday off.
            5 => Nothing(d, DayStatus.Holiday),
            6 => Worked(d, DayStatus.WorkedOnHoliday, 540),
            7 => Worked(d, DayStatus.WorkedOnHoliday, 420, late: 120),
            8 => Worked(d, DayStatus.WorkedOnHoliday, 180, early: 360),
            16 => Worked(d, DayStatus.WorkedOnHoliday, 240),
            _ when checker.IsDayOff(d) => Nothing(d, DayStatus.DayOff),
            _ => Worked(d, DayStatus.Worked, 540),
        }).ToList();

        var figures = PayrollCalculator.Calculate(checker, october, days, [], advances: 0, broughtForward: 0);

        // A day's pay twice, three hours, four hours: 180 + 180 + 60 + 80, and the Friday is not
        // also a day off worked.
        Assert.Equal((4, 500m), (figures.HolidaysWorked, figures.HolidaysWorkedPay));
        Assert.Equal((0, 0m), (figures.DaysOffWorked, figures.DaysOffWorkedPay));
        // Two hours late counts; of the six hours missed on the 8th only its three worked do, so
        // coming in cost nothing: 5 hours at 20.
        Assert.Equal((300, 100m, 0), (figures.ShortMinutes, figures.LatenessPenalty, figures.AbsentDays));
        Assert.Equal((5_900m, 5_800m), (figures.Gross, figures.Net));
        // October's 26 days that are not Fridays, less the 5th not worked, and the Friday worked.
        Assert.Equal(26, figures.DaysWorked);
    }

    [Fact]
    public void Flexible_hours_have_no_shift_and_their_day_is_the_business_days()
    {
        var clerk = new Employee(Guid.NewGuid(), "Mona", "Warehouse clerk", 4_050, null, WeekDays.Friday, new DateOnly(2026, 1, 1), "12");
        Assert.True(clerk.HasFlexibleHours);
        var tuesday = Monday.AddDays(1);

        // From 04:00 to 04:00: a night past midnight is one day.
        Assert.Equal((At(Monday, 4), At(tuesday, 4)), Timesheet.Window(clerk, Monday));
        Assert.Equal(Monday, Timesheet.DayOf(clerk, At(tuesday, 3, 59)));
        Assert.Equal(tuesday, Timesheet.DayOf(clerk, At(tuesday, 4)));

        // Nothing late, early or away; the hours past nine are overtime.
        var late = Day(clerk, Monday, At(Monday, 13), At(Monday, 15), At(Monday, 16), At(tuesday, 1, 30));
        Assert.Equal((DayStatus.Worked, 690, 0, 0, 0, 150), (late.Status, late.WorkedMinutes, late.LateMinutes, late.EarlyLeaveMinutes, late.AwayMinutes, late.OvertimeMinutes));
        var short_ = Day(clerk, Monday, At(Monday, 13), At(Monday, 17));
        Assert.Equal((240, 0, 0), (short_.WorkedMinutes, short_.ShortMinutes, short_.OvertimeMinutes));
        // A working day without punches is an absence still; their day off is a day off.
        Assert.Equal(DayStatus.Absent, Day(clerk, Monday).Status);
        Assert.Equal(DayStatus.DayOff, Day(clerk, Friday).Status);

        // Given a shift later: measured against it from then.
        clerk.SetShift(new TimeOnly(10, 0));
        Assert.Equal(180, Day(clerk, Monday, At(Monday, 13), At(Monday, 19)).LateMinutes);
    }

    [Fact]
    public void A_daily_worker_is_paid_their_wage_for_each_day_worked_and_never_absent()
    {
        // 250 a day: 27.78 an hour.
        var helper = Checker();
        helper.SetPay(EmploymentType.Daily, 250);
        Assert.Equal((EmploymentType.Daily, 0m, 250m, 250m), (helper.EmploymentType, helper.MonthlySalary, helper.DailyWage, helper.DailyRate));
        var october = new DateOnly(2026, 10, 1);
        DayEvaluation Worked(DateOnly d, DayStatus status, int minutes, int late = 0) => new(d, status, null, null, 2, minutes, late, 0, 0, 0);
        var days = PayrollMonth.Days(october).Select(d => d.Day switch
        {
            // Worked the 5th to the 9th (an hour late on the 7th), three hours of a Friday off (the
            // 9th), and a holiday (the 6th); absent the 12th; on leave the 13th; a holiday off the 14th.
            5 or 8 => Worked(d, DayStatus.Worked, 540),
            6 => Worked(d, DayStatus.WorkedOnHoliday, 540),
            7 => Worked(d, DayStatus.Worked, 480, late: 60),
            9 => Worked(d, DayStatus.WorkedOnDayOff, 180),
            12 => Nothing(d, DayStatus.Absent),
            13 => Nothing(d, DayStatus.Leave),
            14 => Nothing(d, DayStatus.Holiday),
            _ when helper.IsDayOff(d) => Nothing(d, DayStatus.DayOff),
            _ => Nothing(d, DayStatus.Absent),
        }).ToList();

        var figures = PayrollCalculator.Calculate(helper, october, days, [], advances: 100, broughtForward: 0);

        // Four days' wages; the holiday's allowance on top; the Friday as anyone's day off worked,
        // under half a shift: its three hours.
        Assert.Equal((EmploymentType.Daily, 250m, 5, 1_000m), (figures.EmploymentType, figures.DailyWage, figures.DaysWorked, figures.BaseSalary));
        Assert.Equal((250m, 1, 83.33m), (figures.HolidaysWorkedPay, figures.DaysOffWorked, figures.DaysOffWorkedPay));
        // Nothing absent, the hour late at 27.78.
        Assert.Equal((0, 0m, 27.78m), (figures.AbsentDays, figures.AbsencePenalty, figures.LatenessPenalty));
        Assert.Equal(1_000m + 250m + 83.33m - 100m - 27.78m, figures.Net);

        // Half a shift or more of a day off: a day's wage.
        Assert.Equal(250m, PayrollCalculator.DayOffWorkedPay(helper, PayrollCalculator.HalfShiftMinutes));

        // Monthly again: the wage gone, the salary back.
        helper.SetPay(EmploymentType.Monthly, 5_400);
        Assert.Equal((0m, 180m), (helper.DailyWage, helper.DailyRate));
        Assert.Throws<DomainException>(() => helper.SetPay(EmploymentType.Daily, -1));
        Assert.Throws<DomainException>(() => helper.SetPay((EmploymentType)9, 100));
    }

    [Fact]
    public void An_employee_is_in_a_department_General_unless_moved()
    {
        var checker = Checker();
        Assert.Equal(Department.GeneralId, checker.DepartmentId);
        var accounts = new Department(Guid.NewGuid(), " Key accounts ");
        checker.MoveTo(accounts.Id);
        Assert.Equal((accounts.Id, "Key accounts", false), (checker.DepartmentId, accounts.Name, accounts.IsGeneral));
        Assert.True(new Department(Department.GeneralId, "General").IsGeneral);
        Assert.Throws<DomainException>(() => checker.MoveTo(Guid.Empty));
        Assert.Throws<DomainException>(() => accounts.Rename(" "));
    }

    [Fact]
    public void An_employee_holds_one_of_their_departments_roles_and_is_titled_by_it()
    {
        var checker = Checker();
        var accounts = new Department(Guid.NewGuid(), "Key accounts");
        var headChef = new JobRole(Guid.NewGuid(), accounts.Id, " Head checker ");
        Assert.Equal(("Head checker", accounts.Id), (headChef.Name, headChef.DepartmentId));

        // In General still: the accounts's role is not theirs to hold.
        Assert.Throws<DomainException>(() => checker.AssignRole(headChef));
        checker.MoveTo(accounts.Id);
        checker.AssignRole(headChef);
        Assert.Equal((headChef.Id, "Head checker"), (checker.JobRoleId, checker.JobTitle));

        Assert.Throws<DomainException>(() => new JobRole(Guid.NewGuid(), accounts.Id, " "));
        Assert.Throws<DomainException>(() => new JobRole(Guid.NewGuid(), Guid.Empty, "Helper"));
    }

    [Fact]
    public void A_punch_in_without_its_punch_out_is_forgetting_to_check_out_once_the_shift_is_over()
    {
        var checker = Checker(new TimeOnly(10, 0));
        bool Forgot(Employee e, DateOnly day, DateTime now, DayMarkKind? mark = null, params DateTime[] punches) =>
            Timesheet.ForgotToCheckOut(e, Timesheet.Evaluate(e, day, punches, mark, TenMinutesGrace, now), mark, now);

        // In at 10:02, no punch-out: not until the nine hours end at 19:00.
        Assert.False(Forgot(checker, Monday, At(Monday, 18, 59), null, At(Monday, 10, 2)));
        Assert.True(Forgot(checker, Monday, At(Monday, 19, 0), null, At(Monday, 10, 2)));
        // Out again after a break, and not back out: the same.
        Assert.True(Forgot(checker, Monday, At(Monday, 20, 0), null, At(Monday, 10, 2), At(Monday, 14, 0), At(Monday, 15, 0)));
        // Checked out, or never in: nothing to forget.
        Assert.False(Forgot(checker, Monday, Later, null, At(Monday, 10, 2), At(Monday, 19, 0)));
        Assert.False(Forgot(checker, Monday, Later));
        // Suspended, 18 hours on: forgot, still.
        var abandoned = Timesheet.Evaluate(checker, Monday, [At(Monday, 10, 2)], null, TenMinutesGrace, Later);
        Assert.Equal(DayStatus.Suspended, abandoned.Status);
        Assert.True(Timesheet.ForgotToCheckOut(checker, abandoned, null, Later));

        // No shift to end: flexible hours, once the day ends at 04:00 the next morning.
        var flexible = Checker();
        flexible.SetShift(null);
        Assert.False(Forgot(flexible, Monday, At(Monday.AddDays(1), 3, 59), null, At(Monday, 12, 0)));
        Assert.True(Forgot(flexible, Monday, At(Monday.AddDays(1), 4, 0), null, At(Monday, 12, 0)));
        // A day off worked has no shift either: once the day ends, 18 hours after the shift would start.
        Assert.False(Forgot(checker, Friday, At(Friday, 19, 0), null, At(Friday, 12, 0)));
        Assert.True(Forgot(checker, Friday, At(Friday.AddDays(1), 4, 0), null, At(Friday, 12, 0)));
        // A day off marked a working day (a swap) has its shift.
        Assert.True(Forgot(checker, Friday, At(Friday, 19, 0), DayMarkKind.WorkingDay, At(Friday, 10, 0)));
    }

    [Fact]
    public void An_employees_national_id_and_mobile_are_kept_as_digits_however_they_are_typed()
    {
        var checker = Checker();
        Assert.Equal(((string?)null, (string?)null), (checker.NationalId, checker.MobileNumber));

        // Arabic digits, spaces and dashes: the card's 14 digits.
        checker.SetNationalId(" ٢٩٠٠١٠١ ٠١٢٣٤-٥٦ ", Country.Egypt);
        checker.SetMobileNumber("٠١٠ ١٢٣٤-٥٦٧٨");
        Assert.Equal(("29001010123456", "01012345678"), (checker.NationalId, checker.MobileNumber));

        // A passport's number, in capitals; a number from abroad, with its +.
        checker.SetNationalId("a1234567", Country.Egypt);
        checker.SetMobileNumber("+20 (101) 234 5678");
        Assert.Equal(("A1234567", "+201012345678"), (checker.NationalId, checker.MobileNumber));

        Assert.Throws<DomainException>(() => checker.SetNationalId("2900101012345", Country.Egypt));
        Assert.Throws<DomainException>(() => checker.SetNationalId("A12/45", Country.Egypt));
        Assert.Throws<DomainException>(() => checker.SetMobileNumber("010-CALL-ME"));
        Assert.Throws<DomainException>(() => checker.SetMobileNumber("12345"));

        checker.SetNationalId(" ", Country.Egypt);
        checker.SetMobileNumber(null);
        Assert.Equal(((string?)null, (string?)null), (checker.NationalId, checker.MobileNumber));
    }

    [Fact]
    public void A_national_id_is_as_the_companys_country_writes_one_and_a_kept_one_stays_when_the_country_changes()
    {
        var checker = Checker();

        // Saudi Arabia: 10 digits, 1 for a citizen and 2 for a resident's Iqama.
        checker.SetNationalId("1012345678", Country.SaudiArabia);
        Assert.Equal("1012345678", checker.NationalId);
        checker.SetNationalId("٢٠١٢٣٤٥٦٧٨", Country.SaudiArabia);
        Assert.Equal("2012345678", checker.NationalId);
        Assert.Contains("10 digits", Assert.Throws<DomainException>(() => checker.SetNationalId("3012345678", Country.SaudiArabia)).Message);
        Assert.Throws<DomainException>(() => checker.SetNationalId("29001010123456", Country.SaudiArabia));
        // A passport anywhere.
        checker.SetNationalId("A1234567", Country.SaudiArabia);

        // Egypt's 14 digits, kept; the company then in Saudi Arabia, saving them again changes nothing.
        checker.SetNationalId("29001010123456", Country.Egypt);
        checker.SetNationalId("2900-1010-1234-56", Country.SaudiArabia);
        Assert.Equal("29001010123456", checker.NationalId);
        Assert.Throws<DomainException>(() => checker.SetNationalId("29001010123457", Country.SaudiArabia));
    }

    [Fact]
    public void An_employees_id_card_is_a_photo_or_pdf_under_5_MB_replaced_or_taken_off()
    {
        var checker = Checker();
        Assert.False(checker.HasIdPhoto);
        Assert.Throws<DomainException>(() => checker.RemoveIdPhoto());
        Assert.Throws<DomainException>(() => checker.AttachIdPhoto("card.gif", "image/gif", 100, "Omar"));
        Assert.Throws<DomainException>(() => checker.AttachIdPhoto("card.jpg", "image/jpeg", Employee.MaxIdPhotoBytes + 1, "Omar"));
        Assert.Throws<DomainException>(() => checker.AttachIdPhoto("card.jpg", "image/jpeg", 0, "Omar"));

        checker.AttachIdPhoto("../front.JPG", "IMAGE/JPEG", 2_000, "Omar");
        Assert.Equal((true, "front.JPG", "image/jpeg", (int?)2_000, "Omar"), (checker.HasIdPhoto, checker.IdPhotoFileName, checker.IdPhotoContentType, checker.IdPhotoBytes, checker.IdPhotoUploadedBy));
        checker.AttachIdPhoto("scan.pdf", "application/pdf", 3_000, "Sara");
        Assert.Equal(("scan.pdf", "application/pdf", "Sara"), (checker.IdPhotoFileName, checker.IdPhotoContentType, checker.IdPhotoUploadedBy));

        checker.RemoveIdPhoto();
        Assert.Equal((false, (string?)null, (int?)null), (checker.HasIdPhoto, checker.IdPhotoFileName, checker.IdPhotoBytes));
    }

    [Fact]
    public void A_command_for_the_device_is_numbered_and_its_first_answer_kept()
    {
        var users = DeviceCommand.ReadUsers("CQZ", 3, "Omar");
        Assert.Equal("C:3:DATA QUERY USERINFO", users.Line);
        Assert.Throws<DomainException>(() => DeviceCommand.ReadUsers("CQZ", 0, "Omar"));
        var attendance = DeviceCommand.ReadAttendance("CQZ", 4, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 12), "Omar");
        Assert.Equal("C:4:DATA QUERY ATTLOG StartTime=2026-09-01 00:00:00\tEndTime=2026-10-12 23:59:59", attendance.Line);
        Assert.Throws<DomainException>(() => DeviceCommand.ReadAttendance("CQZ", 5, Monday, Monday.AddDays(-1), "Omar"));

        attendance.Answered(0, Later);
        attendance.Answered(-1002, Later.AddMinutes(1));
        Assert.Equal((0, Later), (attendance.ReturnCode, attendance.AnsweredAtUtc));

        // A device user is their number and name, as the device has them.
        var user = new DeviceUser("CQZ", " 7 ", new string('x', 150), Later);
        Assert.Equal(("7", 100), (user.DeviceUserId, user.Name.Length));
        Assert.Throws<DomainException>(() => new DeviceUser("CQZ", " ", "Hassan", Later));
    }

    [Fact]
    public void A_public_holiday_has_a_name_and_runs_one_day_to_ten()
    {
        Assert.Throws<DomainException>(() => new PublicHoliday(Guid.NewGuid(), " ", Monday, Monday, "Omar"));
        Assert.Throws<DomainException>(() => new PublicHoliday(Guid.NewGuid(), "Eid", Monday, Monday.AddDays(-1), "Omar"));
        Assert.Throws<DomainException>(() => new PublicHoliday(Guid.NewGuid(), "Eid", Monday, Monday.AddDays(10), "Omar"));

        var eid = new PublicHoliday(Guid.NewGuid(), " Eid al-Adha ", Monday, Monday.AddDays(3), "Omar");
        Assert.Equal(("Eid al-Adha", 4), (eid.Name, eid.Days));
        Assert.True(eid.Covers(Monday.AddDays(3)));
        Assert.False(eid.Covers(Friday));
        Assert.True(eid.Overlaps(Monday.AddDays(-2), Monday));
        Assert.False(eid.Overlaps(Friday, Friday));
    }

    [Fact]
    public void A_punch_in_left_18_hours_without_its_punch_out_suspends_the_day_for_a_manager()
    {
        var checker = Checker();

        var abandoned = Day(checker, Monday, At(Monday, 10, 30));
        Assert.Equal((DayStatus.Suspended, 0, (DateTime?)null), (abandoned.Status, abandoned.WorkedMinutes, abandoned.LastOut));
        DayStatus At_(DateTime now) => Timesheet.Evaluate(checker, Monday, [At(Monday, 10)], null, TenMinutesGrace, now).Status;
        Assert.Equal(DayStatus.Open, At_(At(Monday, 15)));
        Assert.Equal(DayStatus.Open, At_(At(Monday, 10).AddHours(18).AddMinutes(-1)));
        Assert.Equal(DayStatus.Suspended, At_(At(Monday, 10).AddHours(18)));

        // In at 23:00 for a 10:00 shift: the day's window closes at 04:00, but it stays open till
        // 17:00 the next day, 18 hours on.
        var tuesday = Monday.AddDays(1);
        DayStatus Late(DateTime now) => Timesheet.Evaluate(checker, Monday, [At(Monday, 23)], null, TenMinutesGrace, now).Status;
        Assert.Equal(DayStatus.Open, Late(At(tuesday, 10)));
        Assert.Equal(DayStatus.Suspended, Late(At(tuesday, 17)));
    }

    [Fact]
    public void An_administrative_penalty_is_hours_with_a_reason_deducted_at_the_hourly_rate()
    {
        var checker = Checker();
        Assert.Throws<DomainException>(() => new AdministrativePenalty(checker.Id, Monday, 0, "Late again", "Omar"));
        Assert.Throws<DomainException>(() => new AdministrativePenalty(checker.Id, Monday, 73, "Late again", "Omar"));
        Assert.Throws<DomainException>(() => new AdministrativePenalty(checker.Id, Monday, 1.255m, "Late again", "Omar"));
        Assert.Throws<DomainException>(() => new AdministrativePenalty(checker.Id, Monday, 2, " ", "Omar"));
        var penalty = new AdministrativePenalty(checker.Id, Monday, 2, " Left the store room open ", "Omar");
        Assert.Equal((2m, "Left the store room open"), (penalty.Hours, penalty.Reason));

        var october = new DateOnly(2026, 10, 1);
        var days = PayrollMonth.Days(october).Select(d =>
            checker.IsDayOff(d) ? Nothing(d, DayStatus.DayOff)
            : d == Monday ? new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0, PenaltyHours: 2)
            : d.Day == 13 ? new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0, PenaltyHours: 0.5m)
            : new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0)).ToList();
        var figures = PayrollCalculator.Calculate(checker, october, days, [], advances: 0, broughtForward: 0);

        // Two and a half hours at 20.
        Assert.Equal((2.5m, 50m, 5_350m, 5_350m), (figures.AdministrativePenaltyHours, figures.AdministrativePenalty, figures.Net, figures.WagesCost));
    }

    [Fact]
    public void A_penalty_in_money_is_deducted_as_it_is_beside_the_hours_and_a_disciplinary_actions_is_its_own()
    {
        var checker = Checker();
        var amount = AdministrativePenalty.ForAmount(checker.Id, Monday, 150.5m, " Broke the rota ", "Omar");
        Assert.Equal((0m, (decimal?)150.5m, "Broke the rota", (Guid?)null), (amount.Hours, amount.Amount, amount.Reason, amount.DisciplinaryActionId));
        Assert.Equal(150.5m, amount.DeductionAt(20));
        Assert.Equal(40m, new AdministrativePenalty(checker.Id, Monday, 2, "Late", "Omar").DeductionAt(20));
        Assert.Throws<DomainException>(() => amount.Change(2, "Late", "Omar"));
        Assert.Throws<DomainException>(() => new AdministrativePenalty(checker.Id, Monday, 2, "Late", "Omar").ChangeAmount(100, "Late", "Omar"));
        Assert.Throws<DomainException>(() => AdministrativePenalty.ForAmount(checker.Id, Monday, 0, "Late", "Omar"));
        Assert.Throws<DomainException>(() => AdministrativePenalty.ForAmount(checker.Id, Monday, 10.255m, "Late", "Omar"));
        Assert.Throws<DomainException>(() => AdministrativePenalty.ForAmount(checker.Id, Monday, AdministrativePenalty.MaxAmount + 1, "Late", "Omar"));

        // Two hours and 150.50 on the Monday: 40 + 150.50 off the pay, the money apart.
        var october = new DateOnly(2026, 10, 1);
        var days = PayrollMonth.Days(october).Select(d =>
            checker.IsDayOff(d) ? Nothing(d, DayStatus.DayOff)
            : d == Monday ? new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0, PenaltyHours: 2, PenaltyAmount: 150.5m)
            : new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0)).ToList();
        var figures = PayrollCalculator.Calculate(checker, october, days, [], advances: 0, broughtForward: 0);
        Assert.Equal((2m, 150.5m, 190.5m, 5_209.5m), (figures.AdministrativePenaltyHours, figures.AdministrativePenaltyAmounts, figures.AdministrativePenalty, figures.Net));

        // Kept on the day's record as worked out.
        var record = new DailyRecord(checker.Id, days.Single(d => d.Day == Monday));
        Assert.Equal((2m, 150.5m), (record.ToEvaluation().PenaltyHours, record.ToEvaluation().PenaltyAmount));
    }

    [Fact]
    public void Unpaid_leave_takes_a_days_pay_never_counted_absent_and_worked_after_all_is_a_working_day()
    {
        var checker = Checker();
        Assert.Equal(DayStatus.UnpaidLeave, Timesheet.Evaluate(checker, Monday, [], DayMarkKind.UnpaidLeave, TenMinutesGrace, Later).Status);
        var came = Timesheet.Evaluate(checker, Monday, [At(Monday, 10, 20), At(Monday, 19)], DayMarkKind.UnpaidLeave, TenMinutesGrace, Later);
        Assert.Equal((DayStatus.Worked, 20), (came.Status, came.LateMinutes));
        // Only a leave request's.
        Assert.Throws<DomainException>(() => new DayMark(checker.Id, Monday, DayMarkKind.UnpaidLeave, null, "Omar"));
        Assert.Equal(DayMarkKind.UnpaidLeave, new DayMark(checker.Id, Monday, DayMarkKind.UnpaidLeave, "Unpaid", "Omar", Guid.NewGuid()).Kind);

        // Two days of it in October: 180 each off, and no absence.
        var october = new DateOnly(2026, 10, 1);
        var days = PayrollMonth.Days(october).Select(d =>
            checker.IsDayOff(d) ? Nothing(d, DayStatus.DayOff)
            : d == Monday || d == Monday.AddDays(1) ? Nothing(d, DayStatus.UnpaidLeave)
            : new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0)).ToList();
        var figures = PayrollCalculator.Calculate(checker, october, days, [], advances: 0, broughtForward: 0);
        Assert.Equal((2, 360m, 0, 5_040m, 5_040m), (figures.UnpaidLeaveDays, figures.UnpaidLeaveDeduction, figures.AbsentDays, figures.Net, figures.WagesCost));

        // A daily worker is paid only the days they work: nothing more comes off.
        var helper = Checker();
        helper.SetPay(EmploymentType.Daily, 200);
        Assert.Equal(0m, PayrollCalculator.Calculate(helper, october, days, [], advances: 0, broughtForward: 0).UnpaidLeaveDeduction);
    }

    [Fact]
    public void A_punch_is_kept_to_the_second_on_the_wall_clock_and_ignored_rather_than_removed()
    {
        var at = new DateTime(2026, 10, 12, 10, 0, 7, 450, DateTimeKind.Utc);
        var punch = AttendanceLog.FromDevice("CQZ7232060112", "7", at, 0, 1, null);

        Assert.Equal((new DateTime(2026, 10, 12, 10, 0, 7), DateTimeKind.Unspecified), (punch.PunchedAt, punch.PunchedAt.Kind));
        Assert.False(punch.Counts);
        var employee = Guid.NewGuid();
        punch.AssignTo(employee);
        Assert.True(punch.Counts);
        Assert.Throws<DomainException>(() => punch.AssignTo(Guid.NewGuid()));

        Assert.Throws<DomainException>(() => punch.Ignore(" ", "Omar"));
        punch.Ignore("Scanned twice", "Omar");
        Assert.False(punch.Counts);
        punch.Restore();
        Assert.True(punch.Counts);

        Assert.Throws<DomainException>(() => AttendanceLog.Manual(Guid.NewGuid(), employee, at, "", "Omar"));
        Assert.Equal(PunchSource.Manual, AttendanceLog.Manual(Guid.NewGuid(), employee, at, "The device was down", "Omar").Source);
    }

    [Fact]
    public void A_device_first_seen_waits_for_a_manager()
    {
        var device = AttendanceDevice.FirstSeen(" CQZ7232060112 ", DateTime.UtcNow, "192.168.1.40");
        Assert.Equal(("CQZ7232060112", false), (device.SerialNumber, device.IsApproved));

        Assert.Throws<DomainException>(() => device.Approve(" ", "Omar"));
        device.Approve("Key accounts door", "Omar");
        Assert.True(device.IsApproved);
        device.Revoke();
        Assert.False(device.IsApproved);

        Assert.Throws<DomainException>(() => AttendanceDevice.FirstSeen("SN 1; drop", DateTime.UtcNow, null));
    }

    [Fact]
    public void A_months_pay_is_its_salary_and_additions_less_its_advances_lateness_and_absence()
    {
        var checker = Checker();
        var october = new DateOnly(2026, 10, 1);
        var days = PayrollMonth.Days(october).Select(d =>
            checker.IsDayOff(d) ? Nothing(d, DayStatus.DayOff)
            : d.Day is 5 or 6 ? Nothing(d, DayStatus.Absent)
            : d.Day == 12 ? new DayEvaluation(d, DayStatus.Worked, null, null, 2, 480, 60, 30, 0, 0)
            : d.Day == 13 ? new DayEvaluation(d, DayStatus.Worked, null, null, 2, 660, 0, 0, 0, 120)
            : new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0)).ToList();
        var voided = new PayrollAdjustment(Guid.NewGuid(), checker.Id, october, AdjustmentKind.Incentive, 999, null, "Omar");
        voided.Void("Omar");
        PayrollAdjustment[] adjustments =
        [
            new(Guid.NewGuid(), checker.Id, october, AdjustmentKind.Incentive, 500, "Best month", "Omar"),
            new(Guid.NewGuid(), checker.Id, october, AdjustmentKind.LeaveAllowance, 180, "Worked the 6th of October", "Omar", forDay: new DateOnly(2026, 10, 6)),
            voided,
        ];

        var figures = PayrollCalculator.Calculate(checker, october, days, adjustments, advances: 1_000, broughtForward: 0);

        Assert.Equal(
            (5_400m, 31, 31, 5_400m, 500m, 180m, 120, 40m, 90, 30m, 2, 360m, 1_000m, 0m),
            (figures.MonthlySalary, figures.DaysInMonth, figures.DaysEmployed, figures.BaseSalary, figures.Incentives, figures.LeaveAllowances,
                figures.OvertimeMinutes, figures.OvertimePay, figures.ShortMinutes, figures.LatenessPenalty, figures.AbsentDays, figures.AbsencePenalty,
                figures.Advances, figures.BroughtForward));
        // 5,400 + 500 + 180 + 40 = 6,120, less 1,000 + 30 + 360.
        Assert.Equal((6_120m, 1_390m, 4_730m, 4_730m, 0m), (figures.Gross, figures.Deductions, figures.Net, figures.NetPayable, figures.CarriedForward));
        Assert.Equal(5_730m, figures.WagesCost);
        Assert.Equal((0, 0, 0), (figures.MissingPunchDays, figures.OpenDays, figures.DaysOffWorked));
        Assert.Equal(31 - 5 - 2, figures.DaysWorked);

        var slip = new PayrollSlip(Guid.NewGuid(), checker.Id, october, figures);
        slip.Finalize("Omar");
        Assert.Equal((PayrollSlipStatus.Finalized, 4_730m), (slip.Status, slip.NetPayable));
        Assert.Throws<DomainException>(() => slip.Recalculate(figures));
    }

    [Fact]
    public void A_month_started_part_way_pays_its_share_and_one_that_owes_more_carries_it_over()
    {
        var checker = Checker(hiredOn: new DateOnly(2026, 10, 22));
        var october = new DateOnly(2026, 10, 1);
        var days = PayrollMonth.Days(october).Where(checker.IsEmployedOn)
            .Select(d => checker.IsDayOff(d) ? Nothing(d, DayStatus.DayOff) : new DayEvaluation(d, DayStatus.Worked, null, null, 2, 540, 0, 0, 0, 0));

        var figures = PayrollCalculator.Calculate(checker, october, days, [], advances: 2_000, broughtForward: 150);

        // 10 of October's 31 days.
        Assert.Equal((10, 1_741.94m), (figures.DaysEmployed, figures.BaseSalary));
        Assert.Equal((0m, 408.06m), (figures.NetPayable, figures.CarriedForward));
    }

    [Fact]
    public void A_slip_is_finalized_only_once_every_day_is_settled()
    {
        var checker = Checker();
        var october = new DateOnly(2026, 10, 1);
        var allButOne = PayrollMonth.Days(october).Where(d => d.Day != 31).Select(d => Nothing(d, checker.IsDayOff(d) ? DayStatus.DayOff : DayStatus.Absent)).ToList();

        var open = new PayrollSlip(Guid.NewGuid(), checker.Id, october, PayrollCalculator.Calculate(checker, october, allButOne, [], 0, 0));
        Assert.Equal(1, open.Figures.OpenDays);
        Assert.Throws<DomainException>(() => open.Finalize("Omar"));

        var missing = PayrollCalculator.Calculate(checker, october, [.. allButOne, Nothing(new DateOnly(2026, 10, 31), DayStatus.Suspended)], [], 0, 0);
        var slip = new PayrollSlip(Guid.NewGuid(), checker.Id, october, missing);
        Assert.Equal(
            "1 day is suspended, a punch-in with no punch-out: enter the punch-out, or ignore the stray punch, first.",
            Assert.Throws<DomainException>(() => slip.Finalize("Omar")).Message);

        Assert.Throws<DomainException>(() => PayrollCalculator.Calculate(checker, october, [Nothing(new DateOnly(2026, 11, 1), DayStatus.Absent)], [], 0, 0));
        Assert.Throws<DomainException>(() => PayrollCalculator.Calculate(
            checker, october, [], [new PayrollAdjustment(Guid.NewGuid(), Guid.NewGuid(), october, AdjustmentKind.Incentive, 1, null, "Omar")], 0, 0));
        Assert.Throws<DomainException>(() => new PayrollAdjustment(Guid.NewGuid(), checker.Id, new DateOnly(2026, 10, 2), AdjustmentKind.Incentive, 1, null, "Omar"));
        Assert.Throws<DomainException>(() => new PayrollAdjustment(Guid.NewGuid(), checker.Id, october, AdjustmentKind.Incentive, 0, null, "Omar"));
    }

    [Fact]
    public void An_advance_is_hrs_record_paid_to_someone_from_a_months_pay_or_given_out_later()
    {
        var paid = new DateOnly(2026, 10, 22);
        var hassan = Guid.NewGuid();
        var toHassan = new HrAdvance(Guid.NewGuid(), 500.555m, paid, hassan, new DateOnly(2026, 10, 1), "  Rent ", "Omar");
        Assert.Equal((500.56m, "Rent", false), (toHassan.Amount, toHassan.Notes, toHassan.IsPending));
        var lump = new HrAdvance(Guid.NewGuid(), 900, paid, null, null, null, "Omar");
        Assert.True(lump.IsPending);
        // To someone, from a month's pay; to no one, from none; never a month before it was paid.
        Assert.Throws<DomainException>(() => new HrAdvance(Guid.NewGuid(), 100, paid, hassan, null, null, "Omar"));
        Assert.Throws<DomainException>(() => new HrAdvance(Guid.NewGuid(), 100, paid, null, new DateOnly(2026, 10, 1), null, "Omar"));
        Assert.Throws<DomainException>(() => new HrAdvance(Guid.NewGuid(), 100, paid, hassan, new DateOnly(2026, 9, 1), null, "Omar"));
        Assert.Throws<DomainException>(() => new HrAdvance(Guid.NewGuid(), 0, paid, hassan, new DateOnly(2026, 10, 1), null, "Omar"));
        lump.Void("Omar");
        Assert.Throws<DomainException>(() => lump.Void("Omar"));
    }

    [Fact]
    public void Advances_handed_out_with_no_one_named_are_given_out_a_share_each_from_their_own_month_on()
    {
        var handedOut = new DateOnly(2026, 10, 12);
        var lump = new HrAdvance(Guid.NewGuid(), 1_000, handedOut, null, null, "Operations", "Sara");

        var share = new AdvanceAllocation(Guid.NewGuid(), lump.Id, Guid.NewGuid(), 333.335m, handedOut, new DateOnly(2026, 11, 1), "  Night team  ", "Omar");
        Assert.Equal((333.34m, "Night team", false, lump.Id), (share.Amount, share.Notes, share.IsVoided, share.AdvanceId));
        Assert.Throws<DomainException>(() => new AdvanceAllocation(Guid.NewGuid(), lump.Id, Guid.NewGuid(), 0, handedOut, new DateOnly(2026, 10, 1), null, "Omar"));
        // Never from the pay of a month before it was handed out.
        Assert.Throws<DomainException>(() => new AdvanceAllocation(Guid.NewGuid(), lump.Id, Guid.NewGuid(), 50, handedOut, new DateOnly(2026, 9, 1), null, "Omar"));
        share.Void("Omar");
        Assert.True(share.IsVoided);
        Assert.Throws<DomainException>(() => share.Void("Omar"));
    }

    [Fact]
    public void A_salary_payment_is_of_a_months_pay_on_a_day_of_it_or_after_and_is_voided_once()
    {
        var october = new DateOnly(2026, 10, 1);
        var paid = new SalaryPayment(Guid.NewGuid(), Guid.NewGuid(), october, new DateOnly(2026, 11, 1), 4_730.555m, "  Bank transfer ", "Omar");
        Assert.Equal((4_730.56m, "Bank transfer", false), (paid.Amount, paid.Notes, paid.IsVoided));
        Assert.Throws<DomainException>(() => new SalaryPayment(Guid.NewGuid(), Guid.NewGuid(), october, new DateOnly(2026, 9, 30), 100, null, "Omar"));
        Assert.Throws<DomainException>(() => new SalaryPayment(Guid.NewGuid(), Guid.NewGuid(), october, october, 0, null, "Omar"));
        Assert.Throws<DomainException>(() => new SalaryPayment(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 2), october, 100, null, "Omar"));
        paid.Void("Omar");
        Assert.Equal((true, "Omar"), (paid.IsVoided, paid.VoidedBy));
        Assert.Throws<DomainException>(() => paid.Void("Omar"));
    }

    private static DayEvaluation Nothing(DateOnly day, DayStatus status) => new(day, status, null, null, 0, 0, 0, 0, 0, 0);
}
