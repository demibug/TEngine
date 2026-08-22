[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter()]
    [string]$SourceDirectory,

    [Parameter()]
    [string]$DestinationDirectory,

    [Parameter()]
    [string]$ManifestPath,

    [Parameter()]
    [switch]$Copy,

    [Parameter()]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$engineRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..'))

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $engineRoot 'Origin\reconstructed-project\origin_project\resources\anim\zhangFei'
}
if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $projectRoot 'Assets\SourceAssets\SpineConversion\Generals\ZhangFei'
}
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $projectRoot 'outputs\player-general-synthesis\resource-copy-manifest.json'
}

$SourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$DestinationDirectory = [System.IO.Path]::GetFullPath($DestinationDirectory)
$ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)

$expectedFiles = @('skeleton.json', 'skeleton.atlas', 'skeleton.png')
$originAnimationRoot = Split-Path -Parent $SourceDirectory
$huangZhongCandidates = @(
    Get-ChildItem -LiteralPath $originAnimationRoot -Directory | Where-Object {
        (($_.Name -replace '[^a-zA-Z]', '').ToLowerInvariant()) -eq 'huangzhong'
    } | ForEach-Object { $_.FullName }
)
$huangZhongSpineFound = $huangZhongCandidates.Count -gt 0

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)
    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Manifest {
    param([Parameter(Mandatory = $true)][object]$Value)

    $manifestDirectory = Split-Path -Parent $ManifestPath
    if (-not (Test-Path -LiteralPath $manifestDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $ManifestPath -Value $json -Encoding utf8
}

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Zhang Fei source directory does not exist: $SourceDirectory"
}

$entries = [System.Collections.Generic.List[object]]::new()
$hasConflict = $false

foreach ($fileName in $expectedFiles) {
    $sourcePath = Join-Path $SourceDirectory $fileName
    $destinationPath = Join-Path $DestinationDirectory $fileName

    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required Zhang Fei source file is missing: $sourcePath"
    }

    $sourceItem = Get-Item -LiteralPath $sourcePath
    if ($sourceItem.Length -le 0) {
        throw "Required Zhang Fei source file is empty: $sourcePath"
    }

    $sourceHash = Get-Sha256 -LiteralPath $sourcePath
    $destinationExists = Test-Path -LiteralPath $destinationPath -PathType Leaf
    $destinationHash = $null
    $destinationSize = $null
    $status = 'Missing'

    if ($destinationExists) {
        $destinationItem = Get-Item -LiteralPath $destinationPath
        $destinationHash = Get-Sha256 -LiteralPath $destinationPath
        $destinationSize = $destinationItem.Length
        if ($destinationHash -eq $sourceHash) {
            $status = 'Unchanged'
        }
        else {
            $status = 'Conflict'
            $hasConflict = $true
        }
    }

    $entries.Add([ordered]@{
        file = $fileName
        sourcePath = $sourcePath
        destinationPath = $destinationPath
        sourceSize = $sourceItem.Length
        sourceSha256 = $sourceHash
        destinationExists = $destinationExists
        destinationSize = $destinationSize
        destinationSha256 = $destinationHash
        status = $status
        action = if ($status -eq 'Unchanged') { 'None' } elseif ($status -eq 'Conflict') { 'OverwriteRequiresForce' } else { 'Copy' }
    })
}

$mode = if ($Copy) { if ($WhatIfPreference) { 'WhatIf' } else { 'Copy' } } else { 'Preflight' }
$manifest = [ordered]@{
    schema = 'player-general-synthesis/resource-copy/v1'
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    mode = $mode
    sourceDirectory = $SourceDirectory
    destinationDirectory = $DestinationDirectory
    exactFiles = $expectedFiles
    forceRequested = [bool]$Force
    result = if ($hasConflict) { 'Conflict' } else { 'Ready' }
    files = $entries
    huangZhongFallback = [ordered]@{
        originAnimationRoot = $originAnimationRoot
        checkedCandidateDirectories = $huangZhongCandidates
        dedicatedSpineFound = $huangZhongSpineFound
        prefabAddress = 'BowSoldier'
        projectileType = 'SimpleDynamicArrow'
        projectilePresentation = 'Arrow'
        note = if ($huangZhongSpineFound) {
            'A Huang Zhong-named Origin directory now exists. Keep the declared BowSoldier/Arrow fallback until its contents are separately reviewed and approved; this script does not copy or rename it.'
        }
        else {
            'Origin has no dedicated Huang Zhong Spine. This change intentionally reuses the existing BowSoldier and Arrow presentation; no other general art is copied or renamed.'
        }
    }
}

Write-Manifest -Value $manifest

if (-not $Copy) {
    Write-Output "Preflight complete. No resources were copied."
    Write-Output "Manifest: $ManifestPath"
    foreach ($entry in $entries) {
        Write-Output ("{0}: {1}" -f $entry.file, $entry.status)
    }
    return
}

if ($hasConflict -and -not $Force) {
    throw "Destination contains differing protected files. Review $ManifestPath and rerun with -Copy -Force to overwrite only the three exact Zhang Fei files."
}

if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) {
    if ($PSCmdlet.ShouldProcess($DestinationDirectory, 'Create Zhang Fei destination directory')) {
        New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    }
}

$copied = 0
foreach ($entry in $entries) {
    if ($entry.status -eq 'Unchanged') {
        continue
    }

    $action = if ($entry.status -eq 'Conflict') { 'Overwrite protected Zhang Fei resource' } else { 'Copy Zhang Fei resource' }
    if ($PSCmdlet.ShouldProcess($entry.destinationPath, $action)) {
        Copy-Item -LiteralPath $entry.sourcePath -Destination $entry.destinationPath -Force
        $copied++
    }
}

if (-not $WhatIfPreference) {
    foreach ($entry in $entries) {
        $wasUnchanged = $entry.status -eq 'Unchanged'
        if (-not (Test-Path -LiteralPath $entry.destinationPath -PathType Leaf)) {
            throw "Copy verification failed; destination is missing: $($entry.destinationPath)"
        }
        $actualHash = Get-Sha256 -LiteralPath $entry.destinationPath
        if ($actualHash -ne $entry.sourceSha256) {
            throw "Copy verification failed; SHA-256 differs: $($entry.destinationPath)"
        }
        $entry.destinationExists = $true
        $entry.destinationSize = (Get-Item -LiteralPath $entry.destinationPath).Length
        $entry.destinationSha256 = $actualHash
        $entry.status = if ($wasUnchanged) { 'Unchanged' } else { 'CopiedAndVerified' }
        $entry.action = 'None'
    }
    $manifest.result = 'Verified'
    Write-Manifest -Value $manifest
}

Write-Output "Resource operation complete. Copied files: $copied"
Write-Output "Manifest: $ManifestPath"
