using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace AstroCraft.Client.Rendering;

public sealed unsafe class StagingBufferPool : IDisposable
{
    private const ulong Bucket64K = 64 * 1024;
    private const ulong Bucket256K = 256 * 1024;
    private const ulong Bucket1M = 1024 * 1024;

    private static readonly ulong[] BucketSizes = [Bucket64K, Bucket256K, Bucket1M];

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly List<PooledBuffer>[] _freeLists;
    private readonly Dictionary<ulong, PooledBuffer> _activeBuffers = new();

    public StagingBufferPool(Vk vk, Device device, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _freeLists = new List<PooledBuffer>[BucketSizes.Length];
        for (int i = 0; i < BucketSizes.Length; i++)
        {
            _freeLists[i] = new List<PooledBuffer>();
        }
    }

    public (VkBuffer Buffer, DeviceMemory Memory) Rent(ulong size)
    {
        if (size == 0)
        {
            return (default, default);
        }

        int bucketIndex = SelectBucketIndex(size);
        if (bucketIndex >= 0)
        {
            ulong capacity = BucketSizes[bucketIndex];
            List<PooledBuffer> freeList = _freeLists[bucketIndex];
            if (freeList.Count > 0)
            {
                PooledBuffer pooled = freeList[^1];
                freeList.RemoveAt(freeList.Count - 1);
                _activeBuffers[pooled.Buffer.Handle] = pooled;
                return (pooled.Buffer, pooled.Memory);
            }

            PooledBuffer created = CreatePooledBuffer(capacity);
            _activeBuffers[created.Buffer.Handle] = created;
            return (created.Buffer, created.Memory);
        }

        PooledBuffer oversized = CreatePooledBuffer(size);
        _activeBuffers[oversized.Buffer.Handle] = oversized;
        return (oversized.Buffer, oversized.Memory);
    }

    public void Return(VkBuffer buffer)
    {
        if (buffer.Handle == 0 || !_activeBuffers.Remove(buffer.Handle, out PooledBuffer pooled))
        {
            return;
        }

        int bucketIndex = BucketIndexForCapacity(pooled.Capacity);
        if (bucketIndex >= 0)
        {
            _freeLists[bucketIndex].Add(pooled);
            return;
        }

        DestroyBuffer(pooled.Buffer, pooled.Memory);
    }

    public void Dispose()
    {
        foreach (PooledBuffer pooled in _activeBuffers.Values)
        {
            DestroyBuffer(pooled.Buffer, pooled.Memory);
        }

        _activeBuffers.Clear();

        foreach (List<PooledBuffer> freeList in _freeLists)
        {
            foreach (PooledBuffer pooled in freeList)
            {
                DestroyBuffer(pooled.Buffer, pooled.Memory);
            }

            freeList.Clear();
        }
    }

    private static int SelectBucketIndex(ulong size)
    {
        for (int i = 0; i < BucketSizes.Length; i++)
        {
            if (size <= BucketSizes[i])
            {
                return i;
            }
        }

        return -1;
    }

    private static int BucketIndexForCapacity(ulong capacity)
    {
        for (int i = 0; i < BucketSizes.Length; i++)
        {
            if (capacity == BucketSizes[i])
            {
                return i;
            }
        }

        return -1;
    }

    private PooledBuffer CreatePooledBuffer(ulong capacity)
    {
        CreateBuffer(
            capacity,
            BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out VkBuffer buffer,
            out DeviceMemory memory);
        return new PooledBuffer(buffer, memory, capacity);
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
            throw new InvalidOperationException("Failed to create staging buffer.");
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
            throw new InvalidOperationException("Failed to allocate staging buffer memory.");
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

        throw new InvalidOperationException("Failed to find suitable memory type for staging buffer.");
    }

    private void DestroyBuffer(VkBuffer buffer, DeviceMemory memory)
    {
        if (buffer.Handle == 0)
        {
            return;
        }

        _vk.DestroyBuffer(_device, buffer, null);
        _vk.FreeMemory(_device, memory, null);
    }

    private readonly record struct PooledBuffer(VkBuffer Buffer, DeviceMemory Memory, ulong Capacity);
}
