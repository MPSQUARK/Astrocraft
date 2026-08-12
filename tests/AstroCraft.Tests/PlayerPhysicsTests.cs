using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class PlayerPhysicsTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public PlayerPhysicsTests(FlatWorldFixture flat) => _flat = flat;

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void StrafeAtYawZero_MovesAlongCameraRight(float moveRight)
    {
        GameWorld world = _flat.CreateWorld(1);

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 26f, 0.5f);
        player.YawRadians = 0f;
        player.IsOnGround = true;
        player.Velocity = Vector3.Zero;

        float startX = player.Position.X;
        PlayerInput input = new(0f, moveRight, 0f, 0f, false, false, false, false, false, 0);
        physics.Simulate(player, world, input, (float)GameConstants.TickDurationSeconds);

        if (moveRight > 0f)
        {
            Assert.True(player.Position.X < startX, "moveRight=+1 (D) must strafe along camera right when facing +Z.");
        }
        else
        {
            Assert.True(player.Position.X > startX, "moveRight=-1 (A) must strafe along camera left when facing +Z.");
        }
    }

    [Fact]
    public void Jump_ReachesAtLeastOnePointTwoMeters()
    {
        GameWorld world = _flat.CreateWorld(1);

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 26f, 0.5f);
        player.IsOnGround = true;
        player.Velocity = Vector3.Zero;

        float startY = player.Position.Y;
        float peakY = startY;
        PlayerInput jump = new(0f, 0f, 0f, 0f, true, false, false, false, false, 0);
        PlayerInput air = jump with { Jump = false };

        for (int i = 0; i < 40; i++)
        {
            physics.Simulate(player, world, i == 0 ? jump : air, 0.016f);
            peakY = MathF.Max(peakY, player.Position.Y);
        }

        float apex = peakY - startY;
        Assert.True(apex >= GameConstants.JumpHeightMeters - 0.05f, $"Jump apex {apex:0.###}m should reach {GameConstants.JumpHeightMeters}m.");
    }
}
