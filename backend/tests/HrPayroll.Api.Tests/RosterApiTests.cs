using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HrPayroll.Api.Tests;

// Shift rostering (جداول الشفتات): the shifts, the roster that puts each employee on one or on a day
// off, and the attendance and pay measured against it.
public class RosterApiTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.UnprocessableEntity)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancel);
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<DateOnly> TodayAsync(HttpClient client) =>
        DateOnly.Parse((await Json(await client.GetAsync("/api/hr/today", Cancel))).GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

    private static object Shift(string name, string startsAt, string endsAt, string tone = "Sky") => new { name, startsAt, endsAt, tone };

    private static object Change(Guid employee, DateOnly date, Guid? shiftId = null, bool dayOff = false) =>
        new { employeeId = employee, date = Iso(date), shiftId, dayOff };

    private static Task<HttpResponseMessage> Roster(HttpClient client, params object[] changes) =>
        client.PutAsJsonAsync("/api/hr/roster", new { changes }, Cancel);

    // The employee's day on the day's review.
    private static async Task<JsonElement> DayAsync(HttpClient client, Guid employee, DateOnly date) =>
        (await Json(await client.GetAsync($"/api/hr/attendance?date={Iso(date)}", Cancel))).GetProperty("employees").EnumerateArray()
            .Single(e => e.GetProperty("employee").GetProperty("id").GetGuid() == employee).GetProperty("day");

    [Fact]
    public async Task A_rostered_day_is_measured_against_its_shift_and_a_rostered_day_off_is_no_absence()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var day = today.AddDays(-2);

        // The shifts: two to sixteen hours; past midnight when they end before they start.
        var morning = Guid.NewGuid();
        var night = Guid.NewGuid();
        var made = await Json(await manager.PutAsJsonAsync($"/api/hr/shifts/{morning}", Shift(" Morning ", "08:00:00", "16:00:00", "Amber"), Cancel));
        Assert.Equal(("Morning", "08:00:00", "16:00:00", 480, "Amber"), (made.GetProperty("name").GetString(), made.GetProperty("startsAt").GetString(),
            made.GetProperty("endsAt").GetString(), made.GetProperty("minutes").GetInt32(), made.GetProperty("tone").GetString()));
        Assert.Equal(480, (await Json(await manager.PutAsJsonAsync($"/api/hr/shifts/{night}", Shift("Night", "22:00:00", "06:00:00"), Cancel))).GetProperty("minutes").GetInt32());
        Assert.Contains("There is a Morning shift already.", await Problem(await manager.PutAsJsonAsync($"/api/hr/shifts/{Guid.NewGuid()}", Shift("morning", "07:00:00", "15:00:00"), Cancel)));
        Assert.Contains("1 to 16 hours long", await Problem(await manager.PutAsJsonAsync($"/api/hr/shifts/{Guid.NewGuid()}", Shift("None", "08:00:00", "08:00:00"), Cancel)));

        // Hassan works 10:00 to 19:00, no weekly day off.
        var hassan = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}", new
        {
            requestId = Guid.NewGuid(), name = "Hassan", jobTitle = "Waiter", monthlySalary = 5_400, shiftStartsAt = "10:00:00", daysOff = Array.Empty<string>(),
            hiredOn = Iso(day.AddDays(-10)),
        }, Cancel));

        // An accountant sees the roster, and changes none of it.
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/hr/roster", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Roster(accountant, Change(hassan, day, morning))).StatusCode);

        // The morning shift on the day, the day before off.
        Assert.Equal(HttpStatusCode.NoContent, (await Roster(manager, Change(hassan, day, morning), Change(hassan, day.AddDays(-1), dayOff: true))).StatusCode);
        var roster = await Json(await manager.GetAsync($"/api/hr/roster?from={Iso(day.AddDays(-1))}&to={Iso(day.AddDays(1))}", Cancel));
        var cells = roster.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employee").GetProperty("id").GetGuid() == hassan)
            .GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(
            [(true, true, (string?)null, (string?)null), (true, false, "08:00:00", "16:00:00"), (false, false, "10:00:00", "19:00:00")],
            cells.Select(c => (c.GetProperty("rostered").GetBoolean(), c.GetProperty("off").GetBoolean(), c.GetProperty("startsAt").GetString(),
                c.GetProperty("endsAt").GetString())));
        Assert.Equal(["Morning", "Night"], roster.GetProperty("shifts").EnumerateArray().Select(s => s.GetProperty("name").GetString()));

        // In at 08:30 and out at 16:00: half an hour late on the morning shift, nothing early.
        foreach (var time in new[] { "08:30:00", "16:00:00" })
            await Json(await manager.PostAsJsonAsync("/api/hr/punches", new { punchId = Guid.NewGuid(), employeeId = hassan, punchedAt = $"{Iso(day)}T{time}", reason = "The test" }, Cancel));
        var worked = await DayAsync(manager, hassan, day);
        Assert.Equal(("Morning", $"{Iso(day)}T08:00:00", 30, 0), (worked.GetProperty("shiftName").GetString(), worked.GetProperty("shiftStartsAt").GetString(),
            worked.GetProperty("lateMinutes").GetInt32(), worked.GetProperty("earlyLeaveMinutes").GetInt32()));
        var off = await DayAsync(manager, hassan, day.AddDays(-1));
        Assert.Equal(("DayOff", true, false), (off.GetProperty("status").GetString(), off.GetProperty("isRosteredDayOff").GetBoolean(), off.GetProperty("isWeeklyDayOff").GetBoolean()));

        // The month's pay is measured the same: the half hour short, and no absence for the day off.
        var payroll = await Json(await manager.GetAsync($"/api/hr/payroll?month={Iso(new DateOnly(day.Year, day.Month, 1))}", Cancel));
        var figures = payroll.GetProperty("slips").EnumerateArray().Single(s => s.GetProperty("employee").GetProperty("id").GetGuid() == hassan).GetProperty("figures");
        Assert.Equal(30, figures.GetProperty("shortMinutes").GetInt32());

        // A shift that starts before the night before it ends is refused, the whole change with it.
        var early = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/shifts/{early}", Shift("Early", "05:00:00", "13:00:00"), Cancel));
        Assert.Contains("would start at 05:00, before the day before's ends at 06:00",
            await Problem(await Roster(manager, Change(hassan, day.AddDays(-4), night), Change(hassan, day.AddDays(-3), early))));
        Assert.Null((await DayAsync(manager, hassan, day.AddDays(-4))).GetProperty("shiftName").GetString());

        // Back to his own schedule: 10:00 to 19:00, so out at 16:00 is three hours early.
        Assert.Equal(HttpStatusCode.NoContent, (await Roster(manager, Change(hassan, day))).StatusCode);
        var own = await DayAsync(manager, hassan, day);
        Assert.Equal((JsonValueKind.Null, 0, 180), (own.GetProperty("shiftName").ValueKind, own.GetProperty("lateMinutes").GetInt32(), own.GetProperty("earlyLeaveMinutes").GetInt32()));

        // The rostered day off marked a working day after all: with no punch, absent.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}/days/{Iso(day.AddDays(-1))}", new { kind = "WorkingDay" }, Cancel)).StatusCode);
        Assert.Equal("Absent", (await DayAsync(manager, hassan, day.AddDays(-1))).GetProperty("status").GetString());

        // A shift on the roster stays; one on no day goes.
        Assert.Equal(HttpStatusCode.NoContent, (await Roster(manager, Change(hassan, today.AddDays(1), morning))).StatusCode);
        Assert.Contains("Morning is on the roster on 1 day", await Problem(await manager.DeleteAsync($"/api/hr/shifts/{morning}", Cancel)));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/hr/shifts/{night}", Cancel)).StatusCode);
    }
}
