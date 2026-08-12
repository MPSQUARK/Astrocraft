using System.Runtime.InteropServices;
using AstroCraft.Core.Math;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace AstroCraft.Client.Rendering;

public sealed unsafe class ChunkDrawBatcher : IDisposable
{
    private const ulong InitialCapacityBytes = 4 * 1024 * 1024;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;

    private VkBuffer _opaqueBuffer;
    private DeviceMemory _opaqueMemory;
    private ulong _opaqueCapacityBytes;
    private uint _opaqueVertexCount;

    private VkBuffer _transparentBuffer;
    private DeviceMemory _transparentMemory;
    private ulong _transparentCapacityBytes;
    private uint _transparentVertexCount;

    private long _cachedMeshRevision = -1;

    public ChunkDrawBatcher(Vk vk, Device device, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
    }

    public uint OpaqueVertexCount => _opaqueVertexCount;

    public uint TransparentVertexCount => _transparentVertexCount;

    public void Prepare(IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes, long meshRevision)
    {
        if (meshRevision == _cachedMeshRevision)
        {
            return;
        }

        _cachedMeshRevision = meshRevision;

        int opaqueCount = ChunkDrawBatchCollector.CountOpaqueVertices(meshes);
        int transparentCount = ChunkDrawBatchCollector.CountTransparentVertices(meshes);

        _opaqueVertexCount = (uint)UploadBatch(
            opaqueCount,
            ref _opaqueBuffer,
            ref _opaqueMemory,
            ref _opaqueCapacityBytes,
            meshes,
            transparent: false);

        _transparentVertexCount = (uint)UploadBatch(
            transparentCount,
            ref _transparentBuffer,
            ref _transparentMemory,
            ref _transparentCapacityBytes,
            meshes,
            transparent: true);
    }

    public void Invalidate() => _cachedMeshRevision = -1;

    public void BindAndDrawOpaque(CommandBuffer commandBuffer)
    {
        if (_opaqueVertexCount == 0)
        {
            return;
        }

        VkBuffer vertexBuffer = _opaqueBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);
        _vk.CmdDraw(commandBuffer, _opaqueVertexCount, 1, 0, 0);
    }

    public void BindAndDrawTransparent(CommandBuffer commandBuffer)
    {
        if (_transparentVertexCount == 0)
        {
            return;
        }

        VkBuffer vertexBuffer = _transparentBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);
        _vk.CmdDraw(commandBuffer, _transparentVertexCount, 1, 0, 0);
    }

    public void Dispose()
    {
        DestroyBuffer(ref _opaqueBuffer, ref _opaqueMemory);
        DestroyBuffer(ref _transparentBuffer, ref _transparentMemory);
        _opaqueCapacityBytes = 0;
        _transparentCapacityBytes = 0;
        _opaqueVertexCount = 0;
        _transparentVertexCount = 0;
        _cachedMeshRevision = -1;
    }

    private int UploadBatch(
        int vertexCount,
        ref VkBuffer buffer,
        ref DeviceMemory memory,
        ref ulong capacityBytes,
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        bool transparent)
    {
        if (vertexCount <= 0)
        {
            return 0;
        }

        ulong requiredBytes = (ulong)(vertexCount * Marshal.SizeOf<GpuVertex>());
        EnsureCapacity(requiredBytes, ref buffer, ref memory, ref capacityBytes);

        void* mapped = null;
        _vk.MapMemory(_device, memory, 0, requiredBytes, 0, &mapped);
        Span<GpuVertex> destination = new(mapped, vertexCount);
        int copied = transparent
            ? ChunkDrawBatchCollector.CopyTransparentVertices(meshes, destination)
            : ChunkDrawBatchCollector.CopyOpaqueVertices(meshes, destination);
        _vk.UnmapMemory(_device, memory);
        return copied;
    }

    private void EnsureCapacity(ulong requiredBytes, ref VkBuffer buffer, ref DeviceMemory memory, ref ulong capacityBytes)
    {
        if (capacityBytes >= requiredBytes && buffer.Handle != 0)
        {
            return;
        }

        ulong newCapacity = Math.Max(InitialCapacityBytes, capacityBytes);
        while (newCapacity < requiredBytes)
        {
            newCapacity *= 2;
        }

        DestroyBuffer(ref buffer, ref memory);
        CreateBuffer(newCapacity, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out buffer, out memory);
        capacityBytes = newCapacity;
    }

    private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out VkBuffer buffer, out DeviceMemory memory)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };

        if (_vk.CreateBuffer(_device, ref bufferInfo, null, out buffer) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create chunk draw batch buffer.");
        }

        _vk.GetBufferMemoryRequirements(_device, buffer, out MemoryRequirements requirements);
        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, properties),
        };

        if (_vk.AllocateMemory(_device, ref allocInfo, null, out memory) != Result.Success)
        {
            throw new InvalidOperationException("Failed to allocate chunk draw batch buffer memory.");
        }

        _vk.BindBufferMemory(_device, buffer, memory, 0);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out PhysicalDeviceMemoryProperties memoryProperties);
        for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0
                && (memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Failed to find suitable memory type for chunk draw batch buffer.");
    }

    private void DestroyBuffer(ref VkBuffer buffer, ref DeviceMemory memory)
    {
        if (buffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, buffer, null);
            buffer = default;
        }

        if (memory.Handle != 0)
        {
            _vk.FreeMemory(_device, memory, null);
            memory = default;
        }
    }
}
