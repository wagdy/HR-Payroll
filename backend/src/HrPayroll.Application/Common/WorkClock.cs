namespace HrPayroll.Application.Common;

// The business's clock, in its own time zone: the workday a moment belongs to (a night past
// midnight still the evening's day until the workday starts), its calendar date, and the wall
// clock an attendance device shows.
public interface IWorkClock
{
    DateOnly WorkdayOf(DateTime utc);

    // The calendar date on the wall: after midnight, a day ahead of the workday until it starts.
    DateOnly LocalDateOf(DateTime utc);

    DateTime LocalTimeOf(DateTime utc);
}
