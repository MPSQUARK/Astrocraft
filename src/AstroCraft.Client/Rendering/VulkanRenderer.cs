using System.Numerics;
using System.Runtime.InteropServices;
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
    private static readonly Vector4 ClearColor = new(0.02f, 0.05f, 0.14f, 1f);

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
    private int _currentFrame;
    private bool _framebufferResized;
    private void* _mappedUniform;
    private uint _imageIndex;
    private bool _frameActive;

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
        CreateLogicalDevice();
        CreateSwapchain();
        CreateImageViews();
        CreateRenderPass();
        CreateDescriptorSetLayout();
        CreatePipeline();
        CreateFramebuffers();
        CreateCommandPool();
        CreateUniformBuffer();
        CreateTextureArray();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateCommandBuffers();
        CreateSyncObjects();

        _window.Resize += OnWindowResized;
    }

    public Extent2D Extent => _swapchainExtent;

    public Matrix4x4 BuildViewProjection(PlayerState player, float aspectRatio)
    {
        Vector3 eye = player.EyePosition;
        Vector3 forward = new(
            MathF.Sin(player.YawRadians) * MathF.Cos(player.PitchRadians),
            MathF.Sin(player.PitchRadians),
            MathF.Cos(player.YawRadians) * MathF.Cos(player.PitchRadians));
        Vector3 target = eye + Vector3.Normalize(forward);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        Matrix4x4 projection = CreatePerspective(aspectRatio);
        return view * projection;
    }

    public bool BeginFrame()
    {
        if (_framebufferResized)
        {
            RecreateSwapchain();
        }

        Fence inFlightFence = _inFlightFences[_currentFrame];
        _vk.WaitForFences(_device, 1, ref inFlightFence, new Bool32(true), ulong.MaxValue);

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
        ClearValue clearValue = new() { Color = clearColorValue };

        RenderPassBeginInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[_imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearValue,
        };

        _vk.CmdBeginRenderPass(commandBuffer, ref renderPassInfo, SubpassContents.Inline);
        _frameActive = true;
        return true;
    }

    public void DrawChunks(IReadOnlyDictionary<Core.Math.ChunkPosition, ChunkGpuMesh> meshes, Matrix4x4 modelViewProjection)
    {
        if (!_frameActive)
        {
            return;
        }

        UpdateUniformBuffer(modelViewProjection);

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

        foreach (ChunkGpuMesh mesh in meshes.Values)
        {
            if (mesh.VertexCount == 0)
            {
                continue;
            }

            VkBuffer vertexBuffer = mesh.VertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);
            _vk.CmdDraw(commandBuffer, mesh.VertexCount, 1, 0, 0);
        }
    }

    public void EndFrame()
    {
        if (!_frameActive)
        {
            return;
        }

        CommandBuffer commandBuffer = _commandBuffers[_currentFrame];
        _vk.CmdEndRenderPass(commandBuffer);
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

        _frameActive = false;
        _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
    }

    public ChunkGpuMesh UploadChunkMesh(BlockVertex[] vertices)
    {
        GpuVertex[] gpuVertices = new GpuVertex[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            BlockVertex vertex = vertices[i];
            gpuVertices[i] = new GpuVertex(
                new Vector3(vertex.X, vertex.Y, vertex.Z),
                new Vector2(vertex.U, vertex.V),
                vertex.TextureIndex);
        }

        ulong bufferSize = (ulong)(gpuVertices.Length * Marshal.SizeOf<GpuVertex>());
        CreateBuffer(bufferSize, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out VkBuffer buffer, out DeviceMemory memory);

        void* mapped = null;
        _vk.MapMemory(_device, memory, 0, bufferSize, 0, &mapped);
        Marshal.Copy(MemoryMarshal.AsBytes(gpuVertices.AsSpan()).ToArray(), 0, (IntPtr)mapped, (int)bufferSize);
        _vk.UnmapMemory(_device, memory);

        return new ChunkGpuMesh(buffer, memory, (uint)vertices.Length);
    }

    public void DestroyChunkMesh(ChunkGpuMesh mesh)
    {
        _vk.DestroyBuffer(_device, mesh.VertexBuffer, null);
        _vk.FreeMemory(_device, mesh.Memory, null);
    }

    public void Dispose()
    {
        _window.Resize -= OnWindowResized;
        _vk.DeviceWaitIdle(_device);

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

        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        _vk.DestroyRenderPass(_device, _renderPass, null);

        foreach (ImageView imageView in _swapchainImageViews)
        {
            _vk.DestroyImageView(_device, imageView, null);
        }

        _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
        _vk.DestroySampler(_device, _textureSampler, null);
        _vk.DestroyImageView(_device, _textureImageView, null);
        _vk.DestroyImage(_device, _textureImage, null);
        _vk.FreeMemory(_device, _textureMemory, null);
        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
        _vk.DestroyBuffer(_device, _uniformBuffer, null);
        _vk.FreeMemory(_device, _uniformMemory, null);
        _vk.DestroyDevice(_device, null);
        _khrSurface.DestroySurface(_instance, _surface, null);
        _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
        _glfw.Dispose();
    }

    private void OnWindowResized(Vector2D<int> size) => _framebufferResized = true;

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

    private void CreateSwapchain()
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
        Extent2D extent = capabilities.CurrentExtent;
        if (extent.Width == uint.MaxValue)
        {
            Vector2D<int> size = _window.Size;
            extent.Width = (uint)Math.Max(1, size.X);
            extent.Height = (uint)Math.Max(1, size.Y);
        }

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
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
            OldSwapchain = default,
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

    private void CreateRenderPass()
    {
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

        AttachmentReference colorReference = new(0, ImageLayout.ColorAttachmentOptimal);
        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
        };

        RenderPassCreateInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
        };

        if (_vk.CreateRenderPass(_device, ref renderPassInfo, null, out _renderPass) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create render pass.");
        }
    }

    private void CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding uboBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit,
        };

        DescriptorSetLayoutBinding samplerBinding = new()
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutBinding[] bindings = [uboBinding, samplerBinding];
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

            PipelineMultisampleStateCreateInfo multisampling = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            PipelineColorBlendAttachmentState colorBlendAttachment = new(
                blendEnable: false,
                srcColorBlendFactor: BlendFactor.One,
                dstColorBlendFactor: BlendFactor.Zero,
                colorBlendOp: BlendOp.Add,
                srcAlphaBlendFactor: BlendFactor.One,
                dstAlphaBlendFactor: BlendFactor.Zero,
                alphaBlendOp: BlendOp.Add,
                colorWriteMask: ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit);

            PipelineColorBlendStateCreateInfo colorBlending = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment,
            };

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
                PColorBlendState = &colorBlending,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };

            if (_vk.CreateGraphicsPipelines(_device, default, 1, ref pipelineInfo, null, out _pipeline) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create graphics pipeline.");
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
            ImageView attachment = _swapchainImageViews[i];
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
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
        ulong bufferSize = (ulong)Marshal.SizeOf<UniformBufferObject>();
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

            WriteDescriptorSet[] descriptorWrites = [bufferWrite, imageWrite];
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

    private void UpdateUniformBuffer(Matrix4x4 modelViewProjection)
    {
        UniformBufferObject ubo = new() { ModelViewProjection = Matrix4x4.Transpose(modelViewProjection) };
        Marshal.StructureToPtr(ubo, (IntPtr)_mappedUniform, false);
    }

    private void RecreateSwapchain()
    {
        Vector2D<int> size = _window.Size;
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

        SwapchainKHR oldSwapchain = _swapchain;
        _khrSwapchain.DestroySwapchain(_device, oldSwapchain, null);
        CreateSwapchain();
        CreateImageViews();
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

    private static Matrix4x4 CreatePerspective(float aspectRatio)
    {
        float fov = MathF.PI / 3f;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuVertex
    {
        public Vector3 Position;
        public Vector2 Uv;
        public float TextureIndex;

        public GpuVertex(Vector3 position, Vector2 uv, float textureIndex)
        {
            Position = position;
            Uv = uv;
            TextureIndex = textureIndex;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformBufferObject
    {
        public Matrix4x4 ModelViewProjection;
    }
}
