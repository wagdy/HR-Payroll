using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HrPayroll.Api.Tests;

// Consumables inventory (جرد الاستهلاكيات) on the Custody tab: the supplies, stock received, used up
// and counted, at an average cost an employee is never shown.
public class ConsumablesApiTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.UnprocessableEntity)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancel);
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<DateOnly> TodayAsync(HttpClient client) =>
        DateOnly.Parse((await Json(await client.GetAsync("/api/hr/today", Cancel))).GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

    private static Task<HttpResponseMessage> Move(HttpClient client, Guid item, string type, decimal quantity, decimal? unitCost = null, Guid? movementId = null, DateOnly? date = null) =>
        client.PostAsJsonAsync($"/api/custody/consumables/{item}/movements", new
        {
            movementId = movementId ?? Guid.NewGuid(), type, quantity, unitCost, date = date is { } d ? Iso(d) : null,
        }, Cancel);

    private static async Task<JsonElement> ItemAsync(HttpClient client, Guid item) =>
        (await Json(await client.GetAsync($"/api/custody/consumables/{item}", Cancel))).GetProperty("item");

    private static (decimal Quantity, decimal UnitCost, decimal TotalCost, bool IsLow) Stock(JsonElement item) =>
        (item.GetProperty("quantity").GetDecimal(), item.GetProperty("unitCost").GetDecimal(), item.GetProperty("totalCost").GetDecimal(), item.GetProperty("isLow").GetBoolean());

    [Fact]
    public async Task Supplies_are_received_used_up_and_counted_at_their_average_cost()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var today = await TodayAsync(manager);
        var napkins = Guid.NewGuid();

        var added = await Json(await manager.PutAsJsonAsync($"/api/custody/consumables/{napkins}", new { name = " Napkins ", unit = "pack", reorderLevel = 120 }, Cancel));
        Assert.Equal(("Napkins", "pack", 0m, true), (added.GetProperty("name").GetString(), added.GetProperty("unit").GetString(),
            added.GetProperty("quantity").GetDecimal(), added.GetProperty("isLow").GetBoolean()));
        Assert.Contains("There is a Napkins already.",
            await Problem(await manager.PutAsJsonAsync($"/api/custody/consumables/{Guid.NewGuid()}", new { name = "napkins", unit = "box" }, Cancel)));

        // 100 at 2.50 and 50 at 4.00: 3.00 each. 30 used up, 5 found short on a count.
        await Json(await Move(manager, napkins, "Received", 100, 2.5m, date: today.AddDays(-2)));
        var receipt = Guid.NewGuid();
        await Json(await Move(manager, napkins, "Received", 50, 4m, receipt));
        // Sent again, as a retry would: found, not received twice.
        await Json(await Move(manager, napkins, "Received", 50, 4m, receipt));
        Assert.Equal((150m, 3m, 450m, false), Stock(await ItemAsync(manager, napkins)));

        var used = await Json(await Move(manager, napkins, "Consumed", 30));
        Assert.Equal((90m, 120m), (used.GetProperty("movement").GetProperty("cost").GetDecimal(), used.GetProperty("movement").GetProperty("stockAfter").GetDecimal()));
        Assert.Contains("There are only 120 pack in stock.", await Problem(await Move(manager, napkins, "Consumed", 121)));
        Assert.Contains("on a day that has come", await Problem(await Move(manager, napkins, "Consumed", 1, date: today.AddDays(1))));

        var counted = await Json(await Move(manager, napkins, "Counted", 115));
        Assert.Equal(-5m, counted.GetProperty("movement").GetProperty("quantity").GetDecimal());
        Assert.Equal((115m, 3m, 345m, true), Stock(counted.GetProperty("item")));

        // The list, and an item's movements, the latest first.
        var listed = (await Json(await manager.GetAsync("/api/custody/consumables", Cancel))).EnumerateArray().Single();
        Assert.Equal((115m, Iso(today)), (listed.GetProperty("quantity").GetDecimal(), listed.GetProperty("lastMovedOn").GetString()));
        var movements = (await Json(await manager.GetAsync($"/api/custody/consumables/{napkins}", Cancel))).GetProperty("movements").EnumerateArray().ToList();
        Assert.Equal(["Counted", "Consumed", "Received", "Received"], movements.Select(m => m.GetProperty("type").GetString()));
        Assert.Equal("Boss", movements[0].GetProperty("recordedBy").GetString());

        // Deleted only while nothing was recorded of it.
        Assert.Contains("has its movements recorded", await Problem(await manager.DeleteAsync($"/api/custody/consumables/{napkins}", Cancel)));
        var gloves = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/custody/consumables/{gloves}", new { name = "Gloves", unit = "box" }, Cancel));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/custody/consumables/{gloves}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/custody/consumables/{gloves}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Whoever_works_the_custody_tab_records_at_once_and_an_employee_never_sees_a_cost()
    {
        await using var api = await TestApi.StartAsync();
        var manager = await api.ManagerAsync();
        var accountant = await api.AccountantAsync(manager);
        var cups = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/custody/consumables/{cups}", new { name = "Takeaway cups", unit = "piece" }, Cancel));
        await Json(await manager.PostAsJsonAsync($"/api/custody/consumables/{cups}/movements", new { movementId = Guid.NewGuid(), type = "Received", quantity = 200, unitCost = 1.5m }, Cancel));

        // An accountant's: made at once, nothing asked of a manager.
        var used = await accountant.PostAsJsonAsync($"/api/custody/consumables/{cups}/movements", new { movementId = Guid.NewGuid(), type = "Consumed", quantity = 20 }, Cancel);
        Assert.Equal(HttpStatusCode.OK, used.StatusCode);

        // An employee holding the tab, in the portal.
        var hassan = Guid.NewGuid();
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}", new
        {
            requestId = Guid.NewGuid(), name = "Hassan", jobTitle = "Storekeeper", monthlySalary = 6_000, shiftStartsAt = "10:00:00", daysOff = Array.Empty<string>(),
            hiredOn = "2026-01-05", mobileNumber = "01012345678",
        }, Cancel));
        await Json(await manager.PutAsJsonAsync($"/api/hr/employees/{hassan}/pin", new { pin = "2468" }, Cancel));
        var phone = api.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/custody/consumables", Cancel)).StatusCode);
        await Json(await manager.PutAsJsonAsync($"/api/hr/access/employees/{hassan}", new { tabs = new[] { "Now" } }, Cancel));
        async Task<HttpClient> SignInAsync()
        {
            var signedIn = await Json(await api.Anonymous().PostAsJsonAsync("/api/auth/employee-login", new { mobileNumber = "01012345678", pinCode = "2468" }, Cancel));
            var client = api.Anonymous();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("token").GetString());
            return client;
        }
        phone = await SignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/custody/consumables", Cancel)).StatusCode);

        await Json(await manager.PutAsJsonAsync($"/api/hr/access/employees/{hassan}", new { tabs = new[] { "Custody" } }, Cancel));
        phone = await SignInAsync();
        var seen = (await Json(await phone.GetAsync("/api/custody/consumables", Cancel))).EnumerateArray().Single();
        Assert.Equal(180m, seen.GetProperty("quantity").GetDecimal());
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (seen.GetProperty("unitCost").ValueKind, seen.GetProperty("totalCost").ValueKind));

        // What they receive goes at the average, whatever price they send.
        var received = await Json(await phone.PostAsJsonAsync($"/api/custody/consumables/{cups}/movements", new { movementId = Guid.NewGuid(), type = "Received", quantity = 20, unitCost = 99 }, Cancel));
        Assert.Equal(JsonValueKind.Null, received.GetProperty("movement").GetProperty("cost").ValueKind);
        Assert.Equal((200m, 1.5m, 300m, false), Stock(await ItemAsync(manager, cups)));
    }
}
