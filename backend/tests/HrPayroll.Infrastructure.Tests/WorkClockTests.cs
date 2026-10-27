using Microsoft.Extensions.Options;
using HrPayroll.Infrastructure.Common;

namespace HrPayroll.Infrastructure.Tests;

// No database here: the workday is arithmetic on the business's time zone.
public class WorkClockTests
{
    private static readonly WorkClock Cairo = new(Options.Create(new CompanyOptions { TimeZoneId = "Africa/Cairo", WorkdayStartsAtHour = 4 }));

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void A_moment_after_midnight_belongs_to_the_evening_before_until_the_workday_starts()
    {
        // 01:30 on the 4th in Cairo (October: UTC+3).
        Assert.Equal((new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4)), (Cairo.WorkdayOf(Utc(2026, 10, 3, 22, 30)), Cairo.LocalDateOf(Utc(2026, 10, 3, 22, 30))));
        // 04:30 on the 4th: a new day.
        Assert.Equal(new DateOnly(2026, 10, 4), Cairo.WorkdayOf(Utc(2026, 10, 4, 1, 30)));
    }

    [Fact]
    public void The_wall_clock_is_the_business_time_zones_with_no_zone_attached()
    {
        // January: UTC+2.
        var local = Cairo.LocalTimeOf(Utc(2026, 1, 15, 8));
        Assert.Equal((new DateTime(2026, 1, 15, 10, 0, 0), DateTimeKind.Unspecified), (local, local.Kind));
    }
}
