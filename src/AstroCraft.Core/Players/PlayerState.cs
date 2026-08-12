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
    public bool IsSwimming { get; set; }
    public bool IsOnGround { get; set; }
    public bool JumpedThisTick { get; set; }
    public PlayerInventory Inventory { get; } = new();
    public SurvivalState Survival { get; } = new();
    public float BreakProgress { get; set; }
    public float JumpCooldownSeconds { get; set; }
    public float FallDistance { get; set; }
    public bool JustLanded { get; set; }
    public int WorldLoadGraceTicks { get; set; }
    public BlockId BreakingBlockId { get; set; } = BlockId.Air;
    public int BreakingBlockX { get; set; }
    public int BreakingBlockY { get; set; }
    public int BreakingBlockZ { get; set; }

    public float CollisionHeight => IsSneaking ? GameConstants.PlayerSneakHeight : GameConstants.PlayerHeight;

    public float EyeHeight => IsSneaking ? GameConstants.PlayerSneakEyeHeight : GameConstants.PlayerEyeHeight;

    public Vector3 EyePosition => Position + new Vector3(0f, EyeHeight, 0f);

    public const float DefaultSpawnPitchRadians = -0.35f;
    public const float ScenicSpawnPitchRadians = -0.04f;

    /// <summary>Pitch offset for critic center shot — frames nearby terrain for AO, stone/dirt, and block outline.</summary>
    public const float CriticCenterPitchOffsetRadians = -0.08f;

    /// <summary>Pitch offset for critic look-down — frames grass fringe and scenic cave pit for lava/ore shots.</summary>
    public const float CriticLookDownPitchOffsetRadians = -0.58f;

    /// <summary>Pitch offset for critic horizon shots — frames distant fog band.</summary>
    public const float CriticHorizonPitchOffsetRadians = -0.05f;

    /// <summary>Pitch offset for critic look-up — frames sun disc and volumetric clouds.</summary>
    public const float CriticLookUpPitchOffsetRadians = 0.52f;

    public void ResetToSpawn(Vector3 spawnPosition)
    {
        Position = spawnPosition;
        Velocity = Vector3.Zero;
        YawRadians = 0f;
        PitchRadians = DefaultSpawnPitchRadians;
        Survival.ResetToSpawn();
        BreakProgress = 0f;
        BreakingBlockId = BlockId.Air;
        JumpCooldownSeconds = 0f;
        JumpedThisTick = false;
        FallDistance = 0f;
        JustLanded = false;
        WorldLoadGraceTicks = GameConstants.SpawnGraceTicks;
    }
}
