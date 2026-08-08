using AstroCraft.Core.Math;
using AstroCraft.Core.Rendering;
using AstroCraft.Core.World;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using Silk.NET.Vulkan;

namespace AstroCraft.Client.Rendering;

public sealed class ChunkMeshCache : IDisposable
{
    private readonly Dictionary<ChunkPosition, ChunkGpuMesh> _meshes = new();
    private readonly GameWorld _world;
    private readonly VulkanRenderer _renderer;

    public ChunkMeshCache(GameWorld world, VulkanRenderer renderer)
    {
        _world = world;
        _renderer = renderer;
    }

    public IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> Meshes => _meshes;

    public void Sync(VulkanRenderer renderer, IEnumerable<ChunkPosition> dirtyChunks)
    {
        foreach (ChunkPosition dirty in dirtyChunks)
        {
            if (!_world.TryGetChunk(dirty, out Chunk chunk))
            {
                continue;
            }

            RebuildChunk(renderer, chunk);
        }

        PruneUnloadedChunks();
    }

    public void SyncAllLoaded(VulkanRenderer renderer)
    {
        foreach (Chunk chunk in _world.LoadedChunks)
        {
            if (!chunk.IsDirty && _meshes.ContainsKey(chunk.Position))
            {
                continue;
            }

            RebuildChunk(renderer, chunk);
        }

        PruneUnloadedChunks();
    }

    public void Dispose()
    {
        foreach (ChunkGpuMesh mesh in _meshes.Values)
        {
            _renderer.DestroyChunkMesh(mesh);
        }

        _meshes.Clear();
    }

    private void RebuildChunk(VulkanRenderer renderer, Chunk chunk)
    {
        BlockVertex[] vertices = ChunkMeshBuilder.BuildMesh(chunk, _world);
        if (_meshes.TryGetValue(chunk.Position, out ChunkGpuMesh? existing))
        {
            _renderer.DestroyChunkMesh(existing);
            _meshes.Remove(chunk.Position);
        }

        if (vertices.Length == 0)
        {
            chunk.IsDirty = false;
            return;
        }

        _meshes[chunk.Position] = renderer.UploadChunkMesh(vertices);
        chunk.IsDirty = false;
    }

    private void PruneUnloadedChunks()
    {
        HashSet<ChunkPosition> loaded = _world.LoadedChunkPositions.ToHashSet();
        ChunkPosition[] stale = _meshes.Keys.Where(position => !loaded.Contains(position)).ToArray();
        foreach (ChunkPosition position in stale)
        {
            _renderer.DestroyChunkMesh(_meshes[position]);
            _meshes.Remove(position);
        }
    }
}

public sealed class ChunkGpuMesh
{
    public ChunkGpuMesh(VkBuffer vertexBuffer, DeviceMemory memory, uint vertexCount)
    {
        VertexBuffer = vertexBuffer;
        Memory = memory;
        VertexCount = vertexCount;
    }

    public VkBuffer VertexBuffer { get; }
    public DeviceMemory Memory { get; }
    public uint VertexCount { get; }
}
