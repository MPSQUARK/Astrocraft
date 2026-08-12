using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.World;

namespace AstroCraft.Tests;

internal static class WorldGenerationTestHelpers
{
    internal static IEnumerable<Chunk> LoadedChunksNear(GameWorld world, int centerX, int centerZ, int radiusChunks)
    {
        world.EnsureChunksAround(centerX, centerZ, radiusChunks);
        ChunkPosition center = ChunkPosition.FromBlock(centerX, centerZ);
        for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
        {
            for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
            {
                ChunkPosition position = new(center.X + dx, center.Z + dz);
                if (world.TryGetChunk(position, out Chunk? chunk))
                {
                    yield return chunk;
                }
            }
        }
    }

    internal static int FindSurfaceHeight(Chunk chunk, int localX, int localZ)
    {
        for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
        {
            if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
            {
                return y + 1;
            }
        }

        return GameConstants.SeaLevel;
    }

    internal static BlockId FindSurfaceBlock(Chunk chunk, int localX, int localZ)
    {
        for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
        {
            BlockId block = chunk.GetBlock(localX, y, localZ);
            if (block != BlockId.Air)
            {
                return block;
            }
        }

        return BlockId.Air;
    }

    internal static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
