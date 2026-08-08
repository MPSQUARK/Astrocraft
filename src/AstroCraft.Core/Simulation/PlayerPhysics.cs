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
        if (player.Survival.IsDead)
        {
            return;
        }

        ApplyLook(player, input);
        ApplyMovement(player, world, input, deltaSeconds);
        ApplyGravity(player, world, deltaSeconds);
        ResolveCollisions(player, world);
        ClampToWorld(player);
    }

    private static void ApplyLook(PlayerState player, PlayerInput input)
    {
        player.YawRadians += input.LookDeltaX;
        player.PitchRadians = System.Math.Clamp(player.PitchRadians + input.LookDeltaY, -1.55f, 1.55f);
        player.IsSneaking = input.Sneak;
        player.IsSprinting = input.Sprint && !input.Sneak;
    }

    private void ApplyMovement(PlayerState player, GameWorld world, PlayerInput input, float deltaSeconds)
    {
        Vector3 forward = Vector3.Normalize(new Vector3(MathF.Sin(player.YawRadians), 0f, MathF.Cos(player.YawRadians)));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 move = forward * input.MoveForward + right * input.MoveRight;

        if (move.LengthSquared() > 0f)
        {
            move = Vector3.Normalize(move);
        }

        float speed = ResolveSpeed(player);
        player.Velocity = new Vector3(move.X * speed, player.Velocity.Y, move.Z * speed);

        if (input.Jump && player.IsOnGround)
        {
            player.Velocity = new Vector3(player.Velocity.X, GameConstants.JumpVelocity, player.Velocity.Z);
            player.IsOnGround = false;
        }
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
        if (player.IsOnGround)
        {
            return;
        }

        player.Velocity = new Vector3(
            player.Velocity.X,
            player.Velocity.Y - GameConstants.Gravity * deltaSeconds,
            player.Velocity.Z);
    }

    private void ResolveCollisions(PlayerState player, GameWorld world)
    {
        player.IsOnGround = false;
        Vector3 position = player.Position;
        float halfWidth = GameConstants.PlayerWidth * 0.5f;

        for (int axis = 0; axis < 3; axis++)
        {
            position = ResolveAxis(player, world, position, halfWidth, axis);
        }

        player.Position = position;
    }

    private Vector3 ResolveAxis(PlayerState player, GameWorld world, Vector3 position, float halfWidth, int axis)
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
            0 => position.X + movement * (float)GameConstants.TickDurationSeconds,
            1 => position.Y + movement * (float)GameConstants.TickDurationSeconds,
            2 => position.Z + movement * (float)GameConstants.TickDurationSeconds,
            _ => 0f,
        };

        if (!IntersectsSolid(world, position, next, halfWidth, axis))
        {
            return axis switch
            {
                0 => new Vector3(next, position.Y, position.Z),
                1 => new Vector3(position.X, next, position.Z),
                2 => new Vector3(position.X, position.Y, next),
                _ => position,
            };
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

    private bool IntersectsSolid(GameWorld world, Vector3 position, float nextAxisValue, float halfWidth, int axis)
    {
        Vector3 testPosition = axis switch
        {
            0 => new Vector3(nextAxisValue, position.Y, position.Z),
            1 => new Vector3(position.X, nextAxisValue, position.Z),
            2 => new Vector3(position.X, position.Y, nextAxisValue),
            _ => position,
        };

        int minX = (int)MathF.Floor(testPosition.X - halfWidth);
        int maxX = (int)MathF.Floor(testPosition.X + halfWidth);
        int minY = (int)MathF.Floor(testPosition.Y);
        int maxY = (int)MathF.Floor(testPosition.Y + GameConstants.PlayerHeight);
        int minZ = (int)MathF.Floor(testPosition.Z - halfWidth);
        int maxZ = (int)MathF.Floor(testPosition.Z + halfWidth);

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

    private static void ClampToWorld(PlayerState player)
    {
        if (player.Position.Y < GameConstants.VoidFallY)
        {
            player.Survival.ApplyDamage(GameConstants.MaxHealth);
            player.Survival.IsDead = true;
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
    int HotbarSelection);
