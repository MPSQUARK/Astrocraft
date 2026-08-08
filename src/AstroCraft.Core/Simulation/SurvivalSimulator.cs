using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Simulation;

public sealed class SurvivalSimulator
{
    public void Update(PlayerState player, GameWorld world)
    {
        if (player.Survival.IsDead)
        {
            return;
        }

        BlockPosition head = BlockPosition.FromWorld(player.EyePosition.X, player.EyePosition.Y, player.EyePosition.Z);
        bool submerged = world.IsSubmerged(head.X, head.Y, head.Z);
        bool breathable = world.IsBreathable(head.X, head.Y, head.Z);

        if (!breathable || submerged)
        {
            player.Survival.Oxygen = System.Math.Max(0f, player.Survival.Oxygen - GameConstants.OxygenDrainPerTick);
            if (player.Survival.Oxygen <= 0f)
            {
                player.Survival.ApplyDamage(GameConstants.SuffocationDamagePerTick);
            }
        }
        else
        {
            player.Survival.Oxygen = System.Math.Min(GameConstants.MaxOxygen, player.Survival.Oxygen + GameConstants.OxygenDrainPerTick * 2f);
        }

        player.Survival.Hunger = System.Math.Max(0f, player.Survival.Hunger - 0.002f);
    }
}

public sealed class BlockInteractionSystem
{
    private readonly BlockRegistry _blockRegistry;

    public BlockInteractionSystem(BlockRegistry blockRegistry)
    {
        _blockRegistry = blockRegistry;
    }

    public void UpdateBreaking(PlayerState player, GameWorld world, PlayerInput input)
    {
        if (!input.BreakBlock)
        {
            ResetBreaking(player);
            return;
        }

        if (!TryGetTargetBlock(player, world, out BlockPosition target, out BlockId targetBlock))
        {
            ResetBreaking(player);
            return;
        }

        BlockDefinition definition = _blockRegistry.Get(targetBlock);
        if (!definition.IsBreakable)
        {
            ResetBreaking(player);
            return;
        }

        if (player.BreakingBlockId != targetBlock
            || player.BreakingBlockX != target.X
            || player.BreakingBlockY != target.Y
            || player.BreakingBlockZ != target.Z)
        {
            player.BreakingBlockId = targetBlock;
            player.BreakingBlockX = target.X;
            player.BreakingBlockY = target.Y;
            player.BreakingBlockZ = target.Z;
            player.BreakProgress = 0f;
        }

        float hardness = System.Math.Max(0.1f, definition.Hardness);
        player.BreakProgress += 1f / (hardness * 20f);
        if (player.BreakProgress < 1f)
        {
            return;
        }

        world.TrySetBlock(target.X, target.Y, target.Z, BlockId.Air);
        player.Inventory.TryAddBlock(targetBlock);
        ResetBreaking(player);
    }

    public bool TryPlaceBlock(PlayerState player, GameWorld world, PlayerInput input)
    {
        if (!input.PlaceBlock)
        {
            return false;
        }

        BlockId selected = player.Inventory.SelectedHotbarSlot.BlockId;
        if (selected == BlockId.Air)
        {
            return false;
        }

        if (!TryGetPlacementPosition(player, world, out BlockPosition placement))
        {
            return false;
        }

        if (!world.TrySetBlock(placement.X, placement.Y, placement.Z, selected))
        {
            return false;
        }

        return player.Inventory.TryConsumeSelected(selected);
    }

    private bool TryGetTargetBlock(PlayerState player, GameWorld world, out BlockPosition target, out BlockId blockId)
    {
        return Raycast(player, world, out target, out blockId, includeAir: false);
    }

    private bool TryGetPlacementPosition(PlayerState player, GameWorld world, out BlockPosition placement)
    {
        if (!Raycast(player, world, out BlockPosition hit, out _, includeAir: false))
        {
            placement = default;
            return false;
        }

        Vector3 eye = player.EyePosition;
        Vector3 direction = GetLookDirection(player.YawRadians, player.PitchRadians);
        Vector3 hitPoint = eye + direction * GameConstants.BlockReachDistance;
        Vector3 blockCenter = new(hit.X + 0.5f, hit.Y + 0.5f, hit.Z + 0.5f);
        Vector3 normal = Vector3.Normalize(hitPoint - blockCenter);
        int px = hit.X + (int)MathF.Round(normal.X);
        int py = hit.Y + (int)MathF.Round(normal.Y);
        int pz = hit.Z + (int)MathF.Round(normal.Z);
        placement = new BlockPosition(px, py, pz);

        if (IntersectsPlayer(player, placement))
        {
            return false;
        }

        return world.GetBlock(px, py, pz) == BlockId.Air;
    }

    private static bool IntersectsPlayer(PlayerState player, BlockPosition placement)
    {
        float halfWidth = GameConstants.PlayerWidth * 0.5f;
        float minX = player.Position.X - halfWidth;
        float maxX = player.Position.X + halfWidth;
        float minY = player.Position.Y;
        float maxY = player.Position.Y + GameConstants.PlayerHeight;
        float minZ = player.Position.Z - halfWidth;
        float maxZ = player.Position.Z + halfWidth;

        return placement.X + 1 > minX && placement.X < maxX
            && placement.Y + 1 > minY && placement.Y < maxY
            && placement.Z + 1 > minZ && placement.Z < maxZ;
    }

    private bool Raycast(PlayerState player, GameWorld world, out BlockPosition hit, out BlockId blockId, bool includeAir)
    {
        Vector3 origin = player.EyePosition;
        Vector3 direction = GetLookDirection(player.YawRadians, player.PitchRadians);
        float step = 0.1f;
        for (float distance = 0f; distance <= GameConstants.BlockReachDistance; distance += step)
        {
            Vector3 sample = origin + direction * distance;
            BlockPosition position = BlockPosition.FromWorld(sample.X, sample.Y, sample.Z);
            BlockId current = world.GetBlock(position.X, position.Y, position.Z);
            if (current == BlockId.Air && !includeAir)
            {
                continue;
            }

            hit = position;
            blockId = current;
            return true;
        }

        hit = default;
        blockId = BlockId.Air;
        return false;
    }

    private static Vector3 GetLookDirection(float yaw, float pitch)
    {
        float cosPitch = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(pitch),
            MathF.Cos(yaw) * cosPitch));
    }

    private static void ResetBreaking(PlayerState player)
    {
        player.BreakProgress = 0f;
        player.BreakingBlockId = BlockId.Air;
    }
}
