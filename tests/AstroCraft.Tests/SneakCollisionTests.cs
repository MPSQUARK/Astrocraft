using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class SneakCollisionTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public SneakCollisionTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void Sneaking_ReducesCollisionHeight()
    {
        PlayerState player = new();
        player.IsSneaking = false;
        Assert.Equal(GameConstants.PlayerHeight, player.CollisionHeight);
        Assert.Equal(GameConstants.PlayerEyeHeight, player.EyeHeight);

        player.IsSneaking = true;
        Assert.Equal(GameConstants.PlayerSneakHeight, player.CollisionHeight);
        Assert.Equal(GameConstants.PlayerSneakEyeHeight, player.EyeHeight);
    }

    [Fact]
    public void Sneaking_FitsUnderLowCeiling()
    {
        GameWorld world = _flat.CreateWorld(2);
        for (int x = 0; x <= 6; x++)
        {
            world.TrySetBlock(x, 25, 0, BlockId.Air);
            world.TrySetBlock(x, 26, 0, BlockId.Air);
        }

        for (int x = 1; x <= 5; x++)
        {
            world.TrySetBlock(x, 27, 0, BlockId.Stone);
        }

        PlayerPhysics physics = new(_flat.Registry);
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 25.05f, 0.5f);
        player.YawRadians = MathF.PI / 2f;
        player.IsOnGround = true;
        player.Velocity = Vector3.Zero;
        float startX = player.Position.X;

        PlayerInput sneakForward = new(1f, 0f, 0f, 0f, false, true, false, false, false, 0);
        for (int i = 0; i < 40; i++)
        {
            physics.Simulate(player, world, sneakForward, 0.05f);
        }

        Assert.True(player.IsSneaking);
        Assert.True(player.Position.X > startX + 0.5f, "Sneaking player should pass under a low ceiling.");
    }

    [Fact]
    public void StandingHitbox_IsBlockedWhereSneakFits()
    {
        float feetY = 25.23f;
        float ceilingBottom = 27f;
        float standingTop = feetY + GameConstants.PlayerHeight - GameConstants.CollisionSkin;
        float sneakTop = feetY + GameConstants.PlayerSneakHeight - GameConstants.CollisionSkin;

        Assert.True(standingTop > ceilingBottom);
        Assert.True(sneakTop < ceilingBottom);
    }
}
