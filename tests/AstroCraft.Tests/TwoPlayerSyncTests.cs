using System.Net;
using System.Numerics;
using AstroCraft.Client.Networking;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Hosting;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;
using AstroCraft.Core.Simulation;

namespace AstroCraft.Tests;

[Collection("NetworkIntegration")]
public class TwoPlayerSyncTests
{
    [Fact]
    public async Task TwoHeadlessClients_PlaceAndBreakBlocks_StayInSync()
    {
        const int gamePort = 27520;
        const int discoveryPort = 37520;
        using GameServerHost host = new("Two Player Sync", gamePort, seed: 11, flatWorld: true, discoveryPort: discoveryPort);
        host.Start();
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(3)));
        await Task.Delay(150);

        using GameClientSession clientA = new("127.0.0.1", gamePort, flatWorldHint: true);
        using GameClientSession clientB = new("127.0.0.1", gamePort, flatWorldHint: true);
        clientA.SendHello("Builder");
        clientB.SendHello("Observer");

        await WaitUntilConnectedAsync(clientA, TimeSpan.FromSeconds(5));
        await WaitUntilConnectedAsync(clientB, TimeSpan.FromSeconds(5));
        Assert.NotEqual(clientA.LocalPlayerId, clientB.LocalPlayerId);

        await WaitForChunksAsync(clientA);
        await WaitForChunksAsync(clientB);

        ConnectedClient builder = host.GameServer.Clients.Single(c => c.PlayerId == clientA.LocalPlayerId);
        builder.Player.ResetToSpawn(new Vector3(0.5f, 28f, 0.5f));
        builder.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        builder.Player.Inventory.Hotbar[0].BlockId = BlockId.Concrete;
        builder.Player.Inventory.Hotbar[0].Count = 64;
        host.GameServer.World.EnsureChunksAround(0, 0, 2);

        PlayerInput placeInput = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0,
            builder.Player.YawRadians, builder.Player.PitchRadians);
        for (int attempt = 0; attempt < 120; attempt++)
        {
            clientA.SendInput(placeInput);
            clientA.Poll();
            clientB.Poll();
            clientA.DrainPendingPackets();
            clientB.DrainPendingPackets();
            if (host.GameServer.World.GetBlock(0, 26, 0) == BlockId.Concrete)
            {
                break;
            }

            await Task.Delay(50);
        }

        await WaitForBlockAsync(clientB, 0, 26, 0, BlockId.Concrete, TimeSpan.FromSeconds(6), clientA);
        await WaitForBlockAsync(clientA, 0, 26, 0, BlockId.Concrete, TimeSpan.FromSeconds(6), clientB);
        Assert.Equal(BlockId.Concrete, host.GameServer.World.GetBlock(0, 26, 0));

        ConnectedClient breaker = host.GameServer.Clients.Single(c => c.PlayerId == clientB.LocalPlayerId);
        breaker.Player.ResetToSpawn(new Vector3(0.5f, 28f, 0.5f));
        breaker.Player.PitchRadians = -MathF.PI / 2f + 0.05f;

        PlayerInput breakInput = new(0f, 0f, 0f, 0f, false, false, false, true, false, 0,
            breaker.Player.YawRadians, breaker.Player.PitchRadians);
        for (int attempt = 0; attempt < 320; attempt++)
        {
            clientB.SendInput(breakInput);
            clientA.Poll();
            clientB.Poll();
            clientA.DrainPendingPackets();
            clientB.DrainPendingPackets();
            if (host.GameServer.World.GetBlock(0, 26, 0) == BlockId.Air)
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.Equal(BlockId.Air, host.GameServer.World.GetBlock(0, 26, 0));

        await WaitForBlockAsync(clientA, 0, 26, 0, BlockId.Air, TimeSpan.FromSeconds(12), clientB);
        await WaitForBlockAsync(clientB, 0, 26, 0, BlockId.Air, TimeSpan.FromSeconds(6), clientA);
        Assert.Equal(BlockId.Air, clientA.World.GetBlock(0, 26, 0));
        Assert.Equal(BlockId.Air, clientB.World.GetBlock(0, 26, 0));
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

    private static async Task WaitForChunksAsync(GameClientSession session)
    {
        session.TickChunkStreaming(force: true);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            session.Poll();
            session.DrainPendingPackets();
            if (session.LoadedChunkCount > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out waiting for chunk data.");
    }

    private static async Task WaitForBlockAsync(
        GameClientSession observer,
        int x,
        int y,
        int z,
        BlockId expected,
        TimeSpan timeout,
        params GameClientSession[] alsoPoll)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            observer.Poll();
            observer.DrainPendingPackets();
            foreach (GameClientSession session in alsoPoll)
            {
                session.Poll();
                session.DrainPendingPackets();
            }

            if (observer.World.GetBlock(x, y, z) == expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for block ({x},{y},{z}) to become {expected}.");
    }
}
