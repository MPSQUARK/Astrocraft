using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Simulation;

public sealed class SurvivalSimulator
{
    public void Update(PlayerState player, GameWorld world, float deltaSeconds)
    {
        if (player.Survival.IsDead)
        {
            return;
        }

        if (player.WorldLoadGraceTicks > 0)
        {
            player.WorldLoadGraceTicks--;
            player.FallDistance = 0f;
            player.JustLanded = false;
            UpdateHunger(player, deltaSeconds);
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

        UpdateHunger(player, deltaSeconds);
        ApplyLavaDamage(player, world);
        ApplyFallDamage(player);
        ApplyVoidDamage(player);
    }

    public bool TryEatFood(PlayerState player, BlockRegistry blockRegistry, PlayerInput input)
    {
        if (!input.UseItem)
        {
            return false;
        }

        BlockId selected = player.Inventory.SelectedHotbarSlot.BlockId;
        if (selected == BlockId.Air)
        {
            return false;
        }

        BlockDefinition definition = blockRegistry.Get(selected);
        if (!definition.IsEdible)
        {
            return false;
        }

        if (player.Survival.Hunger >= GameConstants.MaxHunger)
        {
            return false;
        }

        player.Survival.Hunger = System.Math.Min(
            GameConstants.MaxHunger,
            player.Survival.Hunger + definition.HungerRestore);
        player.Survival.Saturation = System.Math.Min(
            GameConstants.MaxSaturation,
            player.Survival.Saturation + definition.SaturationRestore);
        return player.Inventory.TryConsumeSelected(selected);
    }

    private static void ApplyVoidDamage(PlayerState player)
    {
        if (player.WorldLoadGraceTicks > 0)
        {
            return;
        }

        if (player.Position.Y < GameConstants.VoidFallY)
        {
            player.Survival.ApplyDamage(GameConstants.MaxHealth);
        }
    }

    public static float ComputeFallDamage(float fallDistanceBlocks)
    {
        if (fallDistanceBlocks <= GameConstants.SafeFallDistanceBlocks)
        {
            return 0f;
        }

        return (fallDistanceBlocks - GameConstants.SafeFallDistanceBlocks) * GameConstants.FallDamagePerBlockBeyondSafe;
    }

    private static void ApplyFallDamage(PlayerState player)
    {
        if (!player.JustLanded)
        {
            return;
        }

        float damage = ComputeFallDamage(player.FallDistance);
        if (damage > 0f)
        {
            player.Survival.ApplyDamage(damage);
        }

        player.FallDistance = 0f;
        player.JustLanded = false;
    }

    private static void ApplyLavaDamage(PlayerState player, GameWorld world)
    {
        BlockPosition feet = BlockPosition.FromWorld(player.Position.X, player.Position.Y, player.Position.Z);
        BlockPosition head = BlockPosition.FromWorld(player.EyePosition.X, player.EyePosition.Y, player.EyePosition.Z);
        if (world.GetBlock(feet.X, feet.Y, feet.Z) == BlockId.Lava
            || world.GetBlock(head.X, head.Y, head.Z) == BlockId.Lava)
        {
            player.Survival.ApplyDamage(GameConstants.LavaDamagePerTick);
        }
    }

    private static void UpdateHunger(PlayerState player, float deltaSeconds)
    {
        float horizontalSpeed = MathF.Sqrt(
            player.Velocity.X * player.Velocity.X + player.Velocity.Z * player.Velocity.Z);
        if (horizontalSpeed > 0.01f)
        {
            float metersMoved = horizontalSpeed * deltaSeconds;
            float exhaustionRate = player.IsSprinting
                ? GameConstants.ExhaustionPerMeterSprint
                : GameConstants.ExhaustionPerMeterWalk;
            player.Survival.AddExhaustion(metersMoved * exhaustionRate);
        }

        if (player.JumpedThisTick)
        {
            player.Survival.AddExhaustion(GameConstants.ExhaustionPerJump);
        }

        if (player.Survival.Hunger <= 0f)
        {
            player.Survival.ApplyDamage(GameConstants.StarvationDamagePerTick);
        }
    }
}

public sealed class BlockInteractionSystem
{
    private readonly BlockRegistry _blockRegistry;

    public BlockInteractionSystem(BlockRegistry blockRegistry)
    {
        _blockRegistry = blockRegistry;
    }

    public bool UpdateBreaking(
        PlayerState player,
        GameWorld world,
        PlayerInput input,
        float deltaSeconds,
        out BlockId brokenBlock,
        out int brokenX,
        out int brokenY,
        out int brokenZ)
    {
        brokenBlock = BlockId.Air;
        brokenX = 0;
        brokenY = 0;
        brokenZ = 0;

        if (!input.BreakBlock)
        {
            ResetBreaking(player);
            return false;
        }

        if (!TryGetTargetBlock(player, world, out BlockPosition target, out BlockId targetBlock))
        {
            ResetBreaking(player);
            return false;
        }

        BlockDefinition definition = _blockRegistry.Get(targetBlock);
        if (!definition.IsBreakable)
        {
            ResetBreaking(player);
            return false;
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
        float breakSpeed = ToolBreakSpeed.GetMultiplier(player.Inventory.SelectedHotbarSlot.ItemId);
        player.BreakProgress += deltaSeconds * breakSpeed / hardness;
        if (player.BreakProgress < 1f)
        {
            return false;
        }

        world.TrySetBlock(target.X, target.Y, target.Z, BlockId.Air);
        brokenBlock = targetBlock;
        brokenX = target.X;
        brokenY = target.Y;
        brokenZ = target.Z;
        player.Inventory.TryDamageSelectedTool();
        ResetBreaking(player);
        return true;
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

        BlockDefinition selectedDefinition = _blockRegistry.Get(selected);
        bool canPlace = (selectedDefinition.IsSolid && !selectedDefinition.IsEdible)
            || selectedDefinition.IsPlaceable;
        if (!canPlace)
        {
            return false;
        }

        if (!TryGetPlacementPosition(player, world, out BlockPosition placement, out Vector3 faceNormal))
        {
            return false;
        }

        if (!IsWithinPlacementReach(player, placement))
        {
            return false;
        }

        BlockDefinition definition = _blockRegistry.Get(selected);
        BlockAxis axis = BlockAxis.Y;
        if (definition.PlacementOrientation == BlockPlacementOrientation.AxisAligned)
        {
            axis = BlockAxisExtensions.FromPlacementFace(faceNormal);
        }

        if (!world.TrySetBlock(placement.X, placement.Y, placement.Z, selected, axis))
        {
            return false;
        }

        return player.Inventory.TryConsumeSelected(selected);
    }

    public bool TryResolvePlacement(PlayerState player, GameWorld world, out BlockPosition placement) =>
        TryGetPlacementPosition(player, world, out placement);

    public bool TryGetPlacementGhost(
        PlayerState player,
        GameWorld world,
        out BlockPosition placement,
        out bool valid)
    {
        if (!TryGetPlacementPosition(player, world, out placement, out _))
        {
            valid = false;
            return false;
        }

        valid = IsWithinPlacementReach(player, placement);
        return true;
    }

    public bool TryRotateTargetBlock(PlayerState player, GameWorld world)
    {
        if (!TryGetTargetBlock(player, world, out BlockPosition target, out BlockId blockId))
        {
            return false;
        }

        BlockDefinition definition = _blockRegistry.Get(blockId);
        if (definition.PlacementOrientation != BlockPlacementOrientation.AxisAligned)
        {
            return false;
        }

        BlockAxis nextAxis = BlockAxisExtensions.Next(world.GetBlockAxis(target.X, target.Y, target.Z));
        return world.TrySetBlock(target.X, target.Y, target.Z, blockId, nextAxis);
    }

    public bool TryGetTargetBlock(PlayerState player, GameWorld world, out BlockPosition target, out BlockId blockId) =>
        TryGetTargetBlock(player, world, out target, out blockId, out _);

    public bool TryGetTargetBlock(
        PlayerState player,
        GameWorld world,
        out BlockPosition target,
        out BlockId blockId,
        out Vector3 faceNormal)
    {
        if (!Raycast(player, world, out target, out blockId, out _, out BlockPosition previousAir, includeAir: false))
        {
            faceNormal = default;
            return false;
        }

        faceNormal = ComputeFaceNormal(target, previousAir);
        return true;
    }

    private bool TryGetPlacementPosition(
        PlayerState player,
        GameWorld world,
        out BlockPosition placement,
        out Vector3 faceNormal)
    {
        if (!Raycast(player, world, out BlockPosition hit, out _, out _, out BlockPosition previousAir, includeAir: false))
        {
            placement = default;
            faceNormal = default;
            return false;
        }

        placement = previousAir;
        faceNormal = ComputeFaceNormal(hit, previousAir);

        if (IntersectsPlayer(player, placement))
        {
            return false;
        }

        return world.GetBlock(placement.X, placement.Y, placement.Z) == BlockId.Air;
    }

    private bool TryGetPlacementPosition(PlayerState player, GameWorld world, out BlockPosition placement) =>
        TryGetPlacementPosition(player, world, out placement, out _);

    private static bool IntersectsPlayer(PlayerState player, BlockPosition placement)
    {
        float halfWidth = GameConstants.PlayerWidth * 0.5f;
        float minX = player.Position.X - halfWidth;
        float maxX = player.Position.X + halfWidth;
        float minY = player.Position.Y;
        float maxY = player.Position.Y + player.CollisionHeight;
        float minZ = player.Position.Z - halfWidth;
        float maxZ = player.Position.Z + halfWidth;

        return placement.X + 1 > minX && placement.X < maxX
            && placement.Y + 1 > minY && placement.Y < maxY
            && placement.Z + 1 > minZ && placement.Z < maxZ;
    }

    private bool Raycast(
        PlayerState player,
        GameWorld world,
        out BlockPosition hit,
        out BlockId blockId,
        out Vector3 hitPoint,
        out BlockPosition previousAir,
        bool includeAir)
    {
        Vector3 origin = player.EyePosition;
        Vector3 direction = GetLookDirection(player.YawRadians, player.PitchRadians);
        float step = 0.1f;
        BlockPosition previous = BlockPosition.FromWorld(origin.X, origin.Y, origin.Z);
        for (float distance = 0f; distance <= GameConstants.BlockReachDistance; distance += step)
        {
            Vector3 sample = origin + direction * distance;
            BlockPosition position = BlockPosition.FromWorld(sample.X, sample.Y, sample.Z);
            if (position == previous)
            {
                continue;
            }

            BlockId current = world.GetBlock(position.X, position.Y, position.Z);
            if (current == BlockId.Air && !includeAir)
            {
                previous = position;
                continue;
            }

            hit = position;
            blockId = current;
            hitPoint = sample;
            previousAir = previous;
            return true;
        }

        hit = default;
        blockId = BlockId.Air;
        hitPoint = default;
        previousAir = default;
        return false;
    }

    private static Vector3 ComputeFaceNormal(BlockPosition hit, BlockPosition adjacentAir) =>
        new(adjacentAir.X - hit.X, adjacentAir.Y - hit.Y, adjacentAir.Z - hit.Z);

    private static Vector3 GetLookDirection(float yaw, float pitch)
    {
        float cosPitch = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(pitch),
            MathF.Cos(yaw) * cosPitch));
    }

    public static bool IsWithinPlacementReach(PlayerState player, BlockPosition placement)
    {
        Vector3 blockCenter = new(placement.X + 0.5f, placement.Y + 0.5f, placement.Z + 0.5f);
        return Vector3.Distance(player.EyePosition, blockCenter) <= GameConstants.BlockReachDistance;
    }

    private static void ResetBreaking(PlayerState player)
    {
        player.BreakProgress = 0f;
        player.BreakingBlockId = BlockId.Air;
        player.BreakingBlockX = 0;
        player.BreakingBlockY = 0;
        player.BreakingBlockZ = 0;
    }
}
