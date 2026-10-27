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
}
