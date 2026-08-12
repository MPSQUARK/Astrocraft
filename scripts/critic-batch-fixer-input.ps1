param(
    [string]$BatchDir = "",
    [string]$HandoffPath = ""
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
if (-not $BatchDir) {
    $shots = Join-Path $repo "docs\critic-screenshots"
    $latest = Get-ChildItem -Path $shots -Directory -Filter "critic-batch-*" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not $latest) {
        Write-Error "No critic-batch-* directory found under docs/critic-screenshots"
    }
    $BatchDir = $latest.FullName
}

$codePath = Join-Path $BatchDir "critic-code.json"
$todoPath = Join-Path $BatchDir "critic-todo.json"
$visionPath = Join-Path $BatchDir "critic-vision.json"

function Read-CriticReport {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return $null
    }
    return Get-Content $Path -Raw | ConvertFrom-Json
}

$reports = @(
    (Read-CriticReport -Path $codePath),
    (Read-CriticReport -Path $todoPath),
    (Read-CriticReport -Path $visionPath)
) | Where-Object { $null -ne $_ }

if ($reports.Count -eq 0) {
    Write-Error "No critic JSON files found in $BatchDir"
}

$fixerIssues = @()
foreach ($report in $reports) {
    if ($report.verdict -eq "FAIL") {
        foreach ($issue in @($report.issues)) {
            $fixerIssues += @{
                criticType = $report.criticType
                id = $issue.id
                severity = $issue.severity
                whatIsWrong = $issue.whatIsWrong
                howToFix = $issue.howToFix
                files = @($issue.files)
            }
        }
    }
}

$blocked = @($reports | Where-Object { $_.verdict -eq "BLOCKED" })
$failed = @($reports | Where-Object { $_.verdict -eq "FAIL" })
$passed = @($reports | Where-Object { $_.verdict -eq "PASS" })

$output = @{
    batchDir = $BatchDir
    handoffPath = $HandoffPath
    summary = @{
        passCount = $passed.Count
        failCount = $failed.Count
        blockedCount = $blocked.Count
        readyForFixer = ($failed.Count -gt 0 -and $blocked.Count -eq 0)
    }
    topPriorities = @($reports | ForEach-Object {
        if ($_.verdict -eq "FAIL" -and $_.topPriority) {
            @{ criticType = $_.criticType; topPriority = $_.topPriority }
        }
    })
    fixerIssues = $fixerIssues
    blockedReasons = @($blocked | ForEach-Object { $_.blockedReason })
}

$outPath = Join-Path $BatchDir "fixer-input.json"
$output | ConvertTo-Json -Depth 8 | Set-Content -Path $outPath -Encoding utf8

Write-Host "Fixer input: $outPath"
Write-Host "PASS=$($output.summary.passCount) FAIL=$($output.summary.failCount) BLOCKED=$($output.summary.blockedCount)"
if ($output.summary.readyForFixer) {
    Write-Host "READY: spawn fixer/builder with fixer-input.json"
    foreach ($prio in @($output.topPriorities)) {
        Write-Host "  [$($prio.criticType)] $($prio.topPriority)"
    }
} elseif ($blocked.Count -gt 0) {
    Write-Host "BLOCKED: fix smoke/handoff first"
    $output.blockedReasons | ForEach-Object { Write-Host "  - $_" }
} else {
    Write-Host "BATCH ACCEPTED: all critics PASS"
}

$output | ConvertTo-Json -Depth 8 | Write-Output

if ($blocked.Count -gt 0) { exit 2 }
if ($failed.Count -gt 0) { exit 1 }
exit 0
