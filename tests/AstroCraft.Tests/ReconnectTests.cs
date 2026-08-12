using System.Net;
using System.Net.Sockets;
using AstroCraft.Client.Networking;
using AstroCraft.Core.Hosting;
using AstroCraft.Core.Networking;

namespace AstroCraft.Tests;

[Collection("NetworkIntegration")]
public class ReconnectTests
{
    [Fact]
    public async Task Reconnect_SameEndpoint_KeepsPlayerId_PreventsDuplicate()
    {
        const int gamePort = 27121;
        using GameServerHost host = new("Reconnect Id Test", gamePort, seed: 3, flatWorld: true, enableDiscovery: false);
        host.Start();
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(2)));
        await Task.Delay(150);

        using GameClientSession session = new("127.0.0.1", gamePort, flatWorldHint: true);
        session.SendHello("ReconnectTester");
        await WaitUntilConnectedAsync(session, TimeSpan.FromSeconds(5));
        int firstPlayerId = session.LocalPlayerId;
        Assert.Single(host.GameServer.Clients);

        session.AttemptReconnect();
        await Task.Delay(300);
        session.Poll();
        session.DrainPendingPackets();

        Assert.True(session.IsConnected);
        Assert.Equal(firstPlayerId, session.LocalPlayerId);
        Assert.Single(host.GameServer.Clients);
    }

    [Fact]
    public void AttemptReconnect_UsesExponentialBackoff()
    {
        using GameClientSession session = new("127.0.0.1", 9, flatWorldHint: true);
        Assert.True(session.IsReadyToReconnect);
        Assert.Equal(0, session.ReconnectAttempts);

        session.AttemptReconnect();
        Assert.Equal(1, session.ReconnectAttempts);
        Assert.False(session.IsReadyToReconnect);

        session.AttemptReconnect();
        Assert.Equal(1, session.ReconnectAttempts);
    }

    [Fact]
    public async Task Reconnect_ResyncsChunksAfterWelcome()
    {
        const int gamePort = 27122;
        using GameServerHost host = new("Reconnect Chunk Test", gamePort, seed: 5, flatWorld: true, enableDiscovery: false);
        host.Start();
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(2)));
        await Task.Delay(150);

        int chunksBeforeDisconnect;
        {
            using GameClientSession session = new("127.0.0.1", gamePort, flatWorldHint: true);
            session.SendHello("ChunkTester");
            await WaitUntilConnectedAsync(session, TimeSpan.FromSeconds(5));
            session.TickChunkStreaming(force: true);

            for (int i = 0; i < 40; i++)
            {
                session.Poll();
                session.DrainPendingPackets();
                if (session.LoadedChunkCount > 0)
                {
                    break;
                }

                await Task.Delay(25);
            }

            chunksBeforeDisconnect = session.LoadedChunkCount;
            Assert.True(chunksBeforeDisconnect > 0);
        }

        await Task.Delay(500);

        using GameClientSession reconnected = new("127.0.0.1", gamePort, flatWorldHint: true);
        reconnected.SendHello("ChunkTester");
        await WaitUntilConnectedAsync(reconnected, TimeSpan.FromSeconds(10));
        reconnected.TickChunkStreaming(force: true);

        for (int i = 0; i < 60; i++)
        {
            reconnected.Poll();
            reconnected.DrainPendingPackets();
            if (reconnected.LoadedChunkCount > 0)
            {
                break;
            }

            await Task.Delay(25);
        }

        Assert.True(reconnected.LoadedChunkCount > 0);
        Assert.Single(host.GameServer.Clients);
    }

    [Fact]
    public async Task Client_DisconnectThenReconnect_ReceivesWelcomeWithNewPlayerId()
    {
        const int gamePort = 27123;
        using GameServerHost host = new("Reconnect New Id", gamePort, seed: 9, flatWorld: true, enableDiscovery: false);
        host.Start();
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(2)));
        await Task.Delay(100);

        using UdpClient client = new();
        IPEndPoint serverEndpoint = new(IPAddress.Loopback, gamePort);

        await client.SendAsync(NetworkSerializer.WriteClientHello("ReconnectTester"), serverEndpoint);
        (int firstPlayerId, _, _, _, _) = await ReceiveServerWelcomeAsync(client, TimeSpan.FromSeconds(3));
        byte[] disconnect = [(byte)MessageType.Disconnect];
        await client.SendAsync(disconnect, serverEndpoint);
        await Task.Delay(150);
        Assert.Empty(host.GameServer.Clients);

        await client.SendAsync(NetworkSerializer.WriteClientHello("ReconnectTester"), serverEndpoint);
        (int secondPlayerId, _, _, _, _) = await ReceiveServerWelcomeAsync(client, TimeSpan.FromSeconds(3));

        Assert.NotEqual(firstPlayerId, secondPlayerId);
        Assert.Single(host.GameServer.Clients);
    }

    private static async Task WaitUntilConnectedAsync(GameClientSession session, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        DateTime nextHelloUtc = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            session.Poll();
            session.DrainPendingPackets();
            if (session.IsConnected)
            {
                return;
            }

            if (DateTime.UtcNow >= nextHelloUtc)
            {
                session.SendHello(session.LocalPlayer.DisplayName);
                nextHelloUtc = DateTime.UtcNow.AddMilliseconds(400);
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out waiting for client connection.");
    }

    private static async Task<(int PlayerId, int Tick, System.Numerics.Vector3 Spawn, int WorldSeed, bool FlatWorld)> ReceiveServerWelcomeAsync(
        UdpClient client,
        TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        while (true)
        {
            UdpReceiveResult response = await client.ReceiveAsync(cts.Token);
            if (NetworkSerializer.ReadMessageType(response.Buffer) == MessageType.ServerWelcome)
            {
                return NetworkSerializer.ReadServerWelcome(response.Buffer[1..]);
            }
        }
    }
}
