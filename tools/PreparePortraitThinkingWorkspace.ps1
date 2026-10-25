[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PortraitRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$allowedEmotions = @('neutral','happy','angry','sad','afraid','guarded')
$maximumPngBytes = 16777216

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-SafeSourcePath([string]$Root,[string]$RelativePath,[string]$Label) {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { throw "$Label must be a nonempty portrait-root-relative path" }
    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains(':')) { throw "$Label must be portrait-root-relative: $RelativePath" }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    $prefix = $rootFull + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "$Label escapes PortraitRoot: $RelativePath" }
    if ([IO.Path]::GetExtension($full) -ine '.png') { throw "$Label must reference a PNG: $RelativePath" }
    $current = $rootFull
    $relative = $full.Substring($prefix.Length)
    foreach ($piece in $relative.Split([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)) {
        if ($piece -eq '' -or $piece -eq '.' -or $piece -eq '..') { throw "$Label contains an unsafe path segment: $RelativePath" }
        $current = Join-Path $current $piece
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label traverses a reparse point: $RelativePath" }
        }
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "$Label file missing: $RelativePath" }
    return $full
}

function Get-PngBitmap([string]$Path,[string]$Label) {
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -lt 8 -or $file.Length -gt $maximumPngBytes) { throw "$Label PNG size must be between 8 bytes and 16 MiB: $Path" }
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        $signature = New-Object byte[] 8
        $count = $stream.Read($signature,0,8)
        $expected = [byte[]](137,80,78,71,13,10,26,10)
        if ($count -ne 8) { throw "$Label is not a readable PNG: $Path" }
        for ($index=0; $index -lt 8; $index++) { if ($signature[$index] -ne $expected[$index]) { throw "$Label is not a PNG: $Path" } }
    } finally { $stream.Dispose() }
    try { $bitmap = [Drawing.Bitmap]::FromFile($Path) } catch { throw "$Label is not a valid PNG image: $Path" }
    if ($bitmap.Width -le 0 -or $bitmap.Height -le 0) { $bitmap.Dispose(); throw "$Label has invalid dimensions: $Path" }
    return $bitmap
}

function Test-InAnyEyeRegion([int]$X,[int]$Y,$Regions) {
    foreach ($region in $Regions) {
        if ($X -ge [int]$region.x -and $X -lt ([int]$region.x + [int]$region.width) -and
            $Y -ge [int]$region.y -and $Y -lt ([int]$region.y + [int]$region.height)) { return $true }
    }
    return $false
}

$root = [IO.Path]::GetFullPath($PortraitRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "PortraitRoot must exist: $root" }
$originalPath = Join-Path $root 'original.png'
$upperPath = Join-Path $root 'source-upper.png'
$layoutPath = Join-Path $root 'layout.json'
$thinkingLayoutPath = Join-Path $root 'thinking-layout.json'
$gridPath = Join-Path $root 'thinking-reference-grid.png'
if (Test-Path -LiteralPath $gridPath) { throw "Refusing to overwrite existing thinking reference grid: $gridPath" }
foreach ($requiredPath in @($originalPath,$upperPath,$layoutPath)) { if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Required source file missing: $requiredPath" } }

$thinkingLayout = $null
if (Test-Path -LiteralPath $thinkingLayoutPath -PathType Leaf) { $thinkingLayout = Get-Content -LiteralPath $thinkingLayoutPath -Raw -Encoding UTF8 | ConvertFrom-Json }
$emotion = 'neutral'
$referenceName = 'source-upper.png'
$gazeLayout = 'legacy-3x3'
$hasExplicitEmotion = $false
if ($null -ne $thinkingLayout) {
    if ($null -ne $thinkingLayout.PSObject.Properties['emotionId']) { $emotion = [string]$thinkingLayout.emotionId; $hasExplicitEmotion = $true }
    if ($null -ne $thinkingLayout.PSObject.Properties['referenceUpperFile']) { $referenceName = [string]$thinkingLayout.referenceUpperFile }
    if ($null -ne $thinkingLayout.PSObject.Properties['gazeLayout']) {
        $gazeLayout = [string]$thinkingLayout.gazeLayout
        if ($gazeLayout -cne 'horizontal-pair') { throw "thinking-layout gazeLayout must be horizontal-pair when specified" }
    }
}
if ($allowedEmotions -cnotcontains $emotion) { throw "thinking-layout emotionId must be one of: $($allowedEmotions -join ', ')" }
if ($gazeLayout -eq 'horizontal-pair' -and -not $hasExplicitEmotion) { throw 'horizontal-pair gazeLayout requires an explicit emotionId' }
if (-not $hasExplicitEmotion -and $null -ne $thinkingLayout) {
    foreach ($sourceField in @('referenceUpperFile','browSourceFile','halfSourceFile','closedSourceFile')) {
        if ($null -ne $thinkingLayout.PSObject.Properties[$sourceField]) { throw "$sourceField requires explicit emotionId; legacy layouts keep neutral defaults" }
    }
}
if ($hasExplicitEmotion) {
    foreach ($requiredSource in @('referenceUpperFile','browSourceFile','halfSourceFile','closedSourceFile')) {
        if ($null -eq $thinkingLayout.PSObject.Properties[$requiredSource]) { throw "Explicit emotionId requires $requiredSource from the selected emotion" }
    }
    $expectedFiles = [ordered]@{
        referenceUpperFile = "eyes\$emotion\open.png"
        browSourceFile = "eyes\$emotion\open.png"
        halfSourceFile = "eyes\$emotion\half.png"
        closedSourceFile = "eyes\$emotion\closed.png"
    }
    foreach ($sourceName in $expectedFiles.Keys) {
        $actualFull = [IO.Path]::GetFullPath((Join-Path $root ([string]$thinkingLayout.$sourceName)))
        $expectedFull = [IO.Path]::GetFullPath((Join-Path $root ([string]$expectedFiles[$sourceName])))
        if (-not [string]::Equals($actualFull,$expectedFull,[StringComparison]::OrdinalIgnoreCase)) { throw "$sourceName must select $($expectedFiles[$sourceName]) for emotion $emotion" }
    }
}
$referencePath = Get-SafeSourcePath $root $referenceName 'referenceUpperFile'
$halfSourceName = $null
$halfSourcePath = $null
if ($gazeLayout -eq 'horizontal-pair') {
    if ($null -eq $thinkingLayout.PSObject.Properties['halfSourceFile']) { throw 'horizontal-pair gazeLayout requires halfSourceFile' }
    $halfSourceName = [string]$thinkingLayout.halfSourceFile
    $expectedHalfSource = "eyes\$emotion\half.png"
    $actualHalfSource = [IO.Path]::GetFullPath((Join-Path $root $halfSourceName))
    $expectedHalfFull = [IO.Path]::GetFullPath((Join-Path $root $expectedHalfSource))
    if (-not [string]::Equals($actualHalfSource,$expectedHalfFull,[StringComparison]::OrdinalIgnoreCase)) { throw "halfSourceFile must select $expectedHalfSource for emotion $emotion" }
    $halfSourcePath = Get-SafeSourcePath $root $halfSourceName 'halfSourceFile'
}
$optionalSources = @(
    @{property='browSourceFile';label='browSourceFile'},
    @{property='halfSourceFile';label='halfSourceFile'},
    @{property='closedSourceFile';label='closedSourceFile'}
)
if ($null -ne $thinkingLayout) {
    foreach ($source in $optionalSources) {
        $property = $thinkingLayout.PSObject.Properties[$source.property]
        if ($null -ne $property) {
            $sourcePath = Get-SafeSourcePath $root ([string]$property.Value) $source.label
            $sourceBitmap = Get-PngBitmap $sourcePath $source.label
            try { if ($sourceBitmap.Width -ne 64 -or $sourceBitmap.Height -ne 35) { throw "$($source.label) must be 64x35: $($property.Value)" } }
            finally { $sourceBitmap.Dispose() }
        }
    }
}

$layout = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([int]$layout.width -ne 64 -or [int]$layout.height -ne 64 -or [int]$layout.splitY -ne 35) { throw 'Portrait layout must declare 64x64 and splitY=35' }
if (@($layout.eyeRegions).Count -ne 2) { throw 'Portrait layout must declare two eye regions' }
foreach ($region in $layout.eyeRegions) {
    if ([int]$region.width -le 0 -or [int]$region.height -le 0 -or [int]$region.x -lt 0 -or [int]$region.y -lt 0 -or ([int]$region.x+[int]$region.width) -gt 64 -or ([int]$region.y+[int]$region.height) -gt 64) { throw 'Portrait layout eyeRegions must be positive rectangles within 64x64' }
}

$originalBefore = Get-Sha256 $originalPath
$upperBefore = Get-Sha256 $upperPath
$referenceBefore = Get-Sha256 $referencePath
$halfSourceBefore = if ($null -ne $halfSourcePath) { Get-Sha256 $halfSourcePath } else { $null }
$original = Get-PngBitmap $originalPath 'Source original'
$upper = Get-PngBitmap $upperPath 'Source upper half'
$reference = Get-PngBitmap $referencePath 'Selected emotional reference upper half'
$halfSource = if ($null -ne $halfSourcePath) { Get-PngBitmap $halfSourcePath 'Selected emotional half-phase source' } else { $null }
$grid = $null
$tempPath = Join-Path $root ('.thinking-reference-grid-' + [Guid]::NewGuid().ToString('N') + '.tmp.png')
try {
    if ($original.Width -ne 64 -or $original.Height -ne 64) { throw 'Source original must be native 64x64' }
    if ($upper.Width -ne 64 -or $upper.Height -ne 35) { throw 'Source upper half must be 64x35' }
    if ($reference.Width -ne 64 -or $reference.Height -ne 35) { throw 'Selected emotional reference upper half must be 64x35' }
    if ($null -ne $halfSource -and ($halfSource.Width -ne 64 -or $halfSource.Height -ne 35)) { throw 'Selected emotional half-phase source must be 64x35' }

    for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) {
        if ($upper.GetPixel($x,$y).ToArgb() -ne $original.GetPixel($x,$y).ToArgb()) { throw "Source upper half differs from original at ($x,$y)" }
        if (-not (Test-InAnyEyeRegion $x $y $layout.eyeRegions) -and $reference.GetPixel($x,$y).ToArgb() -ne $original.GetPixel($x,$y).ToArgb()) {
            throw "Selected emotional reference differs from original outside eye masks at ($x,$y)"
        }
        if ($null -ne $halfSource -and -not (Test-InAnyEyeRegion $x $y $layout.eyeRegions) -and $halfSource.GetPixel($x,$y).ToArgb() -ne $original.GetPixel($x,$y).ToArgb()) {
            throw "Selected emotional half-phase source differs from original outside eye masks at ($x,$y)"
        }
    } }

    $gridWidth = if ($gazeLayout -eq 'horizontal-pair') { 1024 } else { 1536 }
    $gridHeight = if ($gazeLayout -eq 'horizontal-pair') { 560 } else { 840 }
    $grid = New-Object Drawing.Bitmap($gridWidth,$gridHeight,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y=0; $y -lt $gridHeight; $y++) {
        $sourceY = [int][Math]::Floor(($y % 280) / 8)
        $sourcePhase = if ($gazeLayout -eq 'horizontal-pair' -and $y -ge 280) { $halfSource } else { $reference }
        for ($x=0; $x -lt $gridWidth; $x++) {
            $sourceX = [int][Math]::Floor(($x % 512) / 8)
            $grid.SetPixel($x,$y,$sourcePhase.GetPixel($sourceX,$sourceY))
        }
    }
    $grid.Save($tempPath,[Drawing.Imaging.ImageFormat]::Png)
    $grid.Dispose(); $grid = $null
    $saved = [Drawing.Bitmap]::FromFile($tempPath)
    try { if ($saved.Width -ne $gridWidth -or $saved.Height -ne $gridHeight) { throw "Created thinking grid dimensions are not ${gridWidth}x${gridHeight}" } }
    finally { $saved.Dispose() }
    if ((Get-Sha256 $originalPath) -ne $originalBefore) { throw 'Source original changed during preparation' }
    if ((Get-Sha256 $upperPath) -ne $upperBefore) { throw 'Source upper half changed during preparation' }
    if ((Get-Sha256 $referencePath) -ne $referenceBefore) { throw 'Selected emotional reference changed during preparation' }
    if ($null -ne $halfSourcePath -and (Get-Sha256 $halfSourcePath) -ne $halfSourceBefore) { throw 'Selected emotional half-phase source changed during preparation' }
    if (Test-Path -LiteralPath $gridPath) { throw "Refusing to overwrite existing thinking reference grid: $gridPath" }
    Move-Item -LiteralPath $tempPath -Destination $gridPath
    "PREPARE_PORTRAIT_THINKING_PASS emotion=$emotion gazeLayout=$gazeLayout reference=$referenceName referenceSha256=$referenceBefore halfSource=$halfSourceName halfSourceSha256=$halfSourceBefore size=${gridWidth}x${gridHeight} sourceOriginalSha256=$originalBefore sourceUpperSha256=$upperBefore gridSha256=$(Get-Sha256 $gridPath)"
} catch {
    if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
    throw
} finally {
    if ($null -ne $grid) { $grid.Dispose() }
    $original.Dispose(); $upper.Dispose(); $reference.Dispose(); if ($null -ne $halfSource) { $halfSource.Dispose() }
}
