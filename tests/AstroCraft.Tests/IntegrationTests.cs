using System.Net;
using System.Net.Sockets;
using System.Text;
using AstroCraft.Core.Discovery;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Server;
using AstroCraft.Server.Hosting;

namespace AstroCraft.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task Server_AcceptsClientHello_AndRespondsWithWelcome()
    {
        using GameServerHost host = new("Test Server", 27100, seed: 7, flatWorld: true, discoveryPort: 37100);
        host.Start();
        await Task.Delay(100);

        using UdpClient client = new();
        byte[] hello = NetworkSerializer.WriteClientHello("IntegrationTester");
        await client.SendAsync(hello, new IPEndPoint(IPAddress.Loopback, 27100));

        UdpReceiveResult response = await client.ReceiveAsync();
        Assert.Equal(MessageType.ServerWelcome, NetworkSerializer.ReadMessageType(response.Buffer));
    }

    [Fact]
    public async Task Discovery_ServiceRespondsToBroadcast()
    {
        using LanDiscoveryService discovery = new("Discovery Test", 27101, () => 0, discoveryPort: 37101);
        discovery.Start();
        await Task.Delay(100);

        using LanDiscoveryClient client = new();
        IReadOnlyList<DiscoveredServer> servers = await client.DiscoverAsync(1500, discoveryPort: 37101);
        Assert.Contains(servers, server => server.Name == "Discovery Test" && server.Port == 27101);
    }

    [Fact]
    public void GameServer_Tick_IncrementsCurrentTick()
    {
        GameServer server = new(seed: 3, flatWorld: true);
        int initial = server.CurrentTick;
        server.Tick();
        Assert.Equal(initial + 1, server.CurrentTick);
    }
}
