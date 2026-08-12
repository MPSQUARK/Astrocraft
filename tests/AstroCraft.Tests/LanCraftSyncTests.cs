using System.Numerics;
using AstroCraft.Client.Networking;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Hosting;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;
using AstroCraft.Core.Simulation;

namespace AstroCraft.Tests;

[Collection("NetworkIntegration")]
public class LanCraftSyncTests
{
    [Fact]
    public async Task T175_TwoHeadlessClients_CraftPlanksAndPlaceTorch_ServerStaysAuthoritative()
    {
        const int gamePort = 27525;
        const int discoveryPort = 37525;
        using GameServerHost host = new("Lan Craft Sync", gamePort, seed: 13, flatWorld: true, discoveryPort: discoveryPort);
        host.Start();
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(3)));
        await Task.Delay(150);

        using GameClientSession player1 = new("127.0.0.1", gamePort, flatWorldHint: true);
        using GameClientSession player2 = new("127.0.0.1", gamePort, flatWorldHint: true);
        player1.SendHello("Crafter");
        player2.SendHello("Observer");

        await WaitUntilConnectedAsync(player1, TimeSpan.FromSeconds(5));
        await WaitUntilConnectedAsync(player2, TimeSpan.FromSeconds(5));
        Assert.NotEqual(player1.LocalPlayerId, player2.LocalPlayerId);

        await WaitForChunksAsync(player1);
        await WaitForChunksAsync(player2);

        ConnectedClient crafter = host.GameServer.Clients.Single(c => c.PlayerId == player1.LocalPlayerId);
        crafter.Player.Inventory.TryAddBlock(BlockId.Wood, 1);

        bool crafted = host.GameServer.TryCraft(player1.LocalPlayerId, "planks_from_wood");
        Assert.True(crafted);

        InventorySlot planksSlot = crafter.Player.Inventory.Hotbar
            .Concat(crafter.Player.Inventory.Storage)
            .First(s => s.BlockId == BlockId.Planks);
        Assert.Equal(4, planksSlot.Count);
        Assert.False(crafter.Player.Inventory.TryRemoveBlock(BlockId.Wood, 1));

        crafter.Player.ResetToSpawn(new Vector3(0.5f, 28f, 0.5f));
        crafter.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        crafter.Player.Inventory.Hotbar[0].BlockId = BlockId.Torch;
        crafter.Player.Inventory.Hotbar[0].Count = 4;
        host.GameServer.World.EnsureChunksAround(0, 0, 2);

        PlayerInput placeInput = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0,
            crafter.Player.YawRadians, crafter.Player.PitchRadians);
        for (int attempt = 0; attempt < 120; attempt++)
        {
            player1.SendInput(placeInput);
            player1.Poll();
            player2.Poll();
            player1.DrainPendingPackets();
            player2.DrainPendingPackets();
            if (host.GameServer.World.GetBlock(0, 26, 0) == BlockId.Torch)
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.Equal(BlockId.Torch, host.GameServer.World.GetBlock(0, 26, 0));
        Assert.Equal(BlockAxis.Y, host.GameServer.World.GetBlockAxis(0, 26, 0));
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
}
