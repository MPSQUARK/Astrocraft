param(
    [int]$CriticSeconds = 18,
    [int]$Port = 27016,
    [int]$MinFps = 30,
    [int]$MinScreenshotBytes = 51200,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$RequiredProceduralAngles = @("center", "look-left", "look-right", "look-up", "look-down")
$repo = Split-Path -Parent $PSScriptRoot
$shots = Join-Path $repo "docs\critic-screenshots"
$reportPath = Join-Path $shots "smoke-report.json"
$gauntletScript = Join-Path $PSScriptRoot "critic-gauntlet.ps1"

if (-not $OutputPath) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath = Join-Path $shots "visual-check-$stamp.json"
}

New-Item -ItemType Directory -Force -Path $shots | Out-Null

$result = @{
    timestamp = (Get-Date -Format "o")
    smokeExitCode = $null
    smokeReportPath = $reportPath
    checks = @{
        smokeRan = $false
        buildPass = $false
        fpsPass = $false
        proceduralScreenshotExists = $false
        proceduralScreenshotSizePass = $false
        proceduralAngleShotsComplete = $false
        proceduralAngleShotsSizePass = $false
        mechanicalPass = $false
    }
    criticFps = $null
    proceduralScreenshot = $null
    proceduralScreenshotDir = $null
    proceduralScreenshotBytes = $null
    proceduralAngleShotBytes = @{}
    proceduralAngleMissing = @()
    minFps = $MinFps
    minScreenshotBytes = $MinScreenshotBytes
    gaps = @()
    overallPass = $false
}

function Get-AngleShotBytesFromReport {
    param($Report)

    $bytes = @{}
    if ($Report.proceduralAngleShotBytes) {
        $Report.proceduralAngleShotBytes.PSObject.Properties | ForEach-Object {
            $bytes[$_.Name] = [nullable[long]]$_.Value
        }
        return $bytes
    }

    if ($Report.proceduralAngleShots) {
        foreach ($shot in @($Report.proceduralAngleShots)) {
            $angle = if ($shot.angle) { [string]$shot.angle } else { ([string]$shot.name) -replace '^critic-', '' -replace '\.png$', '' }
            if ($angle) {
                $bytes[$angle] = [nullable[long]]$shot.bytes
            }
        }
    }

    return $bytes
}

Write-Host "Running smoke harness (critic-gauntlet.ps1)..."
$prevErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & $gauntletScript -CriticSeconds $CriticSeconds -Port $Port -MinScreenshotBytes $MinScreenshotBytes
    $result.smokeExitCode = $LASTEXITCODE
    $result.checks.smokeRan = $true
} catch {
    $result.smokeExitCode = 1
    $result.gaps += "Smoke script error: $($_.Exception.Message)"
} finally {
    $ErrorActionPreference = $prevErrorAction
}

if (-not (Test-Path $reportPath)) {
    $result.gaps += "Smoke report missing: $reportPath"
} else {
    try {
        $report = Get-Content $reportPath -Raw | ConvertFrom-Json

        $result.checks.buildPass = [bool]$report.buildPassed
        if (-not $result.checks.buildPass) {
            $result.gaps += "Build did not pass in smoke report"
        }

        if ($null -ne $report.criticFps) {
            $result.criticFps = [double]$report.criticFps
            $result.checks.fpsPass = $result.criticFps -ge $MinFps
            if (-not $result.checks.fpsPass) {
                $result.gaps += "Critic FPS $($result.criticFps) below minimum $MinFps"
            }
        } else {
            $result.gaps += "Critic FPS not recorded in smoke report"
        }

        $proceduralPath = [string]$report.proceduralScreenshot
        $result.proceduralScreenshot = $proceduralPath
        $result.proceduralScreenshotDir = [string]$report.proceduralScreenshotDir
        $result.proceduralAngleShotBytes = Get-AngleShotBytesFromReport -Report $report

        if ($report.proceduralAngleMissing) {
            $result.proceduralAngleMissing = @($report.proceduralAngleMissing | ForEach-Object { [string]$_ })
        } else {
            $result.proceduralAngleMissing = @($RequiredProceduralAngles | Where-Object {
                $null -eq $result.proceduralAngleShotBytes[$_] -or -not (Test-Path (Join-Path $result.proceduralScreenshotDir "critic-$_.png"))
            })
        }

        if ($proceduralPath -and (Test-Path $proceduralPath)) {
            $result.checks.proceduralScreenshotExists = $true
            $result.proceduralScreenshotBytes = (Get-Item $proceduralPath).Length
            $result.checks.proceduralScreenshotSizePass = $result.proceduralScreenshotBytes -gt $MinScreenshotBytes
            if (-not $result.checks.proceduralScreenshotSizePass) {
                $result.gaps += "Procedural screenshot too small ($($result.proceduralScreenshotBytes) bytes, need > $MinScreenshotBytes)"
            }
        } else {
            $result.gaps += "Procedural screenshot missing: $proceduralPath"
        }

        $missingAngles = @()
        $smallAngles = @()
        foreach ($angle in $RequiredProceduralAngles) {
            $bytes = $result.proceduralAngleShotBytes[$angle]
            if ($null -eq $bytes) {
                $missingAngles += $angle
            } elseif ($bytes -le $MinScreenshotBytes) {
                $smallAngles += "$angle ($bytes bytes)"
            }
        }

        $result.proceduralAngleMissing = if ($result.proceduralAngleMissing.Count -gt 0) { $result.proceduralAngleMissing } else { $missingAngles }
        $result.checks.proceduralAngleShotsComplete = ($missingAngles.Count -eq 0)
        $result.checks.proceduralAngleShotsSizePass = ($smallAngles.Count -eq 0) -and $result.checks.proceduralAngleShotsComplete

        if ($missingAngles.Count -gt 0) {
            $result.gaps += "Procedural angle shots missing: $($missingAngles -join ', ')"
        }
        if ($smallAngles.Count -gt 0) {
            $result.gaps += "Procedural angle shots too small (need > $MinScreenshotBytes): $($smallAngles -join ', ')"
        }

        if ($report.gaps -and $report.gaps.Count -gt 0) {
            foreach ($gap in $report.gaps) {
                if (-not $result.gaps.Contains([string]$gap)) {
                    $result.gaps += [string]$gap
                }
            }
        }

        $result.checks.mechanicalPass = [bool]$report.mechanicalPass
    } catch {
        $result.gaps += "Failed to parse smoke report: $($_.Exception.Message)"
    }
}

$result.overallPass = $result.checks.smokeRan -and $result.checks.mechanicalPass

$result | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding utf8
Write-Host "Visual check report: $OutputPath"
Write-Host "Procedural angle shot bytes:"
foreach ($angle in $RequiredProceduralAngles) {
    $bytes = $result.proceduralAngleShotBytes[$angle]
    if ($null -ne $bytes) {
        Write-Host "  - $angle : $bytes bytes"
    } else {
        Write-Host "  - $angle : missing"
    }
}
$result | ConvertTo-Json -Depth 6 | Write-Output

if ($result.overallPass) {
    Write-Host "SMOKE CHECK PASS (spawn agent critics for quality judgment)"
    exit 0
}

Write-Host "SMOKE CHECK FAIL"
if ($result.gaps.Count -gt 0) {
    Write-Host "Gaps:"
    $result.gaps | ForEach-Object { Write-Host "  - $_" }
}
exit 1
