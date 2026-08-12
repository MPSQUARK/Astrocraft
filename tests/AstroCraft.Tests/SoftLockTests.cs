using AstroCraft.Client.Audio;
using AstroCraft.Client.Game;
using AstroCraft.Client.Networking;
using AstroCraft.Client.UI;
using AstroCraft.Core;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.Rendering;
using AstroCraft.Core.Server;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;
using System.Net;
using Silk.NET.Input;

namespace AstroCraft.Tests;

public class SoftLockTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public SoftLockTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void VoidFall_MarksPlayerDead_ForRespawn()
    {
        PlayerState player = new() { PlayerId = 1 };
        player.ResetToSpawn(new System.Numerics.Vector3(0f, GameConstants.RespawnY, 0f));
        player.Position = new System.Numerics.Vector3(0f, GameConstants.VoidFallY - 1f, 0f);

        PlayerPhysics physics = new(_flat.Registry);
        GameWorld world = _flat.CreateWorld(0);
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
    public void Oxygen_Drains_Underwater()
    {
        GameWorld world = _flat.CreateWorld(0);
        world.EnsureChunksAround(20, 0, 1);

        PlayerState player = new();
        player.Position = new System.Numerics.Vector3(20.5f, GameConstants.SeaLevel - 3f, 0.5f);
        float initialOxygen = player.Survival.Oxygen;

        SurvivalSimulator simulator = new();
        simulator.Update(player, world, (float)GameConstants.TickDurationSeconds);

        Assert.True(player.Survival.Oxygen < initialOxygen);
        Assert.True(world.IsSubmerged(
            BlockPosition.FromWorld(player.EyePosition.X, player.EyePosition.Y, player.EyePosition.Z).X,
            BlockPosition.FromWorld(player.EyePosition.X, player.EyePosition.Y, player.EyePosition.Z).Y,
            BlockPosition.FromWorld(player.EyePosition.X, player.EyePosition.Y, player.EyePosition.Z).Z));
    }

    [Fact]
    public void Oxygen_Recovers_OnSurface()
    {
        GameWorld world = _flat.CreateWorld(0);
        PlayerState player = new();
        player.Position = new System.Numerics.Vector3(0.5f, 26f, 0.5f);
        player.Survival.Oxygen = 10f;

        SurvivalSimulator simulator = new();
        simulator.Update(player, world, (float)GameConstants.TickDurationSeconds);

        Assert.True(player.Survival.Oxygen > 10f);
    }

    [Fact]
    public void Oxygen_Depletion_CausesDeath_AndRespawnRestoresState()
    {
        GameWorld world = _flat.CreateWorld(0);
        world.EnsureChunksAround(20, 0, 1);
        PlayerState player = new();
        player.Position = new System.Numerics.Vector3(20.5f, GameConstants.SeaLevel - 3f, 0.5f);
        player.Survival.Oxygen = 0f;

        SurvivalSimulator simulator = new();
        for (int tick = 0; tick < 25; tick++)
        {
            simulator.Update(player, world, (float)GameConstants.TickDurationSeconds);
        }

        Assert.True(player.Survival.IsDead);

        GameServer server = new(seed: 1, flatWorld: true);
        IPEndPoint endpoint = new(IPAddress.Loopback, 22401);
        int playerId = server.ConnectClient(endpoint, "Diver");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.Survival.ApplyDamage(GameConstants.MaxHealth);
        Assert.True(client.Player.Survival.IsDead);

        for (int tick = 0; tick <= GameConstants.RespawnDelayTicks; tick++)
        {
            server.Tick();
        }

        Assert.False(client.Player.Survival.IsDead);
        Assert.Equal(GameConstants.MaxHealth, client.Player.Survival.Health);
        Assert.Equal(GameConstants.MaxOxygen, client.Player.Survival.Oxygen);
    }

    [Fact]
    public void OilBlock_SuffocatesPlayer_WhenHeadSubmerged()
    {
        GameWorld world = _flat.CreateWorld(0);
        PlayerState player = new();
        player.Position = new System.Numerics.Vector3(4.5f, 24f, 4.5f);
        BlockPosition head = BlockPosition.FromWorld(player.EyePosition.X, player.EyePosition.Y, player.EyePosition.Z);
        world.TrySetBlock(head.X, head.Y, head.Z, BlockId.Oil);

        float initialOxygen = player.Survival.Oxygen;
        SurvivalSimulator simulator = new();
        simulator.Update(player, world, (float)GameConstants.TickDurationSeconds);

        Assert.True(player.Survival.Oxygen < initialOxygen);
    }

    [Fact]
    public void ChunkMeshBuilder_ProducesVertices_ForSolidChunk()
    {
        GameWorld world = _flat.CreateWorld(0);
        Chunk chunk = world.GetOrCreateChunk(new Core.Math.ChunkPosition(0, 0));

        ChunkMeshData mesh = ChunkMeshBuilder.BuildMeshes(chunk, world);
        Assert.NotEmpty(mesh.Opaque);
    }

    [Fact]
    public void DeathScreen_DoesNotBlockRespawnCountdown()
    {
        PlayerState player = new();
        player.Survival.ApplyDamage(GameConstants.MaxHealth);
        Assert.True(player.Survival.IsDead);
        Assert.True(player.Survival.RespawnTicksRemaining > 0);

        for (int tick = 0; tick <= GameConstants.RespawnDelayTicks; tick++)
        {
            player.Survival.RespawnTicksRemaining--;
        }

        player.Survival.ResetToSpawn();
        Assert.False(player.Survival.IsDead);
    }

    [Fact]
    public void InventoryOpenClose_DoesNotSoftLockHudState()
    {
        GameHud hud = new();
        hud.IsInventoryOpen = true;
        hud.IsPaused = false;
        Assert.True(hud.IsInventoryOpen);

        hud.IsInventoryOpen = false;
        Assert.False(hud.IsInventoryOpen);
        Assert.False(hud.IsPaused);
    }

    [Fact]
    public void PlacementGhost_TargetBlock_DoesNotSoftLockWhenAirSelected()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new System.Numerics.Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Air;

        bool hasTarget = interaction.TryGetTargetBlock(player, world, out _, out _, out _);
        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        bool placed = interaction.TryPlaceBlock(player, world, input);

        Assert.True(hasTarget);
        Assert.False(placed);
    }

    [Fact]
    public void ReconnectFlow_ClientRemainsPollableWhileDisconnected()
    {
        using GameClientSession session = new("127.0.0.1", 27199, flatWorldHint: true);
        Assert.False(session.IsConnected);
        Assert.False(session.WasEverConnected);

        session.Poll();
        session.DrainPendingPackets();
        session.AttemptReconnect();

        Assert.Equal(1, session.ReconnectAttempts);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public void GameHud_TriggersPickupAndInventoryFullHints()
    {
        GameHud hud = new();
        Assert.Equal(0f, hud.PickupFlashTimer);
        Assert.Equal(0f, hud.InventoryFullHintTimer);

        hud.TriggerPickupFlash();
        hud.TriggerInventoryFullHint();
        Assert.True(hud.PickupFlashTimer > 0f);
        Assert.True(hud.InventoryFullHintTimer > 0f);

        float flagsWithHints = hud.BuildHudFlags();
        Assert.True(((int)flagsWithHints & 512) != 0);
        Assert.True(((int)flagsWithHints & 256) != 0);

        hud.TickHints(10f);
        Assert.Equal(0f, hud.PickupFlashTimer);
        Assert.Equal(0f, hud.InventoryFullHintTimer);
    }

    [Fact]
    public void BlockSounds_ResolveDistinctMaterialGroups()
    {
        Assert.Equal(BlockMaterialGroup.Stone, GameSound.ResolveMaterialGroup(BlockId.Stone));
        Assert.Equal(BlockMaterialGroup.Wood, GameSound.ResolveMaterialGroup(BlockId.Wood));
        Assert.Equal(BlockMaterialGroup.Sand, GameSound.ResolveMaterialGroup(BlockId.Sand));
        Assert.Equal(BlockMaterialGroup.Gravel, GameSound.ResolveMaterialGroup(BlockId.Gravel));
        Assert.Equal(BlockMaterialGroup.Glass, GameSound.ResolveMaterialGroup(BlockId.Glass));
        Assert.NotEqual(
            GameSound.ResolveMaterialGroup(BlockId.Stone),
            GameSound.ResolveMaterialGroup(BlockId.Glass));
    }
}
