using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class BlockInteractionTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public BlockInteractionTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void TryPlaceBlock_PlacesConcreteOnAdjacentAirWhenLookingAtGround()
    {
        GameWorld world = _flat.CreateWorld(2);
        Assert.Equal(BlockId.Grass, world.GetBlock(0, 25, 0));
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Concrete;
        player.Inventory.Hotbar[0].Count = 64;

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        bool placed = interaction.TryPlaceBlock(player, world, input);

        Assert.True(placed);
        Assert.Equal(BlockId.Concrete, world.GetBlock(0, 26, 0));
    }

    [Fact]
    public void TryPlaceBlock_OrientsLogAlongPlacementFace()
    {
        GameWorld world = _flat.CreateWorld(2);
        world.TrySetBlock(0, 26, 0, BlockId.Stone);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(-2.5f, 26f, 0.5f));
        player.YawRadians = MathF.PI / 2f;
        player.PitchRadians = -0.4f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Wood;
        player.Inventory.Hotbar[0].Count = 64;

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        bool placed = interaction.TryPlaceBlock(player, world, input);

        Assert.True(placed);
        Assert.Equal(BlockId.Wood, world.GetBlock(-1, 26, 0));
        Assert.Equal(BlockAxis.X, world.GetBlockAxis(-1, 26, 0));
    }

    [Fact]
    public void TryPlaceBlock_OrientsLogVertically_OnTopFace()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;
        player.Inventory.Hotbar[0].BlockId = BlockId.BirchLog;
        player.Inventory.Hotbar[0].Count = 64;

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        bool placed = interaction.TryPlaceBlock(player, world, input);

        Assert.True(placed);
        Assert.Equal(BlockId.BirchLog, world.GetBlock(0, 26, 0));
        Assert.Equal(BlockAxis.Y, world.GetBlockAxis(0, 26, 0));
    }

    [Fact]
    public void TryPlaceBlock_PlacesTorchOnWall()
    {
        GameWorld world = _flat.CreateWorld(2);
        world.TrySetBlock(0, 26, 0, BlockId.Stone);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(-2.5f, 26f, 0.5f));
        player.YawRadians = MathF.PI / 2f;
        player.PitchRadians = -0.4f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Torch;
        player.Inventory.Hotbar[0].Count = 4;

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        bool placed = interaction.TryPlaceBlock(player, world, input);

        Assert.True(placed);
        Assert.Equal(BlockId.Torch, world.GetBlock(-1, 26, 0));
        Assert.Equal(BlockAxis.X, world.GetBlockAxis(-1, 26, 0));
    }

    [Fact]
    public void TryPlaceBlock_PlacesTorchOnGround()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Torch;
        player.Inventory.Hotbar[0].Count = 4;

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        bool placed = interaction.TryPlaceBlock(player, world, input);

        Assert.True(placed);
        Assert.Equal(BlockId.Torch, world.GetBlock(0, 26, 0));
        Assert.Equal(BlockAxis.Y, world.GetBlockAxis(0, 26, 0));
    }

    [Fact]
    public void BlockAxis_FromPlacementFace_PrefersDominantAxis()
    {
        Assert.Equal(BlockAxis.Y, BlockAxisExtensions.FromPlacementFace(new Vector3(0f, 1f, 0f)));
        Assert.Equal(BlockAxis.X, BlockAxisExtensions.FromPlacementFace(new Vector3(-1f, 0f, 0f)));
        Assert.Equal(BlockAxis.Z, BlockAxisExtensions.FromPlacementFace(new Vector3(0f, 0f, 1f)));
    }

    [Fact]
    public void TryGetTargetBlock_ReturnsGrassBlockWhenLookingDownAtGround()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;

        bool found = interaction.TryGetTargetBlock(player, world, out BlockPosition target, out BlockId blockId, out Vector3 faceNormal);

        Assert.True(found);
        Assert.Equal(BlockId.Grass, blockId);
        Assert.Equal(0, target.X);
        Assert.Equal(25, target.Y);
        Assert.Equal(0, target.Z);
        Assert.Equal(new Vector3(0f, 1f, 0f), faceNormal);
    }

    [Fact]
    public void UpdateBreaking_CanBreakMultipleBlocksWithoutException()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 26.2f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;

        PlayerInput holdBreak = new(0f, 0f, 0f, 0f, false, false, false, true, false, 0);
        ItemEntityWorld items = new();
        ItemPickupSystem pickup = new();
        for (int block = 0; block < 3; block++)
        {
            for (int tick = 0; tick < 40; tick++)
            {
                interaction.UpdateBreaking(
                    player,
                    world,
                    holdBreak,
                    0.05f,
                    out BlockId brokenBlock,
                    out int brokenX,
                    out int brokenY,
                    out int brokenZ);
                if (brokenBlock != BlockId.Air)
                {
                    ItemEntity dropped = items.SpawnAtBlock(brokenX, brokenY, brokenZ, brokenBlock);
                    dropped.PickupCooldownTicks = 0;
                }

                items.Update(world, 0.05f);
                pickup.TryPickup(player, items);
            }
        }

        Assert.Contains(player.Inventory.Hotbar.Concat(player.Inventory.Storage), slot => slot.Count > 0);
    }

    [Fact]
    public void UpdateBreaking_ResetsProgressWhenTargetBlockChanges()
    {
        GameWorld world = _flat.CreateWorld(2);
        world.TrySetBlock(1, 25, 0, BlockId.Stone);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;

        PlayerInput holdBreak = new(0f, 0f, 0f, 0f, false, false, false, true, false, 0);
        interaction.UpdateBreaking(
            player,
            world,
            holdBreak,
            0.2f,
            out BlockId _,
            out int _,
            out int _,
            out int _);
        Assert.Equal(0, player.BreakingBlockX);
        Assert.Equal(25, player.BreakingBlockY);
        float progressBeforeMove = player.BreakProgress;
        Assert.True(progressBeforeMove > 0f);

        player.Position = new Vector3(1.5f, 27f, 0.5f);
        interaction.UpdateBreaking(
            player,
            world,
            holdBreak,
            0.05f,
            out BlockId _,
            out int _,
            out int _,
            out int _);

        Assert.Equal(1, player.BreakingBlockX);
        Assert.True(player.BreakProgress < progressBeforeMove);
    }

    [Fact]
    public void IsWithinPlacementReach_ReturnsFalse_WhenBlockCenterIsTooFar()
    {
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 27f, 0.5f);
        BlockPosition farPlacement = new(10, 26, 0);

        Assert.False(BlockInteractionSystem.IsWithinPlacementReach(player, farPlacement));
    }

    [Fact]
    public void UpdateBreaking_ResetsProgressWhenBreakReleased()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;

        PlayerInput holdBreak = new(0f, 0f, 0f, 0f, false, false, false, true, false, 0);
        interaction.UpdateBreaking(
            player,
            world,
            holdBreak,
            0.05f,
            out BlockId _,
            out int _,
            out int _,
            out int _);
        Assert.True(player.BreakProgress > 0f);

        PlayerInput releaseBreak = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0);
        interaction.UpdateBreaking(
            player,
            world,
            releaseBreak,
            0.05f,
            out BlockId _,
            out int _,
            out int _,
            out int _);
        Assert.Equal(0f, player.BreakProgress);
    }
}
