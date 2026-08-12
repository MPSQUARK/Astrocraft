using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Discovery;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.Hosting;

namespace AstroCraft.Tests;

[CollectionDefinition("NetworkIntegration", DisableParallelization = true)]
public sealed class NetworkIntegrationCollection;

[Collection("NetworkIntegration")]
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

    [Fact]
    public async Task Server_TwoClients_ReceiveWelcomeWithDistinctPlayerIds()
    {
        const int gamePort = 27102;
        const int discoveryPort = 37102;
        using GameServerHost host = new("Two Player Test", gamePort, seed: 7, flatWorld: true, discoveryPort: discoveryPort);
        host.Start();
        await Task.Delay(100);

        using UdpClient client1 = new();
        using UdpClient client2 = new();
        IPEndPoint serverEndpoint = new(IPAddress.Loopback, gamePort);

        await client1.SendAsync(NetworkSerializer.WriteClientHello("PlayerOne"), serverEndpoint);
        await client2.SendAsync(NetworkSerializer.WriteClientHello("PlayerTwo"), serverEndpoint);

        (int playerId1, _, _, _, _) = await ReceiveServerWelcomeAsync(client1, TimeSpan.FromSeconds(3));
        (int playerId2, _, _, _, _) = await ReceiveServerWelcomeAsync(client2, TimeSpan.FromSeconds(3));

        Assert.NotEqual(playerId1, playerId2);
        Assert.Equal(2, host.GameServer.Clients.Count);
    }

    [Fact]
    public async Task Server_BlockPlacement_PropagatesBlockChangedToSecondClient()
    {
        const int gamePort = 27103;
        const int discoveryPort = 37103;
        using GameServerHost host = new("Block Sync Test", gamePort, seed: 7, flatWorld: true, discoveryPort: discoveryPort);
        host.Start();
        await Task.Delay(100);

        using UdpClient client1 = new(new IPEndPoint(IPAddress.Loopback, 0));
        using UdpClient client2 = new(new IPEndPoint(IPAddress.Loopback, 0));
        IPEndPoint serverEndpoint = new(IPAddress.Loopback, gamePort);

        await client1.SendAsync(NetworkSerializer.WriteClientHello("Builder"), serverEndpoint);
        await client2.SendAsync(NetworkSerializer.WriteClientHello("Observer"), serverEndpoint);

        (int builderId, _, _, _, _) = await ReceiveServerWelcomeAsync(client1, TimeSpan.FromSeconds(3));
        await ReceiveServerWelcomeAsync(client2, TimeSpan.FromSeconds(3));
        await DrainUdpAsync(client1, TimeSpan.FromMilliseconds(200));
        await DrainUdpAsync(client2, TimeSpan.FromMilliseconds(200));

        ConnectedClient builder = host.GameServer.Clients.Single(c => c.PlayerId == builderId);
        builder.Player.ResetToSpawn(new Vector3(0.5f, 28f, 0.5f));
        builder.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        builder.Player.Inventory.Hotbar[0].BlockId = BlockId.Concrete;
        builder.Player.Inventory.Hotbar[0].Count = 64;
        host.GameServer.World.EnsureChunksAround(0, 0, 2);

        PlayerInput placeInput = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        byte[] placePacket = NetworkSerializer.WritePlayerInput(builderId, placeInput);

        Task<(int X, int Y, int Z, BlockId BlockId, BlockAxis Axis)> blockChangedTask =
            ReceiveBlockChangedAsync(client2, TimeSpan.FromSeconds(5));

        for (int attempt = 0; attempt < 30; attempt++)
        {
            await client1.SendAsync(placePacket, serverEndpoint);
            if (host.GameServer.World.GetBlock(0, 26, 0) == BlockId.Concrete)
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.Equal(BlockId.Concrete, host.GameServer.World.GetBlock(0, 26, 0));

        (int x, int y, int z, BlockId blockId, _) = await blockChangedTask;

        Assert.Equal(BlockId.Concrete, blockId);
        Assert.Equal(0, x);
        Assert.Equal(26, y);
        Assert.Equal(0, z);
        Assert.Equal(BlockId.Concrete, host.GameServer.World.GetBlock(x, y, z));
    }

    [Fact]
    public void GameServer_BlockPlacement_UpdatesWorldState()
    {
        GameServer server = new(seed: 11, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 55102);
        int playerId = server.ConnectClient(endpoint, "BlockTester");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.ResetToSpawn(new Vector3(0.5f, 28f, 0.5f));
        client.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        client.Player.Inventory.Hotbar[0].BlockId = BlockId.Concrete;
        client.Player.Inventory.Hotbar[0].Count = 64;
        server.World.EnsureChunksAround(0, 0, 2);

        PlayerInput input = new(
            0f,
            0f,
            0f,
            0f,
            false,
            false,
            false,
            false,
            true,
            0,
            client.Player.YawRadians,
            client.Player.PitchRadians);
        server.QueueInput(endpoint, input);
        server.Tick();

        Assert.Equal(BlockId.Concrete, server.World.GetBlock(0, 26, 0));
        Assert.Contains(
            server.World.PendingBlockChanges,
            change => change.X == 0 && change.Y == 26 && change.Z == 0 && change.BlockId == BlockId.Concrete);
    }

    [Fact]
    public void GameServer_BlockPlacement_PlacesTorchOnGround()
    {
        GameServer server = new(seed: 11, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 55103);
        int playerId = server.ConnectClient(endpoint, "TorchTester");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.ResetToSpawn(new Vector3(0.5f, 28f, 0.5f));
        client.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        client.Player.Inventory.Hotbar[0].BlockId = BlockId.Torch;
        client.Player.Inventory.Hotbar[0].Count = 4;
        server.World.EnsureChunksAround(0, 0, 2);

        PlayerInput input = new(
            0f,
            0f,
            0f,
            0f,
            false,
            false,
            false,
            false,
            true,
            0,
            client.Player.YawRadians,
            client.Player.PitchRadians);
        server.QueueInput(endpoint, input);
        server.Tick();

        Assert.Equal(BlockId.Torch, server.World.GetBlock(0, 26, 0));
        Assert.Equal(BlockAxis.Y, server.World.GetBlockAxis(0, 26, 0));
    }

    [Fact]
    public async Task Client_DisconnectThenReconnect_ReceivesNewWelcome()
    {
        const int gamePort = 27104;
        const int discoveryPort = 37104;
        using GameServerHost host = new("Reconnect Test", gamePort, seed: 9, flatWorld: true, discoveryPort: discoveryPort);
        host.Start();
        await Task.Delay(100);

        using UdpClient client = new();
        IPEndPoint serverEndpoint = new(IPAddress.Loopback, gamePort);

        await client.SendAsync(NetworkSerializer.WriteClientHello("ReconnectTester"), serverEndpoint);
        (int firstPlayerId, _, _, _, _) = await ReceiveServerWelcomeAsync(client, TimeSpan.FromSeconds(3));
        await DrainUdpAsync(client, TimeSpan.FromMilliseconds(100));

        byte[] disconnect = [(byte)MessageType.Disconnect];
        await client.SendAsync(disconnect, serverEndpoint);
        await Task.Delay(150);
        Assert.Empty(host.GameServer.Clients);

        await client.SendAsync(NetworkSerializer.WriteClientHello("ReconnectTester"), serverEndpoint);
        (int secondPlayerId, _, _, _, _) = await ReceiveServerWelcomeAsync(client, TimeSpan.FromSeconds(3));

        Assert.NotEqual(firstPlayerId, secondPlayerId);
        Assert.Single(host.GameServer.Clients);
    }

    [Fact]
    public void GameServer_VoidFall_KillsPlayer_AndRespawnsOnNextTick()
    {
        GameServer server = new(seed: 42, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 55110);
        int playerId = server.ConnectClient(endpoint, "VoidTester");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.Position = new Vector3(0.5f, GameConstants.VoidFallY - 1f, 0.5f);

        server.Tick();
        Assert.True(client.Player.Survival.IsDead);

        for (int tick = 0; tick <= GameConstants.RespawnDelayTicks; tick++)
        {
            server.Tick();
        }

        Assert.False(client.Player.Survival.IsDead);
        Assert.True(client.Player.Position.Y > GameConstants.VoidFallY + 1f);
        Assert.Equal(GameConstants.MaxHealth, client.Player.Survival.Health);
    }

    private static async Task<(int PlayerId, int Tick, Vector3 Spawn, int WorldSeed, bool FlatWorld)> ReceiveServerWelcomeAsync(
        UdpClient client,
        TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        try
        {
            while (true)
            {
                UdpReceiveResult response = await client.ReceiveAsync(cts.Token);
                if (NetworkSerializer.ReadMessageType(response.Buffer) == MessageType.ServerWelcome)
                {
                    return NetworkSerializer.ReadServerWelcome(response.Buffer[1..]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Timed out waiting for ServerWelcome.");
        }
    }

    private static async Task<(int X, int Y, int Z, BlockId BlockId, BlockAxis Axis)> ReceiveBlockChangedAsync(
        UdpClient client,
        TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        try
        {
            while (true)
            {
                UdpReceiveResult response = await client.ReceiveAsync(cts.Token);
                if (NetworkSerializer.ReadMessageType(response.Buffer) == MessageType.BlockChanged)
                {
                    return NetworkSerializer.ReadBlockChanged(response.Buffer[1..]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Timed out waiting for BlockChanged.");
        }
    }

    private static async Task DrainUdpAsync(UdpClient client, TimeSpan duration)
    {
        using CancellationTokenSource cts = new(duration);
        try
        {
            while (true)
            {
                await client.ReceiveAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
