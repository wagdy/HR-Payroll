namespace HrPayroll.Api.Hosting;

// On a hosting platform that gives the app its port in PORT (Railway, Render, Heroku): the API
// listens there on every address, and on the attendance device's own port (and the portal's, if
// set) beside it, which the platform exposes apart: on Railway, a TCP Proxy to the device's port
// carries the device's plain HTTP, since Railway's own domains are HTTPS only. With no PORT, the
// configuration's Urls as they are.
internal static class PlatformHosting
{
    public const string PortVariable = "PORT";

    public static void ListenOnPlatformPort(this WebApplicationBuilder builder)
    {
        if (!int.TryParse(builder.Configuration[PortVariable], out var webPort) || webPort <= 0)
            return;
        int?[] ports = [webPort, DevicePort.Of(builder.Configuration), PortalPort.Of(builder.Configuration)];
        builder.WebHost.UseUrls(ports.OfType<int>().Distinct().Select(port => $"http://0.0.0.0:{port}").ToArray());
    }

    // Where a device on the internet reaches the device's port: Attendance:PublicAddress and
    // PublicPort when set, else the Railway TCP Proxy's domain and port when there is one. Null:
    // only the office network reaches it.
    public static (string Address, int Port)? DeviceTarget(IConfiguration configuration)
    {
        var address = configuration["Attendance:PublicAddress"] is { Length: > 0 } set ? set : configuration["RAILWAY_TCP_PROXY_DOMAIN"];
        var port = configuration.GetValue<int?>("Attendance:PublicPort") ?? configuration.GetValue<int?>("RAILWAY_TCP_PROXY_PORT");
        return address is { Length: > 0 } && port is > 0 ? (address, port.Value) : null;
    }

    // Where employees open the portal from anywhere: Portal:PublicUrl when set, else the Railway
    // domain's /portal when there is one.
    public static string? PortalUrl(IConfiguration configuration)
    {
        if (configuration["Portal:PublicUrl"] is { Length: > 0 } set)
            return set;
        return configuration["RAILWAY_PUBLIC_DOMAIN"] is { Length: > 0 } domain ? $"https://{domain}/portal" : null;
    }
}
