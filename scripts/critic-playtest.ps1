param(
    [int]$Seconds = 12,
    [int]$Port = 27016
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$shots = Join-Path $repo "docs\critic-screenshots"
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$shotPath = Join-Path $shots "playtest-$stamp.png"
$logPath = Join-Path $shots "playtest-$stamp.log"

Set-Location $repo

function Stop-AstroCraftProcesses {
    Get-Process -Name "AstroCraft.Server", "AstroCraft.Client" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

Stop-AstroCraftProcesses

try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "compile-shaders.ps1")

    dotnet build --verbosity quiet | Out-Null

    $server = Start-Process -FilePath "dotnet" -ArgumentList @(
        "run", "--project", "src/AstroCraft.Server/AstroCraft.Server.csproj", "--no-build",
        "--", "--name", "Critic Server", "--flat", "--port", "$Port"
    ) -PassThru -WindowStyle Minimized

    Start-Sleep -Seconds 2

    dotnet run --project "src/AstroCraft.Client/AstroCraft.Client.csproj" --no-build -- `
        --connect 127.0.0.1 --port $Port --name Critic --flat `
        --critic-seconds $Seconds --critic-screenshot $shotPath | Out-Null

    if (Test-Path $shotPath) {
        "Screenshot: $shotPath" | Tee-Object -FilePath $logPath
        Write-Output $shotPath
    } else {
        throw "Critic screenshot was not created: $shotPath"
    }
}
finally {
    Stop-AstroCraftProcesses
}
