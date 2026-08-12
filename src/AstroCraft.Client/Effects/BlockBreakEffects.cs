using System.Numerics;
using AstroCraft.Core.Blocks;

namespace AstroCraft.Client.Effects;

public readonly struct BlockParticle
{
    public Vector3 Position { get; init; }
    public Vector3 Velocity { get; init; }
    public float Life { get; init; }
    public float MaxLife { get; init; }
    public float Size { get; init; }
    public float TextureIndex { get; init; }
    public float SpinRadians { get; init; }
}

public sealed class BlockBreakEffects
{
    private const int MaxParticles = 72;
    private const float MiningDustInterval = 0.07f;

    private readonly BlockParticle[] _particles = new BlockParticle[MaxParticles];
    private int _particleCount;
    private float _miningDustCooldown;
    private float _breakBurstTimer;
    private BlockId _burstBlock = BlockId.Air;
    private int _miningBlockX = int.MinValue;
    private int _miningBlockY = int.MinValue;
    private int _miningBlockZ = int.MinValue;
    private BlockId _miningBlockId = BlockId.Air;

    public float BreakBurstTimer => _breakBurstTimer;
    public BlockId BurstBlock => _burstBlock;
    public ReadOnlySpan<BlockParticle> Particles => _particles.AsSpan(0, _particleCount);

    public void OnBlockBroken(int x, int y, int z, BlockId blockId)
    {
        Vector3 center = new(x + 0.5f, y + 0.5f, z + 0.5f);
        SpawnBurst(center, blockId, count: 16);
        _breakBurstTimer = 0.4f;
        _burstBlock = blockId;
        ClearMiningTarget();
    }

    public void OnMiningProgress(int x, int y, int z, BlockId blockId, float progress)
    {
        if (progress <= 0.001f || blockId == BlockId.Air)
        {
            ClearMiningTarget();
            return;
        }

        if (x != _miningBlockX || y != _miningBlockY || z != _miningBlockZ || blockId != _miningBlockId)
        {
            _miningBlockX = x;
            _miningBlockY = y;
            _miningBlockZ = z;
            _miningBlockId = blockId;
            _miningDustCooldown = 0f;
        }
    }

    public void Update(float deltaSeconds)
    {
        if (_breakBurstTimer > 0f)
        {
            _breakBurstTimer = MathF.Max(0f, _breakBurstTimer - deltaSeconds);
        }

        _miningDustCooldown -= deltaSeconds;
        if (_miningDustCooldown <= 0f && _miningBlockY != int.MinValue)
        {
            Vector3 center = new(_miningBlockX + 0.5f, _miningBlockY + 0.5f, _miningBlockZ + 0.5f);
            SpawnDust(center, ResolveTextureIndex(_miningBlockId), count: 2);
            _miningDustCooldown = MiningDustInterval;
        }

        int writeIndex = 0;
        for (int i = 0; i < _particleCount; i++)
        {
            BlockParticle particle = _particles[i];
            float life = particle.Life - deltaSeconds;
            if (life <= 0f)
            {
                continue;
            }

            Vector3 velocity = particle.Velocity;
            velocity.Y -= 18f * deltaSeconds;
            _particles[writeIndex++] = particle with
            {
                Position = particle.Position + velocity * deltaSeconds,
                Velocity = velocity,
                Life = life,
            };
        }

        _particleCount = writeIndex;
    }

    public void Reset()
    {
        _particleCount = 0;
        _breakBurstTimer = 0f;
        _burstBlock = BlockId.Air;
        ClearMiningTarget();
    }

    private void ClearMiningTarget()
    {
        _miningBlockX = int.MinValue;
        _miningBlockY = int.MinValue;
        _miningBlockZ = int.MinValue;
        _miningBlockId = BlockId.Air;
        _miningDustCooldown = 0f;
    }

    private void SpawnBurst(Vector3 center, BlockId blockId, int count)
    {
        float textureIndex = ResolveTextureIndex(blockId);
        Random random = new(Hash(center));

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new(
                (random.NextSingle() - 0.5f) * 0.9f,
                (random.NextSingle() - 0.5f) * 0.9f,
                (random.NextSingle() - 0.5f) * 0.9f);

            Vector3 velocity = new(
                (random.NextSingle() - 0.5f) * 3.5f,
                random.NextSingle() * 2.8f + 1.2f,
                (random.NextSingle() - 0.5f) * 3.5f);

            TryAddParticle(new BlockParticle
            {
                Position = center + offset,
                Velocity = velocity,
                Life = 0.35f + random.NextSingle() * 0.25f,
                MaxLife = 0.6f,
                Size = 0.08f + random.NextSingle() * 0.06f,
                TextureIndex = textureIndex,
            });
        }
    }

    private void SpawnDust(Vector3 center, float textureIndex, int count)
    {
        Random random = new(Hash(center) ^ 0x51A5F);

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new(
                (random.NextSingle() - 0.5f) * 0.6f,
                (random.NextSingle() - 0.5f) * 0.6f,
                (random.NextSingle() - 0.5f) * 0.6f);

            Vector3 velocity = new(
                (random.NextSingle() - 0.5f) * 0.6f,
                random.NextSingle() * 0.5f + 0.2f,
                (random.NextSingle() - 0.5f) * 0.6f);

            TryAddParticle(new BlockParticle
            {
                Position = center + offset,
                Velocity = velocity,
                Life = 0.18f + random.NextSingle() * 0.12f,
                MaxLife = 0.3f,
                Size = 0.05f + random.NextSingle() * 0.03f,
                TextureIndex = textureIndex,
            });
        }
    }

    private void TryAddParticle(BlockParticle particle)
    {
        if (_particleCount >= MaxParticles)
        {
            return;
        }

        _particles[_particleCount++] = particle;
    }

    private static float ResolveTextureIndex(BlockId blockId)
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        BlockDefinition definition = registry.Get(blockId);
        return definition.TextureSide;
    }

    private static int Hash(Vector3 position)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (int)(position.X * 1000f);
            hash = hash * 31 + (int)(position.Y * 1000f);
            hash = hash * 31 + (int)(position.Z * 1000f);
            return hash;
        }
    }
}
