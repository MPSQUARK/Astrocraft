$Script:RequiredProceduralAngles = @("center", "look-left", "look-right", "look-up", "look-down")

function Stop-AstroCraftProcesses {
    Get-Process -Name "AstroCraft.Server", "AstroCraft.Client", "testhost" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Test-UdpPortListening {
    param([int]$Port)

    $matches = netstat -an | Select-String "UDP\s+.*:$Port\s"
    return $null -ne $matches -and $matches.Count -gt 0
}

function Get-AstroCraftWindowTitle {
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
        try {
            $title = $process.MainWindowTitle
            if ($title -and $title -like "AstroCraft*") {
                return $title
            }
        } catch {
        }
    }
    return $null
}

function Get-FpsFromWindowTitle {
    param([string]$Title)
    if ($Title -match '\|\s*(\d+(?:\.\d+)?)\s+FPS\s*\|') {
        return [double]$Matches[1]
    }
    return $null
}

function Get-FileSizeBytes {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return $null
    }
    return (Get-Item $Path).Length
}

function Get-ProceduralAngleShotPath {
    param(
        [string]$Dir,
        [string]$Angle
    )
    return Join-Path $Dir "critic-$Angle.png"
}

function Get-ProceduralAngleShotData {
    param([string]$Dir)

    $bytes = @{}
    $shots = @()
    foreach ($angle in $Script:RequiredProceduralAngles) {
        $path = Get-ProceduralAngleShotPath -Dir $Dir -Angle $angle
        $size = Get-FileSizeBytes -Path $path
        $bytes[$angle] = $size
        $shots += @{
            angle = $angle
            name = "critic-$angle.png"
            path = $path
            bytes = $size
        }
    }

    return @{
        bytes = $bytes
        shots = $shots
    }
}

function Add-ProceduralAngleShotGaps {
    param(
        [hashtable]$Report,
        [int]$MinBytes
    )

    $missingAngles = @()
    $smallAngles = @()
    foreach ($angle in $Script:RequiredProceduralAngles) {
        $bytes = $Report.proceduralAngleShotBytes[$angle]
        if ($null -eq $bytes) {
            $missingAngles += $angle
        } elseif ($bytes -lt $MinBytes) {
            $smallAngles += "$angle ($bytes bytes)"
        }
    }

    if ($missingAngles.Count -gt 0) {
        $Report.gaps += "Procedural angle shots missing: $($missingAngles -join ', ')"
    }
    if ($smallAngles.Count -gt 0) {
        $Report.gaps += "Procedural angle shots too small (min $MinBytes): $($smallAngles -join ', ')"
    }

    $hashByAngle = @{}
    $duplicateAngles = @()
    foreach ($angle in $Script:RequiredProceduralAngles) {
        $path = Get-ProceduralAngleShotPath -Dir $Report.proceduralScreenshotDir -Angle $angle
        if (-not (Test-Path $path)) { continue }
        $bytes = $Report.proceduralAngleShotBytes[$angle]
        if ($null -eq $bytes -or $bytes -lt $MinBytes) { continue }
        $hash = (Get-FileHash -Path $path -Algorithm MD5).Hash
        if ($hashByAngle.ContainsKey($hash)) {
            $duplicateAngles += "$angle duplicates $($hashByAngle[$hash])"
        } else {
            $hashByAngle[$hash] = $angle
        }
    }
    if ($duplicateAngles.Count -gt 0) {
        $Report.gaps += "Procedural angle shots not distinct: $($duplicateAngles -join '; ')"
        foreach ($dup in $duplicateAngles) {
            if ($dup -match '^(\S+) duplicates') {
                $badAngle = $Matches[1]
                $Report.proceduralAngleShotBytes[$badAngle] = $null
            }
        }
    }

    $Report.proceduralAngleMissing = $missingAngles
    $Report.proceduralAngleCount = @($Script:RequiredProceduralAngles | Where-Object {
        $null -ne $Report.proceduralAngleShotBytes[$_] -and $Report.proceduralAngleShotBytes[$_] -ge $MinBytes
    }).Count
}

function Write-PhaseLog {
    param([string]$Message)
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] $Message"
}

function Start-GauntletServer {
    param(
        [string]$RepoPath,
        [int]$Port,
        [string]$ServerName = "Gauntlet Server",
        [int]$Seed = 42
    )

    $serverArgs = @(
        "run", "--project", "src/AstroCraft.Server/AstroCraft.Server.csproj", "--no-build",
        "--", "--name", $ServerName, "--seed", "$Seed", "--port", "$Port"
    )

    return Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $serverArgs `
        -WorkingDirectory $RepoPath `
        -PassThru `
        -WindowStyle Minimized
}

function Wait-ServerReady {
    param(
        [int]$Port,
        [int]$TimeoutSeconds = 30,
        [string]$PlayerName = "GauntletProbe"
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-UdpPortListening -Port $Port) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not (Test-UdpPortListening -Port $Port)) {
        return $false
    }

    $udp = $null
    try {
        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.Client.ReceiveTimeout = 2000
        $endpoint = New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Loopback, $Port)

        $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($PlayerName)
        $hello = New-Object byte[] (2 + $nameBytes.Length)
        $hello[0] = 1
        $hello[1] = [byte]$nameBytes.Length
        [Array]::Copy($nameBytes, 0, $hello, 2, $nameBytes.Length)

        while ((Get-Date) -lt $deadline) {
            try {
                [void]$udp.Send($hello, $hello.Length, $endpoint)
                $remote = New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Any, 0)
                $response = $udp.Receive([ref]$remote)
                if ($response.Length -gt 0 -and $response[0] -eq 2) {
                    return $true
                }
            } catch [System.Net.Sockets.SocketException] {
            }
            Start-Sleep -Milliseconds 500
        }
    }
    finally {
        if ($udp) {
            $udp.Close()
            $udp.Dispose()
        }
    }

    return Test-UdpPortListening -Port $Port
}

function Invoke-CriticClient {
    param(
        [string]$WorkingDirectory,
        [string[]]$ArgumentList,
        [int]$TimeoutSeconds,
        [string]$LogPath
    )

    $logDir = Split-Path $LogPath -Parent
    if ($logDir) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -PassThru `
        -WindowStyle Normal

    $timedOut = $false
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastTitle = ""

    while (-not $proc.HasExited) {
        if ((Get-Date) -gt $deadline) {
            $timedOut = $true
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            break
        }

        $title = Get-AstroCraftWindowTitle
        if ($title -and $title -ne $lastTitle) {
            Write-PhaseLog "Client window: $title"
            $lastTitle = $title
            Set-Content -Path $LogPath -Value $title -Encoding utf8
        }

        Start-Sleep -Milliseconds 500
    }

    $sw.Stop()
    if (-not $timedOut -and -not $proc.HasExited) {
        $proc.WaitForExit()
    }

    $exitCode = if ($proc.HasExited) { $proc.ExitCode } else { -1 }

    return @{
        ExitCode = $exitCode
        TimedOut = $timedOut
        LogPath = $LogPath
        DurationSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    }
}

function Stop-PhaseTimer {
    param(
        [System.Diagnostics.Stopwatch]$Timer,
        [hashtable]$PhaseTimings,
        [string]$PhaseName
    )

    $Timer.Stop()
    $PhaseTimings[$PhaseName] = [math]::Round($Timer.Elapsed.TotalSeconds, 2)
}

function New-PhaseTimer {
    return [System.Diagnostics.Stopwatch]::StartNew()
}

function Set-SmokeMechanicalPass {
    param(
        [hashtable]$Report,
        [int]$MinScreenshotBytes,
        [int]$MinFps,
        [string]$Mode
    )

    $screenshotsOk = ($Report.proceduralScreenshotBytes -ge $MinScreenshotBytes) -and
        ($Report.proceduralAngleCount -eq $Script:RequiredProceduralAngles.Count)
    $fpsOk = $null -ne $Report.criticFps -and $Report.criticFps -ge $MinFps

    $basePass = $Report.buildPassed -and $screenshotsOk -and $fpsOk

    if ($Mode -eq "Full") {
        $Report.mechanicalPass = $basePass -and $Report.testsPassed
    } else {
        $Report.mechanicalPass = $basePass
    }

    $Report.overallPass = $Report.mechanicalPass
}

. (Join-Path $PSScriptRoot "_ref-keywords.ps1")
