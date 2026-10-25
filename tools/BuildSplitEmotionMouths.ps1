[CmdletBinding()]
param([string]$PortraitRoot)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$defaultPortraitRoot = Join-Path $repoRoot 'art/portraits/mobile-mouths/split-mask-prototype'
if ([string]::IsNullOrWhiteSpace($PortraitRoot)) {
    $dataRoot = $defaultPortraitRoot
} elseif ([IO.Path]::IsPathRooted($PortraitRoot)) {
    $dataRoot = [IO.Path]::GetFullPath($PortraitRoot)
} else {
    $dataRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $PortraitRoot))
}
$mouthRoot = Join-Path $dataRoot 'mouth'
$layoutPath = Join-Path $dataRoot 'layout.json'
$originalPath = Join-Path $dataRoot 'original.png'
$shapeOrder = @('A','B','C','D','E','F','G','H','X')
$emotionOrder = @('happy','angry','sad','afraid','guarded')
$labels = [ordered]@{
    neutral = 'Neutral (flat reference)'
    happy = ('Fr'+[char]0x00f6+'hlich')
    angry = ('W'+[char]0x00fc+'tend')
    sad = 'Traurig'
    afraid = ([string][char]0x00c4+'ngstlich')
    guarded = 'Misstrauisch'
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-RepoRelativePath([string]$Path) {
    if ($Path.StartsWith($repoRoot,[StringComparison]::OrdinalIgnoreCase)) {
        return $Path.Substring($repoRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar,'/')
    }
    return $Path.Substring($dataRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar,'/')
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

$layout = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
$width = [int]$layout.width
$height = [int]$layout.height
$splitY = [int]$layout.splitY
if ($width -ne 64 -or $height -ne 64 -or $splitY -ne 35) { throw "Only native 64x64 portraits with splitY=35 are supported for now; layout declares ${width}x${height} splitY=$splitY" }
$lowerHeight = $height - $splitY
$original = [System.Drawing.Bitmap]::FromFile($originalPath)
$inputPaths = [Collections.Generic.List[string]]::new()
$sourceHashesBefore = @{}
$rawInputs = @{}
$neutralInputs = @{}
$allSets = [Collections.Generic.List[object]]::new()
$frameResults = [Collections.Generic.List[object]]::new()
$totalOutsideMaskChanges = 0
$totalFrames = 0
$totalDiffs = 0

try {
    if ($original.Width -ne $width -or $original.Height -ne $height) {
        throw "Original dimensions do not match layout: expected ${width}x${height}; got $($original.Width)x$($original.Height)"
    }
    $region = $layout.mouthRegion
    if ($null -eq $region -or $region.width -le 0 -or $region.height -le 0 -or
        $region.x -lt 0 -or $region.y -lt $splitY -or
        ($region.x + $region.width) -gt $width -or ($region.y + $region.height) -gt $height) {
        throw "Mouth mask must be positive and fit within the portrait's lower half: x=$($region.x) y=$($region.y) width=$($region.width) height=$($region.height); portrait=${width}x${height} splitY=$splitY"
    }
    $expectedInputAspect = $width / [double]$lowerHeight
    $referencePalette = $null
    if ($layout.useOriginalFeaturePalette) {
        . (Join-Path $PSScriptRoot 'PortraitReferencePalette.ps1')
        $referencePalette = New-OriginalFeaturePalette -Original $original -Regions @($region) -IncludeOralDetails
    }
    $tolerance = 0.02

    $inputPaths.Add($layoutPath)
    $inputPaths.Add($originalPath)

    foreach ($shape in $shapeOrder) {
        $path = Join-Path $mouthRoot ($shape + '.png')
        if (-not (Test-Path -LiteralPath $path)) { throw "Neutral flat mouth missing: $path" }
        $inputPaths.Add($path)
        $image = [System.Drawing.Bitmap]::FromFile($path)
        try {
            if ($image.Width -ne $width -or $image.Height -ne $lowerHeight) {
                throw "Neutral flat mouth must match lower-half dimensions ${width}x${lowerHeight}: $shape is $($image.Width)x$($image.Height)"
            }
            $neutralInputs[$shape] = [System.Drawing.Bitmap]::new($image)
        } finally { $image.Dispose() }
    }

    foreach ($emotion in $emotionOrder) {
        $rawPath = Join-Path $mouthRoot ($emotion + '/raw.png')
        $provenancePath = Join-Path $mouthRoot ($emotion + '/provenance.json')
        $selection = $null
        if ($layout.sourceSelections -and $layout.sourceSelections.mouths) {
            $emotionSelection = $layout.sourceSelections.mouths.$emotion
            if ($emotionSelection) {
                if ($emotionSelection -is [string]) { $selection = @{file=[string]$emotionSelection} }
                elseif ($emotionSelection.raw) { $selection = $emotionSelection.raw }
                elseif ($emotionSelection.default) { $selection = $emotionSelection.default }
                elseif ($emotionSelection.file) { $selection = $emotionSelection }
            }
        }
        if ($selection) {
            $selectedFile = if ($selection -is [string]) { [string]$selection } else { [string]$selection.file }
            if ([string]::IsNullOrWhiteSpace($selectedFile)) { throw "Invalid mouth source selection for $emotion" }
            $rawPath = Join-Path $dataRoot $selectedFile
            if ($selection -isnot [string] -and $selection.provenance) {
                $provenancePath = Join-Path $dataRoot ([string]$selection.provenance)
            } else {
                $sourceDirectory = Split-Path -Parent $rawPath
                $sourceName = [IO.Path]::GetFileNameWithoutExtension($rawPath)
                $inferredProvenance = if ($sourceName -eq 'raw') { 'provenance.json' } else { 'provenance-' + $sourceName.Substring(5) + '.json' }
                $provenancePath = Join-Path $sourceDirectory $inferredProvenance
            }
        } elseif ([int]$layout.portraitRecord -eq 77 -and $emotion -eq 'sad' -and (Test-Path -LiteralPath (Join-Path $mouthRoot 'sad/raw-v2.png'))) {
            $rawPath = Join-Path $mouthRoot 'sad/raw-v2.png'
            $provenancePath = Join-Path $mouthRoot 'sad/provenance-v2.json'
        }
        if (-not (Test-Path -LiteralPath $rawPath)) { throw "Emotion raw sheet missing: $rawPath" }
        if (-not (Test-Path -LiteralPath $provenancePath)) { throw "Emotion provenance missing: $provenancePath" }
        $inputPaths.Add($rawPath)
        $inputPaths.Add($provenancePath)
        $raw = [System.Drawing.Bitmap]::FromFile($rawPath)
        try {
            $ratio = $raw.Width / [double]$raw.Height
            if ([Math]::Abs(($ratio / $expectedInputAspect) - 1.0) -gt $tolerance) {
                throw "Mouth sheet aspect ratio differs by more than 2%: $emotion is $($raw.Width)x$($raw.Height)"
            }
            $rawInputs[$emotion] = [System.Drawing.Bitmap]::new($raw)
        } finally { $raw.Dispose() }
    }

    foreach ($path in $inputPaths) { $sourceHashesBefore[$path] = Get-Sha256 $path }

    # Closed neutral poses remain the exact original lower half.
    foreach ($shape in @('A','X')) {
        for ($y = 0; $y -lt $lowerHeight; $y++) {
            for ($x = 0; $x -lt $width; $x++) {
                if ($neutralInputs[$shape].GetPixel($x,$y).ToArgb() -ne $original.GetPixel($x,$y + $splitY).ToArgb()) {
                    throw "Neutral $shape must match original exactly at ($x,$y)"
                }
            }
        }
    }

    $setsToBuild = @(@{id='neutral';label=$labels.neutral;kind='flat'}) + @(
        foreach ($emotion in $emotionOrder) { @{id=$emotion;label=$labels[$emotion];kind='emotion'} }
    )

    foreach ($set in $setsToBuild) {
        $emotion = $set.id
        $setDirectory = Join-Path $mouthRoot $emotion
        if (-not (Test-Path -LiteralPath $setDirectory)) {
            New-Item -ItemType Directory -Path $setDirectory | Out-Null
        }
        $frames = [Collections.Generic.List[object]]::new()

        foreach ($shapeIndex in 0..8) {
            $shape = $shapeOrder[$shapeIndex]
            $processed = [System.Drawing.Bitmap]::new($width,$lowerHeight)
            $difference = [System.Drawing.Bitmap]::new($width,$lowerHeight)
            $candidate = $null
            $graphics = $null
            $raw = $null
            try {
                if ($set.kind -eq 'flat') {
                    for ($y = 0; $y -lt $lowerHeight; $y++) {
                        for ($x = 0; $x -lt $width; $x++) {
                            $processed.SetPixel($x,$y,$neutralInputs[$shape].GetPixel($x,$y))
                        }
                    }
                } else {
                    $raw = $rawInputs[$emotion]
                    $column = $shapeIndex % 3
                    $row = [Math]::Floor($shapeIndex / 3)
                    $sourceLeft = [int][Math]::Floor($column * $raw.Width / 3.0)
                    $sourceRight = [int][Math]::Floor(($column + 1) * $raw.Width / 3.0)
                    $sourceTop = [int][Math]::Floor($row * $raw.Height / 3.0)
                    $sourceBottom = [int][Math]::Floor(($row + 1) * $raw.Height / 3.0)
                    $sourceWidth = $sourceRight - $sourceLeft
                    $sourceHeight = $sourceBottom - $sourceTop
                    if ($sourceWidth -le 0 -or $sourceHeight -le 0) { throw "Invalid cell bounds: $emotion/$shape" }

                    # Scale the complete raw cell once to native lower-half size; never resize a mouth crop.
                    $candidate = [System.Drawing.Bitmap]::new($width,$lowerHeight)
                    $graphics = [System.Drawing.Graphics]::FromImage($candidate)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
                    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $graphics.DrawImage(
                        $raw,
                        [System.Drawing.Rectangle]::new(0,0,$width,$lowerHeight),
                        [single]$sourceLeft,
                        [single]$sourceTop,
                        [single]$sourceWidth,
                        [single]$sourceHeight,
                        [System.Drawing.GraphicsUnit]::Pixel
                    )

                    # Optional per-face skin references remove a generator's color cast.
                    # Restrict correction to this face's inspected skin-hue range;
                    # grayscale teeth and dark interiors normally fall outside it.
                    $skinPolicy = if ($layout.mouthSkinPalette) { $layout.mouthSkinPalette.$emotion } else { $null }
                    $skinAnchors = @()
                    if ($skinPolicy) {
                        foreach ($anchor in $skinPolicy.anchors) {
                            if ($anchor.x -lt 0 -or $anchor.x -ge $width -or $anchor.y -lt $splitY -or $anchor.y -ge $height) { throw 'Skin palette anchor outside lower half' }
                            $reference = $original.GetPixel([int]$anchor.x,[int]$anchor.y)
                            $generated = $candidate.GetPixel([int]$anchor.x,[int]$anchor.y-$splitY)
                            $skinAnchors += @{x=[int]$anchor.x;y=[int]$anchor.y;red=($reference.R-$generated.R);green=($reference.G-$generated.G);blue=($reference.B-$generated.B)}
                        }
                        if ($skinAnchors.Count -lt 3) { throw 'At least three inspected skin anchors required' }
                    }

                    for ($y = 0; $y -lt $lowerHeight; $y++) {
                        $fullY = $y + $splitY
                        for ($x = 0; $x -lt $width; $x++) {
                            $basePixel = $original.GetPixel($x,$fullY)
                            $pixel = $basePixel
                            if (Test-InMouthMask $x $fullY $region) {
                                $sample = $candidate.GetPixel($x,$y)
                                if ($skinPolicy -and $sample.GetHue() -ge $skinPolicy.minimumHue -and $sample.GetHue() -le $skinPolicy.maximumHue -and $sample.GetSaturation() -ge 0.25 -and $sample.GetBrightness() -gt 0.12) {
                                    $sumWeight = 0.0; $redShift = 0.0; $greenShift = 0.0; $blueShift = 0.0
                                    foreach ($anchor in $skinAnchors) {
                                        $weight = 1.0 / (1.0 + ($x-$anchor.x)*($x-$anchor.x) + ($fullY-$anchor.y)*($fullY-$anchor.y))
                                        $sumWeight += $weight
                                        $redShift += $weight*$anchor.red; $greenShift += $weight*$anchor.green; $blueShift += $weight*$anchor.blue
                                    }
                                    $sample = [Drawing.Color]::FromArgb(255,
                                        [int][Math]::Max(0,[Math]::Min(255,[Math]::Round($sample.R+$redShift/$sumWeight))),
                                        [int][Math]::Max(0,[Math]::Min(255,[Math]::Round($sample.G+$greenShift/$sumWeight))),
                                        [int][Math]::Max(0,[Math]::Min(255,[Math]::Round($sample.B+$blueShift/$sumWeight))))
                                }
                                if ((Get-MaskEdgeDistance $x $fullY $region) -eq 0) {
                                    $pixel = [System.Drawing.Color]::FromArgb(255,
                                        [int][Math]::Round(($basePixel.R + $sample.R) / 2.0),
                                        [int][Math]::Round(($basePixel.G + $sample.G) / 2.0),
                                        [int][Math]::Round(($basePixel.B + $sample.B) / 2.0))
                                } else {
                                    $pixel = [System.Drawing.Color]::FromArgb(255,$sample.R,$sample.G,$sample.B)
                                }
                            }
                            if ($referencePalette -and (Test-InMouthMask $x $fullY $region)) { $pixel = $referencePalette.Quantize($pixel) }
                            $processed.SetPixel($x,$y,$pixel)
                        }
                    }
                }

                $changed = 0
                $outside = 0
                for ($y = 0; $y -lt $lowerHeight; $y++) {
                    $fullY = $y + $splitY
                    for ($x = 0; $x -lt $width; $x++) {
                        $baseline = $original.GetPixel($x,$fullY)
                        $pixel = $processed.GetPixel($x,$y)
                        if ($pixel.ToArgb() -ne $baseline.ToArgb()) {
                            $changed++
                            if (-not (Test-InMouthMask $x $fullY $region)) { $outside++ }
                            $difference.SetPixel($x,$y,[System.Drawing.Color]::Magenta)
                        } else {
                            $difference.SetPixel($x,$y,[System.Drawing.Color]::Black)
                        }
                    }
                }
                if ($outside -ne 0) { throw "Changes outside mouth mask before save: $emotion/$shape ($outside pixels)" }

                $filePath = Join-Path $mouthRoot ($emotion + '/' + $shape + '.png')
                $diffPath = Join-Path $mouthRoot ($emotion + '/' + $shape + '-diff.png')
                $fileRelative = Get-RepoRelativePath $filePath
                $diffRelative = Get-RepoRelativePath $diffPath
                $processed.Save($filePath,[System.Drawing.Imaging.ImageFormat]::Png)
                $difference.Save($diffPath,[System.Drawing.Imaging.ImageFormat]::Png)

                # Independently reload saved files and verify exact mask invariants and diff counts.
                $saved = [System.Drawing.Bitmap]::FromFile($filePath)
                $savedDiff = [System.Drawing.Bitmap]::FromFile($diffPath)
                try {
                    if ($saved.Width -ne $width -or $saved.Height -ne $lowerHeight) { throw "Saved frame dimensions invalid: $emotion/$shape" }
                    if ($savedDiff.Width -ne $width -or $savedDiff.Height -ne $lowerHeight) { throw "Saved diff dimensions invalid: $emotion/$shape" }
                    $savedChanged = 0
                    $savedOutside = 0
                    $diffMismatch = 0
                    for ($y = 0; $y -lt $lowerHeight; $y++) {
                        $fullY = $y + $splitY
                        for ($x = 0; $x -lt $width; $x++) {
                            $baseline = $original.GetPixel($x,$fullY)
                            $pixel = $saved.GetPixel($x,$y)
                            $isChanged = $pixel.ToArgb() -ne $baseline.ToArgb()
                            if ($isChanged) {
                                $savedChanged++
                                if (-not (Test-InMouthMask $x $fullY $region)) { $savedOutside++ }
                            }
                            $expectedDiff = if ($isChanged) { [System.Drawing.Color]::Magenta.ToArgb() } else { [System.Drawing.Color]::Black.ToArgb() }
                            if ($savedDiff.GetPixel($x,$y).ToArgb() -ne $expectedDiff) { $diffMismatch++ }
                        }
                    }
                    if ($savedOutside -ne 0) { throw "Saved frame changes outside mouth mask: $emotion/$shape ($savedOutside pixels)" }
                    if ($savedChanged -ne $changed) { throw "Saved frame changed-pixel count mismatch: $emotion/$shape" }
                    if ($diffMismatch -ne 0) { throw "Saved diff does not match frame: $emotion/$shape" }
                    if ($set.kind -eq 'flat' -and $shape -in @('A','X') -and $savedChanged -ne 0) {
                        throw "Neutral closed mouth changed: $shape"
                    }

                    $totalOutsideMaskChanges += $savedOutside
                    $frames.Add([ordered]@{
                        shape = $shape
                        file = $fileRelative
                        difference = $diffRelative
                        changedPixels = $savedChanged
                        sha256 = Get-Sha256 $filePath
                    })
                    $frameResults.Add([ordered]@{emotion=$emotion;shape=$shape;changedPixels=$savedChanged;outsideMaskChangedPixels=$savedOutside;diffMismatchPixels=$diffMismatch})
                    $totalFrames++
                    $totalDiffs++
                } finally { $saved.Dispose(); $savedDiff.Dispose() }
            } finally {
                $processed.Dispose()
                $difference.Dispose()
                if ($graphics) { $graphics.Dispose() }
                if ($candidate) { $candidate.Dispose() }
            }
        }

        if ($frames.Count -ne 9) { throw "Frame count invalid for ${emotion}: $($frames.Count)" }
        $allSets.Add([ordered]@{id=$emotion;label=$set.label;frames=$frames.ToArray()})
    }

    $unchangedInputs = [Collections.Generic.List[object]]::new()
    $inputHashesValid = $true
    foreach ($path in $inputPaths) {
        $after = Get-Sha256 $path
        $unchanged = $after -eq $sourceHashesBefore[$path]
        if (-not $unchanged) { $inputHashesValid = $false }
        $unchangedInputs.Add([ordered]@{path=(Get-RepoRelativePath $path);sha256Before=$sourceHashesBefore[$path];sha256After=$after;unchanged=$unchanged})
    }
    if (-not $inputHashesValid) { throw 'One or more source inputs changed during the build' }
    if ($totalFrames -ne 54 -or $totalDiffs -ne 54) { throw "Expected 54 frames and diffs; got $totalFrames and $totalDiffs" }
    if ($totalOutsideMaskChanges -ne 0) { throw "Saved PNGs changed $totalOutsideMaskChanges pixels outside the mask" }

    $portraitRecord = if ($null -ne $layout.portraitRecord) { $layout.portraitRecord } else { 77 }
    $bank = if ($layout.bank) { $layout.bank } else { 'TFAC00I0.RCI' }
    $manifest = [ordered]@{
        schemaVersion = 1
        portraitRecord = $portraitRecord
        bank = $bank
        status = 'prototype-user-review-pending-not-installed'
        sets = $allSets.ToArray()
        verification = [ordered]@{
            result = 'pass'
            method = "System.Drawing; full raw 3x3 cells normalized to ${width}x${lowerHeight} with nearest-neighbor; mask and diffs rechecked from saved PNGs"
            frameSize = @{width=$width;height=$lowerHeight}
            shapes = $shapeOrder
            setCount = $allSets.Count
            expectedSetCount = 6
            frameCount = $totalFrames
            expectedFrameCount = 54
            diffCount = $totalDiffs
            outsideMaskChangedPixels = $totalOutsideMaskChanges
            mask = @{x=$region.x;y=$region.y;width=$region.width;height=$region.height;edgePixels=1;edgeBlend=0.5;coreOpacity=1.0}
            splitY = $splitY
            sourceHashesUnchanged = $inputHashesValid
            sourceHashChecks = $unchangedInputs.ToArray()
            frameChecks = $frameResults.ToArray()
            neutralAXPixelIdenticalToOriginal = $true
            emotionalAXGeneratedFromRaw = $true
            visualAcceptance = 'pending'
        }
    }
    $manifestPath = Join-Path $mouthRoot 'emotion-sets.json'
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    "SPLIT_EMOTION_MOUTH_BUILD_PASS sets=$($allSets.Count) frames=$totalFrames diffs=$totalDiffs outsideMaskPixels=$totalOutsideMaskChanges"
} finally {
    foreach ($emotion in $rawInputs.Keys) { $rawInputs[$emotion].Dispose() }
    foreach ($shape in $neutralInputs.Keys) { $neutralInputs[$shape].Dispose() }
    $original.Dispose()
}
