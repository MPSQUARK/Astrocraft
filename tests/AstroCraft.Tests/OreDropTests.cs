using System.Net;
using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Server;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Tests;

public class OreDropTests
{
    [Theory]
    [InlineData(BlockId.IronOre, BlockId.IronOre)]
    [InlineData(BlockId.CopperOre, BlockId.CopperOre)]
    [InlineData(BlockId.Stone, BlockId.Cobblestone)]
    [InlineData(BlockId.Dirt, BlockId.Dirt)]
    [InlineData(BlockId.Grass, BlockId.Dirt)]
    public void BlockRegistry_GetDrop_ReturnsExpectedItem(BlockId broken, BlockId expectedDrop)
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        Assert.Equal(expectedDrop, registry.GetDrop(broken));
    }

    [Fact]
    public void BlockRegistry_GetDropStack_CoalOreYieldsCoalItem()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        StackKey drop = registry.GetDropStack(BlockId.CoalOre);
        Assert.Equal(ItemId.Coal, drop.ItemId);
        Assert.Equal(BlockId.Air, drop.BlockId);
    }

    [Fact]
    public void GameServer_BreakingOre_SpawnsMatchingDropEntity()
    {
        GameServer server = new(seed: 9, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 55210);
        int playerId = server.ConnectClient(endpoint, "Miner");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        client.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        server.World.EnsureChunksAround(0, 0, 2);
        server.World.TrySetBlock(0, 25, 0, BlockId.IronOre);

        PlayerInput breakInput = new(
            0f,
            0f,
            0f,
            0f,
            false,
            false,
            false,
            true,
            false,
            0,
            client.Player.YawRadians,
            client.Player.PitchRadians);

        bool blockBroken = false;
        for (int tick = 0; tick < 120; tick++)
        {
            server.QueueInput(endpoint, breakInput);
            server.Tick();
            if (server.World.GetBlock(0, 25, 0) == BlockId.Air)
            {
                blockBroken = true;
                break;
            }
        }

        Assert.True(blockBroken);
        Assert.Contains(server.ItemEntities.Entities, entity => entity.Stack.BlockId == BlockId.IronOre);
    }

    [Fact]
    public void GameServer_BreakingStone_SpawnsCobbleDrop()
    {
        GameServer server = new(seed: 9, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 55211);
        int playerId = server.ConnectClient(endpoint, "Miner");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        client.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        server.World.EnsureChunksAround(0, 0, 2);
        server.World.TrySetBlock(0, 25, 0, BlockId.Stone);

        PlayerInput breakInput = new(
            0f,
            0f,
            0f,
            0f,
            false,
            false,
            false,
            true,
            false,
            0,
            client.Player.YawRadians,
            client.Player.PitchRadians);

        for (int tick = 0; tick < 80; tick++)
        {
            server.QueueInput(endpoint, breakInput);
            server.Tick();
            if (server.World.GetBlock(0, 25, 0) == BlockId.Air)
            {
                break;
            }
        }

        Assert.Contains(server.ItemEntities.Entities, entity => entity.Stack.BlockId == BlockId.Cobblestone);
    }

    [Fact]
    public void GameServer_BreakingCoalOre_SpawnsCoalItemDrop()
    {
        GameServer server = new(seed: 9, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 55212);
        int playerId = server.ConnectClient(endpoint, "CoalMiner");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        client.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        server.World.EnsureChunksAround(0, 0, 2);
        server.World.TrySetBlock(0, 25, 0, BlockId.CoalOre);

        PlayerInput breakInput = new(
            0f,
            0f,
            0f,
            0f,
            false,
            false,
            false,
            true,
            false,
            0,
            client.Player.YawRadians,
            client.Player.PitchRadians);

        for (int tick = 0; tick < 120; tick++)
        {
            server.QueueInput(endpoint, breakInput);
            server.Tick();
            if (server.World.GetBlock(0, 25, 0) == BlockId.Air)
            {
                break;
            }
        }

        Assert.Equal(BlockId.Air, server.World.GetBlock(0, 25, 0));
        Assert.Contains(server.ItemEntities.Entities, entity => entity.Stack.ItemId == ItemId.Coal);
    }
}
