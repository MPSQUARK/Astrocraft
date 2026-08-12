# Resolves glslc.exe from the Vulkan SDK (env var or standard install path).
# Dot-source from other scripts: . "$PSScriptRoot\resolve-glslc.ps1"

function Resolve-Glslc {
    $candidates = @()

    if ($env:VULKAN_SDK) {
        $candidates += Join-Path $env:VULKAN_SDK "Bin\glslc.exe"
    }

    $sdkRoot = "C:\VulkanSDK"
    if (Test-Path $sdkRoot) {
        $versions = Get-ChildItem -Path $sdkRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending
        foreach ($version in $versions) {
            $candidates += Join-Path $version.FullName "Bin\glslc.exe"
        }
    }

    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) {
            return (Resolve-Path $path).Path
        }
    }

    return $null
}

function Resolve-VulkanSdkRoot {
    param([string]$GlslcPath)

    if (-not $GlslcPath) {
        return $null
    }

    # ...\VulkanSDK\<version>\Bin\glslc.exe -> SDK root is two levels up from Bin
    return (Split-Path (Split-Path $GlslcPath -Parent) -Parent)
}
