using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Tests;

public class WorldGenerationTests
{
    [Fact]
    public void ProceduralGenerator_ProducesDeterministicSurface_ForSameSeed()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        ProceduralWorldGenerator generatorA = new(42);
        ProceduralWorldGenerator generatorB = new(42);
        GameWorld worldA = new(registry, generatorA);
        GameWorld worldB = new(registry, generatorB);

        Chunk chunkA = worldA.GetOrCreateChunk(new ChunkPosition(0, 0));
        Chunk chunkB = worldB.GetOrCreateChunk(new ChunkPosition(0, 0));

        Assert.Equal(chunkA.GetBlock(0, 20, 0), chunkB.GetBlock(0, 20, 0));
    }

    [Fact]
    public void FlatGenerator_CreatesGrassLayerAtY25()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        GameWorld world = new(registry, new FlatWorldGenerator());
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        Assert.Equal(BlockId.Grass, chunk.GetBlock(0, 25, 0));
        Assert.Equal(BlockId.Air, chunk.GetBlock(0, 30, 0));
    }

    [Fact]
    public void ChunkPosition_FloorDiv_WorksForNegativeCoordinates()
    {
        ChunkPosition position = ChunkPosition.FromBlock(-1, -1);
        Assert.Equal(-1, position.X);
        Assert.Equal(-1, position.Z);
    }
}
