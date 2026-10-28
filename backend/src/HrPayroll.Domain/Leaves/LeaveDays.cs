using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Employees;

namespace HrPayroll.Domain.Leaves;

// The days a leave takes: those of its dates the employee would work, neither a day off of theirs
// (a weekly one, or the roster's) nor a public holiday, and while employed. A Sunday to Thursday
// asked by one off on Fridays and Saturdays is five days; with a holiday on the Tuesday, four.
public static class LeaveDays
{
    public static IReadOnlyList<DateOnly> Of(Employee employee, DateOnly startDate, DateOnly endDate, IEnumerable<PublicHoliday> holidays) =>
        Of(WorkSchedule.Of(employee), startDate, endDate, holidays);

    public static IReadOnlyList<DateOnly> Of(WorkSchedule schedule, DateOnly startDate, DateOnly endDate, IEnumerable<PublicHoliday> holidays)
    {
        var list = holidays.ToList();
        var days = new List<DateOnly>();
        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            if (schedule.Employee.IsEmployedOn(day) && !schedule.Plan(day).Off && !list.Any(h => h.Covers(day)))
                days.Add(day);
        }

        return days;
    }
}
