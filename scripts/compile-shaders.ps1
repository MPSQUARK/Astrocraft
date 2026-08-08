$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$shaderDir = Join-Path $repoRoot "src\AstroCraft.Client\Shaders"
$glslc = $env:VULKAN_SDK

if ($glslc) {
    $glslc = Join-Path $glslc "Bin\glslc.exe"
}

if ($glslc -and (Test-Path $glslc)) {
    & $glslc (Join-Path $shaderDir "shader.vert") -o (Join-Path $shaderDir "shader.vert.spv")
    & $glslc (Join-Path $shaderDir "shader.frag") -o (Join-Path $shaderDir "shader.frag.spv")
    Write-Host "Compiled shaders with glslc."
    exit 0
}

$compileTool = Join-Path $repoRoot "tools\CompileShaders\CompileShaders.csproj"
if (Test-Path $compileTool) {
    dotnet run --project $compileTool
    exit $LASTEXITCODE
}

python (Join-Path $repoRoot "scripts\generate-spirv.py")
Write-Host "Generated embedded SPIR-V fallback. Install Vulkan SDK glslc for source-accurate shaders."
