using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Simulation;

public sealed class PlayerPhysics
{
    private readonly BlockRegistry _blockRegistry;

    public PlayerPhysics(BlockRegistry blockRegistry)
    {
        _blockRegistry = blockRegistry;
    }

    public void Simulate(PlayerState player, GameWorld world, PlayerInput input, float deltaSeconds)
    {
        player.JumpedThisTick = false;
        ApplyLook(player, input, world);
        if (player.Survival.IsDead)
        {
            return;
        }

        bool wasOnGround = player.IsOnGround;
        ApplyMovement(player, world, input, deltaSeconds);
        ApplyGravity(player, world, deltaSeconds);
        ResolveCollisions(player, world, deltaSeconds);
        UpdateFallTracking(player, wasOnGround, deltaSeconds);
        ClampToWorld(player);
    }

    private void ApplyLook(PlayerState player, PlayerInput input, GameWorld world)
    {
        player.YawRadians += input.LookDeltaX;
        player.PitchRadians = System.Math.Clamp(player.PitchRadians + input.LookDeltaY, -1.55f, 1.55f);
        player.IsSneaking = input.Sneak;
        bool swimming = IsPlayerSubmerged(player, world);
        player.IsSwimming = swimming;
        player.IsSprinting = input.Sprint && !input.Sneak && !swimming;
    }

    private void ApplyMovement(PlayerState player, GameWorld world, PlayerInput input, float deltaSeconds)
    {
        if (player.JumpCooldownSeconds > 0f)
        {
            player.JumpCooldownSeconds = MathF.Max(0f, player.JumpCooldownSeconds - deltaSeconds);
        }

        Vector3 forward = Vector3.Normalize(new Vector3(MathF.Sin(player.YawRadians), 0f, MathF.Cos(player.YawRadians)));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 move = forward * input.MoveForward + right * input.MoveRight;

        if (move.LengthSquared() > 0f)
        {
            move = Vector3.Normalize(move);
        }

        if (player.IsSwimming)
        {
            ApplySwimMovement(player, move, input, deltaSeconds);
            return;
        }

        float speed = ResolveSpeed(player);
        player.Velocity = new Vector3(move.X * speed, player.Velocity.Y, move.Z * speed);

        if (input.Jump && player.IsOnGround && player.JumpCooldownSeconds <= 0f)
        {
            player.Velocity = new Vector3(player.Velocity.X, GameConstants.JumpVelocity, player.Velocity.Z);
            player.IsOnGround = false;
            player.JumpCooldownSeconds = GameConstants.JumpCooldownSeconds;
            player.JumpedThisTick = true;
        }
    }

    private static void ApplySwimMovement(PlayerState player, Vector3 move, PlayerInput input, float deltaSeconds)
    {
        float damp = MathF.Max(0f, 1f - GameConstants.SwimDrag * deltaSeconds);
        float targetX = move.X * GameConstants.SwimSpeed;
        float targetZ = move.Z * GameConstants.SwimSpeed;
        float velocityX = move.LengthSquared() > 0f ? targetX : player.Velocity.X * damp;
        float velocityZ = move.LengthSquared() > 0f ? targetZ : player.Velocity.Z * damp;
        float velocityY = player.Velocity.Y;

        if (input.Jump)
        {
            velocityY = GameConstants.SwimAscendSpeed;
        }

        player.Velocity = new Vector3(velocityX, velocityY, velocityZ);
        player.IsOnGround = false;
    }

    private static float ResolveSpeed(PlayerState player)
    {
        if (player.IsSneaking)
        {
            return GameConstants.SneakSpeed;
        }

        if (player.IsSprinting)
        {
            return GameConstants.SprintSpeed;
        }

        return GameConstants.WalkSpeed;
    }

    private static void ApplyGravity(PlayerState player, GameWorld world, float deltaSeconds)
    {
        if (player.IsSwimming)
        {
            player.Velocity = new Vector3(
                player.Velocity.X,
                player.Velocity.Y - GameConstants.SwimGravity * deltaSeconds,
                player.Velocity.Z);
            return;
        }

        if (player.IsOnGround && player.Velocity.Y <= 0f)
        {
            return;
        }

        player.Velocity = new Vector3(
            player.Velocity.X,
            player.Velocity.Y - GameConstants.Gravity * deltaSeconds,
            player.Velocity.Z);
    }

    private void ResolveCollisions(PlayerState player, GameWorld world, float deltaSeconds)
    {
        player.IsOnGround = false;
        Vector3 position = player.Position;
        float halfWidth = GameConstants.PlayerWidth * 0.5f;
        float collisionHeight = player.CollisionHeight;
        Vector3 horizontalIntent = new(player.Velocity.X * deltaSeconds, 0f, player.Velocity.Z * deltaSeconds);

        ReadOnlySpan<int> axisOrder = player.Velocity.Y > 0.05f
            ? stackalloc int[] { 1, 0, 2 }
            : stackalloc int[] { 0, 2, 1 };

        for (int i = 0; i < axisOrder.Length; i++)
        {
            position = ResolveAxis(player, world, position, halfWidth, collisionHeight, axisOrder[i], deltaSeconds);
        }

        if (player.IsOnGround && horizontalIntent.LengthSquared() > 1e-8f)
        {
            Vector3 stepped = TryStepUp(world, position, horizontalIntent, halfWidth, collisionHeight);
            if (stepped != position)
            {
                position = stepped;
                player.IsOnGround = true;
                player.Velocity = new Vector3(player.Velocity.X, 0f, player.Velocity.Z);
            }
        }

        player.Position = position;
    }

    private Vector3 TryStepUp(GameWorld world, Vector3 position, Vector3 horizontalIntent, float halfWidth, float collisionHeight)
    {
        Vector3 target = position + horizontalIntent;
        if (!IntersectsSolidAt(world, target, halfWidth, collisionHeight))
        {
            return position;
        }

        for (float step = GameConstants.StepHeight; step >= 0.25f; step -= 0.25f)
        {
            Vector3 raised = position + new Vector3(0f, step, 0f);
            if (IntersectsSolidAt(world, raised, halfWidth, collisionHeight))
            {
                continue;
            }

            Vector3 raisedTarget = raised + horizontalIntent;
            if (!IntersectsSolidAt(world, raisedTarget, halfWidth, collisionHeight))
            {
                return raisedTarget;
            }
        }

        return position;
    }

    private Vector3 ResolveAxis(
        PlayerState player,
        GameWorld world,
        Vector3 position,
        float halfWidth,
        float collisionHeight,
        int axis,
        float deltaSeconds)
    {
        Vector3 velocity = player.Velocity;
        float movement = axis switch
        {
            0 => velocity.X,
            1 => velocity.Y,
            2 => velocity.Z,
            _ => 0f,
        };

        if (MathF.Abs(movement) < 0.0001f)
        {
            return position;
        }

        float next = axis switch
        {
            0 => position.X + movement * deltaSeconds,
            1 => position.Y + movement * deltaSeconds,
            2 => position.Z + movement * deltaSeconds,
            _ => 0f,
        };

        Vector3 testPosition = axis switch
        {
            0 => new Vector3(next, position.Y, position.Z),
            1 => new Vector3(position.X, next, position.Z),
            2 => new Vector3(position.X, position.Y, next),
            _ => position,
        };

        if (!IntersectsSolidAt(world, testPosition, halfWidth, collisionHeight))
        {
            return testPosition;
        }

        if (axis is 0 or 2)
        {
            Vector3 horizontalDelta = axis == 0
                ? new Vector3(movement * deltaSeconds, 0f, 0f)
                : new Vector3(0f, 0f, movement * deltaSeconds);
            Vector3 stepped = TryStepUp(world, position, horizontalDelta, halfWidth, collisionHeight);
            if (stepped != position)
            {
                player.IsOnGround = true;
                player.Velocity = new Vector3(player.Velocity.X, 0f, player.Velocity.Z);
                return stepped;
            }
        }

        if (axis == 1 && movement < 0f)
        {
            player.IsOnGround = true;
        }

        velocity = axis switch
        {
            0 => new Vector3(0f, velocity.Y, velocity.Z),
            1 => new Vector3(velocity.X, 0f, velocity.Z),
            2 => new Vector3(velocity.X, velocity.Y, 0f),
            _ => velocity,
        };
        player.Velocity = velocity;

        return position;
    }

    private bool IntersectsSolidAt(GameWorld world, Vector3 feetPosition, float halfWidth, float collisionHeight)
    {
        float skin = GameConstants.CollisionSkin;
        int minX = (int)MathF.Floor(feetPosition.X - halfWidth + skin);
        int maxX = (int)MathF.Floor(feetPosition.X + halfWidth - skin);
        int minY = (int)MathF.Floor(feetPosition.Y + skin);
        int maxY = (int)MathF.Floor(feetPosition.Y + collisionHeight - skin);
        int minZ = (int)MathF.Floor(feetPosition.Z - halfWidth + skin);
        int maxZ = (int)MathF.Floor(feetPosition.Z + halfWidth - skin);

        for (int y = minY; y <= maxY; y++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    BlockId blockId = world.GetBlock(x, y, z);
                    if (_blockRegistry.IsSolid(blockId))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsPlayerSubmerged(PlayerState player, GameWorld world)
    {
        BlockPosition head = BlockPosition.FromWorld(player.EyePosition.X, player.EyePosition.Y, player.EyePosition.Z);
        BlockPosition body = BlockPosition.FromWorld(
            player.Position.X,
            player.Position.Y + player.CollisionHeight * 0.5f,
            player.Position.Z);
        return world.IsSubmerged(head.X, head.Y, head.Z) || world.IsSubmerged(body.X, body.Y, body.Z);
    }

    private static void UpdateFallTracking(PlayerState player, bool wasOnGround, float deltaSeconds)
    {
        if (player.IsSwimming)
        {
            player.FallDistance = 0f;
            player.JustLanded = false;
            return;
        }

        if (player.IsOnGround)
        {
            player.JustLanded = !wasOnGround && player.FallDistance > 0f;
            if (wasOnGround)
            {
                player.FallDistance = 0f;
                player.JustLanded = false;
            }

            return;
        }

        player.JustLanded = false;
        if (player.Velocity.Y < 0f)
        {
            player.FallDistance += -player.Velocity.Y * deltaSeconds;
        }
    }

    private static void ClampToWorld(PlayerState player)
    {
        if (player.Position.Y < GameConstants.VoidFallY)
        {
            player.Survival.ApplyDamage(GameConstants.MaxHealth);
        }
    }
}

public readonly record struct PlayerInput(
    float MoveForward,
    float MoveRight,
    float LookDeltaX,
    float LookDeltaY,
    bool Jump,
    bool Sneak,
    bool Sprint,
    bool BreakBlock,
    bool PlaceBlock,
    int HotbarSelection,
    float YawRadians = float.NaN,
    float PitchRadians = float.NaN,
    bool UseItem = false,
    bool RotateBlock = false);
