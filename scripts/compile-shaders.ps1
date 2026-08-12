$ErrorActionPreference = "Stop"

. "$PSScriptRoot\resolve-glslc.ps1"

$repoRoot = Split-Path -Parent $PSScriptRoot
$shaderDir = Join-Path $repoRoot "src\AstroCraft.Client\Shaders"
$glslc = Resolve-Glslc

if (-not $glslc) {
    Write-Error @"
glslc not found. Install the Vulkan SDK from https://vulkan.lunarg.com/
and ensure VULKAN_SDK is set, or install to C:\VulkanSDK\<version>\.
AstroCraft requires glslc to compile Shaders\shader.vert and shader.frag - no fallback SPIR-V is used.
"@
    exit 1
}

$sdkRoot = Resolve-VulkanSdkRoot -GlslcPath $glslc
if ($sdkRoot -and -not $env:VULKAN_SDK) {
    $env:VULKAN_SDK = $sdkRoot
}

& $glslc (Join-Path $shaderDir "shader.vert") -o (Join-Path $shaderDir "shader.vert.spv")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $glslc (Join-Path $shaderDir "shader.frag") -o (Join-Path $shaderDir "shader.frag.spv")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Compiled shaders with glslc: $glslc"
