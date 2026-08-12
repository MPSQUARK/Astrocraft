using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AstroCraft.Client.Rendering;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Rendering;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;
using Silk.NET.Vulkan;

namespace AstroCraft.Tests;

public sealed class ChunkRenderingPerfTests : IClassFixture<FlatWorldFixture>, IClassFixture<ProceduralWorldFixture>
{
    private readonly FlatWorldFixture _flat;
    private readonly ProceduralWorldFixture _procedural;

    public ChunkRenderingPerfTests(FlatWorldFixture flat, ProceduralWorldFixture procedural)
    {
        _flat = flat;
        _procedural = procedural;
    }

    [Fact]
    public void ChunkMeshBuilder_FlatChunk_BuildsUnderTimeBudget()
    {
        GameWorld world = _flat.CreateWorld(0);
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 8; i++)
        {
            BlockVertex[] mesh = ChunkMeshBuilder.BuildMesh(chunk, world);
            Assert.NotEmpty(mesh);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 800, $"Chunk mesh build took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ChunkMeshBuilder_SurfaceColumns_StillMeshesEveryExposedFace()
    {
        // Regression test: SurfaceColumns detail must never skip whole voxels. It previously
        // skipped any block with a solid block anywhere above it in the column, which hollowed
        // out terrain and produced see-through gaps on cliffs, caves, and overhangs at distance.
        // Lower detail levels are only allowed to drop per-vertex lighting/AO, not geometry.
        GameWorld world = _procedural.SharedWorld;
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        BlockVertex[] full = ChunkMeshBuilder.BuildMesh(chunk, world, ChunkMeshDetail.Full);
        BlockVertex[] surface = ChunkMeshBuilder.BuildMesh(chunk, world, ChunkMeshDetail.SurfaceColumns);

        Assert.NotEmpty(full);
        Assert.NotEmpty(surface);
        Assert.Equal(full.Length, surface.Length);
    }

    [Fact]
    public void ChunkMeshBuilder_SurfaceColumns_RendersSideFaceAtCliffEdge()
    {
        // Regression test for the "see-through terrain" bug: a solid block that is covered
        // from directly above (e.g. by grass at the surface) must still get its side/bottom
        // faces meshed at distant LOD if a neighboring column has been carved away (a cliff).
        // The old SurfaceColumns skip logic dropped the whole voxel in this case, leaving a
        // hollow shell that exposed sky/void through the cliff face.
        GameWorld world = _flat.CreateWorld(1);
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        const int cliffX = 5;
        const int openX = 6;
        const int z = 5;

        // Carve a vertical cliff: openX column becomes air above bedrock, cliffX stays solid.
        for (int y = 1; y < GameConstants.WorldHeight; y++)
        {
            chunk.SetBlock(openX, y, z, BlockId.Air);
        }

        Assert.Equal(BlockId.Dirt, chunk.GetBlock(cliffX, 24, z));

        BlockVertex[] surfaceMesh = ChunkMeshBuilder.BuildMesh(chunk, world, ChunkMeshDetail.SurfaceColumns);

        // The dirt block at (cliffX, 24, z) is covered above by grass but exposed on +X toward
        // the carved-out column. Its +X face quad sits at world X = cliffX + 1 = openX.
        bool hasCliffSideFace = surfaceMesh.Any(v =>
            v.Nx == 1f && v.Ny == 0f && v.Nz == 0f
            && MathF.Abs(v.X - openX) < 0.001f
            && v.Y is >= 24f and <= 25f
            && v.Z is >= z and <= z + 1f);

        Assert.True(hasCliffSideFace, "Expected a +X side face at the carved cliff edge; terrain would be hollow/see-through otherwise.");
    }

    [Fact]
    public void ChunkMeshBuilder_Leaves_AreOpaqueWithStandardCulling()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        Assert.False(registry.IsTransparent(BlockId.Leaves));

        GameWorld world = _flat.CreateWorld(3);
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));
        chunk.SetBlock(5, 30, 5, BlockId.Leaves);
        chunk.SetBlock(6, 30, 5, BlockId.Leaves);

        BlockVertex[] pair = ChunkMeshBuilder.BuildMesh(chunk, world);
        chunk.SetBlock(6, 30, 5, BlockId.Air);
        BlockVertex[] single = ChunkMeshBuilder.BuildMesh(chunk, world);

        Assert.True(pair.Length < single.Length * 2,
            "Adjacent opaque leaves should cull shared faces for performance.");
    }

    [Fact]
    public void FrustumCuller_SkipsChunksBehindCamera()
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            new Vector3(0f, 32f, 0f),
            new Vector3(0f, 32f, 1f),
            Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 500f);
        projection.M22 *= -1f;
        Matrix4x4 mvp = view * projection;

        Vector3 behindMin = new(-8f, 0f, -32f);
        Vector3 behindMax = new(8f, 64f, -16f);
        Vector3 aheadMin = new(-8f, 0f, 8f);
        Vector3 aheadMax = new(8f, 64f, 24f);

        Assert.False(FrustumCuller.IsAabbVisible(behindMin, behindMax, mvp));
        Assert.True(FrustumCuller.IsAabbVisible(aheadMin, aheadMax, mvp));
    }

    [Fact]
    public void ChunkDrawBatchCollector_CountsOnlyVisibleChunkVertices()
    {
        GpuVertex[] visibleOpaque = { default, default, default };
        GpuVertex[] hiddenOpaque = { default, default };
        var meshes = new Dictionary<ChunkPosition, ChunkGpuMesh>
        {
            [new ChunkPosition(0, 1)] = new(
                default, default, 3, null, null, 0,
                visibleOpaque, [],
                ChunkMeshDetail.Full),
            [new ChunkPosition(0, -2)] = new(
                default, default, 2, null, null, 0,
                hiddenOpaque, [],
                ChunkMeshDetail.Full),
        };

        Matrix4x4 view = Matrix4x4.CreateLookAt(
            new Vector3(0f, 32f, 0f),
            new Vector3(0f, 32f, 1f),
            Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 500f);
        projection.M22 *= -1f;
        FrustumPlanes frustum = FrustumCuller.ExtractFrustum(view * projection);

        int opaqueCount = ChunkDrawBatchCollector.CountVisibleOpaqueVertices(meshes, frustum, new ChunkPosition(0, 1));
        Assert.Equal(3, opaqueCount);

        Span<GpuVertex> batch = stackalloc GpuVertex[8];
        int copied = ChunkDrawBatchCollector.CopyVisibleOpaqueVertices(meshes, frustum, batch, new ChunkPosition(0, 1));
        Assert.Equal(3, copied);
    }

    [Fact]
    public void ChunkDrawBatchCollector_CopiesAllMeshesForDrawBatch()
    {
        GpuVertex[] opaque = { default, default };
        var meshes = new Dictionary<ChunkPosition, ChunkGpuMesh>
        {
            [new ChunkPosition(0, 0)] = new(
                default, default, 2, null, null, 0,
                opaque, [],
                ChunkMeshDetail.Full),
        };

        Assert.Equal(2, ChunkDrawBatchCollector.CountOpaqueVertices(meshes));
        Span<GpuVertex> batch = stackalloc GpuVertex[4];
        Assert.Equal(2, ChunkDrawBatchCollector.CopyOpaqueVertices(meshes, batch));
    }
}
