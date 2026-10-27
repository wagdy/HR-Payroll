using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using HrPayroll.Application;
using HrPayroll.Api.Auth;
using HrPayroll.Api.Controllers;
using HrPayroll.Api.ErrorHandling;
using HrPayroll.Api.Hosting;
using HrPayroll.Api.Hubs;
using HrPayroll.Infrastructure;
using HrPayroll.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
// On a hosting platform, its PORT, and the device's own port beside it.
builder.ListenOnPlatformPort();

builder.Services.AddHrApplication();
builder.Services.AddHrInfrastructure(builder.Configuration);
builder.Services.AddHrAuthentication(builder.Configuration);
builder.Services.AddSingleton<HrPayroll.Application.Hr.IAttendanceNotifier, AttendanceNotifier>();

// Enums travel as names ("Monthly", "Pending"), over REST and the hub alike.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ApplicationExceptionHandler.Customize);
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

// /health says the process is up, /health/ready that it can reach its database too.
builder.Services.AddHealthChecks().AddDbContextCheck<HrDbContext>(name: "database", tags: ["ready"]);

// Per address, on top of each account's own lockout: one browser cannot cycle through passwords
// across every account, nor a phone through PINs across every employee.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { errors = new[] { "Too many sign-in attempts. Wait a minute and try again." } }, cancellationToken);
    };
    o.AddPolicy(AuthController.SignInRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy(AuthController.EmployeeSignInRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Only needed when the Angular app is served from somewhere else, such as `ng serve` during
// development. Credentials are allowed because the SignalR client negotiates with them.
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

await app.PrepareDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The attendance device's own port, when there is one: nothing but its /iclock paths there.
app.UseDevicePort(DevicePort.Of(app.Configuration));
// The employee portal's own port, when there is one: its sign-in, its API and the app's files.
app.UsePortalPort(PortalPort.Of(app.Configuration));

// Before authentication: the page itself is public, and only the API behind it needs a
// sign-in.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = SpaHosting.SetCacheHeaders });

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapControllers();

// A connection closes when its token expires, so a push never outlives the sign-in.
app.MapHub<HrHub>(HrHub.Path, o => o.CloseOnAuthenticationExpiration = true);

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapFallback(SpaHosting.ServeIndexAsync).AllowAnonymous();

app.Run();

// For the API tests' WebApplicationFactory.
public partial class Program;
