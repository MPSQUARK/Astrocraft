using System.Net;
using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class ItemPickupTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public ItemPickupTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void TryPickup_AddsToHotbarWhenPlayerWithinRadius()
    {
        GameWorld world = _flat.CreateWorld(0);
        ItemEntityWorld items = new();
        ItemPickupSystem pickup = new();
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 26f, 0.5f));

        items.Spawn(new Vector3(0.5f, 25.5f, 0.5f), Vector3.Zero, BlockId.Grass).PickupCooldownTicks = 0;
        pickup.TryPickup(player, items);

        Assert.Empty(items.Entities);
        Assert.Equal(BlockId.Grass, player.Inventory.Hotbar[0].BlockId);
        Assert.Equal(1, player.Inventory.Hotbar[0].Count);
    }

    [Fact]
    public void TryPickup_DoesNotPickupWhenOutsideRadius()
    {
        GameWorld world = _flat.CreateWorld(0);
        ItemEntityWorld items = new();
        ItemPickupSystem pickup = new();
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 30f, 0.5f));

        items.Spawn(new Vector3(0.5f, 25.5f, 0.5f), Vector3.Zero, BlockId.Grass);
        pickup.TryPickup(player, items);

        Assert.Single(items.Entities);
        Assert.All(player.Inventory.Hotbar, slot => Assert.Equal(BlockId.Air, slot.BlockId));
    }

    [Fact]
    public void TryPickup_RespectsPickupCooldown()
    {
        ItemEntityWorld items = new();
        ItemPickupSystem pickup = new();
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 26f, 0.5f));

        ItemEntity entity = items.Spawn(new Vector3(0.5f, 25.5f, 0.5f), Vector3.Zero, BlockId.Dirt);
        entity.PickupCooldownTicks = 5;
        pickup.TryPickup(player, items);

        Assert.Single(items.Entities);
    }

    [Fact]
    public void TryPickup_ReturnsInventoryFull_WhenInventoryIsFull()
    {
        ItemEntityWorld items = new();
        ItemPickupSystem pickup = new();
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 26f, 0.5f));

        foreach (InventorySlot slot in player.Inventory.Hotbar.Concat(player.Inventory.Storage))
        {
            slot.BlockId = BlockId.Stone;
            slot.Count = 64;
        }

        items.Spawn(new Vector3(0.5f, 25.5f, 0.5f), Vector3.Zero, BlockId.Grass).PickupCooldownTicks = 0;
        ItemPickupResult result = pickup.TryPickup(player, items);

        Assert.Equal(ItemPickupResult.InventoryFull, result);
        Assert.Single(items.Entities);
    }

    [Fact]
    public void UpdateMagnet_PullsItemsTowardPlayer()
    {
        ItemEntityWorld items = new();
        ItemPickupSystem pickup = new();
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 26f, 0.5f));

        ItemEntity entity = items.Spawn(new Vector3(2.5f, 25.5f, 0.5f), Vector3.Zero, BlockId.Dirt);
        entity.PickupCooldownTicks = 0;
        Vector3 start = entity.Position;

        pickup.UpdateMagnet(player, items, 0.2f);

        Assert.True(Vector3.Distance(entity.Position, player.Position + new Vector3(0f, 0.5f, 0f))
            < Vector3.Distance(start, player.Position + new Vector3(0f, 0.5f, 0f)));
    }

    [Fact]
    public void GameServer_BreakBlock_SpawnsItem_PickupAddsToInventory()
    {
        GameServer server = new(seed: 5, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 55200);
        int playerId = server.ConnectClient(endpoint, "Miner");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        client.Player.PitchRadians = -MathF.PI / 2f + 0.05f;
        server.World.EnsureChunksAround(0, 0, 2);

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

        int grassBefore = CountBlockInInventory(client.Player.Inventory, BlockId.Dirt);
        bool blockBroken = false;
        for (int tick = 0; tick < 60; tick++)
        {
            server.QueueInput(endpoint, breakInput);
            server.Tick();
            if (server.World.GetBlock(0, 25, 0) == BlockId.Air)
            {
                blockBroken = true;
                client.Player.Position = new Vector3(20.5f, 27f, 20.5f);
                server.QueueInput(endpoint, new PlayerInput(
                    0f,
                    0f,
                    0f,
                    0f,
                    false,
                    false,
                    false,
                    false,
                    false,
                    0,
                    client.Player.YawRadians,
                    client.Player.PitchRadians));
                break;
            }
        }

        Assert.True(blockBroken);
        Assert.Equal(BlockId.Air, server.World.GetBlock(0, 25, 0));
        for (int tick = 0; tick < 5; tick++)
        {
            server.Tick();
        }

        Assert.Equal(grassBefore, CountBlockInInventory(client.Player.Inventory, BlockId.Dirt));
        Assert.NotEmpty(server.ItemEntities.Entities);

        client.Player.Position = new Vector3(0.5f, 26f, 0.5f);
        server.QueueInput(endpoint, new PlayerInput(
            0f,
            0f,
            0f,
            0f,
            false,
            false,
            false,
            false,
            false,
            0,
            client.Player.YawRadians,
            client.Player.PitchRadians));
        for (int tick = 0; tick < 10; tick++)
        {
            server.Tick();
        }

        Assert.Equal(grassBefore + 1, CountBlockInInventory(client.Player.Inventory, BlockId.Dirt));
        Assert.DoesNotContain(server.ItemEntities.Entities, entity => entity.Stack.BlockId == BlockId.Dirt);
    }

    private static int CountBlockInInventory(PlayerInventory inventory, BlockId blockId)
    {
        int total = 0;
        foreach (InventorySlot slot in inventory.Hotbar.Concat(inventory.Storage))
        {
            if (slot.BlockId == blockId)
            {
                total += slot.Count;
            }
        }

        return total;
    }
}
