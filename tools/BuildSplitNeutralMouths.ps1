[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PortraitRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([IO.Path]::IsPathRooted($PortraitRoot)) {
    $dataRoot = [IO.Path]::GetFullPath($PortraitRoot)
} else {
    $dataRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $PortraitRoot))
}
$layoutPath = Join-Path $dataRoot 'layout.json'
$originalPath = Join-Path $dataRoot 'original.png'
$mouthRoot = Join-Path $dataRoot 'mouth'
$rawPath = Join-Path $mouthRoot 'neutral/raw.png'
$provenancePath = Join-Path $mouthRoot 'neutral/provenance.json'
$shapeOrder = @('A','B','C','D','E','F','G','H','X')

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-InMouthMask([int]$X, [int]$FullY, $Region) {
    return ($X -ge $Region.x -and $X -lt ($Region.x + $Region.width) -and
        $FullY -ge $Region.y -and $FullY -lt ($Region.y + $Region.height))
}

function Get-MaskEdgeDistance([int]$X, [int]$FullY, $Region) {
    $left = $X - $Region.x
    $right = ($Region.x + $Region.width - 1) - $X
    $top = $FullY - $Region.y
    $bottom = ($Region.y + $Region.height - 1) - $FullY
    return [Math]::Min([Math]::Min($left,$right),[Math]::Min($top,$bottom))
}

foreach ($path in @($layoutPath,$originalPath,$rawPath,$provenancePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required neutral-mouth input missing: $path" }
}

$layout = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
$width = [int]$layout.width
$height = [int]$layout.height
$splitY = [int]$layout.splitY
if ($width -ne 64 -or $height -ne 64 -or $splitY -ne 35) {
    throw "Only native 64x64 portraits with splitY=35 are supported for now; layout declares ${width}x${height} splitY=$splitY"
}
$lowerHeight = $height - $splitY
$region = $layout.mouthRegion
if ($null -eq $region -or $region.width -le 0 -or $region.height -le 0 -or
    $region.x -lt 0 -or $region.y -lt $splitY -or
    ($region.x + $region.width) -gt $width -or ($region.y + $region.height) -gt $height) {
    throw "Mouth mask must be positive and fit within the portrait's lower half: x=$($region.x) y=$($region.y) width=$($region.width) height=$($region.height); portrait=${width}x${height} splitY=$splitY"
}

$original = [Drawing.Bitmap]::FromFile($originalPath)
$raw = [Drawing.Bitmap]::FromFile($rawPath)
$sourceHashesBefore = @{
    layout = Get-Sha256 $layoutPath
    original = Get-Sha256 $originalPath
    raw = Get-Sha256 $rawPath
    provenance = Get-Sha256 $provenancePath
}
$frameChecks = [Collections.Generic.List[object]]::new()
$totalOutsideMaskChanges = 0
$totalFrames = 0

try {
    if ($original.Width -ne $width -or $original.Height -ne $height) {
        throw "Original dimensions do not match layout: expected ${width}x${height}; got $($original.Width)x$($original.Height)"
    }
    $referencePalette = $null
    if ($layout.useOriginalFeaturePalette) {
        . (Join-Path $PSScriptRoot 'PortraitReferencePalette.ps1')
        $referencePalette = New-OriginalFeaturePalette -Original $original -Regions @($region) -IncludeOralDetails
    }
    if ($raw.Width -lt 3 -or $raw.Height -lt 3) { throw "Neutral raw atlas must have a 3x3 grid; got $($raw.Width)x$($raw.Height)" }
    $expectedAspect = $width / [double]$lowerHeight
    $rawAspect = ($raw.Width / 3.0) / ($raw.Height / 3.0)
    if ([Math]::Abs(($rawAspect / $expectedAspect) - 1.0) -gt 0.02) {
        throw "Neutral raw 3x3 cell aspect mismatch: expected $expectedAspect; got $rawAspect"
    }

    $frameIndex = 0
    foreach ($shape in $shapeOrder) {
        $processed = [Drawing.Bitmap]::new($width,$lowerHeight)
        $candidate = $null
        $graphics = $null
        try {
            for ($y = 0; $y -lt $lowerHeight; $y++) {
                for ($x = 0; $x -lt $width; $x++) {
                    $processed.SetPixel($x,$y,$original.GetPixel($x,$y+$splitY))
                }
            }

            if ($shape -notin @('A','X')) {
                $column = $frameIndex % 3
                $row = [int][Math]::Floor($frameIndex / 3.0)
                $sourceLeft = [int][Math]::Floor($column * $raw.Width / 3.0)
                $sourceRight = [int][Math]::Floor(($column + 1) * $raw.Width / 3.0)
                $sourceTop = [int][Math]::Floor($row * $raw.Height / 3.0)
                $sourceBottom = [int][Math]::Floor(($row + 1) * $raw.Height / 3.0)
                $sourceWidth = $sourceRight - $sourceLeft
                $sourceHeight = $sourceBottom - $sourceTop
                if ($sourceWidth -le 0 -or $sourceHeight -le 0) { throw "Invalid source cell bounds: $shape" }

                $candidate = [Drawing.Bitmap]::new($width,$lowerHeight)
                $graphics = [Drawing.Graphics]::FromImage($candidate)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.DrawImage(
                    $raw,
                    [Drawing.Rectangle]::new(0,0,$width,$lowerHeight),
                    [single]$sourceLeft,
                    [single]$sourceTop,
                    [single]$sourceWidth,
                    [single]$sourceHeight,
                    [Drawing.GraphicsUnit]::Pixel
                )

                for ($y = 0; $y -lt $lowerHeight; $y++) {
                    $fullY = $y + $splitY
                    for ($x = 0; $x -lt $width; $x++) {
                        if (-not (Test-InMouthMask $x $fullY $region)) { continue }
                        $base = $original.GetPixel($x,$fullY)
                        $sample = $candidate.GetPixel($x,$y)
                        if ((Get-MaskEdgeDistance $x $fullY $region) -eq 0) {
                            $pixel = [Drawing.Color]::FromArgb(255,
                                [int][Math]::Round(($base.R + $sample.R) / 2.0),
                                [int][Math]::Round(($base.G + $sample.G) / 2.0),
                                [int][Math]::Round(($base.B + $sample.B) / 2.0))
                        } else {
                            $pixel = [Drawing.Color]::FromArgb(255,$sample.R,$sample.G,$sample.B)
                        }
                        if ($referencePalette) { $pixel = $referencePalette.Quantize($pixel) }
                        $processed.SetPixel($x,$y,$pixel)
                    }
                }
            }

            $changed = 0
            $outside = 0
            $closedMismatch = 0
            for ($y = 0; $y -lt $lowerHeight; $y++) {
                $fullY = $y + $splitY
                for ($x = 0; $x -lt $width; $x++) {
                    $base = $original.GetPixel($x,$fullY)
                    $pixel = $processed.GetPixel($x,$y)
                    if ($pixel.ToArgb() -ne $base.ToArgb()) {
                        $changed++
                        if (-not (Test-InMouthMask $x $fullY $region)) { $outside++ }
                    }
                    if ($shape -in @('A','X') -and $pixel.ToArgb() -ne $base.ToArgb()) { $closedMismatch++ }
                }
            }
            if ($outside -ne 0) { throw "Changed $outside pixels outside mouth mask in neutral $shape" }
            if ($closedMismatch -ne 0) { throw "Neutral closed frame $shape differs from original by $closedMismatch pixels" }

            $outputPath = Join-Path $mouthRoot ($shape + '.png')
            $processed.Save($outputPath,[Drawing.Imaging.ImageFormat]::Png)
            $saved = [Drawing.Bitmap]::FromFile($outputPath)
            try {
                if ($saved.Width -ne $width -or $saved.Height -ne $lowerHeight) { throw "Saved frame dimensions invalid: $shape" }
                $savedChanged = 0
                $savedOutside = 0
                $savedClosedMismatch = 0
                for ($y = 0; $y -lt $lowerHeight; $y++) {
                    $fullY = $y + $splitY
                    for ($x = 0; $x -lt $width; $x++) {
                        $base = $original.GetPixel($x,$fullY)
                        $pixel = $saved.GetPixel($x,$y)
                        if ($pixel.ToArgb() -ne $base.ToArgb()) {
                            $savedChanged++
                            if (-not (Test-InMouthMask $x $fullY $region)) { $savedOutside++ }
                        }
                        if ($shape -in @('A','X') -and $pixel.ToArgb() -ne $base.ToArgb()) { $savedClosedMismatch++ }
                    }
                }
                if ($savedOutside -ne 0 -or $savedClosedMismatch -ne 0) { throw "Saved neutral $shape violates mask or A/X invariants" }
                if ($savedChanged -ne $changed) { throw "Saved changed-pixel count mismatch: $shape" }
                $totalOutsideMaskChanges += $savedOutside
                $frameChecks.Add([ordered]@{
                    shape = $shape
                    file = 'mouth/' + $shape + '.png'
                    width = $width
                    height = $lowerHeight
                    changedPixels = $savedChanged
                    outsideMaskChangedPixels = $savedOutside
                    exactlyOriginalClosed = ($shape -in @('A','X') -and $savedClosedMismatch -eq 0)
                    sha256 = Get-Sha256 $outputPath
                })
                $totalFrames++
            } finally { $saved.Dispose() }
        } finally {
            $processed.Dispose()
            if ($graphics) { $graphics.Dispose() }
            if ($candidate) { $candidate.Dispose() }
        }
        $frameIndex++
    }

    $sourceHashesAfter = @{
        layout = Get-Sha256 $layoutPath
        original = Get-Sha256 $originalPath
        raw = Get-Sha256 $rawPath
        provenance = Get-Sha256 $provenancePath
    }
    foreach ($key in $sourceHashesBefore.Keys) {
        if ($sourceHashesBefore[$key] -ne $sourceHashesAfter[$key]) { throw "Source input changed during neutral-mouth build: $key" }
    }
    if ($totalFrames -ne 9 -or $totalOutsideMaskChanges -ne 0) { throw "Expected 9 verified frames with zero outside-mask changes; got frames=$totalFrames outside=$totalOutsideMaskChanges" }

    $portraitRecord = if ($null -ne $layout.portraitRecord) { $layout.portraitRecord } else { 77 }
    $bank = if ($layout.bank) { $layout.bank } else { 'TFAC00I0.RCI' }
    $manifest = [ordered]@{
        schemaVersion = 1
        portraitRecord = $portraitRecord
        bank = $bank
        status = 'neutral-flat-frames-generated-review-pending'
        original = 'original.png'
        layout = 'layout.json'
        rawAtlas = 'mouth/neutral/raw.png'
        provenance = 'mouth/neutral/provenance.json'
        rawSha256 = $sourceHashesBefore.raw
        provenanceSha256 = $sourceHashesBefore.provenance
        originalSha256 = $sourceHashesBefore.original
        layoutSha256 = $sourceHashesBefore.layout
        rawGrid = @{columns=3;rows=3;order=$shapeOrder}
        frameSize = @{width=$width;height=$lowerHeight}
        splitY = $splitY
        mask = @{x=$region.x;y=$region.y;width=$region.width;height=$region.height;edgePixels=1;edgeBlend=0.5;coreOpacity=1.0}
        nativeZeroOutsideMask = ($totalOutsideMaskChanges -eq 0)
        neutralAXPixelIdenticalToOriginal = $true
        sourceHashesUnchanged = $true
        frames = $frameChecks.ToArray()
    }
    $manifestPath = Join-Path $mouthRoot 'neutral/flat-mouth-manifest.json'
    $manifest | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    "SPLIT_NEUTRAL_MOUTH_BUILD_PASS record=$portraitRecord frames=$totalFrames outsideMaskPixels=$totalOutsideMaskChanges"
} finally {
    $raw.Dispose()
    $original.Dispose()
}
