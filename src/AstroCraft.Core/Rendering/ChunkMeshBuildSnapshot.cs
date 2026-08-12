using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Rendering;

/// <summary>
/// Immutable 3×3 chunk neighborhood captured on the main thread for background meshing.
/// </summary>
public sealed class ChunkMeshBuildSnapshot
{
    private readonly BlockRegistry _registry;
    private readonly ChunkPosition _center;
    private readonly BlockId[] _centerBlocks;
    private readonly byte[] _centerAxes;
    private readonly Dictionary<ChunkPosition, BlockId[]> _neighborBlocks = new();
    private readonly Dictionary<ChunkPosition, byte[]> _neighborAxes = new();

    private ChunkMeshBuildSnapshot(
        BlockRegistry registry,
        ChunkPosition center,
        BlockId[] centerBlocks,
        byte[] centerAxes)
    {
        _registry = registry;
        _center = center;
        _centerBlocks = centerBlocks;
        _centerAxes = centerAxes;
    }

    public ChunkPosition Center => _center;

    public BlockRegistry BlockRegistry => _registry;

    public static ChunkMeshBuildSnapshot? TryCapture(Chunk centerChunk, GameWorld world)
    {
        BlockId[] centerBlocks = new BlockId[centerChunk.Blocks.Length];
        centerChunk.Blocks.CopyTo(centerBlocks);
        byte[] centerAxes = new byte[centerBlocks.Length];
        for (int z = 0; z < GameConstants.ChunkSizeZ; z++)
        {
            for (int y = 0; y < GameConstants.ChunkSizeY; y++)
            {
                for (int x = 0; x < GameConstants.ChunkSizeX; x++)
                {
                    centerAxes[ChunkIndex(x, y, z)] = (byte)centerChunk.GetBlockAxis(x, y, z);
                }
            }
        }

        var snapshot = new ChunkMeshBuildSnapshot(
            world.BlockRegistry,
            centerChunk.Position,
            centerBlocks,
            centerAxes);

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                ChunkPosition neighborPos = new(centerChunk.Position.X + dx, centerChunk.Position.Z + dz);
                if (!world.TryGetChunk(neighborPos, out Chunk neighbor))
                {
                    continue;
                }

                BlockId[] blocks = new BlockId[neighbor.Blocks.Length];
                neighbor.Blocks.CopyTo(blocks);
                byte[] axes = new byte[blocks.Length];
                for (int z = 0; z < GameConstants.ChunkSizeZ; z++)
                {
                    for (int y = 0; y < GameConstants.ChunkSizeY; y++)
                    {
                        for (int x = 0; x < GameConstants.ChunkSizeX; x++)
                        {
                            axes[ChunkIndex(x, y, z)] = (byte)neighbor.GetBlockAxis(x, y, z);
                        }
                    }
                }

                snapshot._neighborBlocks[neighborPos] = blocks;
                snapshot._neighborAxes[neighborPos] = axes;
            }
        }

        return snapshot;
    }

    public BlockId GetBlock(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConstants.WorldHeight)
        {
            return BlockId.Air;
        }

        ChunkPosition chunkPos = ChunkPosition.FromBlock(worldX, worldZ);
        int localX = Mod(worldX, GameConstants.ChunkSizeX);
        int localZ = Mod(worldZ, GameConstants.ChunkSizeZ);
        int index = ChunkIndex(localX, worldY, localZ);

        if (chunkPos == _center)
        {
            return _centerBlocks[index];
        }

        if (_neighborBlocks.TryGetValue(chunkPos, out BlockId[]? blocks))
        {
            return blocks[index];
        }

        return BlockId.Air;
    }

    public BlockAxis GetBlockAxis(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConstants.WorldHeight)
        {
            return BlockAxis.Y;
        }

        ChunkPosition chunkPos = ChunkPosition.FromBlock(worldX, worldZ);
        int localX = Mod(worldX, GameConstants.ChunkSizeX);
        int localZ = Mod(worldZ, GameConstants.ChunkSizeZ);
        int index = ChunkIndex(localX, worldY, localZ);

        if (chunkPos == _center)
        {
            return (BlockAxis)_centerAxes[index];
        }

        if (_neighborAxes.TryGetValue(chunkPos, out byte[]? axes))
        {
            return (BlockAxis)axes[index];
        }

        return BlockAxis.Y;
    }

    private static int ChunkIndex(int localX, int localY, int localZ) =>
        localX + GameConstants.ChunkSizeX * (localY + GameConstants.ChunkSizeY * localZ);

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
