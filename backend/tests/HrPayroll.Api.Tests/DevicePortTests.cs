using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using HrPayroll.Api.Hosting;

namespace HrPayroll.Api.Tests;

// Whether an attendance device on the network can reach a port: only when the API listens on it
// for the network, not for this machine alone. The development API listens on the device's port
// 5092 on every address, and on 5090 for this machine's own screens.
public class DevicePortTests
{
    private static readonly string[] Development = ["http://localhost:5090", "http://0.0.0.0:5092", "http://0.0.0.0:5093"];

    [Fact]
    public void A_port_bound_on_every_address_is_reached_from_the_network_and_one_on_localhost_is_not()
    {
        Assert.True(DevicePort.ListensOnNetwork(Development, 5092));
        Assert.True(DevicePort.ListensOnNetwork(Development, 5093));
        Assert.False(DevicePort.ListensOnNetwork(Development, 5090));
        // Not bound at all: the screen's own port, served by another server.
        Assert.False(DevicePort.ListensOnNetwork(Development, 4220));
    }

    [Theory]
    [InlineData("http://[::]:5092", true)]
    [InlineData("http://*:5092", true)]
    [InlineData("http://+:5092", true)]
    [InlineData("http://192.168.1.57:5092", true)]
    [InlineData("http://127.0.0.1:5092", false)]
    [InlineData("http://[::1]:5092", false)]
    [InlineData("http://localhost:5092", false)]
    public void Each_way_of_binding_is_read_as_the_network_or_this_machine_alone(string bound, bool reached) =>
        Assert.Equal(reached, DevicePort.ListensOnNetwork([bound], 5092));

    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    // In the cloud, the device is pointed at the platform's proxy to its own port; the portal is
    // the platform's domain. Said plainly in the settings, or taken from Railway's own variables.
    [Fact]
    public void In_the_cloud_the_device_is_given_the_proxys_address_and_port()
    {
        Assert.Null(PlatformHosting.DeviceTarget(Settings()));
        Assert.Null(PlatformHosting.PortalUrl(Settings()));

        var railway = Settings(("RAILWAY_TCP_PROXY_DOMAIN", "shuttle.proxy.rlwy.net"), ("RAILWAY_TCP_PROXY_PORT", "15140"), ("RAILWAY_PUBLIC_DOMAIN", "hr.up.railway.app"));
        Assert.Equal(("shuttle.proxy.rlwy.net", 15140), PlatformHosting.DeviceTarget(railway));
        Assert.Equal("https://hr.up.railway.app/portal", PlatformHosting.PortalUrl(railway));

        var set = Settings(("Attendance:PublicAddress", "adms.example.com"), ("Attendance:PublicPort", "8090"), ("Portal:PublicUrl", "https://hr.example.com/portal"),
            ("RAILWAY_TCP_PROXY_DOMAIN", "shuttle.proxy.rlwy.net"), ("RAILWAY_TCP_PROXY_PORT", "15140"));
        Assert.Equal(("adms.example.com", 8090), PlatformHosting.DeviceTarget(set));
        Assert.Equal("https://hr.example.com/portal", PlatformHosting.PortalUrl(set));
    }

    [Fact]
    public async Task The_devices_tab_is_told_the_cloud_address()
    {
        await using var api = await TestApi.StartAsync(new Dictionary<string, string>
        {
            ["RAILWAY_TCP_PROXY_DOMAIN"] = "shuttle.proxy.rlwy.net", ["RAILWAY_TCP_PROXY_PORT"] = "15140", ["RAILWAY_PUBLIC_DOMAIN"] = "hr.up.railway.app",
        });
        var manager = await api.ManagerAsync();

        var response = await manager.GetAsync("/api/hr/devices/connection", TestContext.Current.CancellationToken);
        var connection = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(("shuttle.proxy.rlwy.net", 15140, "https://hr.up.railway.app/portal"),
            (connection.GetProperty("publicAddress").GetString(), connection.GetProperty("publicPort").GetInt32(), connection.GetProperty("portalUrl").GetString()));
    }
}
