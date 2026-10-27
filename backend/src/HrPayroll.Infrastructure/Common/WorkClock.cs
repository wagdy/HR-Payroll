using Microsoft.Extensions.Options;
using HrPayroll.Application.Common;
using HrPayroll.Domain.Company;
using HrPayroll.Domain.Employees;

namespace HrPayroll.Infrastructure.Common;

// The company's settings as the configuration first gives them (the Company section): only until
// the first start has made the settings row, which a manager changes on Company Settings after.
public sealed class CompanyOptions
{
    public const string Section = "Company";

    public string Name { get; set; } = "My company";

    // IANA id. Works on Linux, macOS and Windows alike.
    public string TimeZoneId { get; set; } = "Africa/Cairo";

    // The hour the workday rolls over, in local time. With 4, a punch at 01:30 belongs to the
    // evening before: a night past midnight is one day.
    public int WorkdayStartsAtHour { get; set; } = 4;

    public Country Country { get; set; } = Country.Egypt;

    // Empty: amounts are written as numbers alone.
    public string CurrencySymbol { get; set; } = string.Empty;

    public CurrencyPosition CurrencyPosition { get; set; } = CurrencyPosition.Before;

    public WeekDays Weekends { get; set; } = WeekDays.Friday;
}

// The company's settings held for the whole app (ICompanyProfile): the configuration's until the
// database's are read at start-up, then each save's.
public sealed class CompanyProfileStore(IOptions<CompanyOptions> options) : ICompanyProfile
{
    private volatile CompanyProfile _current = new(
        options.Value.Name, new MoneyFormat(options.Value.CurrencySymbol, options.Value.CurrencyPosition), options.Value.Country,
        options.Value.TimeZoneId, options.Value.WorkdayStartsAtHour, options.Value.Weekends, HasLogo: false, LogoUpdatedAtUtc: null);

    public CompanyProfile Current => _current;

    public void Set(CompanyProfile profile) => _current = profile;
}

// Everything is stored in UTC. This is the one place that knows the company's local time: which
// workday a moment belongs to, and what an attendance device's clock shows. It follows the time
// zone and workday hour of Company Settings as they are saved.
public sealed class WorkClock(ICompanyProfile company) : IWorkClock
{
    private volatile Zone _zone = new(string.Empty, TimeZoneInfo.Utc);

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, ZoneNow());

    public DateOnly WorkdayOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc).AddHours(-company.Current.WorkdayStartsAtHour));

    public DateOnly LocalDateOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public DateTime LocalTimeOf(DateTime utc) => DateTime.SpecifyKind(ToLocal(utc), DateTimeKind.Unspecified);

    // Looked up once a zone, not each time.
    private TimeZoneInfo ZoneNow()
    {
        var id = company.Current.TimeZoneId;
        var zone = _zone;
        if (zone.Id != id)
            _zone = zone = new Zone(id, TimeZoneInfo.FindSystemTimeZoneById(id));
        return zone.Info;
    }

    private sealed record Zone(string Id, TimeZoneInfo Info);
}
