using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AstroCraft.Client.Effects;
using AstroCraft.Client.UI;
using AstroCraft.Core;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.Rendering;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace AstroCraft.Client.Rendering;

public sealed unsafe class VulkanRenderer : IDisposable
{
    private const int MaxFramesInFlight = 2;
    private const int TextureLayerCount = 32;
    private const int InventorySlotCount = 36;
    private static readonly Vector4 ClearColor = new(0.025f, 0.045f, 0.13f, 1f);

    private readonly IWindow _window;
    private Vk _vk = null!;
    private Glfw _glfw = null!;
    private KhrSurface _khrSurface = null!;
    private KhrSwapchain _khrSwapchain = null!;

    private Instance _instance;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _presentQueue;
    private uint _graphicsQueueFamily;
    private uint _presentQueueFamily;
    private SwapchainKHR _swapchain;
    private VkImage[] _swapchainImages = [];
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;
    private ImageView[] _swapchainImageViews = [];
    private RenderPass _renderPass;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;
    private Pipeline _transparentPipeline;
    private Pipeline _skyPipeline;
    private Framebuffer[] _framebuffers = [];
    private CommandPool _commandPool;
    private CommandBuffer[] _commandBuffers = [];
    private VkSemaphore[] _imageAvailableSemaphores = [];
    private VkSemaphore[] _renderFinishedSemaphores = [];
    private Fence[] _inFlightFences = [];
    private VkBuffer _uniformBuffer;
    private DeviceMemory _uniformMemory;
    private VkImage _textureImage;
    private DeviceMemory _textureMemory;
    private ImageView _textureImageView;
    private Sampler _textureSampler;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet[] _descriptorSets = [];
    private VkImage _depthImage;
    private DeviceMemory _depthMemory;
    private ImageView _depthImageView;
    private int _currentFrame;
    private int _lastSubmittedFrame = -1;
    private bool _framebufferResized;
    private readonly List<ChunkGpuMesh> _pendingMeshDestroys = new();
    private void* _mappedUniform;
    private VkBuffer _skyVertexBuffer;
    private DeviceMemory _skyVertexMemory;
    private VkBuffer _particleVertexBuffer;
    private DeviceMemory _particleVertexMemory;
    private int _particleVertexCapacity;
    private VkBuffer _inventoryBuffer;
    private DeviceMemory _inventoryMemory;
    private void* _mappedInventory;
    private uint _imageIndex;
    private uint _lastPresentedImageIndex;
    private bool _frameActive;
    private string? _scheduledCapturePath;
    private VkBuffer _captureStagingBuffer;
    private DeviceMemory _captureStagingMemory;
    private ulong _captureStagingCapacity;
    private ChunkDrawBatcher? _chunkDrawBatcher;
    private StagingBufferPool? _stagingBufferPool;

    public RayTracingCapabilities RayTracing { get; }

    public VulkanRenderer(IWindow window)
    {
        _window = window;
        _vk = Vk.GetApi();
        _glfw = Glfw.GetApi();

        if (!_glfw.VulkanSupported())
        {
            throw new InvalidOperationException("Vulkan is not supported on this machine.");
        }

        CreateInstance();
        CreateSurface();
        PickPhysicalDevice();
        RayTracing = RayTracingCapabilities.Probe(_vk, _physicalDevice);
        Console.WriteLine(
            $"[Vulkan] Ray tracing ({RayTracingCapabilities.RayTracingPipelineExtensionName}): " +
            $"{(RayTracing.IsSupported ? "available" : "not available")}");
        CreateLogicalDevice();
        _stagingBufferPool = new StagingBufferPool(_vk, _device, _physicalDevice);
        _chunkDrawBatcher = new ChunkDrawBatcher(_vk, _device, _physicalDevice);
        CreateSwapchain();
        CreateImageViews();
        CreateDepthResources();
        CreateRenderPass();
        CreateDescriptorSetLayout();
        CreatePipeline();
        CreateFramebuffers();
        CreateCommandPool();
        CreateUniformBuffer();
        CreateInventoryBuffer();
        CreateSkyVertexBuffer();
        CreateParticleBuffer();
        CreateTextureArray();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateCommandBuffers();
        CreateSyncObjects();

        _window.Resize += OnWindowResized;
        _window.FramebufferResize += OnFramebufferResized;
    }

    public Extent2D Extent => _swapchainExtent;

    public Vector2D<int> DrawableSize => GetDrawableSize();

    public void NotifyDrawableSizeChanged(Vector2D<int> drawableSize)
    {
        if (drawableSize.X > 0 && drawableSize.Y > 0
            && (drawableSize.X != (int)_swapchainExtent.Width || drawableSize.Y != (int)_swapchainExtent.Height))
        {
            _framebufferResized = true;
        }
    }

    public Matrix4x4 BuildViewProjection(PlayerState player, float aspectRatio, float fieldOfViewDegrees = 70f)
    {
        Vector3 eye = player.EyePosition;
        Vector3 forward = new(
            MathF.Sin(player.YawRadians) * MathF.Cos(player.PitchRadians),
            MathF.Sin(player.PitchRadians),
            MathF.Cos(player.YawRadians) * MathF.Cos(player.PitchRadians));
        Vector3 target = eye + Vector3.Normalize(forward);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        Matrix4x4 projection = CreatePerspective(aspectRatio, fieldOfViewDegrees);
        return view * projection;
    }

    public bool BeginFrame()
    {
        Vector2D<int> drawableSize = GetDrawableSize();
        if (drawableSize.X > 0 && drawableSize.Y > 0
            && (drawableSize.X != (int)_swapchainExtent.Width || drawableSize.Y != (int)_swapchainExtent.Height))
        {
            _framebufferResized = true;
        }

        if (_framebufferResized)
        {
            RecreateSwapchain();
            if (_swapchainExtent.Width == 0 || _swapchainExtent.Height == 0)
            {
                return false;
            }
        }

        Fence inFlightFence = _inFlightFences[_currentFrame];
        _vk.WaitForFences(_device, 1, ref inFlightFence, new Bool32(true), ulong.MaxValue);
        FlushPendingMeshDestroys();

        Result acquireResult = _khrSwapchain.AcquireNextImage(
            _device,
            _swapchain,
            ulong.MaxValue,
            _imageAvailableSemaphores[_currentFrame],
            default,
            ref _imageIndex);

        if (acquireResult == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return false;
        }

        if (acquireResult != Result.Success && acquireResult != Result.SuboptimalKhr)
        {
            throw new InvalidOperationException($"Failed to acquire swapchain image: {acquireResult}");
        }

        Fence resetFence = _inFlightFences[_currentFrame];
        _vk.ResetFences(_device, 1, ref resetFence);

        CommandBuffer commandBuffer = _commandBuffers[_currentFrame];
        _vk.ResetCommandBuffer(commandBuffer, 0);

        CommandBufferBeginInfo beginInfo = new() { SType = StructureType.CommandBufferBeginInfo };
        _vk.BeginCommandBuffer(commandBuffer, ref beginInfo);

        ClearColorValue clearColorValue = new();
        clearColorValue.Float32_0 = ClearColor.X;
        clearColorValue.Float32_1 = ClearColor.Y;
        clearColorValue.Float32_2 = ClearColor.Z;
        clearColorValue.Float32_3 = ClearColor.W;
        ClearValue colorClear = new() { Color = clearColorValue };
        ClearValue depthClear = new() { DepthStencil = new ClearDepthStencilValue(1f, 0) };
        ClearValue[] clearValues = [colorClear, depthClear];
        fixed (ClearValue* clearValuesPtr = clearValues)
        {
            RenderPassBeginInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass,
                Framebuffer = _framebuffers[_imageIndex],
                RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
                ClearValueCount = (uint)clearValues.Length,
                PClearValues = clearValuesPtr,
            };

            _vk.CmdBeginRenderPass(commandBuffer, ref renderPassInfo, SubpassContents.Inline);
        }
        _frameActive = true;
        return true;
    }

    public void DrawChunks(
        IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes,
        Matrix4x4 modelViewProjection,
        long meshRevision,
        Vector3 cameraPosition,
        Vector2 viewportSize,
        Vector4 survivalHud,
        float hudFlags,
        float overlayProgress,
        ReadOnlySpan<int> inventorySlots,
        Vector3 targetBlockMin,
        float hasTarget,
        float timeOfDay,
        float breakBurstTimer = 0f,
        float breakingBlockTexture = 0f,
        Vector3 ghostBlockMin = default,
        float ghostActive = 0f,
        float ghostValid = 0f,
        float ghostTexture = 0f,
        float heldItemTexture = 0f,
        float hasHeldItem = 0f,
        float time = 0f)
    {
        if (!_frameActive)
        {
            return;
        }

        UpdateInventoryBuffer(inventorySlots);
        UpdateUniformBuffer(
            modelViewProjection,
            cameraPosition,
            viewportSize,
            survivalHud,
            hudFlags,
            overlayProgress,
            targetBlockMin,
            hasTarget,
            timeOfDay,
            breakBurstTimer,
            breakingBlockTexture,
            ghostBlockMin,
            ghostActive,
            ghostValid,
            ghostTexture,
            heldItemTexture,
            hasHeldItem,
            time);

        CommandBuffer commandBuffer = _commandBuffers[_currentFrame];
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);

        Viewport viewport = new(0, 0, _swapchainExtent.Width, _swapchainExtent.Height, 0, 1);
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        Rect2D scissor = new(new Offset2D(0, 0), _swapchainExtent);
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

        DescriptorSet descriptorSet = _descriptorSets[_currentFrame];
        _vk.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);

        DrawOpaqueChunkMeshes(commandBuffer, meshes);
        DrawTransparentChunkMeshes(commandBuffer, meshes);

        // Sky fullscreen triangle at far depth — drawn last; depth test keeps it behind geometry.
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _skyPipeline);
        VkBuffer skyVertexBuffer = _skyVertexBuffer;
        ulong skyOffset = 0;
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &skyVertexBuffer, &skyOffset);
        _vk.CmdDraw(commandBuffer, 3, 1, 0, 0);
    }

    private void DrawOpaqueChunkMeshes(CommandBuffer commandBuffer, IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes)
    {
        foreach (KeyValuePair<ChunkPosition, ChunkGpuMesh> entry in meshes)
        {
            ChunkGpuMesh mesh = entry.Value;
            if (mesh.OpaqueVertexCount == 0)
            {
                continue;
            }

            VkBuffer vertexBuffer = mesh.OpaqueVertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);
            _vk.CmdDraw(commandBuffer, mesh.OpaqueVertexCount, 1, 0, 0);
        }
    }

    private void DrawTransparentChunkMeshes(CommandBuffer commandBuffer, IReadOnlyDictionary<ChunkPosition, ChunkGpuMesh> meshes)
    {
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _transparentPipeline);

        DescriptorSet descriptorSet = _descriptorSets[_currentFrame];
        _vk.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);

        foreach (KeyValuePair<ChunkPosition, ChunkGpuMesh> entry in meshes)
        {
            ChunkGpuMesh mesh = entry.Value;
            if (mesh.TransparentVertexCount == 0 || mesh.TransparentVertexBuffer is null)
            {
                continue;
            }

            VkBuffer vertexBuffer = mesh.TransparentVertexBuffer.Value;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);
            _vk.CmdDraw(commandBuffer, mesh.TransparentVertexCount, 1, 0, 0);
        }

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
    }

    public void DrawBlockParticles(ReadOnlySpan<BlockParticle> particles, Vector3 cameraPosition)
    {
        if (!_frameActive || particles.IsEmpty)
        {
            return;
        }

        Span<GpuVertex> vertices = stackalloc GpuVertex[particles.Length * 6];
        int vertexCount = BuildParticleVertices(particles, cameraPosition, vertices);
        if (vertexCount == 0)
        {
            return;
        }

        EnsureParticleCapacity(vertexCount);
        ulong bufferSize = (ulong)(vertexCount * Marshal.SizeOf<GpuVertex>());
        void* mapped = null;
        _vk.MapMemory(_device, _particleVertexMemory, 0, bufferSize, 0, &mapped);
        MemoryMarshal.AsBytes(vertices[..vertexCount]).CopyTo(new Span<byte>(mapped, (int)bufferSize));
        _vk.UnmapMemory(_device, _particleVertexMemory);

        CommandBuffer commandBuffer = _commandBuffers[_currentFrame];
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);

        DescriptorSet descriptorSet = _descriptorSets[_currentFrame];
        _vk.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);

        VkBuffer vertexBuffer = _particleVertexBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);
        _vk.CmdDraw(commandBuffer, (uint)vertexCount, 1, 0, 0);
    }

    public void DrawItemEntities(ReadOnlySpan<BlockParticle> items, Vector3 cameraPosition)
    {
        if (!_frameActive || items.IsEmpty)
        {
            return;
        }

        Span<GpuVertex> vertices = stackalloc GpuVertex[items.Length * 6];
        int vertexCount = BuildSpinningItemVertices(items, cameraPosition, vertices);
        if (vertexCount == 0)
        {
            return;
        }

        EnsureParticleCapacity(vertexCount);
        ulong bufferSize = (ulong)(vertexCount * Marshal.SizeOf<GpuVertex>());
        void* mapped = null;
        _vk.MapMemory(_device, _particleVertexMemory, 0, bufferSize, 0, &mapped);
        MemoryMarshal.AsBytes(vertices[..vertexCount]).CopyTo(new Span<byte>(mapped, (int)bufferSize));
        _vk.UnmapMemory(_device, _particleVertexMemory);

        CommandBuffer commandBuffer = _commandBuffers[_currentFrame];
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);

        DescriptorSet descriptorSet = _descriptorSets[_currentFrame];
        _vk.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);

        VkBuffer vertexBuffer = _particleVertexBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);
        _vk.CmdDraw(commandBuffer, (uint)vertexCount, 1, 0, 0);
    }

    private static int BuildSpinningItemVertices(
        ReadOnlySpan<BlockParticle> items,
        Vector3 cameraPosition,
        Span<GpuVertex> destination)
    {
        int writeIndex = 0;

        foreach (BlockParticle item in items)
        {
            Vector3 toCamera = cameraPosition - item.Position;
            if (toCamera.LengthSquared() < 1e-6f)
            {
                toCamera = Vector3.UnitZ;
            }

            toCamera = Vector3.Normalize(toCamera);
            Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toCamera));
            if (right.LengthSquared() < 1e-6f)
            {
                right = Vector3.UnitX;
            }

            Vector3 up = Vector3.Cross(toCamera, right);
            float cos = MathF.Cos(item.SpinRadians);
            float sin = MathF.Sin(item.SpinRadians);
            Vector3 rotatedRight = right * cos + up * sin;
            Vector3 rotatedUp = up * cos - right * sin;

            float half = item.Size * 0.5f;
            Vector3 center = item.Position;
            Vector3 v0 = center - rotatedRight * half - rotatedUp * half;
            Vector3 v1 = center + rotatedRight * half - rotatedUp * half;
            Vector3 v2 = center + rotatedRight * half + rotatedUp * half;
            Vector3 v3 = center - rotatedRight * half + rotatedUp * half;

            if (writeIndex + 6 > destination.Length)
            {
                break;
            }

            destination[writeIndex++] = new GpuVertex(v0, new Vector2(0f, 0f), item.TextureIndex, toCamera, 1f);
            destination[writeIndex++] = new GpuVertex(v1, new Vector2(1f, 0f), item.TextureIndex, toCamera, 1f);
            destination[writeIndex++] = new GpuVertex(v2, new Vector2(1f, 1f), item.TextureIndex, toCamera, 1f);
            destination[writeIndex++] = new GpuVertex(v0, new Vector2(0f, 0f), item.TextureIndex, toCamera, 1f);
            destination[writeIndex++] = new GpuVertex(v2, new Vector2(1f, 1f), item.TextureIndex, toCamera, 1f);
            destination[writeIndex++] = new GpuVertex(v3, new Vector2(0f, 1f), item.TextureIndex, toCamera, 1f);
        }

        return writeIndex;
    }

    private static int BuildParticleVertices(
        ReadOnlySpan<BlockParticle> particles,
        Vector3 cameraPosition,
        Span<GpuVertex> destination)
    {
        int writeIndex = 0;
        Vector3 upAxis = Vector3.UnitY;

        foreach (BlockParticle particle in particles)
        {
            Vector3 toCamera = cameraPosition - particle.Position;
            if (toCamera.LengthSquared() < 1e-6f)
            {
                toCamera = Vector3.UnitZ;
            }

            toCamera = Vector3.Normalize(toCamera);
            Vector3 right = Vector3.Normalize(Vector3.Cross(upAxis, toCamera));
            if (right.LengthSquared() < 1e-6f)
            {
                right = Vector3.UnitX;
            }

            Vector3 up = Vector3.Cross(toCamera, right);
            float half = particle.Size * 0.5f;
            float fade = Math.Clamp(particle.Life / Math.Max(0.001f, particle.MaxLife), 0f, 1f);

            Vector3 center = particle.Position;
            Vector3 v0 = center - right * half - up * half;
            Vector3 v1 = center + right * half - up * half;
            Vector3 v2 = center + right * half + up * half;
            Vector3 v3 = center - right * half + up * half;

            if (writeIndex + 6 > destination.Length)
            {
                break;
            }

            destination[writeIndex++] = new GpuVertex(v0, new Vector2(0f, 0f), particle.TextureIndex, toCamera, fade);
            destination[writeIndex++] = new GpuVertex(v1, new Vector2(1f, 0f), particle.TextureIndex, toCamera, fade);
            destination[writeIndex++] = new GpuVertex(v2, new Vector2(1f, 1f), particle.TextureIndex, toCamera, fade);
            destination[writeIndex++] = new GpuVertex(v0, new Vector2(0f, 0f), particle.TextureIndex, toCamera, fade);
            destination[writeIndex++] = new GpuVertex(v2, new Vector2(1f, 1f), particle.TextureIndex, toCamera, fade);
            destination[writeIndex++] = new GpuVertex(v3, new Vector2(0f, 1f), particle.TextureIndex, toCamera, fade);
        }

        return writeIndex;
    }

    private void EnsureParticleCapacity(int vertexCount)
    {
        if (vertexCount <= _particleVertexCapacity)
        {
            return;
        }

        if (_particleVertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _particleVertexBuffer, null);
            _vk.FreeMemory(_device, _particleVertexMemory, null);
        }

        _particleVertexCapacity = Math.Max(vertexCount, 256);
        ulong bufferSize = (ulong)(_particleVertexCapacity * Marshal.SizeOf<GpuVertex>());
        CreateBuffer(
            bufferSize,
            BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _particleVertexBuffer,
            out _particleVertexMemory);
    }

    public void ScheduleFrameCapture(string outputPath) => _scheduledCapturePath = outputPath;

    public void EndFrame()
    {
        if (!_frameActive)
        {
            return;
        }

        CommandBuffer commandBuffer = _commandBuffers[_currentFrame];
        _vk.CmdEndRenderPass(commandBuffer);

        string? capturePath = _scheduledCapturePath;
        bool capturing = capturePath is not null;
        if (capturing)
        {
            RecordSwapchainReadback(commandBuffer, _swapchainImages[_imageIndex]);
        }

        _vk.EndCommandBuffer(commandBuffer);

        VkSemaphore waitSemaphore = _imageAvailableSemaphores[_currentFrame];
        VkSemaphore signalSemaphore = _renderFinishedSemaphores[_currentFrame];
        Fence submitFence = _inFlightFences[_currentFrame];
        PipelineStageFlags waitStages = PipelineStageFlags.ColorAttachmentOutputBit;

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore,
        };

        _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, submitFence);
        _lastSubmittedFrame = _currentFrame;

        if (capturing && OperatingSystem.IsWindows())
        {
            _vk.WaitForFences(_device, 1, ref submitFence, new Bool32(true), ulong.MaxValue);
            if (!TrySaveScheduledCapture(capturePath!))
            {
                _scheduledCapturePath = null;
            }
        }

        SwapchainKHR swapchain = _swapchain;
        uint imageIndex = _imageIndex;
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signalSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex,
        };

        Result presentResult = _khrSwapchain.QueuePresent(_presentQueue, ref presentInfo);
        if (presentResult is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
        {
            _framebufferResized = true;
        }

        _lastPresentedImageIndex = _imageIndex;
        _frameActive = false;
        _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
    }

    [SupportedOSPlatform("windows")]
    public bool TryCapturePresentedFrame(string outputPath)
    {
        ScheduleFrameCapture(outputPath);
        return false;
    }

    private void RecordSwapchainReadback(CommandBuffer commandBuffer, VkImage swapchainImage)
    {
        uint width = _swapchainExtent.Width;
        uint height = _swapchainExtent.Height;
        if (width == 0 || height == 0)
        {
            return;
        }

        ulong bufferSize = width * height * 4;
        EnsureCaptureStagingBuffer(bufferSize);

        ImageMemoryBarrier toTransfer = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.PresentSrcKhr,
            NewLayout = ImageLayout.TransferSrcOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = swapchainImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
        };

        _vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            ref toTransfer);

        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = width,
            BufferImageHeight = height,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1),
        };

        _vk.CmdCopyImageToBuffer(
            commandBuffer,
            swapchainImage,
            ImageLayout.TransferSrcOptimal,
            _captureStagingBuffer,
            1,
            ref region);

        toTransfer.OldLayout = ImageLayout.TransferSrcOptimal;
        toTransfer.NewLayout = ImageLayout.PresentSrcKhr;
        toTransfer.SrcAccessMask = AccessFlags.TransferReadBit;
        toTransfer.DstAccessMask = AccessFlags.MemoryReadBit;

        _vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.ColorAttachmentOutputBit,
            0,
            0,
            null,
            0,
            null,
            1,
            ref toTransfer);
    }

    private void EnsureCaptureStagingBuffer(ulong bufferSize)
    {
        if (_captureStagingBuffer.Handle != 0 && bufferSize <= _captureStagingCapacity)
        {
            return;
        }

        if (_captureStagingBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _captureStagingBuffer, null);
            _vk.FreeMemory(_device, _captureStagingMemory, null);
        }

        _captureStagingCapacity = bufferSize;
        CreateBuffer(
            bufferSize,
            BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _captureStagingBuffer,
            out _captureStagingMemory);
    }

    [SupportedOSPlatform("windows")]
    private bool TrySaveScheduledCapture(string outputPath)
    {
        uint width = _swapchainExtent.Width;
        uint height = _swapchainExtent.Height;
        if (width == 0 || height == 0 || _captureStagingBuffer.Handle == 0)
        {
            return false;
        }

        ulong bufferSize = width * height * 4;
        void* mapped = null;
        _vk.MapMemory(_device, _captureStagingMemory, 0, bufferSize, 0, &mapped);
        try
        {
            if (!CriticScreenshot.TrySaveBgra32(new ReadOnlySpan<byte>(mapped, (int)bufferSize), (int)width, (int)height, outputPath))
            {
                return false;
            }
        }
        finally
        {
            _vk.UnmapMemory(_device, _captureStagingMemory);
        }

        _scheduledCapturePath = null;
        return true;
    }

    public void WaitForLastSubmittedFrame()
    {
        if (_lastSubmittedFrame < 0)
        {
            return;
        }

        Fence fence = _inFlightFences[_lastSubmittedFrame];
        _vk.WaitForFences(_device, 1, ref fence, new Bool32(true), ulong.MaxValue);
    }

    public ChunkGpuMesh UploadChunkMesh(ChunkMeshData meshData, ChunkMeshDetail detail = ChunkMeshDetail.Full)
    {
        GpuVertex[] opaqueCpu = GpuVertex.FromBlockVertices(meshData.Opaque);
        GpuVertex[] transparentCpu = GpuVertex.FromBlockVertices(meshData.Transparent);
        (VkBuffer opaqueBuffer, DeviceMemory opaqueMemory, uint opaqueCount) =
            UploadVertices(opaqueCpu);
        if (meshData.Transparent.Length == 0)
        {
            return new ChunkGpuMesh(opaqueBuffer, opaqueMemory, opaqueCount, null, null, 0, opaqueCpu, transparentCpu, detail);
        }

        (VkBuffer transparentBuffer, DeviceMemory transparentMemory, uint transparentCount) =
            UploadVertices(transparentCpu);
        return new ChunkGpuMesh(
            opaqueBuffer,
            opaqueMemory,
            opaqueCount,
            transparentBuffer,
            transparentMemory,
            transparentCount,
            opaqueCpu,
            transparentCpu,
            detail);
    }

    public ChunkGpuMesh UploadChunkMesh(BlockVertex[] vertices, ChunkMeshDetail detail = ChunkMeshDetail.Full)
    {
        GpuVertex[] cpuVertices = GpuVertex.FromBlockVertices(vertices);
        (VkBuffer buffer, DeviceMemory memory, uint count) = UploadVertices(cpuVertices);
        return new ChunkGpuMesh(buffer, memory, count, null, null, 0, cpuVertices, [], detail);
    }

    public bool TryUpdateChunkMesh(ChunkGpuMesh existing, ChunkMeshData meshData, out ChunkGpuMesh updated)
    {
        if (meshData.Opaque.Length == 0 && meshData.Transparent.Length == 0)
        {
            updated = existing;
            return false;
        }

        if (existing.OpaqueVertexCount < meshData.Opaque.Length
            || existing.TransparentVertexCount < meshData.Transparent.Length)
        {
            updated = existing;
            return false;
        }

        if (meshData.Opaque.Length > 0)
        {
            GpuVertex[] opaqueVertices = GpuVertex.FromBlockVertices(meshData.Opaque);
            ulong opaqueSize = (ulong)(opaqueVertices.Length * Marshal.SizeOf<GpuVertex>());
            WriteGpuVertices(opaqueVertices, existing.OpaqueMemory, opaqueSize);
        }

        if (meshData.Transparent.Length > 0 && existing.TransparentMemory is not null)
        {
            GpuVertex[] transparentVertices = GpuVertex.FromBlockVertices(meshData.Transparent);
            ulong transparentSize = (ulong)(transparentVertices.Length * Marshal.SizeOf<GpuVertex>());
            WriteGpuVertices(transparentVertices, existing.TransparentMemory.Value, transparentSize);
        }

        updated = new ChunkGpuMesh(
            existing.OpaqueVertexBuffer,
            existing.OpaqueMemory,
            (uint)meshData.Opaque.Length,
            existing.TransparentVertexBuffer,
            existing.TransparentMemory,
            (uint)meshData.Transparent.Length,
            GpuVertex.FromBlockVertices(meshData.Opaque),
            GpuVertex.FromBlockVertices(meshData.Transparent),
            existing.Detail);
        return true;
    }

    public bool TryUpdateChunkMesh(ChunkGpuMesh existing, BlockVertex[] vertices, out ChunkGpuMesh updated)
    {
        if (vertices.Length == 0 || existing.OpaqueVertexCount < vertices.Length)
        {
            updated = existing;
            return false;
        }

        GpuVertex[] gpuVertices = GpuVertex.FromBlockVertices(vertices);
        ulong bufferSize = (ulong)(gpuVertices.Length * Marshal.SizeOf<GpuVertex>());
        WriteGpuVertices(gpuVertices, existing.OpaqueMemory, bufferSize);
        updated = existing with { OpaqueVertexCount = (uint)vertices.Length, OpaqueCpuVertices = gpuVertices };
        return true;
    }

    private (VkBuffer Buffer, DeviceMemory Memory, uint Count) UploadVertices(GpuVertex[] vertices)
    {
        if (vertices.Length == 0)
        {
            return (default, default, 0);
        }

        ulong bufferSize = (ulong)(vertices.Length * Marshal.SizeOf<GpuVertex>());
        (VkBuffer buffer, DeviceMemory memory) = _stagingBufferPool!.Rent(bufferSize);
        WriteGpuVertices(vertices, memory, bufferSize);
        return (buffer, memory, (uint)vertices.Length);
    }

    private void WriteGpuVertices(GpuVertex[] gpuVertices, DeviceMemory memory, ulong bufferSize)
    {
        void* mapped = null;
        _vk.MapMemory(_device, memory, 0, bufferSize, 0, &mapped);
        MemoryMarshal.AsBytes(gpuVertices.AsSpan()).CopyTo(new Span<byte>(mapped, (int)bufferSize));
        _vk.UnmapMemory(_device, memory);
    }

    public void DestroyChunkMesh(ChunkGpuMesh mesh)
    {
        _pendingMeshDestroys.Add(mesh);
    }

    private void FlushPendingMeshDestroys()
    {
        foreach (ChunkGpuMesh mesh in _pendingMeshDestroys)
        {
            _stagingBufferPool?.Return(mesh.OpaqueVertexBuffer);
            if (mesh.TransparentVertexBuffer is not null)
            {
                _stagingBufferPool?.Return(mesh.TransparentVertexBuffer.Value);
            }
        }

        _pendingMeshDestroys.Clear();
    }

    public void Dispose()
    {
        _window.Resize -= OnWindowResized;
        _window.FramebufferResize -= OnFramebufferResized;
        _vk.DeviceWaitIdle(_device);
        FlushPendingMeshDestroys();
        _chunkDrawBatcher?.Dispose();
        _chunkDrawBatcher = null;
        _stagingBufferPool?.Dispose();
        _stagingBufferPool = null;

        if (_captureStagingBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _captureStagingBuffer, null);
            _vk.FreeMemory(_device, _captureStagingMemory, null);
        }

        foreach (VkSemaphore semaphore in _imageAvailableSemaphores)
        {
            _vk.DestroySemaphore(_device, semaphore, null);
        }

        foreach (VkSemaphore semaphore in _renderFinishedSemaphores)
        {
            _vk.DestroySemaphore(_device, semaphore, null);
        }

        foreach (Fence fence in _inFlightFences)
        {
            _vk.DestroyFence(_device, fence, null);
        }

        _vk.DestroyCommandPool(_device, _commandPool, null);

        foreach (Framebuffer framebuffer in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, framebuffer, null);
        }

        _vk.DestroyPipeline(_device, _skyPipeline, null);
        _vk.DestroyPipeline(_device, _transparentPipeline, null);
        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        _vk.DestroyRenderPass(_device, _renderPass, null);

        foreach (ImageView imageView in _swapchainImageViews)
        {
            _vk.DestroyImageView(_device, imageView, null);
        }

        _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
        _vk.DestroySampler(_device, _textureSampler, null);
        _vk.DestroyImageView(_device, _depthImageView, null);
        _vk.DestroyImage(_device, _depthImage, null);
        _vk.FreeMemory(_device, _depthMemory, null);
        _vk.DestroyImageView(_device, _textureImageView, null);
        _vk.DestroyImage(_device, _textureImage, null);
        _vk.FreeMemory(_device, _textureMemory, null);
        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
        _vk.DestroyBuffer(_device, _uniformBuffer, null);
        _vk.FreeMemory(_device, _uniformMemory, null);
        _vk.DestroyBuffer(_device, _inventoryBuffer, null);
        _vk.FreeMemory(_device, _inventoryMemory, null);
        _vk.DestroyBuffer(_device, _skyVertexBuffer, null);
        _vk.FreeMemory(_device, _skyVertexMemory, null);
        if (_particleVertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _particleVertexBuffer, null);
            _vk.FreeMemory(_device, _particleVertexMemory, null);
        }
        _vk.DestroyDevice(_device, null);
        _khrSurface.DestroySurface(_instance, _surface, null);
        _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
        _glfw.Dispose();
    }

    private void OnWindowResized(Vector2D<int> size) => _framebufferResized = true;

    private void OnFramebufferResized(Vector2D<int> size) => _framebufferResized = true;

    private Vector2D<int> GetDrawableSize()
    {
        Vector2D<int> framebufferSize = _window.FramebufferSize;
        if (framebufferSize.X > 0 && framebufferSize.Y > 0)
        {
            return framebufferSize;
        }

        return _window.Size;
    }

    private Extent2D ChooseSwapchainExtent(SurfaceCapabilitiesKHR capabilities)
    {
        Vector2D<int> drawableSize = GetDrawableSize();
        Extent2D desired = new(
            (uint)Math.Max(1, drawableSize.X),
            (uint)Math.Max(1, drawableSize.Y));

        if (capabilities.MinImageExtent.Width > 0)
        {
            desired.Width = Math.Clamp(desired.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
            desired.Height = Math.Clamp(desired.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);
        }

        if (capabilities.CurrentExtent.Width == uint.MaxValue)
        {
            return desired;
        }

        // Win32 often reports a stale CurrentExtent during resize; prefer the live framebuffer size.
        if (desired.Width != capabilities.CurrentExtent.Width || desired.Height != capabilities.CurrentExtent.Height)
        {
            return desired;
        }

        return capabilities.CurrentExtent;
    }

    private void CreateInstance()
    {
        uint extensionCount = 0;
        byte** extensions = _glfw.GetRequiredInstanceExtensions(out extensionCount);
        if (extensions is null)
        {
            throw new InvalidOperationException("GLFW did not return required Vulkan extensions.");
        }

        string[] extensionNames = new string[extensionCount];
        for (int i = 0; i < extensionCount; i++)
        {
            extensionNames[i] = Marshal.PtrToStringAnsi((IntPtr)extensions[i]) ?? string.Empty;
        }

        byte* appName = (byte*)SilkMarshal.StringToPtr("AstroCraft");
        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = appName,
            ApplicationVersion = Vk.MakeVersion(1, 0, 0),
            PEngineName = appName,
            EngineVersion = Vk.MakeVersion(1, 0, 0),
            ApiVersion = Vk.Version12,
        };

        byte** extensionNamesPtr = (byte**)SilkMarshal.StringArrayToPtr(extensionNames);
        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint)extensionNames.Length,
            PpEnabledExtensionNames = extensionNamesPtr,
        };

        if (_vk.CreateInstance(ref createInfo, null, out _instance) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create Vulkan instance.");
        }

        SilkMarshal.Free((nint)appName);
        SilkMarshal.Free((nint)extensionNamesPtr);
    }

    private void CreateSurface()
    {
        VkHandle instanceHandle = new(_instance.Handle);
        WindowHandle* windowHandle = (WindowHandle*)_window.Handle;
        VkNonDispatchableHandle surfaceHandle;
        if (_glfw.CreateWindowSurface(instanceHandle, windowHandle, null, &surfaceHandle) != 0)
        {
            throw new InvalidOperationException("Failed to create window surface.");
        }

        _surface = new SurfaceKHR(surfaceHandle.Handle);

        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
        {
            throw new InvalidOperationException("KHR_surface extension is unavailable.");
        }
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, null);
        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No Vulkan physical devices found.");
        }

        PhysicalDevice[] devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, devicesPtr);
        }

        _physicalDevice = devices[0];
    }

    private void CreateLogicalDevice()
    {
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref queueFamilyCount, null);
        QueueFamilyProperties[] queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref queueFamilyCount, queueFamiliesPtr);
        }

        _graphicsQueueFamily = FindQueueFamily(queueFamilies, QueueFlags.GraphicsBit);
        _presentQueueFamily = FindPresentFamily(queueFamilies);

        float queuePriority = 1f;
        DeviceQueueCreateInfo queueCreateInfo = new()
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamily,
            QueueCount = 1,
            PQueuePriorities = &queuePriority,
        };

        string[] deviceExtensions = [KhrSwapchain.ExtensionName];
        byte** extensionNamesPtr = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions);
        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            EnabledExtensionCount = (uint)deviceExtensions.Length,
            PpEnabledExtensionNames = extensionNamesPtr,
        };

        if (_vk.CreateDevice(_physicalDevice, ref createInfo, null, out _device) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create logical device.");
        }

        SilkMarshal.Free((nint)extensionNamesPtr);
        _vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, out _graphicsQueue);
        _vk.GetDeviceQueue(_device, _presentQueueFamily, 0, out _presentQueue);

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
        {
            throw new InvalidOperationException("KHR_swapchain extension is unavailable.");
        }
    }

    private void CreateSwapchain(SwapchainKHR oldSwapchain = default)
    {
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out SurfaceCapabilitiesKHR capabilities);

        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, null);
        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* formatsPtr = formats)
        {
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, formatsPtr);
        }

        SurfaceFormatKHR surfaceFormat = formats[0];
        foreach (SurfaceFormatKHR format in formats)
        {
            if (format.Format == Format.B8G8R8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                surfaceFormat = format;
                break;
            }
        }

        _swapchainFormat = surfaceFormat.Format;
        Extent2D extent = ChooseSwapchainExtent(capabilities);
        _swapchainExtent = extent;
        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
        {
            imageCount = capabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR createInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
            OldSwapchain = oldSwapchain,
            ImageSharingMode = SharingMode.Exclusive,
        };

        if (_graphicsQueueFamily != _presentQueueFamily)
        {
            uint[] queueFamilyIndices = [_graphicsQueueFamily, _presentQueueFamily];
            fixed (uint* indicesPtr = queueFamilyIndices)
            {
                createInfo.ImageSharingMode = SharingMode.Concurrent;
                createInfo.QueueFamilyIndexCount = 2;
                createInfo.PQueueFamilyIndices = indicesPtr;
                if (_khrSwapchain.CreateSwapchain(_device, ref createInfo, null, out _swapchain) != Result.Success)
                {
                    throw new InvalidOperationException("Failed to create swapchain.");
                }
            }
        }
        else if (_khrSwapchain.CreateSwapchain(_device, ref createInfo, null, out _swapchain) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create swapchain.");
        }

        if (oldSwapchain.Handle != 0)
        {
            _khrSwapchain.DestroySwapchain(_device, oldSwapchain, null);
        }

        uint swapchainImageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref swapchainImageCount, null);
        _swapchainImages = new VkImage[swapchainImageCount];
        fixed (VkImage* imagesPtr = _swapchainImages)
        {
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref swapchainImageCount, imagesPtr);
        }
    }

    private void CreateImageViews()
    {
        _swapchainImageViews = new ImageView[_swapchainImages.Length];
        for (int i = 0; i < _swapchainImages.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _swapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = _swapchainFormat,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };

            if (_vk.CreateImageView(_device, ref createInfo, null, out _swapchainImageViews[i]) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create swapchain image view.");
            }
        }
    }

    private void CreateDepthResources()
    {
        Format depthFormat = FindDepthFormat();
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(_swapchainExtent.Width, _swapchainExtent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = depthFormat,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive,
        };

        if (_vk.CreateImage(_device, ref imageInfo, null, out _depthImage) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create depth image.");
        }

        _vk.GetImageMemoryRequirements(_device, _depthImage, out MemoryRequirements requirements);
        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };

        if (_vk.AllocateMemory(_device, ref allocInfo, null, out _depthMemory) != Result.Success)
        {
            throw new InvalidOperationException("Failed to allocate depth memory.");
        }

        _vk.BindImageMemory(_device, _depthImage, _depthMemory, 0);

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = depthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1),
        };

        if (_vk.CreateImageView(_device, ref viewInfo, null, out _depthImageView) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create depth image view.");
        }
    }

    private Format FindDepthFormat()
    {
        Format[] candidates = [Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint];
        foreach (Format format in candidates)
        {
            _vk.GetPhysicalDeviceFormatProperties(_physicalDevice, format, out FormatProperties properties);
            if ((properties.OptimalTilingFeatures & FormatFeatureFlags.DepthStencilAttachmentBit) != 0)
            {
                return format;
            }
        }

        throw new InvalidOperationException("No supported depth format found.");
    }

    private void CreateRenderPass()
    {
        Format depthFormat = FindDepthFormat();
        AttachmentDescription colorAttachment = new()
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };

        AttachmentDescription depthAttachment = new()
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };

        AttachmentReference colorReference = new(0, ImageLayout.ColorAttachmentOptimal);
        AttachmentReference depthReference = new(1, ImageLayout.DepthStencilAttachmentOptimal);
        AttachmentDescription[] attachments = [colorAttachment, depthAttachment];
        fixed (AttachmentDescription* attachmentsPtr = attachments)
        {
            SubpassDescription subpass = new()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorReference,
                PDepthStencilAttachment = &depthReference,
            };

            RenderPassCreateInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)attachments.Length,
                PAttachments = attachmentsPtr,
                SubpassCount = 1,
                PSubpasses = &subpass,
            };

            if (_vk.CreateRenderPass(_device, ref renderPassInfo, null, out _renderPass) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create render pass.");
            }
        }
    }

    private void CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding uboBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutBinding samplerBinding = new()
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutBinding inventoryBinding = new()
        {
            Binding = 2,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutBinding[] bindings = [uboBinding, samplerBinding, inventoryBinding];
        fixed (DescriptorSetLayoutBinding* bindingsPtr = bindings)
        {
            DescriptorSetLayoutCreateInfo layoutInfo = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = bindingsPtr,
            };

            if (_vk.CreateDescriptorSetLayout(_device, ref layoutInfo, null, out _descriptorSetLayout) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create descriptor set layout.");
            }
        }
    }

    private void CreatePipeline()
    {
        byte[] vertShader = LoadShader("shader.vert.spv");
        byte[] fragShader = LoadShader("shader.frag.spv");

        ShaderModule vertModule = CreateShaderModule(vertShader);
        ShaderModule fragModule = CreateShaderModule(fragShader);

        byte* mainName = (byte*)SilkMarshal.StringToPtr("main");

        PipelineShaderStageCreateInfo vertStage = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertModule,
            PName = mainName,
        };

        PipelineShaderStageCreateInfo fragStage = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragModule,
            PName = mainName,
        };

        PipelineShaderStageCreateInfo[] shaderStages = [vertStage, fragStage];

        VertexInputBindingDescription bindingDescription = new()
        {
            Binding = 0,
            Stride = (uint)Marshal.SizeOf<GpuVertex>(),
            InputRate = VertexInputRate.Vertex,
        };

        VertexInputAttributeDescription[] attributeDescriptions =
        [
            new VertexInputAttributeDescription(0, 0, Format.R32G32B32Sfloat, 0),
            new VertexInputAttributeDescription(1, 0, Format.R32G32Sfloat, 12),
            new VertexInputAttributeDescription(2, 0, Format.R32Sfloat, 20),
            new VertexInputAttributeDescription(3, 0, Format.R32G32B32A32Sfloat, 24),
            new VertexInputAttributeDescription(4, 0, Format.R32Sfloat, 40),
        ];

        DescriptorSetLayout descriptorSetLayout = _descriptorSetLayout;

        fixed (PipelineShaderStageCreateInfo* shaderStagesPtr = shaderStages)
        fixed (VertexInputAttributeDescription* attributesPtr = attributeDescriptions)
        {
            PipelineVertexInputStateCreateInfo vertexInputInfo = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &bindingDescription,
                VertexAttributeDescriptionCount = (uint)attributeDescriptions.Length,
                PVertexAttributeDescriptions = attributesPtr,
            };

            PipelineInputAssemblyStateCreateInfo inputAssembly = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = false,
            };

            Viewport viewport = new(0, 0, _swapchainExtent.Width, _swapchainExtent.Height, 0, 1);
            Rect2D scissor = new(new Offset2D(0, 0), _swapchainExtent);

            DynamicState[] dynamicStates = [DynamicState.Viewport, DynamicState.Scissor];
            fixed (DynamicState* dynamicStatesPtr = dynamicStates)
            {
                PipelineDynamicStateCreateInfo dynamicState = new()
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = (uint)dynamicStates.Length,
                    PDynamicStates = dynamicStatesPtr,
                };

                PipelineViewportStateCreateInfo viewportState = new()
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &viewport,
                    ScissorCount = 1,
                    PScissors = &scissor,
                };

            PipelineRasterizationStateCreateInfo rasterizer = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1f,
                CullMode = CullModeFlags.BackBit,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false,
            };

            PipelineRasterizationStateCreateInfo skyRasterizer = rasterizer;
            skyRasterizer.CullMode = CullModeFlags.None;

            PipelineMultisampleStateCreateInfo multisampling = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            // Leaf alpha cutout uses fragment discard; transparent blocks use a second pass with blending.
            PipelineColorBlendAttachmentState opaqueBlendAttachment = new(
                blendEnable: false,
                srcColorBlendFactor: BlendFactor.One,
                dstColorBlendFactor: BlendFactor.Zero,
                colorBlendOp: BlendOp.Add,
                srcAlphaBlendFactor: BlendFactor.One,
                dstAlphaBlendFactor: BlendFactor.Zero,
                alphaBlendOp: BlendOp.Add,
                colorWriteMask: ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit);

            PipelineColorBlendAttachmentState transparentBlendAttachment = new(
                blendEnable: true,
                srcColorBlendFactor: BlendFactor.SrcAlpha,
                dstColorBlendFactor: BlendFactor.OneMinusSrcAlpha,
                colorBlendOp: BlendOp.Add,
                srcAlphaBlendFactor: BlendFactor.One,
                dstAlphaBlendFactor: BlendFactor.OneMinusSrcAlpha,
                alphaBlendOp: BlendOp.Add,
                colorWriteMask: ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit);

            PipelineColorBlendStateCreateInfo opaqueBlending = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &opaqueBlendAttachment,
            };

            PipelineColorBlendStateCreateInfo transparentBlending = opaqueBlending;
            transparentBlending.PAttachments = &transparentBlendAttachment;

            PipelineDepthStencilStateCreateInfo worldDepthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.LessOrEqual,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };

            PipelineDepthStencilStateCreateInfo skyDepthStencil = worldDepthStencil;
            skyDepthStencil.DepthWriteEnable = false;

            PipelineDepthStencilStateCreateInfo transparentDepthStencil = worldDepthStencil;
            transparentDepthStencil.DepthWriteEnable = false;

            PipelineLayoutCreateInfo pipelineLayoutInfo = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &descriptorSetLayout,
            };

            if (_vk.CreatePipelineLayout(_device, ref pipelineLayoutInfo, null, out _pipelineLayout) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create pipeline layout.");
            }

                GraphicsPipelineCreateInfo pipelineInfo = new()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = (uint)shaderStages.Length,
                    PStages = shaderStagesPtr,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PDepthStencilState = &worldDepthStencil,
                    PColorBlendState = &opaqueBlending,
                    PDynamicState = &dynamicState,
                    Layout = _pipelineLayout,
                    RenderPass = _renderPass,
                    Subpass = 0,
                };

                Result pipelineResult = _vk.CreateGraphicsPipelines(_device, default, 1, ref pipelineInfo, null, out _pipeline);
                if (pipelineResult != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"Failed to create graphics pipeline ({pipelineResult}). " +
                        $"Recompile shaders with scripts/compile-shaders.ps1 (requires Vulkan SDK glslc). " +
                        $"Vertex SPIR-V: {vertShader.Length} bytes, Fragment SPIR-V: {fragShader.Length} bytes.");
                }

                pipelineInfo.PDepthStencilState = &transparentDepthStencil;
                pipelineInfo.PColorBlendState = &transparentBlending;
                pipelineResult = _vk.CreateGraphicsPipelines(_device, default, 1, ref pipelineInfo, null, out _transparentPipeline);
                if (pipelineResult != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"Failed to create transparent graphics pipeline ({pipelineResult}). " +
                        $"Recompile shaders with scripts/compile-shaders.ps1 (requires Vulkan SDK glslc).");
                }

                pipelineInfo.PDepthStencilState = &skyDepthStencil;
                pipelineInfo.PRasterizationState = &skyRasterizer;
                pipelineResult = _vk.CreateGraphicsPipelines(_device, default, 1, ref pipelineInfo, null, out _skyPipeline);
                if (pipelineResult != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"Failed to create sky graphics pipeline ({pipelineResult}). " +
                        $"Recompile shaders with scripts/compile-shaders.ps1 (requires Vulkan SDK glslc).");
                }
            }
        }

        _vk.DestroyShaderModule(_device, fragModule, null);
        _vk.DestroyShaderModule(_device, vertModule, null);
        SilkMarshal.Free((nint)mainName);
    }

    private void CreateFramebuffers()
    {
        _framebuffers = new Framebuffer[_swapchainImageViews.Length];
        for (int i = 0; i < _swapchainImageViews.Length; i++)
        {
            ImageView[] attachments = [_swapchainImageViews[i], _depthImageView];
            fixed (ImageView* attachmentsPtr = attachments)
            {
                FramebufferCreateInfo framebufferInfo = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _renderPass,
                    AttachmentCount = (uint)attachments.Length,
                    PAttachments = attachmentsPtr,
                    Width = _swapchainExtent.Width,
                    Height = _swapchainExtent.Height,
                    Layers = 1,
                };

                if (_vk.CreateFramebuffer(_device, ref framebufferInfo, null, out _framebuffers[i]) != Result.Success)
                {
                    throw new InvalidOperationException("Failed to create framebuffer.");
                }
            }
        }
    }

    private void CreateCommandPool()
    {
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = _graphicsQueueFamily,
        };

        if (_vk.CreateCommandPool(_device, ref poolInfo, null, out _commandPool) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create command pool.");
        }
    }

    private void CreateUniformBuffer()
    {
        int uboSize = Marshal.SizeOf<UniformBufferObject>();
        if (uboSize != UniformBufferObject.Std140Size)
        {
            throw new InvalidOperationException(
                $"Uniform buffer size mismatch: C# struct is {uboSize} bytes, std140 expects {UniformBufferObject.Std140Size}.");
        }

        ulong bufferSize = (ulong)uboSize;
        CreateBuffer(bufferSize, BufferUsageFlags.UniformBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out _uniformBuffer, out _uniformMemory);

        void* mapped = null;
        _vk.MapMemory(_device, _uniformMemory, 0, bufferSize, 0, &mapped);
        _mappedUniform = mapped;
    }

    private void CreateTextureArray()
    {
        int tileSize = TextureAtlasGenerator.TileSize;
        int layers = TextureLayerCount;
        byte[] atlas = TextureAtlasGenerator.GenerateRgbaPixels();
        byte[] arrayPixels = new byte[tileSize * tileSize * 4 * layers];

        for (int tile = 0; tile < layers; tile++)
        {
            int tileX = tile % TextureAtlasGenerator.TilesPerRow;
            int tileY = tile / TextureAtlasGenerator.TilesPerRow;
            for (int y = 0; y < tileSize; y++)
            {
                for (int x = 0; x < tileSize; x++)
                {
                    int atlasX = tileX * tileSize + x;
                    int atlasY = tileY * tileSize + y;
                    int atlasIndex = (atlasY * TextureAtlasGenerator.AtlasSize + atlasX) * 4;
                    int layerIndex = (tile * tileSize * tileSize + y * tileSize + x) * 4;
                    arrayPixels[layerIndex] = atlas[atlasIndex];
                    arrayPixels[layerIndex + 1] = atlas[atlasIndex + 1];
                    arrayPixels[layerIndex + 2] = atlas[atlasIndex + 2];
                    arrayPixels[layerIndex + 3] = atlas[atlasIndex + 3];
                }
            }
        }

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D((uint)tileSize, (uint)tileSize, 1),
            MipLevels = 1,
            ArrayLayers = (uint)layers,
            Format = Format.R8G8B8A8Unorm,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive,
        };

        if (_vk.CreateImage(_device, ref imageInfo, null, out _textureImage) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create texture image.");
        }

        _vk.GetImageMemoryRequirements(_device, _textureImage, out MemoryRequirements requirements);
        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };

        if (_vk.AllocateMemory(_device, ref allocInfo, null, out _textureMemory) != Result.Success)
        {
            throw new InvalidOperationException("Failed to allocate texture memory.");
        }

        _vk.BindImageMemory(_device, _textureImage, _textureMemory, 0);
        UploadTexturePixels(arrayPixels, (uint)tileSize, (uint)tileSize, (uint)layers);

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _textureImage,
            ViewType = ImageViewType.Type2DArray,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, (uint)layers),
        };

        if (_vk.CreateImageView(_device, ref viewInfo, null, out _textureImageView) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create texture image view.");
        }

        SamplerCreateInfo samplerInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = false,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            MipmapMode = SamplerMipmapMode.Nearest,
        };

        if (_vk.CreateSampler(_device, ref samplerInfo, null, out _textureSampler) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create texture sampler.");
        }
    }

    private void UploadTexturePixels(byte[] pixels, uint width, uint height, uint layers)
    {
        ulong imageSize = (ulong)pixels.Length;
        CreateBuffer(imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out VkBuffer stagingBuffer, out DeviceMemory stagingMemory);

        void* mapped = null;
        _vk.MapMemory(_device, stagingMemory, 0, imageSize, 0, &mapped);
        Marshal.Copy(pixels, 0, (IntPtr)mapped, pixels.Length);
        _vk.UnmapMemory(_device, stagingMemory);

        CommandBuffer commandBuffer = BeginSingleTimeCommands();

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _textureImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, layers),
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
        };

        _vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            ref barrier);

        BufferImageCopy region = new()
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, layers),
            ImageExtent = new Extent3D(width, height, 1),
        };

        _vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer, _textureImage, ImageLayout.TransferDstOptimal, 1, ref region);

        barrier.OldLayout = ImageLayout.TransferDstOptimal;
        barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
        barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
        barrier.DstAccessMask = AccessFlags.ShaderReadBit;

        _vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            0,
            0,
            null,
            0,
            null,
            1,
            ref barrier);

        EndSingleTimeCommands(commandBuffer);
        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize[] poolSizes =
        [
            new DescriptorPoolSize(DescriptorType.UniformBuffer, (uint)MaxFramesInFlight),
            new DescriptorPoolSize(DescriptorType.CombinedImageSampler, (uint)MaxFramesInFlight),
            new DescriptorPoolSize(DescriptorType.StorageBuffer, (uint)MaxFramesInFlight),
        ];

        fixed (DescriptorPoolSize* poolSizesPtr = poolSizes)
        {
            DescriptorPoolCreateInfo poolInfo = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = poolSizesPtr,
                MaxSets = (uint)MaxFramesInFlight,
            };

            if (_vk.CreateDescriptorPool(_device, ref poolInfo, null, out _descriptorPool) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create descriptor pool.");
            }
        }
    }

    private void CreateDescriptorSets()
    {
        DescriptorSetLayout[] layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, _descriptorSetLayout);

        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = (uint)MaxFramesInFlight,
                PSetLayouts = layoutsPtr,
            };

            _descriptorSets = new DescriptorSet[MaxFramesInFlight];
            fixed (DescriptorSet* descriptorSetsPtr = _descriptorSets)
            {
                if (_vk.AllocateDescriptorSets(_device, ref allocInfo, descriptorSetsPtr) != Result.Success)
                {
                    throw new InvalidOperationException("Failed to allocate descriptor sets.");
                }
            }
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorBufferInfo bufferInfo = new(_uniformBuffer, 0, (ulong)Marshal.SizeOf<UniformBufferObject>());
            DescriptorImageInfo imageInfo = new(_textureSampler, _textureImageView, ImageLayout.ShaderReadOnlyOptimal);
            DescriptorBufferInfo inventoryInfo = new(_inventoryBuffer, 0, (ulong)(InventorySlotCount * sizeof(int)));

            WriteDescriptorSet bufferWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo,
            };

            WriteDescriptorSet imageWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSets[i],
                DstBinding = 1,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfo,
            };

            WriteDescriptorSet inventoryWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSets[i],
                DstBinding = 2,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &inventoryInfo,
            };

            WriteDescriptorSet[] descriptorWrites = [bufferWrite, imageWrite, inventoryWrite];
            fixed (WriteDescriptorSet* descriptorWritesPtr = descriptorWrites)
            {
                _vk.UpdateDescriptorSets(_device, (uint)descriptorWrites.Length, descriptorWritesPtr, 0, null);
            }
        }
    }

    private void CreateCommandBuffers()
    {
        _commandBuffers = new CommandBuffer[MaxFramesInFlight];
        fixed (CommandBuffer* commandBuffersPtr = _commandBuffers)
        {
            CommandBufferAllocateInfo allocInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)MaxFramesInFlight,
            };

            if (_vk.AllocateCommandBuffers(_device, ref allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new InvalidOperationException("Failed to allocate command buffers.");
            }
        }
    }

    private void CreateSyncObjects()
    {
        _imageAvailableSemaphores = new VkSemaphore[MaxFramesInFlight];
        _renderFinishedSemaphores = new VkSemaphore[MaxFramesInFlight];
        _inFlightFences = new Fence[MaxFramesInFlight];

        SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            if (_vk.CreateSemaphore(_device, ref semaphoreInfo, null, out _imageAvailableSemaphores[i]) != Result.Success
                || _vk.CreateSemaphore(_device, ref semaphoreInfo, null, out _renderFinishedSemaphores[i]) != Result.Success
                || _vk.CreateFence(_device, ref fenceInfo, null, out _inFlightFences[i]) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create synchronization objects.");
            }
        }
    }

    private void CreateInventoryBuffer()
    {
        ulong bufferSize = (ulong)(InventorySlotCount * sizeof(int));
        CreateBuffer(
            bufferSize,
            BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _inventoryBuffer,
            out _inventoryMemory);

        void* mapped = null;
        _vk.MapMemory(_device, _inventoryMemory, 0, bufferSize, 0, &mapped);
        _mappedInventory = mapped;
        new Span<int>(_mappedInventory, InventorySlotCount).Clear();
    }

    private void UpdateInventoryBuffer(ReadOnlySpan<int> inventorySlots)
    {
        if (_mappedInventory is null)
        {
            return;
        }

        Span<int> destination = new(_mappedInventory, InventorySlotCount);
        if (inventorySlots.Length >= InventorySlotCount)
        {
            inventorySlots[..InventorySlotCount].CopyTo(destination);
            return;
        }

        destination.Clear();
        inventorySlots.CopyTo(destination);
    }

    private void UpdateUniformBuffer(
        Matrix4x4 modelViewProjection,
        Vector3 cameraPosition,
        Vector2 viewportSize,
        Vector4 survivalHud,
        float hudFlags,
        float overlayProgress,
        Vector3 targetBlockMin,
        float hasTarget,
        float timeOfDay,
        float breakBurstTimer,
        float breakingBlockTexture,
        Vector3 ghostBlockMin,
        float ghostActive,
        float ghostValid,
        float ghostTexture,
        float heldItemTexture,
        float hasHeldItem,
        float time)
    {
        if (!Matrix4x4.Invert(modelViewProjection, out Matrix4x4 inverseViewProjection))
        {
            inverseViewProjection = Matrix4x4.Identity;
        }

        UniformBufferObject ubo = new()
        {
            ModelViewProjection = modelViewProjection,
            InverseViewProjection = inverseViewProjection,
            CameraPosition = cameraPosition,
            SurvivalHud = survivalHud,
            ViewportSize = viewportSize,
            HudFlags = hudFlags,
            BreakProgress = overlayProgress,
            TargetBlockMin = targetBlockMin,
            HasTarget = hasTarget,
            TimeOfDay = timeOfDay,
            BreakBurstTimer = breakBurstTimer,
            BreakingBlockTexture = breakingBlockTexture,
            GhostBlockMin = ghostBlockMin,
            GhostActive = ghostActive,
            GhostValid = ghostValid,
            GhostTexture = ghostTexture,
            HeldItemTexture = heldItemTexture,
            HasHeldItem = hasHeldItem,
            Time = time,
        };
        Marshal.StructureToPtr(ubo, (IntPtr)_mappedUniform, false);
    }

    private void CreateSkyVertexBuffer()
    {
        GpuVertex[] skyVertices =
        [
            new GpuVertex(new Vector3(-1f, -1f, 0f), Vector2.Zero, -1f, Vector3.UnitY, 1f),
            new GpuVertex(new Vector3(3f, -1f, 0f), Vector2.Zero, -1f, Vector3.UnitY, 1f),
            new GpuVertex(new Vector3(-1f, 3f, 0f), Vector2.Zero, -1f, Vector3.UnitY, 1f),
        ];

        ulong bufferSize = (ulong)(skyVertices.Length * Marshal.SizeOf<GpuVertex>());
        CreateBuffer(bufferSize, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out _skyVertexBuffer, out _skyVertexMemory);

        void* mapped = null;
        _vk.MapMemory(_device, _skyVertexMemory, 0, bufferSize, 0, &mapped);
        Marshal.Copy(MemoryMarshal.AsBytes(skyVertices.AsSpan()).ToArray(), 0, (IntPtr)mapped, (int)bufferSize);
        _vk.UnmapMemory(_device, _skyVertexMemory);
    }

    private void CreateParticleBuffer()
    {
        _particleVertexCapacity = 0;
    }

    private void RecreateSwapchain()
    {
        Vector2D<int> size = GetDrawableSize();
        if (size.X == 0 || size.Y == 0)
        {
            return;
        }

        _vk.DeviceWaitIdle(_device);

        foreach (Framebuffer framebuffer in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, framebuffer, null);
        }

        foreach (ImageView imageView in _swapchainImageViews)
        {
            _vk.DestroyImageView(_device, imageView, null);
        }

        _vk.DestroyImageView(_device, _depthImageView, null);
        _vk.DestroyImage(_device, _depthImage, null);
        _vk.FreeMemory(_device, _depthMemory, null);

        SwapchainKHR oldSwapchain = _swapchain;
        CreateSwapchain(oldSwapchain);
        CreateImageViews();
        CreateDepthResources();
        CreateFramebuffers();
        _framebufferResized = false;
    }

    private CommandBuffer BeginSingleTimeCommands()
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };

        CommandBuffer commandBuffer;
        if (_vk.AllocateCommandBuffers(_device, ref allocInfo, &commandBuffer) != Result.Success)
        {
            throw new InvalidOperationException("Failed to allocate single-time command buffer.");
        }

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };

        _vk.BeginCommandBuffer(commandBuffer, ref beginInfo);
        return commandBuffer;
    }

    private void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        _vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
        };

        _vk.QueueSubmit(_graphicsQueue, 1, ref submitInfo, default);
        _vk.QueueWaitIdle(_graphicsQueue);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, ref commandBuffer);
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
            throw new InvalidOperationException("Failed to create buffer.");
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
            throw new InvalidOperationException("Failed to allocate buffer memory.");
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

        throw new InvalidOperationException("Failed to find suitable memory type.");
    }

    private ShaderModule CreateShaderModule(byte[] code)
    {
        fixed (byte* codePtr = code)
        {
            ShaderModuleCreateInfo createInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)codePtr,
            };

            if (_vk.CreateShaderModule(_device, ref createInfo, null, out ShaderModule shaderModule) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create shader module.");
            }

            return shaderModule;
        }
    }

    private static byte[] LoadShader(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Shader file not found: {path}");
        }

        return File.ReadAllBytes(path);
    }

    private static Matrix4x4 CreatePerspective(float aspectRatio, float fieldOfViewDegrees = 70f)
    {
        float fov = fieldOfViewDegrees * MathF.PI / 180f;
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspectRatio, 0.1f, 500f);
        projection.M22 *= -1f;
        return projection;
    }

    private static uint FindQueueFamily(IReadOnlyList<QueueFamilyProperties> queueFamilies, QueueFlags requiredFlags)
    {
        for (uint i = 0; i < queueFamilies.Count; i++)
        {
            if ((queueFamilies[(int)i].QueueFlags & requiredFlags) == requiredFlags)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Required queue family not found.");
    }

    private uint FindPresentFamily(IReadOnlyList<QueueFamilyProperties> queueFamilies)
    {
        for (uint i = 0; i < queueFamilies.Count; i++)
        {
            _khrSurface.GetPhysicalDeviceSurfaceSupport(_physicalDevice, i, _surface, out Bool32 supported);
            if (supported)
            {
                return i;
            }
        }

        throw new InvalidOperationException("No present queue family found.");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct UniformBufferObject
    {
        public Matrix4x4 ModelViewProjection;
        public Matrix4x4 InverseViewProjection;
        public Vector3 CameraPosition;
        private readonly float _padding1;
        public Vector4 SurvivalHud;
        public Vector2 ViewportSize;
        public float HudFlags;
        public float BreakProgress;
        public Vector3 TargetBlockMin;
        private readonly float _paddingTargetBlock;
        public float HasTarget;
        public float TimeOfDay;
        public float BreakBurstTimer;
        public float BreakingBlockTexture;
        public Vector3 GhostBlockMin;
        private readonly float _paddingGhost;
        public float GhostActive;
        public float GhostValid;
        public float GhostTexture;
        public float HeldItemTexture;
        public float HasHeldItem;
        public float Time;
        private readonly float _paddingEnd2;
        private readonly float _paddingEnd3;

        public const int Std140Size = 256;
    }
}
