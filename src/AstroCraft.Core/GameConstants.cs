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
    public const float PlayerWidth = 0.6f;
    public const float Gravity = 28f;
    public const float JumpVelocity = 9.5f;
    public const float WalkSpeed = 4.3f;
    public const float SprintSpeed = 5.6f;
    public const float SneakSpeed = 1.3f;

    public const int HotbarSize = 9;
    public const int InventoryRows = 3;
    public const int InventoryColumns = 9;
    public const int InventorySize = InventoryRows * InventoryColumns;

    public const int DefaultViewDistanceChunks = 6;
    public const int MaxPlayers = 16;

    public const int DiscoveryPort = 27015;
    public const int DefaultGamePort = 27016;
    public const int SnapshotIntervalTicks = 100;

    public const float MaxHealth = 20f;
    public const float MaxHunger = 20f;
    public const float MaxOxygen = 20f;
    public const float OxygenDrainPerTick = 0.05f;
    public const float SuffocationDamagePerTick = 1f;
    public const float VoidFallY = -10f;
    public const float RespawnY = SeaLevel + 4f;
}
