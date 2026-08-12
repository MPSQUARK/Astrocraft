using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class VoidDamageTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;
    private static readonly float TickDelta = (float)GameConstants.TickDurationSeconds;

    public VoidDamageTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void VoidDamage_KillsPlayer_WhenBelowVoidFallY()
    {
        GameWorld world = _flat.CreateWorld(0);
        PlayerState player = new();
        player.Position = new Vector3(0.5f, GameConstants.VoidFallY - 1f, 0.5f);

        SurvivalSimulator simulator = new();
        simulator.Update(player, world, TickDelta);

        Assert.True(player.Survival.IsDead);
        Assert.Equal(0f, player.Survival.Health);
    }

    [Fact]
    public void VoidDamage_DoesNotKillPlayer_WhenAboveVoidFallY()
    {
        GameWorld world = _flat.CreateWorld(0);
        PlayerState player = new();
        player.Position = new Vector3(0.5f, GameConstants.VoidFallY, 0.5f);

        SurvivalSimulator simulator = new();
        simulator.Update(player, world, TickDelta);

        Assert.False(player.Survival.IsDead);
        Assert.Equal(GameConstants.MaxHealth, player.Survival.Health);
    }
}
