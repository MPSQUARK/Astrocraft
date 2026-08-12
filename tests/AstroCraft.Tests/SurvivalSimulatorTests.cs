using AstroCraft.Core;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;
using System.Numerics;

namespace AstroCraft.Tests;

public class SurvivalSimulatorTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;
    private readonly GameWorld _world;
    private static readonly float TickDelta = (float)GameConstants.TickDurationSeconds;

    public SurvivalSimulatorTests(FlatWorldFixture flat)
    {
        _flat = flat;
        _world = flat.CreateWorld(1);
    }

    [Fact]
    public void Hunger_DoesNotDrain_WhenIdle()
    {
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 26f, 0.5f);
        player.Velocity = Vector3.Zero;
        player.IsSprinting = false;

        SurvivalSimulator simulator = new();
        for (int tick = 0; tick < 200; tick++)
        {
            simulator.Update(player, _world, TickDelta);
        }

        Assert.Equal(GameConstants.MaxHunger, player.Survival.Hunger);
        Assert.Equal(GameConstants.MaxSaturation, player.Survival.Saturation);
    }

    [Fact]
    public void Hunger_DrainsSaturationFirst_WhenSprinting()
    {
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 26f, 0.5f);
        player.IsSprinting = true;
        player.Velocity = new Vector3(0f, 0f, GameConstants.SprintSpeed);

        float initialSaturation = player.Survival.Saturation;
        SurvivalSimulator simulator = new();
        for (int tick = 0; tick < 300; tick++)
        {
            simulator.Update(player, _world, TickDelta);
        }

        Assert.Equal(GameConstants.MaxHunger, player.Survival.Hunger);
        Assert.True(player.Survival.Saturation < initialSaturation);
    }

    [Fact]
    public void Hunger_Drains_WhenSprinting_AfterSaturationDepleted()
    {
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 26f, 0.5f);
        player.IsSprinting = true;
        player.Velocity = new Vector3(0f, 0f, GameConstants.SprintSpeed);
        player.Survival.Saturation = 0f;

        float initialHunger = player.Survival.Hunger;
        SurvivalSimulator simulator = new();
        for (int tick = 0; tick < 500; tick++)
        {
            simulator.Update(player, _world, TickDelta);
        }

        Assert.True(player.Survival.Hunger < initialHunger);
    }

    [Fact]
    public void Jump_AddsExhaustion()
    {
        SurvivalState survival = new();
        float initialSaturation = survival.Saturation;

        for (int jump = 0; jump < 80; jump++)
        {
            survival.AddExhaustion(GameConstants.ExhaustionPerJump);
        }

        Assert.True(survival.Saturation < initialSaturation || survival.Hunger < GameConstants.MaxHunger);
    }

    [Fact]
    public void Starvation_DamagesHealth_WhenHungerEmpty()
    {
        PlayerState player = new();
        player.Position = new Vector3(0.5f, 26f, 0.5f);
        player.Survival.Hunger = 0f;
        player.Survival.Saturation = 0f;

        float initialHealth = player.Survival.Health;
        SurvivalSimulator simulator = new();
        for (int tick = 0; tick < 200; tick++)
        {
            simulator.Update(player, _world, TickDelta);
        }

        Assert.True(player.Survival.Health < initialHealth);
    }

    [Fact]
    public void ResetToSpawn_RestoresHungerAndSaturation()
    {
        SurvivalState survival = new();
        survival.Hunger = 0f;
        survival.Saturation = 0f;
        survival.Exhaustion = 3f;

        survival.ResetToSpawn();

        Assert.Equal(GameConstants.MaxHunger, survival.Hunger);
        Assert.Equal(GameConstants.MaxSaturation, survival.Saturation);
        Assert.Equal(0d, survival.Exhaustion);
    }
}
