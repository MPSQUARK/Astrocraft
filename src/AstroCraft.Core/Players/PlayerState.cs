using System.Numerics;
using AstroCraft.Core.Blocks;

namespace AstroCraft.Core.Players;

public sealed class PlayerState
{
    public int PlayerId { get; init; }
    public string DisplayName { get; set; } = "Player";
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public float YawRadians { get; set; }
    public float PitchRadians { get; set; }
    public bool IsSneaking { get; set; }
    public bool IsSprinting { get; set; }
    public bool IsOnGround { get; set; }
    public PlayerInventory Inventory { get; } = new();
    public SurvivalState Survival { get; } = new();
    public float BreakProgress { get; set; }
    public BlockId BreakingBlockId { get; set; } = BlockId.Air;
    public int BreakingBlockX { get; set; }
    public int BreakingBlockY { get; set; }
    public int BreakingBlockZ { get; set; }

    public Vector3 EyePosition => Position + new Vector3(0f, GameConstants.PlayerEyeHeight, 0f);

    public void ResetToSpawn(Vector3 spawnPosition)
    {
        Position = spawnPosition;
        Velocity = Vector3.Zero;
        Survival.ResetToSpawn();
        BreakProgress = 0f;
        BreakingBlockId = BlockId.Air;
    }
}
