using AstroCraft.Core;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Rendering;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Tests;

public class SoftLockTests
{
    [Fact]
    public void VoidFall_MarksPlayerDead_ForRespawn()
    {
        PlayerState player = new() { PlayerId = 1 };
        player.ResetToSpawn(new System.Numerics.Vector3(0f, GameConstants.RespawnY, 0f));
        player.Position = new System.Numerics.Vector3(0f, GameConstants.VoidFallY - 1f, 0f);

        BlockRegistry registry = BlockRegistry.CreateDefault();
        PlayerPhysics physics = new(registry);
        GameWorld world = new(registry, new FlatWorldGenerator());
        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0);
        physics.Simulate(player, world, input, (float)GameConstants.TickDurationSeconds);

        Assert.True(player.Survival.IsDead);
    }

    [Fact]
    public void Survival_ResetToSpawn_RestoresPlayableState()
    {
        SurvivalState survival = new();
        survival.ApplyDamage(GameConstants.MaxHealth);
        Assert.True(survival.IsDead);

        survival.ResetToSpawn();
        Assert.False(survival.IsDead);
        Assert.Equal(GameConstants.MaxHealth, survival.Health);
        Assert.Equal(GameConstants.MaxOxygen, survival.Oxygen);
    }

    [Fact]
    public void ChunkMeshBuilder_ProducesVertices_ForSolidChunk()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        GameWorld world = new(registry, new FlatWorldGenerator());
        Chunk chunk = world.GetOrCreateChunk(new Core.Math.ChunkPosition(0, 0));

        BlockVertex[] mesh = ChunkMeshBuilder.BuildMesh(chunk, world);
        Assert.NotEmpty(mesh);
    }
}
