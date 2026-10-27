using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Net.Sockets;

namespace HrPayroll.Api.Hosting;

// The attendance device's own port (Attendance:DevicePort), when the system's screens stay on
// its machine and only the device reaches it over the network: the API listens there too (as
// --urls says), and answers nothing there but the device's /iclock paths. Unset, every port
// answers everything.
internal static class DevicePort
{
    public const string Setting = "Attendance:DevicePort";

    public static int? Of(IConfiguration configuration) => configuration.GetValue<int?>(Setting);

    public static IApplicationBuilder UseDevicePort(this IApplicationBuilder app, int? port)
    {
        if (port is not { } devicePort)
            return app;
        return app.Use(async (context, next) =>
        {
            if (context.Connection.LocalPort == devicePort && !context.Request.Path.StartsWithSegments("/iclock"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }

    // Whether the API listens on this port where the network reaches it: on every address
    // (0.0.0.0, [::], *) or on one that is not this machine's loopback. Listening on localhost
    // alone, a device or a phone cannot reach it.
    public static bool ListensOnNetwork(IServer server, int port) =>
        ListensOnNetwork(server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [], port);

    // The same, of the addresses the server says it is bound to: "http://0.0.0.0:5092".
    public static bool ListensOnNetwork(IEnumerable<string> bound, int port)
    {
        foreach (var address in bound)
        {
            var url = address.Replace("://*", "://0.0.0.0", StringComparison.Ordinal).Replace("://+", "://0.0.0.0", StringComparison.Ordinal);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Port != port)
                continue;
            if (uri.Host is "0.0.0.0" or "[::]" or "::")
                return true;
            if (System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) ? !System.Net.IPAddress.IsLoopback(ip) : uri.Host != "localhost")
                return true;
        }

        return false;
    }

    // This machine's addresses on its networks, for the device to be given one: IPv4, up, not
    // its own loopback.
    public static IReadOnlyList<string> Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString())
            .Distinct()
            .ToList();
}
