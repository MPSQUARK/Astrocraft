using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Math;
using AstroCraft.Core.Rendering;
using AstroCraft.Core.World;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using Silk.NET.Vulkan;

namespace AstroCraft.Client.Rendering;

public sealed class ChunkMeshCache : IDisposable
{
    private const int MaxConcurrentMeshBuilds = 2;

    private readonly Dictionary<ChunkPosition, ChunkGpuMesh> _meshes = new();
    private readonly HashSet<ChunkPosition> _pendingRebuilds = new();
    private readonly HashSet<ChunkPosition> _inFlightBuilds = new();
    private readonly ConcurrentQueue<CompletedMeshBuild> _completedBuilds = new();
    private readonly object _buildStateLock = new();
    private readonly GameWorld _world;
    private readonly VulkanRenderer _renderer;
    private long _totalVertexCount;
    private long _meshRevision;

    public ChunkMeshCache(GameWorld world, VulkanRenderer renderer)
    {
        _world = world;
        _renderer = renderer;
    }

    public IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> Meshes => _meshes;

    public long TotalVertexCount => _totalVertexCount;

    public long MeshRevision => _meshRevision;

    public bool HasMesh(ChunkPosition position) => _meshes.ContainsKey(position);

    public bool HasPlayerChunkMesh(ChunkPosition playerChunk) => _meshes.ContainsKey(playerChunk);

    public bool HasPendingWork => _pendingRebuilds.Count > 0 || !_completedBuilds.IsEmpty || _inFlightBuilds.Count > 0;

    public void QueueDirty(IEnumerable<ChunkPosition> dirtyChunks)
    {
        foreach (ChunkPosition dirty in dirtyChunks)
        {
            _pendingRebuilds.Add(dirty);
        }
    }

    public void QueueChunksWithoutMesh()
    {
        foreach (Chunk chunk in _world.LoadedChunks)
        {
            if (!_meshes.ContainsKey(chunk.Position))
            {
                _pendingRebuilds.Add(chunk.Position);
            }
        }
    }

    public void RemoveChunkMesh(ChunkPosition position)
    {
        if (!_meshes.TryGetValue(position, out ChunkGpuMesh? mesh))
        {
            _pendingRebuilds.Remove(position);
            lock (_buildStateLock)
            {
                _inFlightBuilds.Remove(position);
            }

            return;
        }

        _totalVertexCount -= mesh.OpaqueVertexCount + mesh.TransparentVertexCount;
        _renderer.DestroyChunkMesh(mesh);
        _meshes.Remove(position);
        _pendingRebuilds.Remove(position);
        lock (_buildStateLock)
        {
            _inFlightBuilds.Remove(position);
        }

        _meshRevision++;
    }

    public int ProcessPending(
        VulkanRenderer renderer,
        int maxRebuilds,
        ChunkPosition playerChunk,
        int maxElapsedMilliseconds = 0)
    {
        Stopwatch? budget = maxElapsedMilliseconds > 0 ? Stopwatch.StartNew() : null;
        bool OverBudget() => budget is not null && budget.ElapsedMilliseconds >= maxElapsedMilliseconds;

        int remaining = maxRebuilds;
        int processed = UploadCompletedBuilds(renderer, remaining);
        remaining -= processed;
        if (remaining <= 0 || OverBudget() || _pendingRebuilds.Count == 0)
        {
            return processed;
        }

        ChunkPosition[] pending = _pendingRebuilds.ToArray();
        Array.Sort(pending, (a, b) =>
            ChunkDistanceSquared(a, playerChunk).CompareTo(ChunkDistanceSquared(b, playerChunk)));

        foreach (ChunkPosition position in pending)
        {
            if (remaining <= 0 || OverBudget())
            {
                break;
            }

            int inFlightCount;
            lock (_buildStateLock)
            {
                inFlightCount = _inFlightBuilds.Count;
            }

            if (inFlightCount >= MaxConcurrentMeshBuilds)
            {
                break;
            }

            if (!_pendingRebuilds.Contains(position))
            {
                continue;
            }

            if (!_world.TryGetChunk(position, out Chunk chunk))
            {
                _pendingRebuilds.Remove(position);
                continue;
            }

            if (_meshes.ContainsKey(position) && !chunk.IsDirty)
            {
                _pendingRebuilds.Remove(position);
                continue;
            }

            ChunkMeshBuildSnapshot? snapshot = ChunkMeshBuildSnapshot.TryCapture(chunk, _world);
            if (snapshot is null)
            {
                continue;
            }

            _pendingRebuilds.Remove(position);
            lock (_buildStateLock)
            {
                _inFlightBuilds.Add(position);
            }

            ThreadPool.QueueUserWorkItem(_ => BuildMeshOnWorker(position, snapshot));
            remaining--;
            processed++;
        }

        return processed;
    }

    public void Dispose()
    {
        foreach (ChunkGpuMesh mesh in _meshes.Values)
        {
            _renderer.DestroyChunkMesh(mesh);
        }

        _meshes.Clear();
        _pendingRebuilds.Clear();
        lock (_buildStateLock)
        {
            _inFlightBuilds.Clear();
        }

        while (_completedBuilds.TryDequeue(out _))
        {
        }

        _totalVertexCount = 0;
        _meshRevision = 0;
    }

    private void BuildMeshOnWorker(ChunkPosition position, ChunkMeshBuildSnapshot snapshot)
    {
        try
        {
            ChunkMeshData meshData = ChunkMeshBuilder.BuildMeshes(snapshot, ChunkMeshDetail.Full);
            _completedBuilds.Enqueue(new CompletedMeshBuild(position, meshData));
        }
        finally
        {
            lock (_buildStateLock)
            {
                _inFlightBuilds.Remove(position);
            }
        }
    }

    private int UploadCompletedBuilds(VulkanRenderer renderer, int maxUploads)
    {
        int uploaded = 0;
        while (uploaded < maxUploads && _completedBuilds.TryDequeue(out CompletedMeshBuild build))
        {
            if (!_world.TryGetChunk(build.Position, out Chunk chunk))
            {
                continue;
            }

            ApplyMeshBuild(renderer, chunk, build.Position, build.MeshData);
            uploaded++;
        }

        return uploaded;
    }

    private static int ChunkDistance(ChunkPosition a, ChunkPosition b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dz = Math.Abs(a.Z - b.Z);
        return Math.Max(dx, dz);
    }

    private static long ChunkDistanceSquared(ChunkPosition a, ChunkPosition b)
    {
        long dx = a.X - b.X;
        long dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private void ApplyMeshBuild(VulkanRenderer renderer, Chunk chunk, ChunkPosition position, ChunkMeshData meshData)
    {
        long previousVertices = 0;
        if (_meshes.TryGetValue(position, out ChunkGpuMesh? existingForCount) && existingForCount is not null)
        {
            previousVertices = existingForCount.OpaqueVertexCount + existingForCount.TransparentVertexCount;
        }

        if (_meshes.TryGetValue(position, out ChunkGpuMesh? existing))
        {
            if (meshData.Opaque.Length == 0 && meshData.Transparent.Length == 0)
            {
                renderer.DestroyChunkMesh(existing);
                _meshes.Remove(position);
                chunk.IsDirty = false;
                _totalVertexCount -= previousVertices;
                _meshRevision++;
                return;
            }

            if (renderer.TryUpdateChunkMesh(existing, meshData, out ChunkGpuMesh updated))
            {
                _meshes[position] = updated with { Detail = ChunkMeshDetail.Full };
                chunk.IsDirty = false;
                _totalVertexCount += updated.OpaqueVertexCount + updated.TransparentVertexCount - previousVertices;
                _meshRevision++;
                return;
            }

            renderer.DestroyChunkMesh(existing);
            _meshes.Remove(position);
        }

        if (meshData.Opaque.Length == 0 && meshData.Transparent.Length == 0)
        {
            chunk.IsDirty = false;
            _totalVertexCount -= previousVertices;
            _meshRevision++;
            return;
        }

        ChunkGpuMesh uploaded = renderer.UploadChunkMesh(meshData, ChunkMeshDetail.Full);
        _meshes[position] = uploaded;
        chunk.IsDirty = false;
        _totalVertexCount += uploaded.OpaqueVertexCount + uploaded.TransparentVertexCount - previousVertices;
        _meshRevision++;
    }

    private readonly record struct CompletedMeshBuild(ChunkPosition Position, ChunkMeshData MeshData);
}

public sealed record class ChunkGpuMesh(
    VkBuffer OpaqueVertexBuffer,
    DeviceMemory OpaqueMemory,
    uint OpaqueVertexCount,
    VkBuffer? TransparentVertexBuffer,
    DeviceMemory? TransparentMemory,
    uint TransparentVertexCount,
    GpuVertex[] OpaqueCpuVertices,
    GpuVertex[] TransparentCpuVertices,
    ChunkMeshDetail Detail = ChunkMeshDetail.Full)
{
    public VkBuffer VertexBuffer => OpaqueVertexBuffer;
    public DeviceMemory Memory => OpaqueMemory;
    public uint VertexCount => OpaqueVertexCount;
}
