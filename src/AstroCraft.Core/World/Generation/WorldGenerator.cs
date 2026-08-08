using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;

namespace AstroCraft.Core.World.Generation;

public interface IWorldGenerator
{
    void GenerateChunk(GameWorld world, Chunk chunk);
}

public sealed class ProceduralWorldGenerator(int seed) : IWorldGenerator
{
    public void GenerateChunk(GameWorld world, Chunk chunk)
    {
        int baseX = chunk.Position.X * GameConstants.ChunkSizeX;
        int baseZ = chunk.Position.Z * GameConstants.ChunkSizeZ;

        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int surfaceHeight = ComputeSurfaceHeight(worldX, worldZ);

                for (int y = 0; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = ResolveColumnBlock(y, surfaceHeight, worldX, worldZ);
                    chunk.SetBlock(localX, y, localZ, block);
                }
            }
        }

        CarveCaves(world, chunk, baseX, baseZ);
    }

    private int ComputeSurfaceHeight(int worldX, int worldZ)
    {
        float noise = FractalNoise(worldX * 0.01f, worldZ * 0.01f, seed);
        int height = GameConstants.SeaLevel + (int)(noise * 12f);
        return System.Math.Clamp(height, 6, GameConstants.WorldHeight - 4);
    }

    private static BlockId ResolveColumnBlock(int y, int surfaceHeight, int worldX, int worldZ)
    {
        if (y == 0)
        {
            return BlockId.Bedrock;
        }

        if (y < surfaceHeight - 4)
        {
            return BlockId.Stone;
        }

        if (y < surfaceHeight - 1)
        {
            return BlockId.Dirt;
        }

        if (y == surfaceHeight - 1)
        {
            return BlockId.Grass;
        }

        if (y < GameConstants.SeaLevel)
        {
            return BlockId.Water;
        }

        if (y == surfaceHeight && Hash(worldX, y, worldZ) % 17 == 0)
        {
            return BlockId.Wood;
        }

        return BlockId.Air;
    }

    private void CarveCaves(GameWorld world, Chunk chunk, int baseX, int baseZ)
    {
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                for (int y = 4; y < GameConstants.SeaLevel + 8; y++)
                {
                    int worldX = baseX + localX;
                    int worldZ = baseZ + localZ;
                    float caveNoise = FractalNoise(worldX * 0.08f, y * 0.08f + 50f, worldZ * 0.08f + seed);
                    if (caveNoise > 0.62f)
                    {
                        BlockId current = chunk.GetBlock(localX, y, localZ);
                        if (current is BlockId.Stone or BlockId.Dirt or BlockId.Gravel)
                        {
                            chunk.SetBlock(localX, y, localZ, BlockId.Air);
                        }
                    }
                }
            }
        }
    }

    private static float FractalNoise(float x, float z, int seedOffset)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        for (int octave = 0; octave < 4; octave++)
        {
            value += Noise(x * frequency, z * frequency, seedOffset + octave * 131) * amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return value;
    }

    private static float FractalNoise(float x, float y, float z)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        for (int octave = 0; octave < 3; octave++)
        {
            value += Noise(x * frequency, y * frequency + 17f, (int)z + octave * 97) * amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return value;
    }

    private static float Noise(float x, float y, int seed)
    {
        int xi = (int)MathF.Floor(x);
        int yi = (int)MathF.Floor(y);
        float xf = x - xi;
        float yf = y - yi;
        float a = HashFloat(xi, yi, seed);
        float b = HashFloat(xi + 1, yi, seed);
        float c = HashFloat(xi, yi + 1, seed);
        float d = HashFloat(xi + 1, yi + 1, seed);
        float u = Smooth(xf);
        float v = Smooth(yf);
        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    private static float HashFloat(int x, int y, int seed) => HashSeed(x ^ (y * 374761393), seed) / (float)int.MaxValue;

    private static int Hash(int x, int y, int z) => HashSeed(x ^ (y * 374761393) ^ (z * 668265263), 0);

    private static int HashSeed(int x, int seed)
    {
        unchecked
        {
            int hash = seed;
            hash ^= x * 1619;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            return hash;
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

public sealed class FlatWorldGenerator : IWorldGenerator
{
    public void GenerateChunk(GameWorld world, Chunk chunk)
    {
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                for (int y = 0; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = y switch
                    {
                        0 => BlockId.Bedrock,
                        < 24 => BlockId.Stone,
                        24 => BlockId.Dirt,
                        25 => BlockId.Grass,
                        _ => BlockId.Air,
                    };
                    chunk.SetBlock(localX, y, localZ, block);
                }
            }
        }
    }
}
