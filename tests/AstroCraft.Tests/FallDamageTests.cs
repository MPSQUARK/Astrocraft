using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class FallDamageTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;
    private static readonly float TickDelta = (float)GameConstants.TickDurationSeconds;
    private static readonly PlayerInput IdleInput = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0);

    public FallDamageTests(FlatWorldFixture flat) => _flat = flat;

    [Theory]
    [InlineData(3f, 0f)]
    [InlineData(2.5f, 0f)]
    [InlineData(4f, 1f)]
    [InlineData(8f, 5f)]
    public void ComputeFallDamage_ScalesBeyondSafeThreshold(float fallDistance, float expectedDamage)
    {
        Assert.Equal(expectedDamage, SurvivalSimulator.ComputeFallDamage(fallDistance));
    }

    [Fact]
    public void FallDamage_NoDamage_WhenLandingWithinSafeDistance()
    {
        GameWorld world = _flat.CreateWorld(1);

        PlayerPhysics physics = new(_flat.Registry);
        SurvivalSimulator survival = new();
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 28.5f, 0.5f);
        player.Velocity = Vector3.Zero;
        player.IsOnGround = false;

        float initialHealth = player.Survival.Health;
        SimulateUntilGrounded(physics, survival, player, world, maxTicks: 120);

        Assert.True(player.IsOnGround);
        Assert.Equal(initialHealth, player.Survival.Health);
    }

    [Fact]
    public void FallDamage_AppliesDamage_WhenLandingBeyondSafeDistance()
    {
        GameWorld world = _flat.CreateWorld(1);

        PlayerPhysics physics = new(_flat.Registry);
        SurvivalSimulator survival = new();
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 35f, 0.5f);
        player.Velocity = Vector3.Zero;
        player.IsOnGround = false;

        float initialHealth = player.Survival.Health;
        SimulateUntilGrounded(physics, survival, player, world, maxTicks: 200);

        Assert.True(player.IsOnGround);
        Assert.True(player.Survival.Health < initialHealth);
        Assert.True(player.FallDistance <= 0.01f);
    }

    [Fact]
    public void FallDamage_SurvivalSimulatorApplies_OnJustLanded()
    {
        PlayerState player = new();
        player.JustLanded = true;
        player.FallDistance = 6f;
        player.Position = new Vector3(0.5f, 26f, 0.5f);

        GameWorld world = _flat.CreateWorld(0);
        SurvivalSimulator survival = new();

        float initialHealth = player.Survival.Health;
        survival.Update(player, world, TickDelta);

        Assert.Equal(initialHealth - 3f, player.Survival.Health);
        Assert.Equal(0f, player.FallDistance);
        Assert.False(player.JustLanded);
    }

    private static void SimulateUntilGrounded(
        PlayerPhysics physics,
        SurvivalSimulator survival,
        PlayerState player,
        GameWorld world,
        int maxTicks)
    {
        for (int tick = 0; tick < maxTicks; tick++)
        {
            physics.Simulate(player, world, IdleInput, TickDelta);
            survival.Update(player, world, TickDelta);
            if (player.IsOnGround && tick > 0)
            {
                return;
            }
        }
    }
}
