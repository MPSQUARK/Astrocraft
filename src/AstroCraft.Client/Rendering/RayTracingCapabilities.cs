using Silk.NET.Vulkan;

namespace AstroCraft.Client.Rendering;

public sealed class RayTracingCapabilities
{
    private const int MaxExtensionNameSize = 256;
    public const string RayTracingPipelineExtensionName = "VK_KHR_ray_tracing_pipeline";

    public bool IsSupported { get; init; }

    public static unsafe RayTracingCapabilities Probe(Vk vk, PhysicalDevice physicalDevice)
    {
        uint extensionCount = 0;
        vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, ref extensionCount, null);

        if (extensionCount == 0)
        {
            return new RayTracingCapabilities { IsSupported = false };
        }

        ExtensionProperties[] extensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* extensionsPtr = extensions)
        {
            vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, ref extensionCount, extensionsPtr);
        }

        foreach (ExtensionProperties extension in extensions)
        {
            if (ReadExtensionName(extension) == RayTracingPipelineExtensionName)
            {
                return new RayTracingCapabilities { IsSupported = true };
            }
        }

        return new RayTracingCapabilities { IsSupported = false };
    }

    private static unsafe string ReadExtensionName(ExtensionProperties extension)
    {
        byte* namePtr = extension.ExtensionName;
        int length = 0;
        while (length < MaxExtensionNameSize && namePtr[length] != 0)
        {
            length++;
        }

        return System.Text.Encoding.UTF8.GetString(namePtr, length);
    }
}
