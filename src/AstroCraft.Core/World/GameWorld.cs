using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Core.World;

public sealed class GameWorld
{
    private readonly Dictionary<ChunkPosition, Chunk> _chunks = new();
    private readonly BlockRegistry _blockRegistry;
    private readonly IWorldGenerator _generator;
    private readonly List<BlockChange> _pendingBlockChanges = new();

    public GameWorld(BlockRegistry blockRegistry, IWorldGenerator generator)
    {
        _blockRegistry = blockRegistry;
        _generator = generator;
    }

    public BlockRegistry BlockRegistry => _blockRegistry;

    public bool IsFlatWorld { get; init; }

    public IReadOnlyList<BlockChange> PendingBlockChanges => _pendingBlockChanges;

    public void ClearPendingBlockChanges() => _pendingBlockChanges.Clear();

    public BlockId GetBlock(int worldX, int worldY, int worldZ)
    {
        if (!IsInsideWorld(worldY))
        {
            return BlockId.Air;
        }

        Chunk chunk = GetOrCreateChunk(ChunkPosition.FromBlock(worldX, worldZ));
        int localX = Mod(worldX, GameConstants.ChunkSizeX);
        int localZ = Mod(worldZ, GameConstants.ChunkSizeZ);
        return chunk.GetBlock(localX, worldY, localZ);
    }

    public bool TrySetBlock(int worldX, int worldY, int worldZ, BlockId blockId)
    {
        if (!IsInsideWorld(worldY))
        {
            return false;
        }

        BlockDefinition definition = _blockRegistry.Get(blockId);
        if (!definition.IsBreakable && blockId != BlockId.Air && definition.Hardness < 0)
        {
            return false;
        }

        Chunk chunk = GetOrCreateChunk(ChunkPosition.FromBlock(worldX, worldZ));
        int localX = Mod(worldX, GameConstants.ChunkSizeX);
        int localZ = Mod(worldZ, GameConstants.ChunkSizeZ);
        chunk.SetBlock(localX, worldY, localZ, blockId);
        _pendingBlockChanges.Add(new BlockChange(worldX, worldY, worldZ, blockId));
        return true;
    }

    public Chunk GetOrCreateChunk(ChunkPosition position)
    {
        if (_chunks.TryGetValue(position, out Chunk? existing))
        {
            return existing;
        }

        Chunk chunk = new(position);
        _generator.GenerateChunk(this, chunk);
        _chunks[position] = chunk;
        return chunk;
    }

    public bool TryGetChunk(ChunkPosition position, out Chunk chunk) => _chunks.TryGetValue(position, out chunk!);

    public IEnumerable<ChunkPosition> LoadedChunkPositions => _chunks.Keys;

    public IEnumerable<Chunk> LoadedChunks => _chunks.Values;

    public void EnsureChunksAround(int centerBlockX, int centerBlockZ, int radiusChunks)
    {
        ChunkPosition center = ChunkPosition.FromBlock(centerBlockX, centerBlockZ);
        for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
        {
            for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
            {
                GetOrCreateChunk(new ChunkPosition(center.X + dx, center.Z + dz));
            }
        }
    }

    public IEnumerable<ChunkPosition> EnsureChunksAroundTracked(
        int centerBlockX,
        int centerBlockZ,
        int radiusChunks)
    {
        ChunkPosition center = ChunkPosition.FromBlock(centerBlockX, centerBlockZ);
        for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
        {
            for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
            {
                ChunkPosition position = new(center.X + dx, center.Z + dz);
                if (_chunks.ContainsKey(position))
                {
                    continue;
                }

                GetOrCreateChunk(position);
                yield return position;
            }
        }
    }

    public bool IsSolid(int worldX, int worldY, int worldZ)
    {
        BlockId blockId = GetBlock(worldX, worldY, worldZ);
        return _blockRegistry.IsSolid(blockId);
    }

    public bool IsBreathable(int worldX, int worldY, int worldZ)
    {
        BlockId headBlock = GetBlock(worldX, worldY, worldZ);
        BlockId bodyBlock = GetBlock(worldX, worldY - 1, worldZ);
        return !_blockRegistry.Get(headBlock).BlocksOxygen && !_blockRegistry.Get(bodyBlock).BlocksOxygen;
    }

    public bool IsSubmerged(int worldX, int worldY, int worldZ)
    {
        BlockId block = GetBlock(worldX, worldY, worldZ);
        return block is BlockId.Water or BlockId.Oil;
    }

    private static bool IsInsideWorld(int worldY) => worldY >= 0 && worldY < GameConstants.WorldHeight;

    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
