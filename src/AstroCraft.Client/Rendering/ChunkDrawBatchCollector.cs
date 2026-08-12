using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Math;

namespace AstroCraft.Client.Rendering;

public static class ChunkDrawBatchCollector
{
    public static int CountOpaqueVertices(IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes)
    {
        int total = 0;
        foreach (ChunkGpuMesh mesh in meshes.Values)
        {
            total += (int)mesh.OpaqueVertexCount;
        }

        return total;
    }

    public static int CountTransparentVertices(IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes)
    {
        int total = 0;
        foreach (ChunkGpuMesh mesh in meshes.Values)
        {
            total += (int)mesh.TransparentVertexCount;
        }

        return total;
    }

    public static int CopyOpaqueVertices(
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        Span<GpuVertex> destination)
    {
        int offset = 0;
        foreach (ChunkGpuMesh mesh in meshes.Values)
        {
            if (mesh.OpaqueVertexCount == 0)
            {
                continue;
            }

            ReadOnlySpan<GpuVertex> source = mesh.OpaqueCpuVertices.AsSpan(0, (int)mesh.OpaqueVertexCount);
            source.CopyTo(destination[offset..]);
            offset += source.Length;
        }

        return offset;
    }

    public static int CopyTransparentVertices(
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        Span<GpuVertex> destination)
    {
        int offset = 0;
        foreach (ChunkGpuMesh mesh in meshes.Values)
        {
            if (mesh.TransparentVertexCount == 0)
            {
                continue;
            }

            ReadOnlySpan<GpuVertex> source = mesh.TransparentCpuVertices.AsSpan(0, (int)mesh.TransparentVertexCount);
            source.CopyTo(destination[offset..]);
            offset += source.Length;
        }

        return offset;
    }

    public static int CountVisibleOpaqueVertices(
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        FrustumPlanes frustum,
        ChunkPosition playerChunk,
        int alwaysIncludeDistanceChunks = 2)
    {
        int total = 0;
        foreach ((ChunkPosition position, ChunkGpuMesh mesh) in meshes)
        {
            if (mesh.OpaqueVertexCount == 0 || !ShouldInclude(position, playerChunk, frustum, alwaysIncludeDistanceChunks))
            {
                continue;
            }

            total += (int)mesh.OpaqueVertexCount;
        }

        return total;
    }

    public static int CountVisibleTransparentVertices(
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        FrustumPlanes frustum,
        ChunkPosition playerChunk,
        int alwaysIncludeDistanceChunks = 2)
    {
        int total = 0;
        foreach ((ChunkPosition position, ChunkGpuMesh mesh) in meshes)
        {
            if (mesh.TransparentVertexCount == 0 || !ShouldInclude(position, playerChunk, frustum, alwaysIncludeDistanceChunks))
            {
                continue;
            }

            total += (int)mesh.TransparentVertexCount;
        }

        return total;
    }

    public static int CopyVisibleOpaqueVertices(
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        FrustumPlanes frustum,
        Span<GpuVertex> destination,
        ChunkPosition playerChunk,
        int alwaysIncludeDistanceChunks = 2)
    {
        int offset = 0;
        foreach ((ChunkPosition position, ChunkGpuMesh mesh) in meshes)
        {
            if (mesh.OpaqueVertexCount == 0 || !ShouldInclude(position, playerChunk, frustum, alwaysIncludeDistanceChunks))
            {
                continue;
            }

            ReadOnlySpan<GpuVertex> source = mesh.OpaqueCpuVertices.AsSpan(0, (int)mesh.OpaqueVertexCount);
            source.CopyTo(destination[offset..]);
            offset += source.Length;
        }

        return offset;
    }

    public static int CopyVisibleTransparentVertices(
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        FrustumPlanes frustum,
        Span<GpuVertex> destination,
        ChunkPosition playerChunk,
        int alwaysIncludeDistanceChunks = 2)
    {
        int offset = 0;
        foreach ((ChunkPosition position, ChunkGpuMesh mesh) in meshes)
        {
            if (mesh.TransparentVertexCount == 0 || !ShouldInclude(position, playerChunk, frustum, alwaysIncludeDistanceChunks))
            {
                continue;
            }

            ReadOnlySpan<GpuVertex> source = mesh.TransparentCpuVertices.AsSpan(0, (int)mesh.TransparentVertexCount);
            source.CopyTo(destination[offset..]);
            offset += source.Length;
        }

        return offset;
    }

    private static bool ShouldInclude(
        ChunkPosition position,
        ChunkPosition playerChunk,
        FrustumPlanes frustum,
        int alwaysIncludeDistanceChunks)
    {
        if (ChunkDistance(position, playerChunk) <= alwaysIncludeDistanceChunks)
        {
            return true;
        }

        return IsChunkVisible(position, frustum);
    }

    private static int ChunkDistance(ChunkPosition a, ChunkPosition b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dz = Math.Abs(a.Z - b.Z);
        return Math.Max(dx, dz);
    }

    private static bool IsChunkVisible(ChunkPosition position, FrustumPlanes frustum)
    {
        float minX = position.X * GameConstants.ChunkSizeX;
        float minZ = position.Z * GameConstants.ChunkSizeZ;
        Vector3 chunkMin = new(minX, 0f, minZ);
        Vector3 chunkMax = new(
            minX + GameConstants.ChunkSizeX,
            GameConstants.ChunkSizeY,
            minZ + GameConstants.ChunkSizeZ);
        return FrustumCuller.IsAabbVisible(chunkMin, chunkMax, frustum);
    }
}
