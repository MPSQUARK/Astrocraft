using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Rendering;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;
using Xunit;

namespace AstroCraft.Tests;

public sealed class CrossPlantMeshTests
{
[Fact(Skip = "Cross-plants disabled for client performance until LOD is implemented.")]
    public void TallGrass_RendersCrossPlanes_NotCube()
    {
        GameWorld world = CreateWorldWithBlock(BlockId.TallGrass);
        Chunk chunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(4, 4));
        ChunkMeshData mesh = ChunkMeshBuilder.BuildMeshes(chunk, world);
        BlockVertex[] plantVerts = mesh.Opaque.Where(v => v.TextureIndex == 62).ToArray();

        Assert.Equal(12, plantVerts.Length);
    }

[Fact(Skip = "Cross-plants disabled for client performance until LOD is implemented.")]
    public void ShortGrass_RendersCrossPlanes()
    {
        GameWorld world = CreateWorldWithBlock(BlockId.ShortGrass);
        Chunk chunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(4, 4));
        ChunkMeshData mesh = ChunkMeshBuilder.BuildMeshes(chunk, world);
        BlockVertex[] plantVerts = mesh.Opaque.Where(v => v.TextureIndex == 64).ToArray();

        Assert.Equal(12, plantVerts.Length);
        float minY = plantVerts.Min(v => v.Y);
        float maxY = plantVerts.Max(v => v.Y);
        Assert.True(maxY - minY < 1f);
        Assert.True(maxY - minY > 0.3f);
    }

    [Fact]
    public void Stone_StillRendersCubeFaces()
    {
        GameWorld world = CreateWorldWithBlock(BlockId.Stone);
        Chunk chunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(4, 4));
        ChunkMeshData mesh = ChunkMeshBuilder.BuildMeshes(chunk, world);

        Assert.True(mesh.Opaque.Length >= 24);
    }

    private static GameWorld CreateWorldWithBlock(BlockId blockId)
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        GameWorld world = new(registry, new FlatWorldGenerator());
        world.TrySetBlock(4, 26, 4, blockId);
        return world;
    }
}
