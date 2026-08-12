using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class SwimmingTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public SwimmingTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void SubmergedPlayer_IsSwimming_AndFallsSlowerThanOnLand()
    {
        GameWorld world = _flat.CreateWorld(0);
        world.EnsureChunksAround(20, 0, 1);
        FillWaterColumn(world, 20, 0, 24, 29);

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(20.5f, 27f, 0.5f);
        player.Velocity = Vector3.Zero;

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0);
        physics.Simulate(player, world, input, 0.05f);
        Assert.True(player.IsSwimming);

        float submergedY = player.Position.Y;
        for (int i = 0; i < 9; i++)
        {
            physics.Simulate(player, world, input, 0.05f);
        }

        float submergedDrop = submergedY - player.Position.Y;

        player.Position = new Vector3(0.5f, 40f, 0.5f);
        player.IsOnGround = false;
        player.Velocity = Vector3.Zero;
        float airY = player.Position.Y;
        for (int i = 0; i < 10; i++)
        {
            physics.Simulate(player, world, input, 0.05f);
        }

        float airDrop = airY - player.Position.Y;
        Assert.True(submergedDrop < airDrop * 0.5f, "Swim gravity should fall much slower than normal gravity.");
    }

    [Fact]
    public void Swimming_DisablesSprintSpeed()
    {
        GameWorld world = _flat.CreateWorld(0);
        world.EnsureChunksAround(20, 0, 1);
        FillWaterColumn(world, 20, 0, 24, 29);

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(20.5f, 27f, 0.5f);

        PlayerInput sprint = new(1f, 0f, 0f, 0f, false, false, true, false, false, 0);
        physics.Simulate(player, world, sprint, (float)GameConstants.TickDurationSeconds);

        Assert.True(player.IsSwimming);
        Assert.False(player.IsSprinting);
        Assert.True(MathF.Abs(player.Velocity.X) <= GameConstants.SwimSpeed + 0.01f);
    }

    [Fact]
    public void Swimming_AppliesHorizontalDrag_WhenInputReleased()
    {
        GameWorld world = _flat.CreateWorld(0);
        world.EnsureChunksAround(20, 0, 1);
        FillWaterColumn(world, 20, 0, 24, 29);

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(20.5f, 27f, 0.5f);

        PlayerInput move = new(1f, 0f, 0f, 0f, false, false, false, false, false, 0);
        physics.Simulate(player, world, move, 0.1f);
        float speedAfterMove = MathF.Sqrt(player.Velocity.X * player.Velocity.X + player.Velocity.Z * player.Velocity.Z);

        PlayerInput idle = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0);
        for (int i = 0; i < 5; i++)
        {
            physics.Simulate(player, world, idle, 0.1f);
        }

        float speedAfterDrag = MathF.Sqrt(player.Velocity.X * player.Velocity.X + player.Velocity.Z * player.Velocity.Z);
        Assert.True(speedAfterMove > 0.5f);
        Assert.True(speedAfterDrag < speedAfterMove * 0.5f);
    }

    [Fact]
    public void OilCountsAsSubmerged_ForSwimming()
    {
        GameWorld world = _flat.CreateWorld(0);
        for (int y = 24; y <= 28; y++)
        {
            world.TrySetBlock(4, y, 4, BlockId.Oil);
        }

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(4.5f, 25f, 4.5f);

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0);
        physics.Simulate(player, world, input, (float)GameConstants.TickDurationSeconds);

        Assert.True(player.IsSwimming);
    }

    private static void FillWaterColumn(GameWorld world, int x, int z, int minY, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            world.TrySetBlock(x, y, z, BlockId.Water);
        }
    }
}
