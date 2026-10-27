using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using HrPayroll.Api.Auth;
using HrPayroll.Infrastructure.Identity;
using HrPayroll.Infrastructure.Persistence;

namespace HrPayroll.Api.Hosting;

internal static class DatabaseStartup
{
    // Runs before the app listens: the database brought up to date, and the first manager made
    // when there is no user yet. Set Database:MigrateOnStartup to false where something else
    // applies the migrations.
    public static async Task PrepareDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
            await scope.ServiceProvider.GetRequiredService<HrDbContext>().Database.MigrateAsync();

        var first = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value.FirstManager;
        await scope.ServiceProvider.GetRequiredService<UserDirectory>()
            .EnsureFirstManagerAsync(first.Email, first.Name, first.Password, CancellationToken.None);
    }
}
