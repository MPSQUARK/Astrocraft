param(
    [string[]]$Keywords = @(),
    [string]$KeywordsFile = "",
    [string]$ReferenceMaterialDir = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "critic\_ref-keywords.ps1")

$repo = Split-Path -Parent $PSScriptRoot
if (-not $ReferenceMaterialDir) {
    $ReferenceMaterialDir = Join-Path $repo "ReferenceMaterial"
}

if ($KeywordsFile -and (Test-Path $KeywordsFile)) {
    $raw = Get-Content $KeywordsFile -Raw | ConvertFrom-Json
    if ($raw.keywords) {
        $Keywords = @($raw.keywords)
    }
}

if (-not $OutputPath) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $shots = Join-Path $repo "docs\critic-screenshots"
    New-Item -ItemType Directory -Force -Path $shots | Out-Null
    $OutputPath = Join-Path $shots "reference-match-$stamp.json"
}

$report = Write-ReferenceMatchReport `
    -ReferenceMaterialDir $ReferenceMaterialDir `
    -Keywords $Keywords `
    -OutputPath $OutputPath

Write-Host "Reference match report: $OutputPath"
Write-Host "Keywords: $($report.keywords -join ', ')"
Write-Host "Matched $($report.matchCount) file(s):"
foreach ($entry in @($report.matchedFiles)) {
    Write-Host "  - $($entry.filename)"
}

$report | ConvertTo-Json -Depth 4 | Write-Output
