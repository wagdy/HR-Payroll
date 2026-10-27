using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.SignalR.Client;
using HrPayroll.Application.Hr;
using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Employees;
using HrPayroll.Infrastructure.Excel;

namespace HrPayroll.Api.Tests;

// HR & Payroll through the API: employees as a manager makes them and an accountant asks for them,
// advances out of the safe, the takings and the daily batch, the attendance device's punches, and
// the month's pay.
public class HrApiTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    private static async Task<string> Problem(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancel);
    }

    // The employees listed, without the outsider: an employee too, signed in to the portal.
    private static IEnumerable<JsonElement> Listed(JsonElement employees) =>
        employees.EnumerateArray().Where(e => e.GetProperty("name").GetString() != TestApi.OutsiderName);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<DateOnly> TodayAsync(HttpClient client) =>
        DateOnly.Parse((await Json(await client.GetAsync("/api/hr/today", Cancel))).GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

    private static object Employee(string name, string job, decimal salary, DateOnly hiredOn, string? device = null, string shift = "10:00:00", string[]? daysOff = null) =>
        new { requestId = Guid.NewGuid(), name, jobTitle = job, monthlySalary = salary, shiftStartsAt = shift, daysOff = daysOff ?? [], hiredOn = Iso(hiredOn), deviceUserId = device };

    private static async Task<Guid> HireAsync(HttpClient manager, string name, DateOnly hiredOn, string? device = null)
    {
        var id = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{id}", Employee(name, "Chef", 5_400, hiredOn, device), Cancel));
        return id;
    }

    private static Task<HttpResponseMessage> AdvanceAsync(HttpClient client, Guid employee, decimal amount, DateOnly date) =>
        client.PostAsJsonAsync("/api/hr/advances", new
        {
            advanceId = Guid.NewGuid(), requestId = Guid.NewGuid(), amount, date = Iso(date), employeeId = employee,
        }, Cancel);

    private static async Task ApproveAsync(HttpClient manager, JsonElement asked) =>
        await Json(await manager.PostAsJsonAsync($"/api/approval-requests/{asked.GetProperty("request").GetProperty("id").GetGuid()}/approve", new { }, Cancel));

    [Fact]
    public async Task Employees_are_a_managers_at_once_and_an_accountants_once_approved()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var outsider = await api.OutsiderAsync();
        var start = new DateOnly(2026, 9, 1);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/hr/employees", Cancel)).StatusCode);

        var hassanId = Guid.NewGuid();
        var hassan = await Json(await manager.PutAsJsonAsync(
            $"/api/hr/employees/{hassanId}", Employee(" Hassan ", "Chef", 5_400, start, "7", daysOff: ["Friday"]), Cancel));
        Assert.Equal(("Hassan", "10:00:00", "19:00:00", 180m, 20m), (hassan.GetProperty("name").GetString(), hassan.GetProperty("shiftStartsAt").GetString(),
            hassan.GetProperty("shiftEndsAt").GetString(), hassan.GetProperty("dailyRate").GetDecimal(), hassan.GetProperty("hourlyRate").GetDecimal()));
        Assert.Equal(["Friday"], hassan.GetProperty("daysOff").EnumerateArray().Select(d => d.GetString()));

        // Another with the same number on the device is refused.
        Assert.Contains("Hassan has number 7", await Problem(await manager.PutAsJsonAsync($"/api/hr/employees/{Guid.NewGuid()}", Employee("Karim", "Waiter", 4_000, start, "7"), Cancel)));

        // An accountant's new employee waits: not on the list until approved.
        var monaId = Guid.NewGuid();
        var asked = await Json(await accountant.PutAsJsonAsync($"/api/hr/employees/{monaId}", Employee("Mona", "Waiter", 4_000, start, shift: "12:00:00"), Cancel));
        Assert.Equal("Requested", asked.GetProperty("outcome").GetString());
        Assert.Equal("New employee · Mona · Waiter · 4,000.00 a month, from 12:00", asked.GetProperty("request").GetProperty("summary").GetString());
        Assert.Single(Listed(await Json(await manager.GetAsync("/api/hr/employees", Cancel))));
        await ApproveAsync(manager, asked);
        Assert.Equal(["Hassan", "Mona"], Listed(await Json(await manager.GetAsync("/api/hr/employees", Cancel))).Select(e => e.GetProperty("name").GetString()));

        // A change asked for says what it changes, and locks the employee until it is decided.
        var raise = await Json(await accountant.PutAsJsonAsync($"/api/hr/employees/{monaId}", Employee("Mona", "Waiter", 4_500, start, shift: "12:00:00"), Cancel));
        Assert.Equal(
            [("Pay", "4,000.00 a month", "4,500.00 a month")],
            raise.GetProperty("request").GetProperty("changes").EnumerateArray()
                .Select(c => (c.GetProperty("label").GetString(), c.GetProperty("before").GetString(), c.GetProperty("after").GetString())));
        Assert.Contains("can be changed once a manager has decided", await Problem(await manager.PostAsJsonAsync(
            $"/api/hr/employees/{monaId}/leave", new { requestId = Guid.NewGuid(), lastDay = "2026-10-15" }, Cancel)));
        await ApproveAsync(manager, raise);

        // Leaving, asked for and approved; and back, at once for a manager.
        var leaves = await Json(await accountant.PostAsJsonAsync($"/api/hr/employees/{monaId}/leave", new { requestId = Guid.NewGuid(), lastDay = "2026-10-15" }, Cancel));
        Assert.Equal("Mona leaves · last day 15 Oct 2026", leaves.GetProperty("request").GetProperty("summary").GetString());
        await ApproveAsync(manager, leaves);
        var mona = (await Json(await manager.GetAsync("/api/hr/employees", Cancel))).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == monaId);
        Assert.Equal((4_500m, false, "2026-10-15"), (mona.GetProperty("monthlySalary").GetDecimal(), mona.GetProperty("isActive").GetBoolean(), mona.GetProperty("leftOn").GetString()));
        var back = await Json(await manager.PostAsJsonAsync($"/api/hr/employees/{monaId}/return", new { requestId = Guid.NewGuid(), returnsOn = "2026-11-01" }, Cancel));
        Assert.Equal((true, "2026-11-01"), (back.GetProperty("isActive").GetBoolean(), back.GetProperty("hiredOn").GetString()));
    }

    [Fact]
    public async Task An_employees_profile_keeps_their_id_card_and_mobile_and_the_cards_photo()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var outsider = await api.OutsiderAsync();
        var start = new DateOnly(2026, 9, 1);
        object Profile(string name, string? nationalId, string? mobile, decimal salary = 5_400) => new
        {
            requestId = Guid.NewGuid(), name, jobTitle = "Chef", monthlySalary = salary, shiftStartsAt = "10:00:00", daysOff = Array.Empty<string>(),
            hiredOn = Iso(start), nationalId, mobileNumber = mobile,
        };

        // Typed in Arabic digits, with spaces and dashes: kept as the card's digits.
        var hassanId = Guid.NewGuid();
        var hassan = await Json(await manager.PutAsJsonAsync(
            $"/api/hr/employees/{hassanId}", Profile("Hassan Mahmoud Ali", "٢٩٠٠١٠١٠١٢٣٤٥٦", "٠١٠ ١٢٣٤-٥٦٧٨"), Cancel));
        Assert.Equal(("29001010123456", "01012345678", JsonValueKind.Null),
            (hassan.GetProperty("nationalId").GetString(), hassan.GetProperty("mobileNumber").GetString(), hassan.GetProperty("idPhoto").ValueKind));

        // One employee, for their profile; whoever sees HR.
        var profile = await Json(await accountant.GetAsync($"/api/hr/employees/{hassanId}", Cancel));
        Assert.Equal(("Hassan Mahmoud Ali", "29001010123456"), (profile.GetProperty("name").GetString(), profile.GetProperty("nationalId").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/hr/employees/{Guid.NewGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/hr/employees/{hassanId}", Cancel)).StatusCode);

        Assert.Contains("Hassan Mahmoud Ali has national ID 29001010123456",
            await Problem(await manager.PutAsJsonAsync($"/api/hr/employees/{Guid.NewGuid()}", Profile("Karim", "29001010123456", null), Cancel)));
        Assert.Contains("14 digits", await Problem(await manager.PutAsJsonAsync($"/api/hr/employees/{Guid.NewGuid()}", Profile("Karim", "2900101", null), Cancel)));
        Assert.Contains("A mobile number is its digits",
            await Problem(await manager.PutAsJsonAsync($"/api/hr/employees/{Guid.NewGuid()}", Profile("Karim", null, "call me"), Cancel)));

        // An accountant's new number is asked of a manager, and says what it changes.
        var asked = await Json(await accountant.PutAsJsonAsync(
            $"/api/hr/employees/{hassanId}", Profile("Hassan Mahmoud Ali", "29001010123456", "01198765432"), Cancel));
        Assert.Equal(
            [("Mobile", "01012345678", "01198765432")],
            asked.GetProperty("request").GetProperty("changes").EnumerateArray()
                .Select(c => (c.GetProperty("label").GetString(), c.GetProperty("before").GetString(), c.GetProperty("after").GetString())));
        await ApproveAsync(manager, asked);
        profile = await Json(await manager.GetAsync($"/api/hr/employees/{hassanId}", Cancel));
        Assert.Equal(("29001010123456", "01198765432"), (profile.GetProperty("nationalId").GetString(), profile.GetProperty("mobileNumber").GetString()));
        // One employee to a mobile: the portal signs in by it.
        Assert.Contains("Hassan Mahmoud Ali has mobile number 01198765432 already",
            await Problem(await manager.PutAsJsonAsync($"/api/hr/employees/{Guid.NewGuid()}", Profile("Karim", null, "011 9876 5432"), Cancel)));

        // The card's photo: whoever manages HR, at once; the picture its type says, under 5 MB.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
        byte[] pdf = [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', 1, 2];
        MultipartFormDataContent Upload(byte[] bytes, string type, string name)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            return new MultipartFormDataContent { { file, "file", name } };
        }
        var photo = $"/api/hr/employees/{hassanId}/id-photo";
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsync(photo, Upload(jpeg, "image/jpeg", "card.jpg"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(photo, Cancel)).StatusCode);
        Assert.Contains("not the photo or PDF it says it is", await Problem(await manager.PutAsync(photo, Upload([1, 2, 3], "image/jpeg", "card.jpg"), Cancel)));
        Assert.Contains("Upload a photo", await Problem(await manager.PutAsync(photo, Upload([0x47, 0x49, 0x46], "image/gif", "card.gif"), Cancel)));
        var big = new byte[HrPayroll.Domain.Employees.Employee.MaxIdPhotoBytes + 1];
        jpeg.CopyTo(big, 0);
        Assert.Contains("under 5 MB", await Problem(await manager.PutAsync(photo, Upload(big, "image/jpeg", "card.jpg"), Cancel)));

        var uploaded = await Json(await accountant.PutAsync(photo, Upload(jpeg, "image/jpeg", "card.jpg"), Cancel));
        var idPhoto = uploaded.GetProperty("idPhoto");
        Assert.Equal(("card.jpg", "image/jpeg", 8), (idPhoto.GetProperty("fileName").GetString(), idPhoto.GetProperty("contentType").GetString(), idPhoto.GetProperty("bytes").GetInt32()));
        Assert.False(string.IsNullOrEmpty(idPhoto.GetProperty("uploadedBy").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(photo, Cancel)).StatusCode);
        var shown = await accountant.GetAsync(photo, Cancel);
        Assert.Equal(("image/jpeg", "nosniff"), (shown.Content.Headers.ContentType!.MediaType, shown.Headers.GetValues("X-Content-Type-Options").Single()));
        Assert.Equal(jpeg, await shown.Content.ReadAsByteArrayAsync(Cancel));
        // The list of employees says there is one, without its bytes.
        var listed = Listed(await Json(await manager.GetAsync("/api/hr/employees", Cancel))).Single();
        Assert.Equal("card.jpg", listed.GetProperty("idPhoto").GetProperty("fileName").GetString());

        // Replaced by a scan; taken off by whoever manages HR.
        await Json(await manager.PutAsync(photo, Upload(pdf, "application/pdf", "scan.pdf"), Cancel));
        var downloaded = await manager.GetAsync($"{photo}?download=true", Cancel);
        Assert.Equal(("application/pdf", "scan.pdf"), (downloaded.Content.Headers.ContentType!.MediaType, downloaded.Content.Headers.ContentDisposition!.FileName));
        Assert.Equal(pdf, await downloaded.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.DeleteAsync(photo, Cancel)).StatusCode);
        var removed = await Json(await manager.DeleteAsync(photo, Cancel));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("idPhoto").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(photo, Cancel)).StatusCode);
        Assert.Contains("no photo of their ID card", await Problem(await manager.DeleteAsync(photo, Cancel)));
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync($"/api/hr/employees/{Guid.NewGuid()}/id-photo", Upload(jpeg, "image/jpeg", "card.jpg"), Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_days_labour_cost_each_departments_cost_and_who_forgot_to_check_out()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var day = today.AddDays(-2);
        var month = new DateOnly(day.Year, day.Month, 1);
        var kitchen = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/departments/{kitchen}", new { name = "Kitchen" }, Cancel));
        async Task<Guid> Hire(string name, string role, Guid? department, decimal salary, string type = "Monthly")
        {
            var id = Guid.NewGuid();
            await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{id}", new
            {
                requestId = Guid.NewGuid(), name, jobTitle = role, monthlySalary = type == "Monthly" ? salary : 0, dailyWage = type == "Daily" ? salary : 0,
                employmentType = type, departmentId = department, shiftStartsAt = "10:00:00", daysOff = Array.Empty<string>(), hiredOn = Iso(day.AddDays(-30)),
            }, Cancel));
            return id;
        }
        var hassan = await Hire("Hassan", "Chef", kitchen, 5_400);
        var samir = await Hire("Samir", "Helper", kitchen, 270, "Daily");
        var mona = await Hire("Mona", "Waiter", null, 4_050);
        var karim = await Hire("Karim", "Chef", kitchen, 6_000);
        await Json(await manager.PostAsJsonAsync($"/api/hr/employees/{karim}/leave", new { requestId = Guid.NewGuid(), lastDay = Iso(day.AddDays(-10)) }, Cancel));
        async Task Punch(Guid who, string time) => await Json(await manager.PostAsJsonAsync("/api/hr/punches", new
        {
            punchId = Guid.NewGuid(), employeeId = who, punchedAt = $"{Iso(day)}T{time}", reason = "Entered for the test",
        }, Cancel));
        // Hassan 10:00 to 21:00, two hours over; Samir in at 10:05 and never out; Mona not in.
        await Punch(hassan, "10:00:00");
        await Punch(hassan, "21:00:00");
        await Punch(samir, "10:05:00");

        // The day's review: Samir forgot to check out.
        var review = await Json(await accountant.GetAsync($"/api/hr/attendance?date={Iso(day)}", Cancel));
        Assert.Equal(1, review.GetProperty("totals").GetProperty("forgotToCheckOut").GetInt32());
        Assert.Equal(
            [("Hassan", false), ("Mona", false), ("Samir", true)],
            review.GetProperty("employees").EnumerateArray()
                .Select(e => (e.GetProperty("employee").GetProperty("name").GetString(), e.GetProperty("day").GetProperty("forgotToCheckOut").GetBoolean())));

        // The day's cost: Hassan at 180 a day and Samir at his 270, Mona not in; Hassan's two hours
        // over beside it, at 20 an hour.
        var cost = await Json(await accountant.GetAsync($"/api/hr/payroll/daily-cost?date={Iso(day)}", Cancel));
        Assert.Equal((3, 2, 450m, 40m), (cost.GetProperty("employed").GetInt32(), cost.GetProperty("worked").GetInt32(),
            cost.GetProperty("cost").GetDecimal(), cost.GetProperty("overtimePay").GetDecimal()));
        Assert.Equal(
            [("Hassan", 180m, 40m, false), ("Samir", 270m, 0m, true)],
            cost.GetProperty("employees").EnumerateArray().Select(e => (e.GetProperty("name").GetString(), e.GetProperty("dayRate").GetDecimal(),
                e.GetProperty("overtimePay").GetDecimal(), e.GetProperty("forgotToCheckOut").GetBoolean())));
        var byDepartment = cost.GetProperty("departments").EnumerateArray().Single();
        Assert.Equal(("Kitchen", 2, 450m), (byDepartment.GetProperty("name").GetString(), byDepartment.GetProperty("worked").GetInt32(),
            byDepartment.GetProperty("cost").GetDecimal()));
        var nobody = await Json(await accountant.GetAsync($"/api/hr/payroll/daily-cost?date={Iso(day.AddDays(-1))}", Cancel));
        Assert.Equal((0, 0m), (nobody.GetProperty("worked").GetInt32(), nobody.GetProperty("cost").GetDecimal()));

        // Each department's cost, and each role's: monthly salaries and daily wages apart, of those
        // employed now (Karim has left).
        var departments = (await Json(await accountant.GetAsync("/api/hr/departments", Cancel))).EnumerateArray().ToDictionary(d => d.GetProperty("name").GetString()!);
        var k = departments["Kitchen"];
        Assert.Equal((5_400m, 270m), (k.GetProperty("monthlyCost").GetDecimal(), k.GetProperty("dailyCost").GetDecimal()));
        Assert.Equal(
            [("Chef", 1, 5_400m, 0m), ("Helper", 1, 0m, 270m)],
            k.GetProperty("roles").EnumerateArray().Select(r => (r.GetProperty("name").GetString(), r.GetProperty("employed").GetInt32(),
                r.GetProperty("monthlyCost").GetDecimal(), r.GetProperty("dailyCost").GetDecimal())));
        Assert.Equal((4_050m, 0m), (departments["General"].GetProperty("monthlyCost").GetDecimal(), departments["General"].GetProperty("dailyCost").GetDecimal()));
        // An employee signed in to the portal, with no HR tab, sees none of it.
        var outsider = await api.OutsiderAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/hr/payroll/daily-cost?date={Iso(day)}", Cancel)).StatusCode);

        // The month's total salaries, before anything is deducted, and its administrative penalties.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/hr/employees/{hassan}/days/{Iso(day)}/penalty", new { hours = 1.5, reason = "Left the walk-in open" }, Cancel)).StatusCode);
        var payroll = await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel));
        var totals = payroll.GetProperty("totals");
        var slips = payroll.GetProperty("slips").EnumerateArray().Select(s => s.GetProperty("figures")).ToList();
        Assert.Equal(slips.Sum(f => f.GetProperty("gross").GetDecimal()), totals.GetProperty("gross").GetDecimal());
        Assert.True(totals.GetProperty("gross").GetDecimal() > 0);
        Assert.Equal(30m, totals.GetProperty("administrativePenalties").GetDecimal());
    }

    [Fact]
    public async Task Employees_come_in_from_an_Excel_sheet_and_go_out_to_one_with_the_payroll()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var outsider = await api.OutsiderAsync();
        var today = await TodayAsync(manager);
        await HireAsync(manager, "Karim", today.AddMonths(-2), "21");
        static byte[] Saved(XLWorkbook workbook)
        {
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        static MultipartFormDataContent Upload(byte[] bytes)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(EmployeeWorkbook.ContentType);
            return new MultipartFormDataContent { { file, "file", "employees.xlsx" } };
        }
        static void Fill(IXLWorksheet sheet, object[][] rows)
        {
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        }
        static IEnumerable<(int, string?)> Skipped(JsonElement result) =>
            result.GetProperty("skipped").EnumerateArray().Select(s => (s.GetProperty("row").GetInt32(), s.GetProperty("reason").GetString()));
        async Task<Dictionary<string, JsonElement>> Employees() =>
            (await Json(await manager.GetAsync("/api/hr/employees", Cancel))).EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);

        // The template: its headings, nothing on it to import by mistake, and the departments.
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/hr/employees/template", Cancel)).StatusCode);
        var download = await accountant.GetAsync("/api/hr/employees/template", Cancel);
        Assert.True(download.IsSuccessStatusCode);
        Assert.Equal((EmployeeWorkbook.ContentType, EmployeeWorkbook.TemplateFileName),
            (download.Content.Headers.ContentType!.MediaType, download.Content.Headers.ContentDisposition!.FileNameStar));
        using var template = new XLWorkbook(await download.Content.ReadAsStreamAsync(Cancel));
        var sheet = template.Worksheet(EmployeeWorkbook.DataSheetName);
        Assert.Equal(
            [EmployeeWorkbook.NameHeader, EmployeeWorkbook.TypeHeader, EmployeeWorkbook.PayHeader, EmployeeWorkbook.DepartmentHeader, EmployeeWorkbook.RoleHeader,
                EmployeeWorkbook.ShiftHeader, EmployeeWorkbook.DaysOffHeader, EmployeeWorkbook.HiredOnHeader, EmployeeWorkbook.DeviceHeader],
            sheet.Row(1).CellsUsed().Select(c => c.GetString()));
        Assert.True(sheet.Cell(2, 1).IsEmpty());
        Assert.Contains("General", template.Worksheet("Lists").Column(2).CellsUsed().Select(c => c.GetString()));

        // Filled in as people do: times and dates as cells or typed, Arabic words and digits, and mistakes.
        Fill(sheet, [
            ["Hassan Ali", "Monthly", 5400, "Kitchen", "Chef", new TimeSpan(10, 0, 0), "Friday", new DateTime(2026, 9, 1), "7"],
            ["منى سعيد", "يومي", "٢٥٠", "kitchen", "Helper", "مرن", "الجمعة والسبت", "1/10/2026", ""],
            ["hassan  ali", "Monthly", 5000, "", "Chef", "10:00", "", "2026-09-01", ""],
            ["Omar", "Monthly", 4000, "", "Waiter", "25:00", "", "2026-09-01", ""],
            ["Sara", "Monthly", 4000, "", "Waiter", "4 PM", "", "2026-09-01", "7"],
            ["Karim", "Monthly", 4000, "", "Waiter", "12:00", "", "2026-09-01", ""],
            ["Ali", "Monthly", 4000, "", "Waiter", "12:00", "Fri, Funday", "2026-09-01", ""],
            ["Nour", "Weekly", 4000, "", "Waiter", "12:00", "", "2026-09-01", ""],
            ["Youssef", "Monthly", 4000, "Service", "Waiter", "12:00", "", "", "33"],
        ]);
        var filled = Saved(template);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsync("/api/hr/employees/import", Upload(filled), Cancel)).StatusCode);
        var result = await Json(await manager.PostAsync("/api/hr/employees/import", Upload(filled), Cancel));
        Assert.Equal(
            [("Hassan Ali", "Kitchen", "Chef", false), ("منى سعيد", "Kitchen", "Helper", false)],
            result.GetProperty("imported").EnumerateArray().Select(e => (e.GetProperty("name").GetString(), e.GetProperty("department").GetString(),
                e.GetProperty("jobTitle").GetString(), e.GetProperty("requested").GetBoolean())));
        Assert.Equal(["Kitchen"], result.GetProperty("departmentsAdded").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal(
            [
                (4, "Hassan Ali is on the sheet twice: row 2 has them already."),
                (5, "Work schedule start: \"25:00\" is not a time of day. Write it as 10:00 or 16:30, or Flexible (مرن)."),
                (6, "Number 7 is on the sheet twice: row 2 has it already."),
                (7, "Karim is in the system already."),
                (8, "Days off: \"Funday\" is not a day of the week. Write them as Friday, Saturday or الجمعة، السبت."),
                (9, "Employment type: \"Weekly\" is neither Monthly nor Daily (شهري أو يومي)."),
                (10, "No start date: the day they started, which their first month's pay counts from."),
            ],
            Skipped(result));

        // On the list as the form would have made them.
        var employees = await Employees();
        var kitchen = (await Json(await manager.GetAsync("/api/hr/departments", Cancel))).EnumerateArray().Single(d => d.GetProperty("name").GetString() == "Kitchen");
        Assert.Equal(["Chef", "Helper"], kitchen.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        var hassan = employees["Hassan Ali"];
        Assert.Equal((kitchen.GetProperty("id").GetGuid(), "Chef", 5_400m, "10:00:00", "2026-09-01", "7"), (hassan.GetProperty("departmentId").GetGuid(),
            hassan.GetProperty("jobTitle").GetString(), hassan.GetProperty("monthlySalary").GetDecimal(), hassan.GetProperty("shiftStartsAt").GetString(),
            hassan.GetProperty("hiredOn").GetString(), hassan.GetProperty("deviceUserId").GetString()));
        Assert.Equal(["Friday"], hassan.GetProperty("daysOff").EnumerateArray().Select(d => d.GetString()));
        var mona = employees["منى سعيد"];
        Assert.Equal(("Daily", 250m, JsonValueKind.Null, "2026-10-01", JsonValueKind.Null), (mona.GetProperty("employmentType").GetString(),
            mona.GetProperty("dailyWage").GetDecimal(), mona.GetProperty("shiftStartsAt").ValueKind, mona.GetProperty("hiredOn").GetString(),
            mona.GetProperty("deviceUserId").ValueKind));
        Assert.Equal(["Friday", "Saturday"], mona.GetProperty("daysOff").EnumerateArray().Select(d => d.GetString()));

        // The same sheet again adds no one twice.
        var again = await Json(await manager.PostAsync("/api/hr/employees/import", Upload(filled), Cancel));
        Assert.Empty(again.GetProperty("imported").EnumerateArray());
        Assert.Contains((2, "Hassan Ali is in the system already."), Skipped(again));

        // An accountant's, headed in Arabic in an order of its own: each asked of a manager, and a
        // department not in the system left for a manager to add.
        using var arabic = new XLWorkbook();
        var theirs = arabic.Worksheets.Add("موظفين");
        string[] headings = ["الاسم", "القسم", "الوظيفة", "الراتب", "بداية الوردية", "تاريخ التعيين"];
        for (var i = 0; i < headings.Length; i++)
            theirs.Cell(1, i + 1).Value = headings[i];
        Fill(theirs, [["Samir", "Kitchen", "chef", 6000, "09:30", "2026-10-05"], ["Laila", "Bar", "Barista", 4500, "09:30", "2026-10-05"]]);
        var arabicFile = Saved(arabic);
        var asked = await Json(await accountant.PostAsync("/api/hr/employees/import", Upload(arabicFile), Cancel));
        Assert.Equal([("Samir", "Chef", true)], asked.GetProperty("imported").EnumerateArray()
            .Select(e => (e.GetProperty("name").GetString(), e.GetProperty("jobTitle").GetString(), e.GetProperty("requested").GetBoolean())));
        Assert.Equal([(3, "Bar is not a department in the system: a manager adds it on the Departments tab first.")], Skipped(asked));
        Assert.Empty(asked.GetProperty("departmentsAdded").EnumerateArray());
        Assert.False((await Employees()).ContainsKey("Samir"));
        Assert.Equal([(2, "Samir is asked for already, waiting for a manager's approval.")],
            Skipped(await Json(await accountant.PostAsync("/api/hr/employees/import", Upload(arabicFile), Cancel))).Take(1));
        var request = (await Json(await manager.GetAsync("/api/approval-requests", Cancel))).GetProperty("pending").EnumerateArray()
            .Single(r => r.GetProperty("module").GetString() == "Employee");
        Assert.Equal("New employee · Samir · Chef · 6,000.00 a month, from 09:30", request.GetProperty("summary").GetString());
        await Json(await manager.PostAsJsonAsync($"/api/approval-requests/{request.GetProperty("id").GetGuid()}/approve", new { }, Cancel));
        Assert.Equal(kitchen.GetProperty("roles")[0].GetProperty("id").GetGuid(), (await Employees())["Samir"].GetProperty("jobRoleId").GetGuid());

        // Everyone employed, exported in the template's columns: read back, it makes them again.
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/hr/employees/export", Cancel)).StatusCode);
        var export = await accountant.GetAsync("/api/hr/employees/export", Cancel);
        Assert.True(export.IsSuccessStatusCode);
        Assert.Equal($"employees-{Iso(today)}.xlsx", export.Content.Headers.ContentDisposition!.FileNameStar);
        using var exported = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync(Cancel)));
        var list = exported.Worksheet(EmployeeWorkbook.DataSheetName);
        Assert.Single(list.Tables);
        // The outsider is employed too: taken off the sheet.
        list.RowsUsed().Skip(1).Single(r => r.Cell(1).GetString() == TestApi.OutsiderName).Delete();
        var rows = list.RowsUsed().Skip(1).ToList();
        // By department, then name.
        Assert.Equal(["Karim", "Hassan Ali", "Samir", "منى سعيد"], rows.Select(r => r.Cell(1).GetString()));
        Assert.Equal(["Hassan Ali", "Monthly", "5,400.00", "Kitchen", "Chef", "10:00", "Friday", "2026-09-01", "7", "180.00", "20.00"],
            rows[1].Cells(1, 11).Select(c => c.GetFormattedString()));
        foreach (var row in rows)
        {
            row.Cell(1).Value = row.Cell(1).GetString() + " Jr";
            row.Cell(9).Value = string.Empty;
        }

        var copies = await Json(await manager.PostAsync("/api/hr/employees/import", Upload(Saved(exported)), Cancel));
        Assert.Empty(copies.GetProperty("skipped").EnumerateArray());
        Assert.Equal(4, copies.GetProperty("imported").GetArrayLength());
        var monaJr = (await Employees())["منى سعيد Jr"];
        Assert.Equal(("Daily", 250m, JsonValueKind.Null, "2026-10-01"), (monaJr.GetProperty("employmentType").GetString(), monaJr.GetProperty("dailyWage").GetDecimal(),
            monaJr.GetProperty("shiftStartsAt").ValueKind, monaJr.GetProperty("hiredOn").GetString()));
        Assert.Equal(["Friday", "Saturday"], monaJr.GetProperty("daysOff").EnumerateArray().Select(d => d.GetString()));

        // The month's payroll for the accounts: each slip's columns, and their totals.
        var month = new DateOnly(today.Year, today.Month, 1);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/hr/payroll/export?month={Iso(month)}", Cancel)).StatusCode);
        var payroll = await accountant.GetAsync($"/api/hr/payroll/export?month={Iso(month)}", Cancel);
        Assert.True(payroll.IsSuccessStatusCode);
        Assert.Equal($"payroll-{month:yyyy-MM}.xlsx", payroll.Content.Headers.ContentDisposition!.FileNameStar);
        using var report = new XLWorkbook(await payroll.Content.ReadAsStreamAsync(Cancel));
        var slips = report.Worksheet("Payroll");
        Assert.StartsWith("Payroll · مسير الرواتب — ", slips.Cell(1, 1).GetString());
        Assert.Equal("Employee · الموظف", slips.Cell(4, 1).GetString());
        var shown = await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel));
        var count = shown.GetProperty("slips").GetArrayLength();
        var net = Enumerable.Range(1, 21).Single(c => slips.Cell(4, c).GetString() == "Net payable · صافي المستحق");
        Assert.Equal(shown.GetProperty("totals").GetProperty("netPayable").GetDecimal(),
            Enumerable.Range(5, count).Sum(r => (decimal)slips.Cell(r, net).GetDouble()));
        Assert.Equal("Total · الإجمالي", slips.Cell(5 + count, 1).GetString());
        Assert.Contains("SUBTOTAL(109", slips.Cell(5 + count, net).FormulaA1);
    }

    [Fact]
    public async Task An_advance_is_owed_until_the_months_pay_and_an_accountants_waits_for_a_manager()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var outsider = await api.OutsiderAsync();
        var today = await TodayAsync(manager);
        var month = new DateOnly(today.Year, today.Month, 1);
        var hassan = await HireAsync(manager, "Hassan", month.AddMonths(-1));
        Task<HttpResponseMessage> Record(HttpClient client, Guid id, decimal amount, Guid? employee, string? notes = null) =>
            client.PostAsJsonAsync("/api/hr/advances", new { advanceId = id, amount, date = Iso(today), employeeId = employee, notes }, Cancel);
        async Task<decimal> Deducted() => (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel)))
            .GetProperty("slips")[0].GetProperty("figures").GetProperty("advances").GetDecimal();

        // A manager's at once, recorded once however often it is sent; refused to someone holding no tab.
        Assert.Equal(HttpStatusCode.Forbidden, (await Record(outsider, Guid.NewGuid(), 100, hassan)).StatusCode);
        var toHassan = Guid.NewGuid();
        var made = await Json(await Record(manager, toHassan, 500, hassan, "Rent"));
        Assert.Equal(("Hassan", Iso(month), "Boss", 500m), (made.GetProperty("employee").GetString(), made.GetProperty("payrollMonth").GetString(),
            made.GetProperty("recordedBy").GetString(), made.GetProperty("amount").GetDecimal()));
        await Json(await Record(manager, toHassan, 500, hassan, "Rent"));

        // Off this month's pay, on the slip.
        Assert.Equal(500m, await Deducted());
        var slip = (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel))).GetProperty("slips")[0];
        Assert.Equal(("Rent", false), (slip.GetProperty("advances")[0].GetProperty("notes").GetString(), slip.GetProperty("advances")[0].GetProperty("isShare").GetBoolean()));
        var allocated = (await Json(await accountant.GetAsync($"/api/hr/advances?month={Iso(month)}", Cancel))).GetProperty("allocated").EnumerateArray().Single();
        Assert.Equal(("Direct", toHassan), (allocated.GetProperty("origin").GetString(), allocated.GetProperty("advanceId").GetGuid()));

        // An accountant's waits for a manager, as their other HR entries do.
        var asked = await Record(accountant, Guid.NewGuid(), 200, hassan);
        Assert.True(asked.StatusCode == HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync(Cancel));
        var request = (await Json(asked)).GetProperty("request");
        Assert.Equal(("Advance", "Advance · Hassan · 200.00"), (request.GetProperty("module").GetString(), request.GetProperty("summary").GetString()));
        Assert.Equal(500m, await Deducted());
        await Json(await manager.PostAsJsonAsync($"/api/approval-requests/{request.GetProperty("id").GetGuid()}/approve", new { }, Cancel));
        Assert.Equal(700m, await Deducted());

        // Voided by a manager, while its month's pay is open.
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsync($"/api/hr/advances/{toHassan}/void", null, Cancel)).StatusCode);
        await Json(await manager.PostAsync($"/api/hr/advances/{toHassan}/void", null, Cancel));
        Assert.Equal(200m, await Deducted());
        Assert.Contains("already been voided", await Problem(await manager.PostAsync($"/api/hr/advances/{toHassan}/void", null, Cancel)));

        // The month is not over: its pay is not finalized yet.
        Assert.Contains("The month is not over", await Problem(await manager.PostAsync($"/api/hr/payroll/{hassan}/finalize?month={Iso(month)}", null, Cancel)));
    }

    [Fact]
    public async Task Advances_handed_out_with_no_one_named_wait_to_be_given_out_and_each_share_is_deducted()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var month = new DateOnly(today.Year, today.Month, 1);
        var hassan = await HireAsync(manager, "Hassan", month.AddMonths(-1));
        var mona = await HireAsync(manager, "Mona", month.AddMonths(-1));
        Task<HttpResponseMessage> Distribute(HttpClient client, Guid advance, params object[] shares) =>
            client.PostAsJsonAsync($"/api/hr/advances/{advance}/distribute", new { shares }, Cancel);
        async Task<JsonElement> Advances() => await Json(await accountant.GetAsync($"/api/hr/advances?month={Iso(month)}", Cancel));

        // A lump sum with no one named, and one paid to Hassan.
        var lump = Guid.NewGuid();
        await Json(await manager.PostAsJsonAsync("/api/hr/advances", new { advanceId = lump, amount = 1_000m, date = Iso(today), employeeId = (Guid?)null, notes = "Operations team" }, Cancel));
        await Json(await AdvanceAsync(manager, hassan, 150, today));
        // An accountant's lump sum waits for a manager, as their advances do.
        var asked = await accountant.PostAsJsonAsync("/api/hr/advances", new { advanceId = Guid.NewGuid(), amount = 400m, date = Iso(today), employeeId = (Guid?)null }, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.StartsWith("Advances not given out yet · 400.00", (await Json(asked)).GetProperty("request").GetProperty("summary").GetString());

        // HR's view: Hassan's direct advance allocated; the lump sum pending, all of it.
        var seen = await Advances();
        Assert.Equal([("Direct", "Hassan", 150m)], seen.GetProperty("allocated").EnumerateArray()
            .Select(a => (a.GetProperty("origin").GetString(), a.GetProperty("employee").GetString(), a.GetProperty("amount").GetDecimal())));
        var pending = seen.GetProperty("pending").EnumerateArray().Single();
        Assert.Equal((lump, 1_000m, 0m, 1_000m, false, "Operations team"), (pending.GetProperty("id").GetGuid(), pending.GetProperty("amount").GetDecimal(),
            pending.GetProperty("allocated").GetDecimal(), pending.GetProperty("remaining").GetDecimal(), pending.GetProperty("fullyAllocated").GetBoolean(),
            pending.GetProperty("notes").GetString()));

        // Given out by a manager, in parts, never more than is left, one share each.
        Assert.Equal(HttpStatusCode.Forbidden, (await Distribute(accountant, lump, new { employeeId = hassan, amount = 500 })).StatusCode);
        var part = await Json(await Distribute(manager, lump, new { employeeId = hassan, amount = 500, notes = "Night shift" }));
        Assert.Equal((500m, 500m), (part.GetProperty("allocated").GetDecimal(), part.GetProperty("remaining").GetDecimal()));
        Assert.Contains("500.00 of these advances is left to give out, and the shares come to 600.00",
            await Problem(await Distribute(manager, lump, new { employeeId = mona, amount = 600 })));
        Assert.Contains("Mona is on the list twice", await Problem(await Distribute(manager, lump,
            new { employeeId = mona, amount = 100 }, new { employeeId = mona, amount = 100 })));
        Assert.Contains("Only advances handed out with no one named",
            await Problem(await Distribute(manager, seen.GetProperty("allocated")[0].GetProperty("id").GetGuid(), new { employeeId = mona, amount = 10 })));
        var all = await Json(await Distribute(manager, lump, new { employeeId = mona, amount = 500 }));
        Assert.Equal((0m, true, 2), (all.GetProperty("remaining").GetDecimal(), all.GetProperty("fullyAllocated").GetBoolean(), all.GetProperty("shares").GetArrayLength()));
        Assert.Contains("all given out already", await Problem(await Distribute(manager, lump, new { employeeId = mona, amount = 1 })));

        // Each share deducted from its employee's pay of the month, on their slip.
        var slips = (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel))).GetProperty("slips").EnumerateArray()
            .ToDictionary(s => s.GetProperty("employee").GetProperty("name").GetString()!);
        Assert.Equal((650m, 500m), (slips["Hassan"].GetProperty("figures").GetProperty("advances").GetDecimal(),
            slips["Mona"].GetProperty("figures").GetProperty("advances").GetDecimal()));
        Assert.Equal(2, slips["Hassan"].GetProperty("advances").GetArrayLength());
        Assert.StartsWith("Share of the advances handed out on", slips["Mona"].GetProperty("advances")[0].GetProperty("notes").GetString());
        Assert.True(slips["Mona"].GetProperty("advances")[0].GetProperty("isShare").GetBoolean());
        seen = await Advances();
        Assert.Equal(3, seen.GetProperty("allocated").GetArrayLength());
        Assert.True(seen.GetProperty("pending")[0].GetProperty("fullyAllocated").GetBoolean());

        // Not voided while shares of it stand; a share taken back goes back to be given out.
        Assert.Contains("take those shares back first", await Problem(await manager.PostAsync($"/api/hr/advances/{lump}/void", null, Cancel)));
        var monasShare = all.GetProperty("shares").EnumerateArray().Single(s => s.GetProperty("employee").GetString() == "Mona").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsync($"/api/hr/advance-shares/{monasShare}/take-back", null, Cancel)).StatusCode);
        var back = await Json(await manager.PostAsync($"/api/hr/advance-shares/{monasShare}/take-back", null, Cancel));
        Assert.Equal((500m, 1), (back.GetProperty("remaining").GetDecimal(), back.GetProperty("shares").GetArrayLength()));
        Assert.Equal(0m, (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel))).GetProperty("slips").EnumerateArray()
            .Single(s => s.GetProperty("employee").GetProperty("name").GetString() == "Mona").GetProperty("figures").GetProperty("advances").GetDecimal());
    }

    [Fact]
    public async Task A_months_pay_is_finalized_with_its_advances_and_paid_by_salary_payments()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lastDay = lastMonth.AddMonths(1).AddDays(-1);
        var days = DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month);
        // Started on the month's last day, which was leave: a day's share of the salary, nothing absent.
        var hassan = await HireAsync(manager, "Hassan", lastDay);
        await Json(await AdvanceAsync(manager, hassan, 50, lastDay));
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PutAsJsonAsync($"/api/hr/employees/{hassan}/days/{Iso(lastDay)}", new { kind = "Leave" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}/days/{Iso(lastDay)}", new { kind = "Leave", notes = "Wedding" }, Cancel)).StatusCode);
        await Json(await manager.PostAsJsonAsync("/api/hr/payroll/adjustments", new
        {
            adjustmentId = Guid.NewGuid(), employeeId = hassan, month = Iso(lastMonth), kind = "Incentive", amount = 100, reason = "First day",
        }, Cancel));

        var share = Math.Round(5_400m / days, 2, MidpointRounding.AwayFromZero);
        var draft = (await Json(await manager.PostAsync($"/api/hr/payroll/calculate?month={Iso(lastMonth)}", null, Cancel))).GetProperty("slips")[0];
        var figures = draft.GetProperty("figures");
        Assert.Equal(("Draft", 1, share, 100m, 50m, 0, 0), (draft.GetProperty("status").GetString(), figures.GetProperty("daysEmployed").GetInt32(),
            figures.GetProperty("baseSalary").GetDecimal(), figures.GetProperty("incentives").GetDecimal(), figures.GetProperty("advances").GetDecimal(),
            figures.GetProperty("absentDays").GetInt32(), figures.GetProperty("openDays").GetInt32()));

        // Paid only once finalized.
        object Payment(decimal amount) => new { paymentId = Guid.NewGuid(), employeeId = hassan, month = Iso(lastMonth), date = Iso(today), amount, notes = "Transfer" };
        Assert.Contains("once it is finalized", await Problem(await manager.PostAsJsonAsync("/api/hr/payroll/payments", Payment(10), Cancel)));

        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsync($"/api/hr/payroll/{hassan}/finalize?month={Iso(lastMonth)}", null, Cancel)).StatusCode);
        var slip = await Json(await manager.PostAsync($"/api/hr/payroll/{hassan}/finalize?month={Iso(lastMonth)}", null, Cancel));
        var net = share + 100 - 50;
        Assert.Equal(("Finalized", net, net), (slip.GetProperty("status").GetString(), slip.GetProperty("figures").GetProperty("netPayable").GetDecimal(),
            slip.GetProperty("salaryOwed").GetDecimal()));

        // Finalized: its days stay, and an advance made later counts next month.
        Assert.Contains("is finalized", await Problem(await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}/days/{Iso(lastDay)}", new { kind = (string?)null }, Cancel)));
        var later = await Json(await AdvanceAsync(manager, hassan, 70, lastDay));
        Assert.Equal(Iso(lastMonth.AddMonths(1)), later.GetProperty("payrollMonth").GetString());

        // Its net paid: no more than is owed; a payment voided is owed again.
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync("/api/hr/payroll/payments", Payment(10), Cancel)).StatusCode);
        Assert.Contains("pay that much or less", await Problem(await manager.PostAsJsonAsync("/api/hr/payroll/payments", Payment(net + 1), Cancel)));
        var paid = await Json(await manager.PostAsJsonAsync("/api/hr/payroll/payments", Payment(net), Cancel));
        Assert.Contains("paid in full", await Problem(await manager.PostAsJsonAsync("/api/hr/payroll/payments", Payment(1), Cancel)));
        var settled = (await Json(await manager.GetAsync($"/api/hr/payroll?month={Iso(lastMonth)}", Cancel))).GetProperty("slips")[0];
        Assert.Equal((net, 0m, "Transfer"), (settled.GetProperty("salaryPaid").GetDecimal(), settled.GetProperty("salaryOwed").GetDecimal(),
            settled.GetProperty("salaries")[0].GetProperty("notes").GetString()));
        await Json(await manager.PostAsync($"/api/hr/payroll/payments/{paid.GetProperty("id").GetGuid()}/void", null, Cancel));
        settled = (await Json(await manager.GetAsync($"/api/hr/payroll?month={Iso(lastMonth)}", Cancel))).GetProperty("slips")[0];
        Assert.Equal((0m, net), (settled.GetProperty("salaryPaid").GetDecimal(), settled.GetProperty("salaryOwed").GetDecimal()));
    }

    [Fact]
    public async Task A_suspended_day_waits_for_the_managers_punch_out_and_a_penalty_comes_off_the_pay()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var day = today.AddDays(-3);
        var month = new DateOnly(day.Year, day.Month, 1);
        var hassan = await HireAsync(manager, "Hassan", today.AddMonths(-2));
        await Json(await manager.PostAsJsonAsync("/api/hr/punches", new
        {
            punchId = Guid.NewGuid(), employeeId = hassan, punchedAt = $"{Iso(day)}T10:00:00", reason = "The device was down",
        }, Cancel));

        // In at 10:00, never out: three days on, suspended, and listed for a manager.
        string Suspended = $"/api/hr/attendance/suspended?from={Iso(day.AddDays(-1))}&to={Iso(today)}";
        var waiting = (await Json(await accountant.GetAsync(Suspended, Cancel))).EnumerateArray().Single();
        Assert.Equal(("Hassan", Iso(day), "Suspended"), (waiting.GetProperty("employee").GetProperty("name").GetString(),
            waiting.GetProperty("day").GetProperty("date").GetString(), waiting.GetProperty("day").GetProperty("status").GetString()));

        // Resolved by the punch-out: after the punch-in, by a manager.
        Task<HttpResponseMessage> Resolve(HttpClient client, string time) => client.PostAsJsonAsync("/api/hr/attendance/resolve", new
        {
            punchId = Guid.NewGuid(), employeeId = hassan, date = Iso(day), punchedOutAt = $"{Iso(day)}T{time}", reason = "Forgot to scan out",
        }, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, (await Resolve(accountant, "19:00:00")).StatusCode);
        Assert.Contains("comes after the punch-in at 10:00", await Problem(await Resolve(manager, "09:30:00")));
        var resolved = await Json(await Resolve(manager, "19:00:00"));
        Assert.Equal(("Worked", 540), (resolved.GetProperty("status").GetString(), resolved.GetProperty("workedMinutes").GetInt32()));
        Assert.Contains("is not suspended", await Problem(await Resolve(manager, "20:00:00")));
        Assert.Empty((await Json(await accountant.GetAsync(Suspended, Cancel))).EnumerateArray());

        // Two hours' administrative penalty, a manager's: on the day, and off the pay at 20 an hour.
        string Penalty = $"/api/hr/employees/{hassan}/days/{Iso(day)}/penalty";
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PutAsJsonAsync(Penalty, new { hours = 2, reason = "Left the walk-in open" }, Cancel)).StatusCode);
        Assert.Contains("Say why", await Problem(await manager.PutAsJsonAsync(Penalty, new { hours = 2 }, Cancel)));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(Penalty, new { hours = 2, reason = "Left the walk-in open" }, Cancel)).StatusCode);
        var marked = (await Json(await accountant.GetAsync($"/api/hr/attendance?date={Iso(day)}", Cancel))).GetProperty("employees")[0].GetProperty("day");
        Assert.Equal((2m, "Left the walk-in open"), (marked.GetProperty("penaltyHours").GetDecimal(), marked.GetProperty("penaltyReason").GetString()));
        async Task<JsonElement> Figures() => (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel))).GetProperty("slips")[0].GetProperty("figures");
        var figures = await Figures();
        Assert.Equal((2m, 40m), (figures.GetProperty("administrativePenaltyHours").GetDecimal(), figures.GetProperty("administrativePenalty").GetDecimal()));

        // And on the month's list of penalties, with what it takes off.
        string Penalties = $"/api/hr/penalties?month={Iso(month)}";
        var listed = (await Json(await accountant.GetAsync(Penalties, Cancel))).EnumerateArray().Single();
        Assert.Equal(("Hassan", Iso(day), 2m, 40m, "Left the walk-in open"), (listed.GetProperty("employeeName").GetString(), listed.GetProperty("date").GetString(),
            listed.GetProperty("hours").GetDecimal(), listed.GetProperty("amount").GetDecimal(), listed.GetProperty("reason").GetString()));

        // A disciplinary action's penalty deduction on the same day: its own, beside the manager's
        // hours, in money, off the same pay.
        var deduction = new HrPayroll.Domain.Performance.DisciplinaryAction(
            Guid.NewGuid(), hassan, day, "Absence", HrPayroll.Domain.Performance.SeverityLevel.PenaltyDeduction, null, 250, "boss", "Boss");
        await api.Db(db =>
        {
            db.DisciplinaryActions.Add(deduction);
            db.AdministrativePenalties.Add(deduction.Penalty()!);
            return db.SaveChangesAsync();
        });
        figures = await Figures();
        Assert.Equal((2m, 250m, 290m), (figures.GetProperty("administrativePenaltyHours").GetDecimal(), figures.GetProperty("administrativePenaltyAmounts").GetDecimal(),
            figures.GetProperty("administrativePenalty").GetDecimal()));
        // The slip lists both, the action's linked to it.
        var lines = (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel))).GetProperty("slips")[0].GetProperty("penalties")
            .EnumerateArray().Select(p => (p.GetProperty("hours").GetDecimal(), p.GetProperty("amount").ValueKind == JsonValueKind.Null ? (decimal?)null : p.GetProperty("amount").GetDecimal(),
                p.GetProperty("disciplinaryActionId").ValueKind == JsonValueKind.Null ? (Guid?)null : p.GetProperty("disciplinaryActionId").GetGuid()))
            .OrderBy(l => l.Item1).ToList();
        Assert.Equal([(0m, (decimal?)250m, (Guid?)deduction.Id), (2m, (decimal?)null, (Guid?)null)], lines);
        marked = (await Json(await accountant.GetAsync($"/api/hr/attendance?date={Iso(day)}", Cancel))).GetProperty("employees")[0].GetProperty("day");
        Assert.Equal((2m, 250m, "Left the walk-in open"), (marked.GetProperty("penaltyHours").GetDecimal(), marked.GetProperty("penaltyAmount").GetDecimal(),
            marked.GetProperty("penaltyReason").GetString()));
        var both = (await Json(await accountant.GetAsync(Penalties, Cancel))).EnumerateArray().ToList();
        var fromAction = both.Single(p => p.GetProperty("disciplinaryActionId").ValueKind != JsonValueKind.Null);
        Assert.Equal((0m, 250m, "Penalty deduction: Absence", deduction.Id), (fromAction.GetProperty("hours").GetDecimal(), fromAction.GetProperty("amount").GetDecimal(),
            fromAction.GetProperty("reason").GetString(), fromAction.GetProperty("disciplinaryActionId").GetGuid()));

        // The manager's own taken off again: the deduction stays.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(Penalty, new { hours = (decimal?)null }, Cancel)).StatusCode);
        Assert.Equal(250m, (await Figures()).GetProperty("administrativePenalty").GetDecimal());
        Assert.Equal([250m], (await Json(await accountant.GetAsync(Penalties, Cancel))).EnumerateArray().Select(p => p.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task A_public_holiday_is_everyones_paid_day_off_and_worked_earns_a_days_pay()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var day = today.AddDays(-3);
        var month = new DateOnly(day.Year, day.Month, 1);
        var hassan = await HireAsync(manager, "Hassan", today.AddMonths(-2));
        await HireAsync(manager, "Mona", today.AddMonths(-2));

        // A manager's to enter, one holiday to a day.
        object Holiday(string name, DateOnly first, DateOnly last) => new { name, firstDay = Iso(first), lastDay = Iso(last) };
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PutAsJsonAsync($"/api/hr/holidays/{id}", Holiday("Armed Forces Day", day, day), Cancel)).StatusCode);
        var added = await Json(await manager.PutAsJsonAsync($"/api/hr/holidays/{id}", Holiday("Armed Forces Day", day, day), Cancel));
        Assert.Equal(("Armed Forces Day", 1), (added.GetProperty("name").GetString(), added.GetProperty("days").GetInt32()));
        Assert.Contains("already", await Problem(await manager.PutAsJsonAsync($"/api/hr/holidays/{Guid.NewGuid()}", Holiday("Eid", day.AddDays(-1), day), Cancel)));
        var listed = (await Json(await accountant.GetAsync($"/api/hr/holidays?year={day.Year}", Cancel))).EnumerateArray().Single();
        Assert.Equal(Iso(day), listed.GetProperty("firstDay").GetString());

        // Hassan works it, half an hour late: measured against his shift, and a day's pay on top.
        foreach (var time in new[] { "10:30:00", "19:00:00" })
        {
            await Json(await manager.PostAsJsonAsync("/api/hr/punches", new
            {
                punchId = Guid.NewGuid(), employeeId = hassan, punchedAt = $"{Iso(day)}T{time}", reason = "Holiday shift",
            }, Cancel));
        }

        async Task<Dictionary<string, JsonElement>> Days() => (await Json(await accountant.GetAsync($"/api/hr/attendance?date={Iso(day)}", Cancel)))
            .GetProperty("employees").EnumerateArray().ToDictionary(e => e.GetProperty("employee").GetProperty("name").GetString()!, e => e.GetProperty("day"));
        var days = await Days();
        Assert.Equal(("WorkedOnHoliday", 30, "Armed Forces Day"), (days["Hassan"].GetProperty("status").GetString(),
            days["Hassan"].GetProperty("lateMinutes").GetInt32(), days["Hassan"].GetProperty("holidayName").GetString()));
        // Mona stays home: a holiday, not an absence.
        Assert.Equal("Holiday", days["Mona"].GetProperty("status").GetString());
        var slip = (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel))).GetProperty("slips").EnumerateArray()
            .Single(s => s.GetProperty("employee").GetProperty("name").GetString() == "Hassan").GetProperty("figures");
        Assert.Equal((1, 180m), (slip.GetProperty("holidaysWorked").GetInt32(), slip.GetProperty("holidaysWorkedPay").GetDecimal()));

        // Taken off, a manager's: the day is a working day again.
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.DeleteAsync($"/api/hr/holidays/{id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/hr/holidays/{id}", Cancel)).StatusCode);
        Assert.Equal("Absent", (await Days())["Mona"].GetProperty("status").GetString());

        // A month whose pay is finalized keeps its holidays as they were.
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lastDay = lastMonth.AddMonths(1).AddDays(-1);
        var ali = await HireAsync(manager, "Ali", lastDay);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/hr/employees/{ali}/days/{Iso(lastDay)}", new { kind = "Leave" }, Cancel)).StatusCode);
        await Json(await manager.PostAsync($"/api/hr/payroll/{ali}/finalize?month={Iso(lastMonth)}", null, Cancel));
        Assert.Contains("is finalized", await Problem(await manager.PutAsJsonAsync($"/api/hr/holidays/{Guid.NewGuid()}", Holiday("Late one", lastDay, lastDay), Cancel)));
    }

    [Fact]
    public async Task Departments_are_a_managers_and_a_daily_worker_is_paid_for_the_days_worked()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        async Task<Dictionary<string, JsonElement>> Departments() =>
            (await Json(await accountant.GetAsync("/api/hr/departments", Cancel))).EnumerateArray().ToDictionary(d => d.GetProperty("name").GetString()!);

        // General is there from the start; the others are a manager's to add, named once.
        var general = (await Departments())["General"];
        Assert.True(general.GetProperty("isGeneral").GetBoolean());
        var kitchen = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PutAsJsonAsync($"/api/hr/departments/{kitchen}", new { name = "Kitchen" }, Cancel)).StatusCode);
        await Json(await manager.PutAsJsonAsync($"/api/hr/departments/{kitchen}", new { name = "Kitchen" }, Cancel));
        Assert.Contains("already", await Problem(await manager.PutAsJsonAsync($"/api/hr/departments/{Guid.NewGuid()}", new { name = " kitchen " }, Cancel)));

        // A daily worker in the kitchen: 300 a day, no salary.
        var day = today.AddDays(-3);
        var month = new DateOnly(day.Year, day.Month, 1);
        var helper = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{helper}", new
        {
            requestId = Guid.NewGuid(), name = "Samir", jobTitle = "Kitchen helper", monthlySalary = 0, employmentType = "Daily", dailyWage = 300,
            departmentId = kitchen, shiftStartsAt = "10:00:00", daysOff = Array.Empty<string>(), hiredOn = Iso(today.AddMonths(-2)),
        }, Cancel));
        var samir = (await Json(await manager.GetAsync("/api/hr/employees", Cancel))).EnumerateArray().Single();
        Assert.Equal(("Daily", 300m, 0m, kitchen, 300m), (samir.GetProperty("employmentType").GetString(), samir.GetProperty("dailyWage").GetDecimal(),
            samir.GetProperty("monthlySalary").GetDecimal(), samir.GetProperty("departmentId").GetGuid(), samir.GetProperty("dailyRate").GetDecimal()));
        Assert.Equal((1, 1), ((await Departments())["Kitchen"].GetProperty("employed").GetInt32(), (await Departments())["Kitchen"].GetProperty("everyone").GetInt32()));

        // One day worked: a day's wage, and every day not worked costs nothing.
        foreach (var time in new[] { "10:00:00", "19:00:00" })
        {
            await Json(await manager.PostAsJsonAsync("/api/hr/punches", new
            {
                punchId = Guid.NewGuid(), employeeId = helper, punchedAt = $"{Iso(day)}T{time}", reason = "Device down",
            }, Cancel));
        }

        var figures = (await Json(await accountant.GetAsync($"/api/hr/payroll?month={Iso(month)}", Cancel))).GetProperty("slips")[0].GetProperty("figures");
        Assert.Equal(("Daily", 1, 300m, 0, 0m), (figures.GetProperty("employmentType").GetString(), figures.GetProperty("daysWorked").GetInt32(),
            figures.GetProperty("baseSalary").GetDecimal(), figures.GetProperty("absentDays").GetInt32(), figures.GetProperty("absencePenalty").GetDecimal()));

        // The kitchen deleted: its people moved first, to a department that stays; General stays.
        Assert.Contains("say which department", await Problem(await manager.DeleteAsync($"/api/hr/departments/{kitchen}", Cancel)));
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.DeleteAsync($"/api/hr/departments/{kitchen}?moveTo={Department.GeneralId}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/hr/departments/{kitchen}?moveTo={Department.GeneralId}", Cancel)).StatusCode);
        Assert.Equal(Department.GeneralId, (await Json(await manager.GetAsync("/api/hr/employees", Cancel))).EnumerateArray().Single().GetProperty("departmentId").GetGuid());
        Assert.Contains("General stays", await Problem(await manager.DeleteAsync($"/api/hr/departments/{Department.GeneralId}", Cancel)));
    }

    [Fact]
    public async Task Job_roles_are_a_departments_and_the_employees_titles_follow_them()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        async Task<JsonElement> DepartmentNamed(string name) =>
            (await Json(await accountant.GetAsync("/api/hr/departments", Cancel))).EnumerateArray().Single(d => d.GetProperty("name").GetString() == name);
        async Task<JsonElement> Employee(Guid id) =>
            (await Json(await accountant.GetAsync("/api/hr/employees", Cancel))).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id);

        // A title typed with no role becomes the department's role of that name.
        var hassan = await HireAsync(manager, "Hassan", today.AddMonths(-2));
        var chef = (await DepartmentNamed("General")).GetProperty("roles").EnumerateArray().Single();
        Assert.Equal(("Chef", 1), (chef.GetProperty("name").GetString(), chef.GetProperty("employed").GetInt32()));
        Assert.Equal(chef.GetProperty("id").GetGuid(), (await Employee(hassan)).GetProperty("jobRoleId").GetGuid());

        // Roles are a manager's to add, one name to a department.
        var kitchen = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/departments/{kitchen}", new { name = "Kitchen" }, Cancel));
        var helper = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PutAsJsonAsync($"/api/hr/departments/{kitchen}/roles/{helper}", new { name = "Helper" }, Cancel)).StatusCode);
        await Json(await manager.PutAsJsonAsync($"/api/hr/departments/{kitchen}/roles/{helper}", new { name = "Helper" }, Cancel));
        Assert.Contains("already", await Problem(await manager.PutAsJsonAsync($"/api/hr/departments/{kitchen}/roles/{Guid.NewGuid()}", new { name = " helper " }, Cancel)));

        // A role is one of the employee's own department's.
        object Details(Guid department, Guid role, string title) => new
        {
            requestId = Guid.NewGuid(), name = "Hassan", jobTitle = title, monthlySalary = 5_400, departmentId = department, jobRoleId = role, shiftStartsAt = "10:00:00",
            daysOff = Array.Empty<string>(), hiredOn = Iso(today.AddMonths(-2)),
        };
        Assert.Contains("another department's", await Problem(await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}", Details(Department.GeneralId, helper, "Helper"), Cancel)));
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}", Details(kitchen, helper, "Helper"), Cancel));
        Assert.Equal(("Helper", kitchen), ((await Employee(hassan)).GetProperty("jobTitle").GetString(), (await Employee(hassan)).GetProperty("departmentId").GetGuid()));

        // Renamed for whoever holds it.
        await Json(await manager.PutAsJsonAsync($"/api/hr/departments/{kitchen}/roles/{helper}", new { name = "Kitchen helper" }, Cancel));
        Assert.Equal("Kitchen helper", (await Employee(hassan)).GetProperty("jobTitle").GetString());

        // Deleted once its holders take another of the department's.
        var cook = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/departments/{kitchen}/roles/{cook}", new { name = "Cook" }, Cancel));
        Assert.Contains("say which role", await Problem(await manager.DeleteAsync($"/api/hr/roles/{helper}", Cancel)));
        Assert.Contains("same department's", await Problem(await manager.DeleteAsync($"/api/hr/roles/{helper}?moveTo={chef.GetProperty("id").GetGuid()}", Cancel)));
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.DeleteAsync($"/api/hr/roles/{helper}?moveTo={cook}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/hr/roles/{helper}?moveTo={cook}", Cancel)).StatusCode);
        Assert.Equal(("Cook", cook), ((await Employee(hassan)).GetProperty("jobTitle").GetString(), (await Employee(hassan)).GetProperty("jobRoleId").GetGuid()));

        // The kitchen deleted: its people to General, its Cook role with them.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/hr/departments/{kitchen}?moveTo={Department.GeneralId}", Cancel)).StatusCode);
        var general = await DepartmentNamed("General");
        Assert.Equal(["Chef", "Cook"], general.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString()).Order());
        Assert.Equal((Department.GeneralId, cook, "Cook"), ((await Employee(hassan)).GetProperty("departmentId").GetGuid(),
            (await Employee(hassan)).GetProperty("jobRoleId").GetGuid(), (await Employee(hassan)).GetProperty("jobTitle").GetString()));
    }

    [Fact]
    public async Task Flexible_hours_have_no_shift_to_be_late_for_and_overtime_past_nine_hours()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var today = await TodayAsync(manager);
        var day = today.AddDays(-3);
        var mona = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{mona}", new
        {
            requestId = Guid.NewGuid(), name = "Mona", jobTitle = "Waiter", monthlySalary = 4_050, shiftStartsAt = (string?)null, daysOff = Array.Empty<string>(),
            hiredOn = Iso(today.AddMonths(-2)),
        }, Cancel));
        var employee = (await Json(await manager.GetAsync("/api/hr/employees", Cancel))).EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, employee.GetProperty("shiftStartsAt").ValueKind);

        // In at 11:30, out at 01:00 the next morning: one day, the business day's, and four and a half
        // hours past nine; nothing late.
        foreach (var at in new[] { $"{Iso(day)}T11:30:00", $"{Iso(day.AddDays(1))}T01:00:00" })
        {
            await Json(await manager.PostAsJsonAsync("/api/hr/punches", new { punchId = Guid.NewGuid(), employeeId = mona, punchedAt = at, reason = "Device down" }, Cancel));
        }

        var worked = (await Json(await manager.GetAsync($"/api/hr/attendance?date={Iso(day)}", Cancel))).GetProperty("employees")[0].GetProperty("day");
        Assert.Equal(("Worked", 810, 0, 0, 270), (worked.GetProperty("status").GetString(), worked.GetProperty("workedMinutes").GetInt32(),
            worked.GetProperty("lateMinutes").GetInt32(), worked.GetProperty("earlyLeaveMinutes").GetInt32(), worked.GetProperty("overtimeMinutes").GetInt32()));
        Assert.Equal((JsonValueKind.Null, $"{Iso(day.AddDays(1))}T04:00:00"), (worked.GetProperty("shiftStartsAt").ValueKind, worked.GetProperty("dayEndsAt").GetString()));
        // A working day with no punches is an absence still.
        Assert.Equal("Absent", (await Json(await manager.GetAsync($"/api/hr/attendance?date={Iso(day.AddDays(-1))}", Cancel)))
            .GetProperty("employees")[0].GetProperty("day").GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_device_is_asked_for_its_users_and_its_queued_punches_come_in_bulk_and_late()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var device = api.Anonymous();
        var today = await TodayAsync(manager);
        const string Serial = "CQZ7232060112";
        await device.GetAsync($"/iclock/cdata?SN={Serial}&options=all", Cancel);
        var deviceId = (await Json(await manager.GetAsync("/api/hr/devices", Cancel))).EnumerateArray().Single().GetProperty("id").GetGuid();
        await Json(await manager.PostAsJsonAsync($"/api/hr/devices/{deviceId}/approve", new { name = "Kitchen door" }, Cancel));
        Task<string> Poll() => device.GetStringAsync($"/iclock/getrequest?SN={Serial}", Cancel);
        Task<HttpResponseMessage> Post(string path, string body) => device.PostAsync(path, new StringContent(body, Encoding.UTF8, "text/plain"), Cancel);
        Assert.Equal("OK", await Poll());

        // A manager's command, handed to the device once when it next asks.
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsync($"/api/hr/devices/{deviceId}/read-users", null, Cancel)).StatusCode);
        var asked = await Json(await manager.PostAsync($"/api/hr/devices/{deviceId}/read-users", null, Cancel));
        Assert.Equal((1, "ReadUsers"), (asked.GetProperty("number").GetInt32(), asked.GetProperty("kind").GetString()));
        Assert.Equal(1, (await Json(await manager.PostAsync($"/api/hr/devices/{deviceId}/read-users", null, Cancel))).GetProperty("number").GetInt32());
        Assert.Equal("C:1:DATA QUERY USERINFO", await Poll());
        Assert.Equal("OK", await Poll());

        // Its users: numbers and names kept, never passwords, cards or fingerprints.
        var users = string.Join("\r\n", "PIN=7\tName=Hassan\tPri=0\tPasswd=1234\tCard=99887766\tGrp=1", "PIN=12\tName=منى\tPri=0\tPasswd=", "FP PIN=7\tFID=6\tSize=1024\tTMP=AAAA");
        Assert.Equal("OK:3", await (await Post($"/iclock/cdata?SN={Serial}&table=USERINFO", users)).Content.ReadAsStringAsync(Cancel));
        Assert.Equal("OK", await (await Post($"/iclock/devicecmd?SN={Serial}", "ID=1&Return=0&CMD=DATA")).Content.ReadAsStringAsync(Cancel));
        var command = (await Json(await manager.GetAsync("/api/hr/devices", Cancel))).EnumerateArray().Single().GetProperty("commands")[0];
        Assert.Equal(0, command.GetProperty("returnCode").GetInt32());
        async Task<Dictionary<string, JsonElement>> DeviceUsers() =>
            (await Json(await accountant.GetAsync("/api/hr/device-users", Cancel))).EnumerateArray().ToDictionary(u => u.GetProperty("deviceUserId").GetString()!);
        var listed = await DeviceUsers();
        Assert.Equal(("Hassan", "منى"), (listed["7"].GetProperty("name").GetString(), listed["12"].GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, listed["7"].GetProperty("employeeId").ValueKind);
        Assert.DoesNotContain("1234", listed["7"].GetRawText());

        // Weeks away from the network: six thousand punches at once, all kept, and once when sent again.
        var start = today.AddDays(-30).ToDateTime(new TimeOnly(0, 0));
        var queued = string.Join("\r\n", Enumerable.Range(0, 6_000).Select(i =>
            $"{(i % 2 == 0 ? "7" : "12")}\t{start.AddMinutes(i * 3).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}\t0\t1\t0\t0\t0"));
        foreach (var _ in Enumerable.Range(0, 2))
            Assert.Equal("OK:6000", await (await Post($"/iclock/cdata?SN={Serial}&table=ATTLOG&Stamp=900", queued)).Content.ReadAsStringAsync(Cancel));
        listed = await DeviceUsers();
        Assert.Equal((3_000, 3_000), (listed["7"].GetProperty("waitingPunches").GetInt32(), listed["12"].GetProperty("waitingPunches").GetInt32()));

        // Number 7 given to an employee: theirs now, and listed against them.
        await HireAsync(manager, "Hassan", today.AddMonths(-2), "7");
        listed = await DeviceUsers();
        Assert.Equal(("Hassan", 0), (listed["7"].GetProperty("employeeName").GetString(), listed["7"].GetProperty("waitingPunches").GetInt32()));

        // Its attendance read again from a day: the device's own command.
        var again = await Json(await manager.PostAsJsonAsync($"/api/hr/devices/{deviceId}/read-attendance", new { from = Iso(today.AddDays(-10)), to = Iso(today) }, Cancel));
        Assert.Equal(2, again.GetProperty("number").GetInt32());
        Assert.Equal($"C:2:DATA QUERY ATTLOG StartTime={Iso(today.AddDays(-10))} 00:00:00\tEndTime={Iso(today)} 23:59:59", await Poll());

        // A punch for a month whose pay is finalized: kept, and listed for a manager to see to.
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lastDay = lastMonth.AddMonths(1).AddDays(-1);
        var ali = await HireAsync(manager, "Ali", lastDay, "21");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/hr/employees/{ali}/days/{Iso(lastDay)}", new { kind = "Leave" }, Cancel)).StatusCode);
        await Json(await manager.PostAsync($"/api/hr/payroll/{ali}/finalize?month={Iso(lastMonth)}", null, Cancel));
        Assert.Equal("OK:1", await (await Post($"/iclock/cdata?SN={Serial}&table=ATTLOG&Stamp=901", $"21\t{Iso(lastDay)} 12:00:00\t0\t1")).Content.ReadAsStringAsync(Cancel));
        var late = (await Json(await accountant.GetAsync("/api/hr/attendance/late-punches", Cancel))).EnumerateArray().Single();
        Assert.Equal(("Ali", Iso(lastDay)), (late.GetProperty("employee").GetProperty("name").GetString(), late.GetProperty("day").GetString()));
        // A lone punch-in there can't be put right any more: not listed as suspended, only as late.
        Assert.DoesNotContain((await Json(await accountant.GetAsync($"/api/hr/attendance/suspended?from={Iso(lastMonth)}&to={Iso(today)}", Cancel))).EnumerateArray(),
            d => d.GetProperty("employee").GetProperty("name").GetString() == "Ali");
        object Settle() => new { punchIds = new[] { late.GetProperty("punch").GetProperty("id").GetGuid() } };
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync("/api/hr/attendance/late-punches/settle", Settle(), Cancel)).StatusCode);
        Assert.Equal(1, (await Json(await manager.PostAsJsonAsync("/api/hr/attendance/late-punches/settle", Settle(), Cancel))).GetProperty("settled").GetInt32());
        Assert.Empty((await Json(await accountant.GetAsync("/api/hr/attendance/late-punches", Cancel))).EnumerateArray());
    }

    [Fact]
    public async Task Who_is_in_now_is_each_ones_scans_today_paired_in_order()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var hassan = await HireAsync(manager, "Hassan", today.AddMonths(-2));
        var mona = await HireAsync(manager, "Mona", today.AddMonths(-2));
        var karim = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{karim}", new
        {
            requestId = Guid.NewGuid(), name = "Karim", jobTitle = "Delivery", monthlySalary = 4_000, shiftStartsAt = (string?)null, daysOff = Array.Empty<string>(),
            hiredOn = Iso(today.AddMonths(-2)),
        }, Cancel));

        // Their day now: a 10:00 shift's runs from 04:00, as flexible hours' does.
        var now = DateTime.Now;
        var day = DateOnly.FromDateTime(now.AddHours(-4));
        var dayStarts = day.ToDateTime(new TimeOnly(4, 0));
        // In two minutes ago and out five after, both within the day whenever the test runs: just
        // after 04:00, or just before the next.
        var dayEnds = dayStarts.AddDays(1);
        var punchedIn = now.AddMinutes(-2) < dayStarts ? dayStarts.AddSeconds(30)
            : now.AddMinutes(4) >= dayEnds ? dayEnds.AddMinutes(-7)
            : now.AddMinutes(-2);
        string At(DateTime time) => time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        async Task Punch(DateTime time) => await Json(await manager.PostAsJsonAsync("/api/hr/punches", new
        {
            punchId = Guid.NewGuid(), employeeId = hassan, punchedAt = At(time), reason = "Device down",
        }, Cancel));
        async Task<Dictionary<string, JsonElement>> Now() => (await Json(await accountant.GetAsync("/api/hr/attendance/now", Cancel)))
            .GetProperty("employees").EnumerateArray().ToDictionary(e => e.GetProperty("employee").GetProperty("name").GetString()!);

        // Whoever sees HR is told each changed card as it changes, over the live connection.
        var pushed = new ConcurrentQueue<EmployeeStatusDto>();
        var batches = new ConcurrentQueue<IReadOnlyList<EmployeeStatusDto>>();
        await using var hub = await api.ConnectHubAsync(accountant, batches);
        hub.On<List<EmployeeStatusDto>>("EmployeeStatusChanged", statuses => statuses.ForEach(pushed.Enqueue));
        async Task<EmployeeStatusDto> Pushed(Guid employee, PresenceState state)
        {
            Assert.True(await TestApi.WaitFor(() => Task.FromResult(pushed.Any(s => s.EmployeeId == employee && s.State == state))), $"No {state} pushed for {employee}.");
            var status = pushed.Last(s => s.EmployeeId == employee && s.State == state);
            pushed.Clear();
            return status;
        }

        await Punch(punchedIn);
        var arrived = await Pushed(hassan, PresenceState.In);
        Assert.Equal((day, punchedIn.AddTicks(-(punchedIn.Ticks % TimeSpan.TicksPerSecond))), (arrived.Day, arrived.Since));
        var first = await Now();
        Assert.Equal(("In", At(punchedIn), Iso(day)), (first["Hassan"].GetProperty("state").GetString(), first["Hassan"].GetProperty("since").GetString(),
            first["Hassan"].GetProperty("day").GetString()));
        // No punch yet: late once their 10:00 has passed; on flexible hours never late.
        Assert.Equal(now > day.ToDateTime(new TimeOnly(10, 0)) ? "Late" : "NotArrived", first["Mona"].GetProperty("state").GetString());
        Assert.Equal(("NotArrived", JsonValueKind.Null), (first["Karim"].GetProperty("state").GetString(), first["Karim"].GetProperty("dueAt").ValueKind));

        // A finger put on twice is one scan: in still. A second scan past that: out. A day off
        // marked: off.
        await Punch(punchedIn.AddMinutes(1));
        Assert.Equal("In", (await Now())["Hassan"].GetProperty("state").GetString());
        await Punch(punchedIn.AddMinutes(5));
        Assert.Equal(punchedIn.AddMinutes(5).ToString("HH:mm:ss"), (await Pushed(hassan, PresenceState.Left)).Since!.Value.ToString("HH:mm:ss"));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/hr/employees/{mona}/days/{Iso(day)}", new { kind = "DayOff" }, Cancel)).StatusCode);
        Assert.Equal(DayStatus.DayOff, (await Pushed(mona, PresenceState.Off)).DayStatus);
        var second = await Now();
        Assert.Equal(("Left", At(punchedIn.AddMinutes(5))), (second["Hassan"].GetProperty("state").GetString(), second["Hassan"].GetProperty("since").GetString()));
        Assert.Equal("Off", second["Mona"].GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_device_port_answers_only_the_device()
    {
        // The test server takes every request on port 0: as if each came in on the device's port.
        await using var api = await TestApi.StartAsync(new Dictionary<string, string> { ["Attendance:DevicePort"] = "0" });
        var device = api.Anonymous();

        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync("/iclock/cdata?SN=CQZ7232060112&options=all", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync("/api/auth/staff", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync("/", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync("/health", Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_device_is_reached_on_its_own_port_and_synced_at_once()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var today = await TodayAsync(manager);
        var day = today.AddDays(-3);
        await HireAsync(manager, "Hassan", today.AddMonths(-2), "7");
        using var k40 = new HrPayroll.Infrastructure.Tests.FakeZkDevice();
        k40.Users.AddRange([(1, "7", "Hassan"), (2, "12", "منى")]);
        k40.Punches.AddRange([
            ("7", day.ToDateTime(new TimeOnly(10, 4)), 0, 1), ("7", day.ToDateTime(new TimeOnly(19, 30)), 1, 1), ("12", day.ToDateTime(new TimeOnly(11, 0)), 0, 15),
        ]);
        object At(string address, int port) => new { address, port };

        // A manager's: the till reaches out for it.
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync("/api/hr/devices/test", At("127.0.0.1", k40.Port), Cancel)).StatusCode);
        var test = await Json(await manager.PostAsJsonAsync("/api/hr/devices/test", At("127.0.0.1", k40.Port), Cancel));
        Assert.Equal((true, "CQZ7232060112", "K40 Pro", 2, 3), (test.GetProperty("reached").GetBoolean(), test.GetProperty("serialNumber").GetString(),
            test.GetProperty("model").GetString(), test.GetProperty("users").GetInt32(), test.GetProperty("records").GetInt32()));
        Assert.Equal("Connected to K40 Pro, serial CQZ7232060112: 2 users and 3 punches on it.", test.GetProperty("message").GetString());

        // Nothing there, or not on a local network: said plainly.
        var closed = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var nothing = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var refused = await Json(await manager.PostAsJsonAsync("/api/hr/devices/test", At("127.0.0.1", nothing), Cancel));
        Assert.False(refused.GetProperty("reached").GetBoolean());
        Assert.Contains($"nothing listens on port {nothing}", refused.GetProperty("message").GetString());
        Assert.Contains("not on a local network", await Problem(await manager.PostAsJsonAsync("/api/hr/devices/test", At("8.8.8.8", 4370), Cancel)));
        Assert.Contains("IP address", await Problem(await manager.PostAsJsonAsync("/api/hr/devices/sync", At("kitchen-door", 4370), Cancel)));
        Assert.Contains($"nothing listens on port {nothing}", await Problem(await manager.PostAsJsonAsync("/api/hr/devices/sync", At("127.0.0.1", nothing), Cancel)));

        // Synced: the device approved as it is new, its users listed, its punches the day's.
        var sync = await Json(await manager.PostAsJsonAsync("/api/hr/devices/sync", At("127.0.0.1", k40.Port), Cancel));
        Assert.Equal((true, "K40 Pro (127.0.0.1)", 2, 1, 3, 3), (sync.GetProperty("newDevice").GetBoolean(), sync.GetProperty("deviceName").GetString(),
            sync.GetProperty("users").GetInt32(), sync.GetProperty("usersNotOnTheTill").GetInt32(), sync.GetProperty("punches").GetInt32(), sync.GetProperty("newPunches").GetInt32()));
        var device = (await Json(await manager.GetAsync("/api/hr/devices", Cancel))).EnumerateArray().Single();
        Assert.Equal((true, "127.0.0.1", "Boss"), (device.GetProperty("isApproved").GetBoolean(), device.GetProperty("lastAddress").GetString(), device.GetProperty("approvedBy").GetString()));
        // Read by the till, it has never called in over ADMS: its punches come only when it is read.
        Assert.Equal((false, JsonValueKind.Null), (device.GetProperty("callingIn").GetBoolean(), device.GetProperty("lastCalledInAtUtc").ValueKind));
        var users = (await Json(await accountant.GetAsync("/api/hr/device-users", Cancel))).EnumerateArray().ToDictionary(u => u.GetProperty("deviceUserId").GetString()!);
        Assert.Equal(("Hassan", "منى", 1), (users["7"].GetProperty("employeeName").GetString(), users["12"].GetProperty("name").GetString(), users["12"].GetProperty("waitingPunches").GetInt32()));
        var hassanDay = (await Json(await accountant.GetAsync($"/api/hr/attendance?date={Iso(day)}", Cancel))).GetProperty("employees").EnumerateArray()
            .Single(e => e.GetProperty("employee").GetProperty("name").GetString() == "Hassan").GetProperty("day");
        Assert.Equal(("Worked", 4, 2), (hassanDay.GetProperty("status").GetString(), hassanDay.GetProperty("lateMinutes").GetInt32(), hassanDay.GetProperty("punches").GetArrayLength()));

        // Synced again, as ADMS sends them again: nothing kept twice; a new punch kept.
        k40.Punches.Add(("7", today.ToDateTime(new TimeOnly(10, 0)), 0, 1));
        sync = await Json(await manager.PostAsJsonAsync("/api/hr/devices/sync", At("127.0.0.1", k40.Port), Cancel));
        Assert.Equal((false, 4, 1), (sync.GetProperty("newDevice").GetBoolean(), sync.GetProperty("punches").GetInt32(), sync.GetProperty("newPunches").GetInt32()));

        // One that called over ADMS and waits: approved by its first sync, and it keeps its serial's place.
        using var door = new HrPayroll.Infrastructure.Tests.FakeZkDevice { SerialNumber = "CQZ0000000002", Model = "K40" };
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Anonymous().GetAsync("/iclock/cdata?SN=CQZ0000000002&options=all", Cancel)).StatusCode);
        sync = await Json(await manager.PostAsJsonAsync("/api/hr/devices/sync", At("127.0.0.1", door.Port), Cancel));
        Assert.Equal((false, "K40 (127.0.0.1)", 0), (sync.GetProperty("newDevice").GetBoolean(), sync.GetProperty("deviceName").GetString(), sync.GetProperty("punches").GetInt32()));
        var both = (await Json(await manager.GetAsync("/api/hr/devices", Cancel))).EnumerateArray().ToList();
        Assert.Equal(2, both.Count);
        Assert.All(both, d => Assert.True(d.GetProperty("isApproved").GetBoolean()));
    }

    [Fact]
    public async Task The_attendance_device_is_refused_until_approved_and_its_punches_make_the_day()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var device = api.Anonymous();
        var today = await TodayAsync(manager);
        var day = today.AddDays(-7);
        var hassan = await HireAsync(manager, "Hassan", today.AddMonths(-2), "7");
        string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";
        Task<HttpResponseMessage> Upload(string body, string stamp = "55") =>
            device.PostAsync($"/iclock/cdata?SN=CQZ7232060112&table=ATTLOG&Stamp={stamp}", new StringContent(body, Encoding.UTF8, "text/plain"), Cancel);
        var punches = Lines($"7\t{Iso(day)} 10:04:00\t0\t1\t0\t0\t0", $"7\t{Iso(day)} 19:30:00\t1\t1\t0\t0\t0", $"99\t{Iso(day)} 11:00:00\t0\t1", "not a punch");

        // Unknown: kept as waiting, and refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync("/iclock/cdata?SN=CQZ7232060112&options=all&pushver=2.4.1", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync("/iclock/getrequest?SN=CQZ7232060112", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(punches)).StatusCode);
        var waiting = (await Json(await manager.GetAsync("/api/hr/devices", Cancel))).EnumerateArray().Single();
        Assert.Equal(("CQZ7232060112", false), (waiting.GetProperty("serialNumber").GetString(), waiting.GetProperty("isApproved").GetBoolean()));
        var deviceId = waiting.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync($"/api/hr/devices/{deviceId}/approve", new { name = "Door" }, Cancel)).StatusCode);
        await Json(await manager.PostAsJsonAsync($"/api/hr/devices/{deviceId}/approve", new { name = "Kitchen door" }, Cancel));

        // Calling in over ADMS: its punches come as they are made.
        Assert.Equal((true, true), (waiting.GetProperty("callingIn").GetBoolean(), waiting.GetProperty("lastCalledInAtUtc").ValueKind == JsonValueKind.String));

        // Where it reaches the till: no device port here, and this machine's addresses.
        var connection = await Json(await accountant.GetAsync("/api/hr/devices/connection", Cancel));
        Assert.Equal(JsonValueKind.Null, connection.GetProperty("devicePort").ValueKind);
        Assert.Equal(JsonValueKind.Array, connection.GetProperty("addresses").ValueKind);

        var options = await (await device.GetAsync("/iclock/cdata?SN=CQZ7232060112&options=all", Cancel)).Content.ReadAsStringAsync(Cancel);
        Assert.StartsWith("GET OPTION FROM: CQZ7232060112\r\nATTLOGStamp=None\r\n", options);
        Assert.Equal("OK:4", await (await Upload(punches)).Content.ReadAsStringAsync(Cancel));
        // Sent again after a lost answer: taken as read, kept once.
        Assert.Equal("OK:4", await (await Upload(punches)).Content.ReadAsStringAsync(Cancel));
        Assert.Contains("ATTLOGStamp=55", await (await device.GetAsync("/iclock/cdata?SN=CQZ7232060112&options=all", Cancel)).Content.ReadAsStringAsync(Cancel));
        Assert.Equal("OK", await (await device.GetAsync("/iclock/getrequest?SN=CQZ7232060112", Cancel)).Content.ReadAsStringAsync(Cancel));

        async Task<JsonElement> Attendance() => await Json(await accountant.GetAsync($"/api/hr/attendance?date={Iso(day)}", Cancel));
        JsonElement Of(JsonElement report, string name) =>
            report.GetProperty("employees").EnumerateArray().Single(e => e.GetProperty("employee").GetProperty("name").GetString() == name).GetProperty("day");
        var report = await Attendance();
        var hassanDay = Of(report, "Hassan");
        // Four minutes late with no grace; past nine hours worked, 26 minutes over.
        Assert.Equal(("Worked", 566, 4, 26, 2), (hassanDay.GetProperty("status").GetString(), hassanDay.GetProperty("workedMinutes").GetInt32(),
            hassanDay.GetProperty("lateMinutes").GetInt32(), hassanDay.GetProperty("overtimeMinutes").GetInt32(), hassanDay.GetProperty("punches").GetArrayLength()));
        Assert.Equal(["99"], report.GetProperty("unassigned").EnumerateArray().Select(p => p.GetProperty("deviceUserId").GetString()));

        // Number 99 given to someone: their punch is theirs now, one short of a day.
        var karim = await HireAsync(manager, "Karim", today.AddMonths(-2), "99");
        report = await Attendance();
        Assert.Empty(report.GetProperty("unassigned").EnumerateArray());
        Assert.Equal("Suspended", Of(report, "Karim").GetProperty("status").GetString());

        // The missing one entered by a manager, with the reason; an accountant cannot.
        object Punch(string time) => new { punchId = Guid.NewGuid(), employeeId = karim, punchedAt = $"{Iso(day)}T{time}", reason = "Forgot to scan out" };
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync("/api/hr/punches", Punch("20:00:00"), Cancel)).StatusCode);
        await Json(await manager.PostAsJsonAsync("/api/hr/punches", Punch("20:00:00"), Cancel));
        var karimDay = Of(await Attendance(), "Karim");
        Assert.Equal(("Worked", 60), (karimDay.GetProperty("status").GetString(), karimDay.GetProperty("lateMinutes").GetInt32()));

        // A punch ignored stops counting, and counts again restored.
        var evening = hassanDay.GetProperty("punches").EnumerateArray().Single(p => p.GetProperty("punchedAt").GetString()!.EndsWith("19:30:00")).GetProperty("id").GetGuid();
        await Json(await manager.PostAsJsonAsync($"/api/hr/punches/{evening}/ignore", new { reason = "Test scan" }, Cancel));
        Assert.Equal("Suspended", Of(await Attendance(), "Hassan").GetProperty("status").GetString());
        await Json(await manager.PostAsync($"/api/hr/punches/{evening}/restore", null, Cancel));

        // A working day (a swap) is only ever a weekly day off; Hassan has none.
        Assert.Contains("only a weekly day off is marked as one", await Problem(await manager.PutAsJsonAsync(
            $"/api/hr/employees/{hassan}/days/{Iso(day)}", new { kind = "WorkingDay" }, Cancel)));

        // Ten minutes' grace: four minutes late is on time.
        await Json(await manager.PutAsJsonAsync("/api/hr/settings", new { graceMinutes = 10, doubleScanMinutes = 3 }, Cancel));
        Assert.Equal(0, Of(await Attendance(), "Hassan").GetProperty("lateMinutes").GetInt32());

        var month = new DateOnly(day.Year, day.Month, 1);
        var sheet = await Json(await accountant.GetAsync($"/api/hr/employees/{hassan}/timesheet?month={Iso(month)}", Cancel));
        Assert.Equal(DateTime.DaysInMonth(month.Year, month.Month), sheet.GetProperty("days").GetArrayLength());
        Assert.Equal(1, sheet.GetProperty("totals").GetProperty("daysWorked").GetInt32());

        // Its clock: told the restaurant's zone as it starts, to set its clock by (left to its own,
        // +8 as shipped, it runs hours ahead). A punch arriving stamped hours ahead says its clock
        // is fast, until that punch is ignored.
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        Assert.Contains($"\r\nTimeZone={cairo.GetUtcOffset(DateTime.UtcNow).TotalHours.ToString("0", CultureInfo.InvariantCulture)}\r\n", options);
        async Task<JsonElement> ClockAhead() =>
            (await Json(await manager.GetAsync("/api/hr/devices", Cancel))).EnumerateArray().Single().GetProperty("clockAheadMinutes");
        Assert.Equal(JsonValueKind.Null, (await ClockAhead()).ValueKind);
        var fast = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairo).AddHours(5);
        Assert.Equal("OK:1", await (await Upload($"77\t{fast.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}\t0\t1", "56")).Content.ReadAsStringAsync(Cancel));
        Assert.InRange((await ClockAhead()).GetInt32(), 299, 301);
        var stray = await api.Db(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.AttendanceLogs.Where(l => l.DeviceUserId == "77").Select(l => l.Id), Cancel));
        await Json(await manager.PostAsJsonAsync($"/api/hr/punches/{stray}/ignore", new { reason = "Device clock 5 hours fast" }, Cancel));
        Assert.Equal(JsonValueKind.Null, (await ClockAhead()).ValueKind);
    }
}
