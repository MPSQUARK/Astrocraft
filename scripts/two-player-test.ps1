param(
    [int]$Seconds = 8,
    [int]$Port = 27017,
    [int]$DiscoveryPort = 27018
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Stop-AstroCraftProcesses {
    Get-Process -Name "AstroCraft.Server", "AstroCraft.Client" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

Stop-AstroCraftProcesses

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "compile-shaders.ps1")

dotnet build --verbosity quiet | Out-Null

$server = Start-Process -FilePath "dotnet" -ArgumentList @(
    "run", "--project", "src/AstroCraft.Server/AstroCraft.Server.csproj", "--no-build",
    "--", "--name", "Two Player Test", "--flat", "--port", "$Port"
) -PassThru -WindowStyle Minimized

Start-Sleep -Seconds 2

$clientArgs = @(
    "run", "--project", "src/AstroCraft.Client/AstroCraft.Client.csproj", "--no-build", "--",
    "--connect", "127.0.0.1", "--port", "$Port", "--flat", "--critic-seconds", "$Seconds"
)

$client1 = Start-Process -FilePath "dotnet" -ArgumentList ($clientArgs + @("--name", "PlayerOne")) -PassThru -WindowStyle Minimized
$client2 = Start-Process -FilePath "dotnet" -ArgumentList ($clientArgs + @("--name", "PlayerTwo")) -PassThru -WindowStyle Minimized

Write-Host "Server PID $($server.Id) on port $Port; clients $($client1.Id), $($client2.Id) for ${Seconds}s"

Wait-Process -Id $client1.Id, $client2.Id -ErrorAction SilentlyContinue
Stop-AstroCraftProcesses

Write-Host "Two-player smoke test finished."
