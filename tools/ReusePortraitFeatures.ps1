[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$TargetRoot,
    [ValidateSet('FullFace','MouthOnly')][string]$Mode = 'MouthOnly'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$emotions = @('neutral','happy','angry','sad','afraid','guarded')
$shapes = @('A','B','C','D','E','F','G','H','X')
$phases = @('open','half','closed')
$sourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$targetRoot = [IO.Path]::GetFullPath($TargetRoot)

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required input missing: $Path" }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-SafePath([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)) { throw "Expected a relative path, got '$Relative'" }
    $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes workspace root: $Relative" }
    return $full
}

function Test-PixelInRegions([int]$X, [int]$Y, $Regions) {
    foreach ($r in $Regions) {
        if ($X -ge [int]$r.x -and $X -lt ([int]$r.x+[int]$r.width) -and
            $Y -ge [int]$r.y -and $Y -lt ([int]$r.y+[int]$r.height)) { return $true }
    }
    return $false
}

function Assert-Region($Region, [string]$Name) {
    if ($null -eq $Region -or [int]$Region.width -le 0 -or [int]$Region.height -le 0 -or
        [int]$Region.x -lt 0 -or [int]$Region.y -lt 0 -or
        ([int]$Region.x+[int]$Region.width) -gt 64 -or ([int]$Region.y+[int]$Region.height) -gt 64) {
        throw "Invalid $Name region in layout"
    }
}

function Assert-OriginalMaskPixels([Drawing.Bitmap]$Source, [Drawing.Bitmap]$Target, $Regions) {
    for ($y=0; $y -lt 64; $y++) {
        for ($x=0; $x -lt 64; $x++) {
            if (Test-PixelInRegions $x $y $Regions) {
                if ($Source.GetPixel($x,$y).ToArgb() -ne $Target.GetPixel($x,$y).ToArgb()) {
                    throw "Original pixel mismatch inside requested feature mask at ($x,$y); no outputs written"
                }
            }
        }
    }
}

function Assert-FrameOutsideMask([Drawing.Bitmap]$Frame, [Drawing.Bitmap]$SourceOriginal, [int]$SplitOffset, [int]$FrameHeight, $Regions, [string]$Name) {
    if ($Frame.Width -ne 64 -or $Frame.Height -ne $FrameHeight) { throw "$Name must be 64x$FrameHeight; got $($Frame.Width)x$($Frame.Height)" }
    for ($y=0; $y -lt $FrameHeight; $y++) {
        for ($x=0; $x -lt 64; $x++) {
            $fullY = $y + $SplitOffset
            if (-not (Test-PixelInRegions $x $fullY $Regions) -and
                $Frame.GetPixel($x,$y).ToArgb() -ne $SourceOriginal.GetPixel($x,$fullY).ToArgb()) {
                throw "$Name has a changed pixel outside its feature mask at ($x,$y)"
            }
        }
    }
}

function New-ComposedFrame([Drawing.Bitmap]$SourceFrame, [Drawing.Bitmap]$TargetOriginal, [int]$SplitOffset, [int]$FrameHeight, $Regions, [bool]$CopyFeature) {
    $result = [Drawing.Bitmap]::new(64,$FrameHeight,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y=0; $y -lt $FrameHeight; $y++) {
        for ($x=0; $x -lt 64; $x++) {
            $fullY = $y + $SplitOffset
            $pixel = $TargetOriginal.GetPixel($x,$fullY)
            if ($CopyFeature -and (Test-PixelInRegions $x $fullY $Regions)) { $pixel = $SourceFrame.GetPixel($x,$y) }
            $result.SetPixel($x,$y,$pixel)
        }
    }
    return $result
}

function Save-Frame([Drawing.Bitmap]$Frame, [string]$Path) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $Frame.Save($Path,[Drawing.Imaging.ImageFormat]::Png)
}

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container) -or -not (Test-Path -LiteralPath $targetRoot -PathType Container)) { throw 'SourceRoot and TargetRoot must be existing directories' }
if ($sourceRoot -eq $targetRoot) { throw 'SourceRoot and TargetRoot must differ' }
$sourceLayoutPath = Join-Path $sourceRoot 'layout.json'
$targetLayoutPath = Join-Path $targetRoot 'layout.json'
$sourceOriginalPath = Join-Path $sourceRoot 'original.png'
$targetOriginalPath = Join-Path $targetRoot 'original.png'
$sourceManifestPath = Join-Path $sourceRoot 'manifest.json'
$sourceLayout = Read-Json $sourceLayoutPath
$targetLayout = Read-Json $targetLayoutPath
$sourceManifest = Read-Json $sourceManifestPath
foreach ($pair in @(@{layout=$sourceLayout;name='source'},@{layout=$targetLayout;name='target'})) {
    if ([int]$pair.layout.width -ne 64 -or [int]$pair.layout.height -ne 64 -or [int]$pair.layout.splitY -ne 35) { throw "$($pair.name) must be a native 64x64 portrait with splitY=35" }
    Assert-Region $pair.layout.mouthRegion "$($pair.name) mouth"
    if (@($pair.layout.eyeRegions).Count -ne 2) { throw "$($pair.name) layout must have two eye regions" }
    foreach ($region in $pair.layout.eyeRegions) { Assert-Region $region "$($pair.name) eye" }
}
if ([int]$sourceLayout.portraitRecord -eq [int]$targetLayout.portraitRecord -and [string]$sourceLayout.bank -eq [string]$targetLayout.bank) {
    # Same identity is allowed only when the caller chose a separate, fresh target workspace.
}
$sourceOriginal = [Drawing.Bitmap]::FromFile($sourceOriginalPath)
$targetOriginal = [Drawing.Bitmap]::FromFile($targetOriginalPath)
$manifestHash = Get-Hash $sourceManifestPath
$sourceOriginalHash = Get-Hash $sourceOriginalPath
$targetOriginalHash = Get-Hash $targetOriginalPath
$stageRoot = Get-SafePath $targetRoot ('.reuse-stage-' + [Guid]::NewGuid().ToString('N'))
$createdDestinations = [Collections.Generic.List[string]]::new()
$sourceFrameHashes = [Collections.Generic.List[object]]::new()
$frameProofs = [Collections.Generic.List[object]]::new()
$allOutputPaths = [Collections.Generic.List[string]]::new()
$eyeSetsOut = [Collections.Generic.List[object]]::new()
$mouthSetsOut = [Collections.Generic.List[object]]::new()
$sourceEyeSets = @{}
$sourceMouthSets = @{}
foreach ($set in $sourceManifest.eyeSets) { $sourceEyeSets[[string]$set.id] = $set }
foreach ($set in $sourceManifest.mouthSets) { $sourceMouthSets[[string]$set.id] = $set }

try {
    if ($sourceOriginal.Width -ne 64 -or $sourceOriginal.Height -ne 64 -or $targetOriginal.Width -ne 64 -or $targetOriginal.Height -ne 64) { throw 'Both originals must be exactly 64x64' }
    $mouthRegionsSource = @($sourceLayout.mouthRegion)
    $mouthRegionsTarget = @($targetLayout.mouthRegion)
    $mouthRegions = $mouthRegionsSource
    if ((ConvertTo-Json -InputObject $sourceLayout.mouthRegion -Compress) -cne (ConvertTo-Json -InputObject $targetLayout.mouthRegion -Compress)) { throw 'Source and target mouth mask geometry differs' }
    Assert-OriginalMaskPixels $sourceOriginal $targetOriginal $mouthRegions

    if ($Mode -eq 'FullFace') {
        if ((ConvertTo-Json -InputObject @($sourceLayout.eyeRegions) -Compress) -cne (ConvertTo-Json -InputObject @($targetLayout.eyeRegions) -Compress)) { throw 'Source and target eye mask geometry differs' }
        Assert-OriginalMaskPixels $sourceOriginal $targetOriginal @($sourceLayout.eyeRegions)
    }

    foreach ($emotion in $emotions) {
        if (-not $sourceMouthSets.ContainsKey($emotion)) { throw "Source manifest lacks mouth set '$emotion'" }
        $frameByShape = @{}
        foreach ($frame in $sourceMouthSets[$emotion].frames) { $frameByShape[[string]$frame.shape] = $frame }
        foreach ($shape in $shapes) { if (-not $frameByShape.ContainsKey($shape)) { throw "Source manifest lacks $emotion/$shape" } }
        $outFrames = [Collections.Generic.List[object]]::new()
        foreach ($shape in $shapes) {
            $src = Get-SafePath $sourceRoot ([string]$frameByShape[$shape].file)
            if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { throw "Source mouth frame missing: $src" }
            $srcBmp = [Drawing.Bitmap]::FromFile($src)
            try {
                Assert-FrameOutsideMask $srcBmp $sourceOriginal 35 29 $mouthRegions "$emotion/$shape"
                $srcHash = Get-Hash $src
                $sourceFrameHashes.Add(@{path=$frameByShape[$shape].file;sha256=$srcHash})
                $copyFeature = -not ($emotion -eq 'neutral' -and $shape -in @('A','X'))
                $composed = New-ComposedFrame $srcBmp $targetOriginal 35 29 $mouthRegions $copyFeature
                try {
                    $relative = "mouth/$emotion/$shape.png"
                    $staged = Get-SafePath $stageRoot $relative
                    Save-Frame $composed $staged
                    $allOutputPaths.Add($relative)
                    $changed = 0
                    for ($y=0; $y -lt 29; $y++) { for ($x=0; $x -lt 64; $x++) {
                        if ($composed.GetPixel($x,$y).ToArgb() -ne $targetOriginal.GetPixel($x,$y+35).ToArgb()) { $changed++ }
                    } }
                    $diff = [Drawing.Bitmap]::new(64,29)
                    try {
                        for ($y=0; $y -lt 29; $y++) { for ($x=0; $x -lt 64; $x++) {
                            $pixelChanged = $composed.GetPixel($x,$y).ToArgb() -ne $targetOriginal.GetPixel($x,$y+35).ToArgb()
                            $diff.SetPixel($x,$y,($(if ($pixelChanged) { [Drawing.Color]::Magenta } else { [Drawing.Color]::Black })))
                        } }
                        $diffRelative = "mouth/$emotion/$shape-diff.png"
                        Save-Frame $diff (Get-SafePath $stageRoot $diffRelative)
                        $allOutputPaths.Add($diffRelative)
                    } finally { $diff.Dispose() }
                    $outFrames.Add([ordered]@{shape=$shape;file=$relative;difference="mouth/$emotion/$shape-diff.png";changedPixels=$changed;sha256=(Get-Hash $staged)})
                    $frameProofs.Add(@{feature='mouth';emotion=$emotion;shape=$shape;sourceSha256=$srcHash;targetSha256=(Get-Hash $staged);outsideMaskChangedPixels=0})
                } finally { $composed.Dispose() }
            } finally { $srcBmp.Dispose() }
        }
        $mouthSetsOut.Add([ordered]@{id=$emotion;label=if ($sourceMouthSets[$emotion].label) {$sourceMouthSets[$emotion].label} else {$emotion};frames=$outFrames.ToArray()})
    }

    # Builder-required flat neutral set, separate from the nested neutral emotion set.
    foreach ($shape in $shapes) {
        $src = Get-SafePath $sourceRoot "mouth/$shape.png"
        if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { throw "Source flat-neutral mouth missing: $src" }
        $srcBmp = [Drawing.Bitmap]::FromFile($src)
        try {
            Assert-FrameOutsideMask $srcBmp $sourceOriginal 35 29 $mouthRegions "flat-neutral/$shape"
            $srcHash = Get-Hash $src
            $sourceFrameHashes.Add(@{path="mouth/$shape.png";sha256=$srcHash})
            $copyFeature = $shape -notin @('A','X')
            $composed = New-ComposedFrame $srcBmp $targetOriginal 35 29 $mouthRegions $copyFeature
            try {
                $relative = "mouth/$shape.png"
                $staged = Get-SafePath $stageRoot $relative
                Save-Frame $composed $staged
                $allOutputPaths.Add($relative)
                if ($shape -in @('A','X')) {
                    for ($y=0; $y -lt 29; $y++) { for ($x=0; $x -lt 64; $x++) {
                        if ($composed.GetPixel($x,$y).ToArgb() -ne $targetOriginal.GetPixel($x,$y+35).ToArgb()) { throw "Flat neutral $shape is not exact target original" }
                    } }
                }
                $frameProofs.Add(@{feature='mouth';emotion='neutral-flat';shape=$shape;sourceSha256=$srcHash;targetSha256=(Get-Hash $staged);outsideMaskChangedPixels=0;neutralAXTargetExact=($shape -in @('A','X'))})
            } finally { $composed.Dispose() }
        } finally { $srcBmp.Dispose() }
    }

    if ($Mode -eq 'FullFace') {
        foreach ($emotion in $emotions) {
            if (-not $sourceEyeSets.ContainsKey($emotion)) { throw "Source manifest lacks eye set '$emotion'" }
            $phasesIn = $sourceEyeSets[$emotion].phases
            $phaseOut = [ordered]@{}
            foreach ($phase in $phases) {
                if (-not $phasesIn.$phase) { throw "Source manifest lacks $emotion/$phase eye phase" }
                $src = Get-SafePath $sourceRoot ([string]$phasesIn.$phase.file)
                if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { throw "Source eye frame missing: $src" }
                $srcBmp = [Drawing.Bitmap]::FromFile($src)
                try {
                    Assert-FrameOutsideMask $srcBmp $sourceOriginal 0 35 @($sourceLayout.eyeRegions) "$emotion/$phase"
                    $srcHash = Get-Hash $src
                    $sourceFrameHashes.Add(@{path=$phasesIn.$phase.file;sha256=$srcHash})
                    $composed = New-ComposedFrame $srcBmp $targetOriginal 0 35 @($targetLayout.eyeRegions) $true
                    try {
                        $relative = "eyes/$emotion/$phase.png"
                        $staged = Get-SafePath $stageRoot $relative
                        Save-Frame $composed $staged
                        $allOutputPaths.Add($relative)
                        $phaseOut[$phase] = @{file=$relative;sha256=(Get-Hash $staged)}
                        $frameProofs.Add(@{feature='eyes';emotion=$emotion;phase=$phase;sourceSha256=$srcHash;targetSha256=(Get-Hash $staged);outsideMaskChangedPixels=0})
                    } finally { $composed.Dispose() }
                } finally { $srcBmp.Dispose() }
            }
            $eyeSetsOut.Add([ordered]@{id=$emotion;label=if ($sourceEyeSets[$emotion].label) {$sourceEyeSets[$emotion].label} else {$emotion};phases=$phaseOut})
        }
    }

    $mouthManifest = [ordered]@{
        schemaVersion=1
        portraitRecord=$targetLayout.portraitRecord
        bank=$targetLayout.bank
        status='feature-reuse-review-pending-not-installed'
        sets=$mouthSetsOut.ToArray()
        verification=[ordered]@{
            result='pass';method='Exact original-pixel mask comparison, source-frame outside-mask verification, and target-original composition'
            frameSize=@{width=64;height=29};splitY=35;setCount=6;frameCount=54;flatNeutralFrameCount=9
            mask=@{x=$targetLayout.mouthRegion.x;y=$targetLayout.mouthRegion.y;width=$targetLayout.mouthRegion.width;height=$targetLayout.mouthRegion.height}
            sourceOriginalSha256=$sourceOriginalHash;targetOriginalSha256=$targetOriginalHash;sourceManifestSha256=$manifestHash
            sourceLayoutSha256=(Get-Hash $sourceLayoutPath);targetLayoutSha256=(Get-Hash $targetLayoutPath)
            sourceFrameHashes=$sourceFrameHashes.ToArray();frameProofs=$frameProofs.ToArray()
            featurePalette='Source feature colors reused inside matched mask; target colors retained everywhere else. Mask equality does not assert whole-portrait palette equality.'
            neutralAXPixelIdenticalToTargetOriginal=$true;outsideMaskChangedPixels=0
        }
    }
    $mouthManifestPath = Get-SafePath $stageRoot 'mouth/emotion-sets.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $mouthManifestPath) -Force | Out-Null
    $mouthManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $mouthManifestPath -Encoding UTF8
    $allOutputPaths.Add('mouth/emotion-sets.json')

    if ($Mode -eq 'FullFace') {
        $reuseManifest = [ordered]@{
            schemaVersion=1;mode=$Mode;portraitRecord=$targetLayout.portraitRecord;bank=$targetLayout.bank
            source=@{portraitRecord=$sourceLayout.portraitRecord;bank=$sourceLayout.bank;originalSha256=$sourceOriginalHash;manifestSha256=$manifestHash}
            target=@{originalSha256=$targetOriginalHash;layoutSha256=(Get-Hash $targetLayoutPath)}
            eyeSets=$eyeSetsOut.ToArray();mouthManifest='mouth/emotion-sets.json'
            verification=@{result='pass';frameCount=18;featureMaskOriginalPixelsMatch=$true;palettePolicy='Source feature pixels reused only inside masks; target source background/hood remains exact'}
        }
        $reusePath = Get-SafePath $stageRoot 'reuse-manifest.json'
        $reuseManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reusePath -Encoding UTF8
        $allOutputPaths.Add('reuse-manifest.json')
    }

    foreach ($relative in $allOutputPaths) {
        $dest = Get-SafePath $targetRoot $relative
        if (Test-Path -LiteralPath $dest) { throw "Refusing to overwrite existing target output: $dest" }
    }
    foreach ($relative in $allOutputPaths) {
        $source = Get-SafePath $stageRoot $relative
        $dest = Get-SafePath $targetRoot $relative
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $destDir -PathType Container)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
        Move-Item -LiteralPath $source -Destination $dest
        $createdDestinations.Add($dest)
    }
    "REUSE_FEATURES_PASS mode=$Mode mouthFrames=63 eyeFrames=$($(if ($Mode -eq 'FullFace') {18} else {0})) record=$($targetLayout.portraitRecord)"
} catch {
    foreach ($path in $createdDestinations) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
    throw
} finally {
    if ($sourceOriginal) { $sourceOriginal.Dispose() }
    if ($targetOriginal) { $targetOriginal.Dispose() }
    if (Test-Path -LiteralPath $stageRoot) {
        $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
        $resolvedTarget = [IO.Path]::GetFullPath($targetRoot).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
        $targetPrefix = $resolvedTarget + [IO.Path]::DirectorySeparatorChar
        if ($resolvedStage.StartsWith($targetPrefix,[StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedStage).StartsWith('.reuse-stage-',[StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
