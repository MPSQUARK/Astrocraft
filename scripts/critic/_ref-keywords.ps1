# Keyword matcher for ReferenceMaterial filenames — curation only, no quality judgment.

function Expand-ReferenceKeywords {
    param([string[]]$Keywords)

    $expanded = @()
    foreach ($kw in $Keywords) {
        if ([string]::IsNullOrWhiteSpace($kw)) { continue }
        foreach ($part in ($kw -split '[,\s;]+')) {
            if (-not [string]::IsNullOrWhiteSpace($part)) {
                $expanded += $part
            }
        }
    }
    return $expanded
}

function Get-ReferenceMaterialKeywords {
    param([string[]]$Keywords)

    $normalized = @(Expand-ReferenceKeywords -Keywords $Keywords | ForEach-Object {
        ($_ -replace '[^a-zA-Z0-9]+', '_').ToLowerInvariant().Trim('_')
    } | Where-Object { $_ })

    return @($normalized | Select-Object -Unique)
}

function Get-ReferenceMaterialFiles {
    param(
        [string]$ReferenceMaterialDir,
        [string[]]$Keywords = @()
    )

    if (-not (Test-Path $ReferenceMaterialDir)) {
        return @()
    }

    $imageExtensions = @(".png", ".jpg", ".jpeg", ".webp")
    $allFiles = Get-ChildItem -Path $ReferenceMaterialDir -File -ErrorAction SilentlyContinue |
        Where-Object { $imageExtensions -contains $_.Extension.ToLowerInvariant() }

    if ($Keywords.Count -eq 0) {
        return @($allFiles | ForEach-Object { $_.FullName })
    }

    $terms = Get-ReferenceMaterialKeywords -Keywords $Keywords
    if ($terms.Count -eq 0) {
        return @($allFiles | ForEach-Object { $_.FullName })
    }

    $matched = @()
    foreach ($file in $allFiles) {
        $name = $file.Name.ToLowerInvariant()
        foreach ($term in $terms) {
            if ($name -like "*$term*") {
                $matched += $file.FullName
                break
            }
        }
    }

    return @($matched | Select-Object -Unique)
}

function Write-ReferenceMatchReport {
    param(
        [string]$ReferenceMaterialDir,
        [string[]]$Keywords,
        [string]$OutputPath
    )

    $files = Get-ReferenceMaterialFiles -ReferenceMaterialDir $ReferenceMaterialDir -Keywords $Keywords
    $report = @{
        timestamp = (Get-Date -Format "o")
        referenceMaterialDir = $ReferenceMaterialDir
        keywords = @(Get-ReferenceMaterialKeywords -Keywords $Keywords)
        matchedFiles = @($files | ForEach-Object {
            @{
                path = $_
                filename = Split-Path $_ -Leaf
            }
        })
        matchCount = $files.Count
    }

    $dir = Split-Path $OutputPath -Parent
    if ($dir) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    $report | ConvertTo-Json -Depth 4 | Set-Content -Path $OutputPath -Encoding utf8
    return $report
}
