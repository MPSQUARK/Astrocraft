using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Entities;

public sealed class ItemEntityWorld
{
    private readonly List<ItemEntity> _entities = new();
    private int _nextId = 1;
    private int _nextPredictedId = -1;

    public IReadOnlyList<ItemEntity> Entities => _entities;

    public ItemEntity Spawn(Vector3 position, Vector3 velocity, BlockId blockId, int count = 1, bool predicted = false) =>
        Spawn(position, velocity, StackKey.Block(blockId), count, predicted);

    public ItemEntity Spawn(Vector3 position, Vector3 velocity, StackKey stack, int count = 1, bool predicted = false)
    {
        ItemEntity entity = new()
        {
            Id = predicted ? _nextPredictedId-- : _nextId++,
            Position = position,
            Velocity = velocity,
            PickupCooldownTicks = predicted ? 0 : GameConstants.ItemPickupDelayTicks,
            IsPredicted = predicted,
        };
        entity.Stack.BlockId = stack.BlockId;
        entity.Stack.ItemId = stack.ItemId;
        entity.Stack.Count = count;
        _entities.Add(entity);
        return entity;
    }

    public ItemEntity SpawnAtBlock(int x, int y, int z, BlockId blockId, bool predicted = false) =>
        SpawnAtBlock(x, y, z, StackKey.Block(blockId), predicted: predicted);

    public ItemEntity SpawnAtBlock(int x, int y, int z, StackKey stack, bool predicted = false)
    {
        Vector3 center = new(x + 0.5f, y + 0.5f, z + 0.5f);
        int hash = unchecked(x * 73856093 ^ y * 19349663 ^ z * 83492791);
        Random random = new(hash);
        Vector3 velocity = new(
            (random.NextSingle() - 0.5f) * 2f,
            0.35f + random.NextSingle() * 0.25f,
            (random.NextSingle() - 0.5f) * 2f);
        return Spawn(center, velocity, stack, predicted: predicted);
    }

    public void Update(GameWorld world, float deltaSeconds)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            ItemEntity entity = _entities[i];
            if (entity.PickupCooldownTicks > 0)
            {
                entity.PickupCooldownTicks--;
            }

            entity.SpinRadians += deltaSeconds * 4f;

            Vector3 velocity = entity.Velocity;
            velocity.Y -= GameConstants.Gravity * 0.12f * deltaSeconds;
            entity.Velocity = velocity;
            entity.Position += velocity * deltaSeconds;

            int blockX = (int)MathF.Floor(entity.Position.X);
            int blockY = (int)MathF.Floor(entity.Position.Y - 0.12f);
            int blockZ = (int)MathF.Floor(entity.Position.Z);
            if (world.IsSolid(blockX, blockY, blockZ))
            {
                entity.Position = new Vector3(entity.Position.X, blockY + 1.08f, entity.Position.Z);
                entity.Velocity = new Vector3(velocity.X * 0.65f, 0f, velocity.Z * 0.65f);
            }
        }
    }

    public bool Remove(int id)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            if (_entities[i].Id == id)
            {
                _entities.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public void RemovePredictedAtBlock(int x, int y, int z, BlockId blockId)
    {
        RemovePredictedAtBlock(x, y, z, StackKey.Block(blockId));
    }

    public void RemovePredictedAtBlock(int x, int y, int z, StackKey stack)
    {
        Vector3 center = new(x + 0.5f, y + 0.5f, z + 0.5f);
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            ItemEntity entity = _entities[i];
            if (!entity.IsPredicted)
            {
                continue;
            }

            if (entity.Stack.BlockId != stack.BlockId || entity.Stack.ItemId != stack.ItemId)
            {
                continue;
            }

            if (Vector3.DistanceSquared(entity.Position, center) < 0.5f)
            {
                _entities.RemoveAt(i);
            }
        }
    }

    public void ApplyServerSnapshot(IReadOnlyList<ItemEntitySnapshot> snapshots)
    {
        _entities.RemoveAll(entity => !entity.IsPredicted);
        foreach (ItemEntitySnapshot snapshot in snapshots)
        {
            ItemEntity entity = new()
            {
                Id = snapshot.Id,
                Position = snapshot.Position,
                Velocity = snapshot.Velocity,
                SpinRadians = snapshot.SpinRadians,
                PickupCooldownTicks = 0,
                IsPredicted = false,
            };
            entity.Stack.BlockId = snapshot.BlockId;
            entity.Stack.ItemId = snapshot.ItemId;
            entity.Stack.Count = snapshot.Count;
            _entities.Add(entity);
        }
    }

    public List<ItemEntity> GetNear(Vector3 center, float radius)
    {
        float radiusSq = radius * radius;
        List<ItemEntity> nearby = new();
        foreach (ItemEntity entity in _entities)
        {
            if (Vector3.DistanceSquared(center, entity.Position) <= radiusSq)
            {
                nearby.Add(entity);
            }
        }

        return nearby;
    }

    public void Clear()
    {
        _entities.Clear();
        _nextId = 1;
        _nextPredictedId = -1;
    }
}

public readonly record struct ItemEntitySnapshot(
    int Id,
    Vector3 Position,
    Vector3 Velocity,
    BlockId BlockId,
    ItemId ItemId,
    int Count,
    float SpinRadians);
