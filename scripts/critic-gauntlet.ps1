param(
    [ValidateSet("Fast", "Full")]
    [string]$Mode = "Fast",
    [switch]$SkipBuild,
    [int]$ClientTimeoutSeconds = 0,
    [int]$CriticSeconds = 45,
    [int]$Port = 27016,
    [int]$MinScreenshotBytes = 51200,
    [int]$MinFps = 30
)

$ErrorActionPreference = "Stop"

if ($ClientTimeoutSeconds -le 0) {
    $ClientTimeoutSeconds = if ($Mode -eq "Full") { 180 } else { 180 }
}

. (Join-Path $PSScriptRoot "critic\_lib.ps1")

$repo = Split-Path -Parent $PSScriptRoot
$referenceMaterialDir = Join-Path $repo "ReferenceMaterial"
$shots = Join-Path $repo "docs\critic-screenshots"
$reportPath = Join-Path $shots "smoke-report.json"
New-Item -ItemType Directory -Force -Path $shots | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$proceduralShot = Join-Path $shots "gauntlet-procedural-$stamp.png"
$proceduralShotDir = Join-Path $shots "gauntlet-procedural-$stamp"
$fpsReportPath = Join-Path $shots "gauntlet-fps-$stamp.json"
$clientLogPath = Join-Path $shots "gauntlet-client-$stamp.log"

Set-Location $repo

$phaseTimings = @{}
$server = $null
$clientTimedOut = $false
$bootstrapTimedOut = $false

$report = @{
    timestamp = (Get-Date -Format "o")
    reportType = "smoke"
    mode = $Mode
    phaseTimings = $phaseTimings
    clientTimedOut = $false
    bootstrapTimedOut = $false
    clientTimeoutSeconds = $ClientTimeoutSeconds
    testsPassed = $false
    buildPassed = $false
    mechanicalPass = $false
    proceduralScreenshot = $proceduralShot
    proceduralScreenshotDir = $proceduralShotDir
    proceduralScreenshotBytes = $null
    proceduralAngleShotBytes = @{}
    proceduralAngleShots = @()
    proceduralAngleCount = 0
    proceduralAngleMissing = @()
    minScreenshotBytes = $MinScreenshotBytes
    criticFps = $null
    criticFpsMin = $MinFps
    criticFpsSource = "windowTitle"
    criticWindowTitle = $null
    clientLogPath = $clientLogPath
    fpsReportPath = $fpsReportPath
    referenceMaterialRoot = $referenceMaterialDir
    overallPass = $false
    gaps = @()
}

Stop-AstroCraftProcesses

try {
    if ($Mode -eq "Full") {
        $timer = New-PhaseTimer
        Write-PhaseLog "Running dotnet test..."
        dotnet test --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            $report.gaps += "dotnet test failed"
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }
        $report.testsPassed = $true
        Stop-PhaseTimer -Timer $timer -PhaseTimings $phaseTimings -PhaseName "dotnetTest"
    } else {
        Write-PhaseLog "Fast mode: skipping dotnet test"
        $phaseTimings["dotnetTest"] = 0
    }

    if ($Mode -eq "Fast") {
        $timer = New-PhaseTimer
        Write-PhaseLog "Compiling shaders (Vulkan SDK glslc)..."
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "compile-shaders.ps1")
        if ($LASTEXITCODE -ne 0) {
            $report.gaps += "shader compile failed (glslc required)"
            throw "Shader compile failed - install Vulkan SDK and ensure glslc is available"
        }
        Stop-PhaseTimer -Timer $timer -PhaseTimings $phaseTimings -PhaseName "shaderCompile"
    } else {
        $phaseTimings["shaderCompile"] = 0
    }

    if (-not $SkipBuild) {
        $timer = New-PhaseTimer
        Write-PhaseLog "Running dotnet build..."
        dotnet build --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            $report.gaps += "dotnet build failed"
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }
        $report.buildPassed = $true
        Stop-PhaseTimer -Timer $timer -PhaseTimings $phaseTimings -PhaseName "dotnetBuild"
    } else {
        Write-PhaseLog "Skipping dotnet build (-SkipBuild)"
        $report.buildPassed = $true
        $phaseTimings["dotnetBuild"] = 0
    }

    $timer = New-PhaseTimer
    Write-PhaseLog "Starting gauntlet server on port $Port..."
    $server = Start-GauntletServer -RepoPath $repo -Port $Port

    Write-PhaseLog "Waiting for server ready (UDP port listen + hello probe, 30s cap)..."
    $serverReady = Wait-ServerReady -Port $Port -TimeoutSeconds 30
    if (-not $serverReady) {
        $report.gaps += "Server did not bind UDP port $Port within 30s"
        throw "Server not ready on port $Port"
    }
    Stop-PhaseTimer -Timer $timer -PhaseTimings $phaseTimings -PhaseName "serverReady"

    $timer = New-PhaseTimer
    Write-PhaseLog "Procedural critic client (${CriticSeconds}s capture, timeout ${ClientTimeoutSeconds}s)..."
    New-Item -ItemType Directory -Force -Path $proceduralShotDir | Out-Null

    $clientArgs = @(
        "run", "--project", "src/AstroCraft.Client/AstroCraft.Client.csproj", "--no-build", "--",
        "--connect", "127.0.0.1", "--port", "$Port", "--name", "GauntletProcedural",
        "--critic-seconds", "$CriticSeconds", "--critic-max-bootstrap-seconds", "90",
        "--critic-screenshot", $proceduralShot,
        "--critic-screenshot-dir", $proceduralShotDir, "--critic-fps-report", $fpsReportPath
    )

    $clientResult = Invoke-CriticClient `
        -WorkingDirectory $repo `
        -ArgumentList $clientArgs `
        -TimeoutSeconds $ClientTimeoutSeconds `
        -LogPath $clientLogPath

    $clientTimedOut = $clientResult.TimedOut
    $report.clientTimedOut = $clientTimedOut
    Stop-PhaseTimer -Timer $timer -PhaseTimings $phaseTimings -PhaseName "clientCapture"

    if ($clientTimedOut) {
        $report.gaps += "Critic client timed out after ${ClientTimeoutSeconds}s"
        throw "Critic client timed out after ${ClientTimeoutSeconds}s"
    }
    if ($clientResult.ExitCode -ne 0) {
        $report.gaps += "Critic client exited with code $($clientResult.ExitCode)"
        throw "Critic client failed with exit code $($clientResult.ExitCode)"
    }

    if (Test-Path $fpsReportPath) {
        $fpsJson = Get-Content $fpsReportPath -Raw | ConvertFrom-Json
        $report.criticFps = [double]$fpsJson.fps
        if ($fpsJson.windowTitle) {
            $report.criticWindowTitle = [string]$fpsJson.windowTitle
        }
        if ($null -ne $fpsJson.bootstrapTimedOut) {
            $bootstrapTimedOut = [bool]$fpsJson.bootstrapTimedOut
            $report.bootstrapTimedOut = $bootstrapTimedOut
            if ($bootstrapTimedOut) {
                $report.gaps += "Critic bootstrap timed out before world ready"
            }
        }
        if ($fpsJson.gaps) {
            foreach ($gap in @($fpsJson.gaps)) {
                $gapText = [string]$gap
                if (-not $report.gaps.Contains($gapText)) {
                    $report.gaps += $gapText
                }
            }
        }
    }

    $title = Get-AstroCraftWindowTitle
    if ($title) {
        $report.criticWindowTitle = $title
        $fps = Get-FpsFromWindowTitle -Title $title
        if ($fps -ne $null -and ($report.criticFps -eq $null -or $fps -gt $report.criticFps)) {
            $report.criticFps = $fps
        }
    }

    if (-not (Test-Path $proceduralShot)) {
        $centerPath = Get-ProceduralAngleShotPath -Dir $proceduralShotDir -Angle "center"
        if (Test-Path $centerPath) {
            Copy-Item -Path $centerPath -Destination $proceduralShot -Force
        }
    }

    if (-not (Test-Path $proceduralShot)) {
        $report.gaps += "Procedural critic screenshot missing"
        throw "Procedural screenshot was not created: $proceduralShot"
    }

    $angleData = Get-ProceduralAngleShotData -Dir $proceduralShotDir
    $report.proceduralAngleShotBytes = $angleData.bytes
    $report.proceduralAngleShots = $angleData.shots
    $report.proceduralScreenshotBytes = Get-FileSizeBytes -Path $proceduralShot
    if ($null -eq $report.proceduralAngleShotBytes["center"]) {
        $report.proceduralAngleShotBytes["center"] = $report.proceduralScreenshotBytes
    }
    Add-ProceduralAngleShotGaps -Report $report -MinBytes $MinScreenshotBytes
    if ($report.proceduralScreenshotBytes -lt $MinScreenshotBytes) {
        $report.gaps += "Procedural screenshot too small ($($report.proceduralScreenshotBytes) bytes, min $MinScreenshotBytes)"
    }

    Set-SmokeMechanicalPass -Report $report -MinScreenshotBytes $MinScreenshotBytes -MinFps $MinFps -Mode $Mode
    if ($report.criticFps -eq $null -or $report.criticFps -lt $MinFps) {
        $report.gaps += "Critic FPS below $MinFps or not recorded"
    }
}
catch {
    $report.overallPass = $false
    $report.mechanicalPass = $false
    if (-not $report.gaps.Contains($_.Exception.Message)) {
        $report.gaps += $_.Exception.Message
    }
    Write-Error $_
}
finally {
    if ($server) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
    Stop-AstroCraftProcesses

    $report.clientTimedOut = $clientTimedOut
    $report.bootstrapTimedOut = $bootstrapTimedOut
    $report.phaseTimings = $phaseTimings

    $report | ConvertTo-Json -Depth 8 | Set-Content -Path $reportPath -Encoding utf8
  # Legacy path for scripts that still look for gauntlet-report.json
    $legacyPath = Join-Path $shots "gauntlet-report.json"
    $report | ConvertTo-Json -Depth 8 | Set-Content -Path $legacyPath -Encoding utf8

    Write-PhaseLog "Smoke report: $reportPath"
    $timingSummary = ($phaseTimings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)s" }) -join ', '
    Write-PhaseLog "Mode: $Mode | Phase timings: $timingSummary"
    if ($report.proceduralAngleCount -ne $null) {
        Write-PhaseLog "Procedural angle shots: $($report.proceduralAngleCount)/$($Script:RequiredProceduralAngles.Count) captured"
        foreach ($angle in $Script:RequiredProceduralAngles) {
            $bytes = $report.proceduralAngleShotBytes[$angle]
            if ($null -ne $bytes) {
                Write-PhaseLog "  - $angle : $bytes bytes"
            }
        }
    }
    if ($clientTimedOut) {
        Write-PhaseLog "Client timed out after ${ClientTimeoutSeconds}s (log: $clientLogPath)"
    }
    if ($bootstrapTimedOut) {
        Write-PhaseLog "Bootstrap timed out before world ready"
    }
    if ($report.mechanicalPass) {
        Write-PhaseLog "SMOKE PASS (mechanical checks only; agent critics judge quality)"
    } else {
        Write-PhaseLog "SMOKE FAIL"
        if ($report.gaps.Count -gt 0) {
            Write-PhaseLog "Gaps:"
            $report.gaps | ForEach-Object { Write-PhaseLog "  - $_" }
        }
    }
}

if (-not $report.mechanicalPass) {
    exit 1
}
