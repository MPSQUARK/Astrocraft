namespace AstroCraft.Core;

public static class GameConstants
{
    public const int TickRate = 20;
    public const double TickDurationSeconds = 1.0 / TickRate;

    public const int ChunkSizeX = 16;
    public const int ChunkSizeY = 64;
    public const int ChunkSizeZ = 16;

    public const int WorldHeight = ChunkSizeY;
    public const int SeaLevel = 28;

    public const float BlockReachDistance = 5.5f;
    public const float PlayerHeight = 1.8f;
    public const float PlayerEyeHeight = 1.62f;
    public const float PlayerSneakHeight = 1.5f;
    public const float PlayerSneakEyeHeight = 1.35f;
    public const float PlayerWidth = 0.6f;
    public const float Gravity = 28f;
    public const float SwimGravity = 4.2f;
    public const float SwimSpeed = 2.0f;
    public const float SwimDrag = 6f;
    public const float SwimAscendSpeed = 3.5f;
    public const float JumpHeightMeters = 1.2f;
    public const float JumpVelocity = 8.367f; // sqrt(2 * Gravity * JumpHeightMeters) ≈ 1.2 m apex
    public const float StepHeight = 1.0f;
    public const float CollisionSkin = 0.02f;
    public const float WalkSpeed = 2.8f;
    public const float SprintSpeed = 4.0f;
    public const float SneakSpeed = 1.1f;
    public const float JumpCooldownSeconds = 0.35f;

    public const int HotbarSize = 9;
    public const int InventoryRows = 3;
    public const int InventoryColumns = 9;
    public const int InventorySize = InventoryRows * InventoryColumns;

    public const int DefaultViewDistanceChunks = 4;
    /// <summary>Client meshes every chunk the server streams (same radius).</summary>
    public const int ClientMeshDistanceChunks = DefaultViewDistanceChunks;
    public const int NearChunkRadius = 2;
    public const int RearChunkRadius = 1;
    public const int MaxChunkRequestsPerPacket = 12;
    public const int MaxClientChunkRequestsPerTick = 6;
    public const int MaxServerChunkResponsesPerTick = 8;
    public const int FullDetailMeshDistanceChunks = 2;
    public const int SurfaceOnlyMeshDistanceChunks = 4;
    public const int MaxPlayers = 16;

    public const int DiscoveryPort = 27015;
    public const int DefaultGamePort = 27016;
    public const int SnapshotIntervalTicks = 100;

    public const float MaxHealth = 20f;
    public const float MaxHunger = 20f;
    public const float MaxSaturation = 20f;
    public const float MaxOxygen = 20f;
    public const float OxygenDrainPerTick = 0.05f;
    public const float SuffocationDamagePerTick = 1f;

    // Minecraft-style hunger: exhaustion accumulates from activity; every 4 points drains saturation or 0.5 hunger.
    public const float HungerExhaustionThreshold = 4f;
    public const float HungerLossPerExhaustionCycle = 0.5f;
    public const float SaturationLossPerExhaustionCycle = 1f;
    public const float ExhaustionPerMeterWalk = 0.01f;
    public const float ExhaustionPerMeterSprint = 0.1f;
    public const float ExhaustionPerJump = 0.05f;
    public const float StarvationDamagePerTick = 1f / 80f;
    public const float LavaDamagePerTick = 2f;
    public const float SafeFallDistanceBlocks = 3f;
    public const float FallDamagePerBlockBeyondSafe = 1f;
    public const float OxygenLowThreshold = 5f;
    public const int RespawnDelayTicks = 40;
    public const int SpawnGraceTicks = 100;
    public const float VoidFallY = -10f;
    public const float RespawnY = SeaLevel + 4f;

    public const float ItemPickupRadius = 1.5f;
    public const float ItemMagnetRadius = 3.5f;
    public const float ItemMagnetPullSpeed = 5f;
    public const int ItemPickupDelayTicks = 8;
    public const float ItemEntitySyncRadius = 12f;

    public const double DayCycleSeconds = 20 * 60;
}
