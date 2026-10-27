using Microsoft.Extensions.Options;
using HrPayroll.Application.Common;

namespace HrPayroll.Infrastructure.Common;

// The business this system runs for: its name, its time zone and when its workday starts.
public sealed class CompanyOptions
{
    public const string Section = "Company";

    public string Name { get; set; } = "My company";

    // IANA id. Works on Linux, macOS and Windows alike.
    public string TimeZoneId { get; set; } = "Africa/Cairo";

    // The hour the workday rolls over, in local time. With 4, a punch at 01:30 belongs to the
    // evening before: a night past midnight is one day.
    public int WorkdayStartsAtHour { get; set; } = 4;
}

// Everything is stored in UTC. This is the one place that knows the business's local time:
// which workday a moment belongs to, and what an attendance device's clock shows.
public sealed class WorkClock(IOptions<CompanyOptions> options) : IWorkClock
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);
    private readonly int _dayStartsAtHour = options.Value.WorkdayStartsAtHour;

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, _zone);

    public DateOnly WorkdayOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc).AddHours(-_dayStartsAtHour));

    public DateOnly LocalDateOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public DateTime LocalTimeOf(DateTime utc) => DateTime.SpecifyKind(ToLocal(utc), DateTimeKind.Unspecified);
}
