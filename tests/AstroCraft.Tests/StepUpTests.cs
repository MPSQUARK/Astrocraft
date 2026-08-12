using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class StepUpTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public StepUpTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void WalkingIntoOneBlockLedge_StepsUpUsingStepHeight()
    {
        GameWorld world = _flat.CreateWorld(2);

        int floorY = 26;
        for (int x = -1; x <= 1; x++)
        {
            world.TrySetBlock(x, floorY, 2, BlockId.Stone);
        }

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(0.5f, floorY, 1.85f);
        player.IsOnGround = true;
        player.Velocity = Vector3.Zero;
        float startY = player.Position.Y;

        PlayerInput walk = new(1f, 0f, 0f, 0f, false, false, false, false, false, 0);
        for (int i = 0; i < 10; i++)
        {
            physics.Simulate(player, world, walk, 0.05f);
        }

        Assert.True(player.Position.Z >= 1.95f);
        Assert.True(player.Position.Y >= startY + GameConstants.StepHeight - 0.15f);
    }

    [Fact]
    public void StepUp_ClimbsAtMostOneBlockPerObstacle()
    {
        GameWorld world = _flat.CreateWorld(2);

        int floorY = 26;
        for (int x = -1; x <= 1; x++)
        {
            world.TrySetBlock(x, floorY, 2, BlockId.Stone);
            world.TrySetBlock(x, floorY + 1, 2, BlockId.Stone);
        }

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(0.5f, floorY, 0.5f);
        player.IsOnGround = true;

        PlayerInput walk = new(1f, 0f, 0f, 0f, false, false, false, false, false, 0);
        for (int i = 0; i < 20; i++)
        {
            physics.Simulate(player, world, walk, 0.05f);
        }

        Assert.True(player.Position.Y < floorY + GameConstants.StepHeight + 0.2f,
            "A single step-up should not climb a 2-block wall in one motion.");
    }
}
