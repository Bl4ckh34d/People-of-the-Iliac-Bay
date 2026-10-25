[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PortraitRoot,
    [Parameter(Mandatory)][string]$GeneratedPath,
    [Parameter(Mandatory)][string]$RequestJsonPath,
    [Parameter(Mandatory)][string]$ArgumentsJsonPath
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5) { throw 'SavePortraitThinkingRawOutput.ps1 requires Windows PowerShell 5' }
Add-Type -AssemblyName System.Drawing
$allowedEmotions = @('neutral','happy','angry','sad','afraid','guarded')
$maximumPngBytes = 16777216

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-PngInfo([string]$Path,[string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label file missing: $Path" }
    if ([IO.Path]::GetExtension($Path) -ine '.png') { throw "$Label must use .png extension: $Path" }
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -lt 8 -or $file.Length -gt $maximumPngBytes) { throw "$Label PNG size must be between 8 bytes and 16 MiB: $Path" }
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 8) { throw "$Label is not a readable PNG: $Path" }
        $signature = New-Object byte[] 8
        $read = $stream.Read($signature,0,8)
        $expected = [byte[]](137,80,78,71,13,10,26,10)
        if ($read -ne 8) { throw "$Label is not a readable PNG: $Path" }
        for ($i = 0; $i -lt 8; $i++) { if ($signature[$i] -ne $expected[$i]) { throw "$Label is not a PNG: $Path" } }
    } finally { $stream.Dispose() }
    $bitmap = [Drawing.Bitmap]::FromFile($Path)
    try {
        return [ordered]@{width=$bitmap.Width;height=$bitmap.Height}
    } finally { $bitmap.Dispose() }
}

function Assert-WithinRoot([string]$Path,[string]$Root,[string]$Label) {
    $full = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) { throw "$Label escapes PortraitRoot: $full" }
    return $full
}

function Get-SafeSourcePath([string]$RelativePath,[string]$Root,[string]$Label) {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { throw "$Label must be a nonempty portrait-root-relative path" }
    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains(':')) { throw "$Label must be portrait-root-relative: $RelativePath" }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $base $RelativePath))
    $prefix = $base + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "$Label escapes PortraitRoot: $RelativePath" }
    if ([IO.Path]::GetExtension($full) -ine '.png') { throw "$Label must reference a PNG: $RelativePath" }
    $current = $base
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

function Test-InAnyEyeRegion([int]$X,[int]$Y,$Regions) {
    foreach ($region in $Regions) {
        if ($X -ge [int]$region.x -and $X -lt ([int]$region.x + [int]$region.width) -and
            $Y -ge [int]$region.y -and $Y -lt ([int]$region.y + [int]$region.height)) { return $true }
    }
    return $false
}

function Assert-GridMatchesSource([string]$GridPath,[string]$SourcePath,[string]$GazeLayout = 'legacy-3x3',[string]$HalfSourcePath = $null) {
    $grid = [Drawing.Bitmap]::FromFile($GridPath)
    $source = [Drawing.Bitmap]::FromFile($SourcePath)
    $halfSource = if ($GazeLayout -eq 'horizontal-pair') { [Drawing.Bitmap]::FromFile($HalfSourcePath) } else { $null }
    try {
        $expectedWidth = if ($GazeLayout -eq 'horizontal-pair') { 1024 } else { 1536 }
        $expectedHeight = if ($GazeLayout -eq 'horizontal-pair') { 560 } else { 840 }
        if ($grid.Width -ne $expectedWidth -or $grid.Height -ne $expectedHeight) { throw "Thinking reference grid must be ${expectedWidth}x${expectedHeight}" }
        for ($y=0; $y -lt $expectedHeight; $y++) {
            $sourceY = [int][Math]::Floor(($y % 280) / 8)
            $phaseSource = if ($GazeLayout -eq 'horizontal-pair' -and $y -ge 280) { $halfSource } else { $source }
            $phaseLabel = if ($GazeLayout -eq 'horizontal-pair' -and $y -ge 280) { 'selected half-phase source' } else { 'selected emotional source' }
            for ($x=0; $x -lt $expectedWidth; $x++) {
                $sourceX = [int][Math]::Floor(($x % 512) / 8)
                if ($grid.GetPixel($x,$y).ToArgb() -ne $phaseSource.GetPixel($sourceX,$sourceY).ToArgb()) {
                    throw "Thinking reference grid differs from $phaseLabel at ($x,$y)"
                }
            }
        }
    } finally { $grid.Dispose(); $source.Dispose(); if ($null -ne $halfSource) { $halfSource.Dispose() } }
}

function Assert-Aspect([hashtable]$Info,[double]$Expected,[string]$Label) {
    $actual = [double]$Info.width / [double]$Info.height
    if ([Math]::Abs(($actual / $Expected) - 1.0) -gt 0.01) { throw "$Label aspect ratio must be within 1% of $Expected (actual $actual)" }
}

$root = [IO.Path]::GetFullPath($PortraitRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "PortraitRoot must exist: $root" }
$originalPath = Join-Path $root 'original.png'
$upperPath = Join-Path $root 'source-upper.png'
$actualReferencePath = Join-Path $root 'thinking-reference-grid.png'
$layoutPath = Join-Path $root 'layout.json'
$thinkingLayoutPath = Join-Path $root 'thinking-layout.json'
foreach ($required in @($originalPath,$upperPath,$actualReferencePath,$layoutPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required thinking workspace file missing: $required" }
}

$originalInfo = Get-PngInfo $originalPath 'Source original'
if ($originalInfo.width -ne 64 -or $originalInfo.height -ne 64) { throw 'Source original must be native 64x64' }
$upperInfo = Get-PngInfo $upperPath 'Source upper half'
if ($upperInfo.width -ne 64 -or $upperInfo.height -ne 35) { throw 'Source upper half must be 64x35' }
$referenceInfo = Get-PngInfo $actualReferencePath 'Thinking reference grid'
$layout = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([int]$layout.width -ne 64 -or [int]$layout.height -ne 64 -or [int]$layout.splitY -ne 35) { throw 'Portrait layout must declare 64x64 and splitY=35' }
if ($null -eq $layout.portraitRecord -or [string]::IsNullOrWhiteSpace([string]$layout.bank)) { throw 'Portrait layout must declare portraitRecord and bank' }
if (@($layout.eyeRegions).Count -ne 2) { throw 'Portrait layout must declare two eye regions' }
foreach ($region in $layout.eyeRegions) {
    if ([int]$region.width -le 0 -or [int]$region.height -le 0 -or [int]$region.x -lt 0 -or [int]$region.y -lt 0 -or ([int]$region.x+[int]$region.width) -gt 64 -or ([int]$region.y+[int]$region.height) -gt 64) { throw 'Portrait layout eyeRegions must be positive rectangles within 64x64' }
}
$thinkingLayout = $null
if (Test-Path -LiteralPath $thinkingLayoutPath -PathType Leaf) { $thinkingLayout = Get-Content -LiteralPath $thinkingLayoutPath -Raw -Encoding UTF8 | ConvertFrom-Json }
$emotion = 'neutral'
$referenceSourceName = 'source-upper.png'
$gazeLayout = 'legacy-3x3'
$hasExplicitEmotion = $false
if ($null -ne $thinkingLayout) {
    if ($null -ne $thinkingLayout.PSObject.Properties['emotionId']) { $emotion = [string]$thinkingLayout.emotionId; $hasExplicitEmotion = $true }
    if ($null -ne $thinkingLayout.PSObject.Properties['referenceUpperFile']) { $referenceSourceName = [string]$thinkingLayout.referenceUpperFile }
    if ($null -ne $thinkingLayout.PSObject.Properties['gazeLayout']) {
        $gazeLayout = [string]$thinkingLayout.gazeLayout
        if ($gazeLayout -cne 'horizontal-pair') { throw 'thinking-layout gazeLayout must be horizontal-pair when specified' }
    }
}
if ($allowedEmotions -cnotcontains $emotion) { throw "thinking-layout emotionId must be one of: $($allowedEmotions -join ', ')" }
if ($gazeLayout -eq 'horizontal-pair' -and -not $hasExplicitEmotion) { throw 'horizontal-pair gazeLayout requires an explicit emotionId' }
$expectedGridWidth = if ($gazeLayout -eq 'horizontal-pair') { 1024 } else { 1536 }
$expectedGridHeight = if ($gazeLayout -eq 'horizontal-pair') { 560 } else { 840 }
if ($referenceInfo.width -ne $expectedGridWidth -or $referenceInfo.height -ne $expectedGridHeight) { throw "Thinking reference grid must be ${expectedGridWidth}x${expectedGridHeight}" }
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
$referenceSourcePath = Get-SafeSourcePath $referenceSourceName $root 'referenceUpperFile'
$referenceSourceHash = Get-Sha256 $referenceSourcePath
$referenceSourceInfo = Get-PngInfo $referenceSourcePath 'Selected emotional reference upper half'
if ($referenceSourceInfo.width -ne 64 -or $referenceSourceInfo.height -ne 35) { throw 'Selected emotional reference upper half must be 64x35' }
$halfSourceName = $null
$halfSourcePath = $null
$halfSourceHash = $null
$halfSourceInfo = $null
if ($gazeLayout -eq 'horizontal-pair') {
    if ($null -eq $thinkingLayout.PSObject.Properties['halfSourceFile']) { throw 'horizontal-pair gazeLayout requires halfSourceFile' }
    $halfSourceName = [string]$thinkingLayout.halfSourceFile
    $expectedHalfSource = "eyes\$emotion\half.png"
    $expectedHalfFull = [IO.Path]::GetFullPath((Join-Path $root $expectedHalfSource))
    $actualHalfFull = [IO.Path]::GetFullPath((Join-Path $root $halfSourceName))
    if (-not [string]::Equals($actualHalfFull,$expectedHalfFull,[StringComparison]::OrdinalIgnoreCase)) { throw "halfSourceFile must select $expectedHalfSource for emotion $emotion" }
    $halfSourcePath = Get-SafeSourcePath $halfSourceName $root 'halfSourceFile'
    $halfSourceHash = Get-Sha256 $halfSourcePath
    $halfSourceInfo = Get-PngInfo $halfSourcePath 'Selected emotional half-phase source'
    if ($halfSourceInfo.width -ne 64 -or $halfSourceInfo.height -ne 35) { throw 'Selected emotional half-phase source must be 64x35' }
}
if ($null -ne $thinkingLayout) {
    foreach ($sourceField in @(@{property='browSourceFile';label='browSourceFile'},@{property='halfSourceFile';label='halfSourceFile'},@{property='closedSourceFile';label='closedSourceFile'})) {
        $property = $thinkingLayout.PSObject.Properties[$sourceField.property]
        if ($null -ne $property) {
            $sourcePath = Get-SafeSourcePath ([string]$property.Value) $root $sourceField.label
            $sourceInfo = Get-PngInfo $sourcePath $sourceField.label
            if ($sourceInfo.width -ne 64 -or $sourceInfo.height -ne 35) { throw "$($sourceField.label) must be 64x35" }
        }
    }
}
$originalBitmap = [Drawing.Bitmap]::FromFile($originalPath)
$upperBitmap = [Drawing.Bitmap]::FromFile($upperPath)
$sourceBitmap = [Drawing.Bitmap]::FromFile($referenceSourcePath)
$halfSourceBitmap = if ($null -ne $halfSourcePath) { [Drawing.Bitmap]::FromFile($halfSourcePath) } else { $null }
try {
    for ($y=0; $y -lt 35; $y++) { for ($x=0; $x -lt 64; $x++) {
        if ($upperBitmap.GetPixel($x,$y).ToArgb() -ne $originalBitmap.GetPixel($x,$y).ToArgb()) { throw "Source upper half differs from original at ($x,$y)" }
        if (-not (Test-InAnyEyeRegion $x $y $layout.eyeRegions) -and $sourceBitmap.GetPixel($x,$y).ToArgb() -ne $originalBitmap.GetPixel($x,$y).ToArgb()) {
            throw "Selected emotional reference differs from original outside eye masks at ($x,$y)"
        }
        if ($null -ne $halfSourceBitmap -and -not (Test-InAnyEyeRegion $x $y $layout.eyeRegions) -and $halfSourceBitmap.GetPixel($x,$y).ToArgb() -ne $originalBitmap.GetPixel($x,$y).ToArgb()) {
            throw "Selected emotional half-phase source differs from original outside eye masks at ($x,$y)"
        }
    } }
} finally { $originalBitmap.Dispose(); $upperBitmap.Dispose(); $sourceBitmap.Dispose(); if ($null -ne $halfSourceBitmap) { $halfSourceBitmap.Dispose() } }
Assert-GridMatchesSource $actualReferencePath $referenceSourcePath $gazeLayout $halfSourcePath
if ((Get-Sha256 $referenceSourcePath) -ne $referenceSourceHash) { throw 'Selected emotional reference changed during validation' }
if ($null -ne $halfSourcePath -and (Get-Sha256 $halfSourcePath) -ne $halfSourceHash) { throw 'Selected emotional half-phase source changed during validation' }

if (-not (Test-Path -LiteralPath $RequestJsonPath -PathType Leaf)) { throw "Request JSON missing: $RequestJsonPath" }
if (-not (Test-Path -LiteralPath $ArgumentsJsonPath -PathType Leaf)) { throw "Arguments JSON missing: $ArgumentsJsonPath" }
$requestSourceHash = Get-Sha256 $RequestJsonPath
$argumentsSourceHash = Get-Sha256 $ArgumentsJsonPath
$request = Get-Content -LiteralPath $RequestJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$arguments = Get-Content -LiteralPath $ArgumentsJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($request.prompt -isnot [string] -or $request.output_hint -isnot [string]) { throw 'Request JSON prompt and output_hint must be JSON strings' }
$requestNames = @($request.PSObject.Properties.Name | Sort-Object)
if (($requestNames -join ',') -ne 'output_hint,prompt') { throw 'Request JSON must contain exactly prompt and output_hint' }
if ($arguments.prompt -isnot [string]) { throw 'Arguments JSON prompt must be a string' }
$argumentNames = @($arguments.PSObject.Properties.Name | Sort-Object)
if ($arguments.PSObject.Properties.Name -contains 'num_last_images_to_include') { throw 'Arguments JSON must not contain num_last_images_to_include' }
$hasTransparencyOption = $arguments.PSObject.Properties.Name -contains 'transparent_background'
$expectedArgumentNames = if ($hasTransparencyOption) { 'prompt,referenced_image_paths,transparent_background' } else { 'prompt,referenced_image_paths' }
if (($argumentNames -join ',') -ne $expectedArgumentNames) { throw 'Arguments JSON has unknown fields or is missing required fields' }
if ($hasTransparencyOption) {
    if ($arguments.transparent_background -isnot [bool]) { throw 'transparent_background must be the boolean false when present' }
    if ($arguments.transparent_background) { throw 'transparent_background must be false for portrait atlases' }
}
if ($arguments.referenced_image_paths -isnot [System.Array] -or @($arguments.referenced_image_paths).Count -ne 1) { throw 'Arguments JSON must reference exactly one image' }
$argumentReference = [string]$arguments.referenced_image_paths[0]
if (-not [IO.Path]::IsPathRooted($argumentReference)) { throw 'Referenced thinking grid path must be absolute' }
$argumentReferenceFull = [IO.Path]::GetFullPath($argumentReference)
if (-not [string]::Equals($argumentReferenceFull,[IO.Path]::GetFullPath($actualReferencePath),[StringComparison]::OrdinalIgnoreCase)) {
    throw "Arguments JSON must reference the exact thinking grid: $actualReferencePath"
}
if ([string]::IsNullOrWhiteSpace($request.prompt) -or [string]::IsNullOrWhiteSpace($request.output_hint)) { throw 'Request prompt and output_hint must be nonempty' }
if (-not [string]::Equals($request.prompt,$arguments.prompt,[StringComparison]::Ordinal)) { throw 'Request prompt differs from actual arguments prompt' }
if (-not [IO.Path]::IsPathRooted($GeneratedPath)) { throw 'GeneratedPath must be absolute' }
$generatedFullPath = [IO.Path]::GetFullPath($GeneratedPath)
if (-not $request.output_hint.Contains($GeneratedPath)) { throw "GeneratedPath does not appear exactly in output_hint: $GeneratedPath" }
$generatedInfo = Get-PngInfo $generatedFullPath 'Generated output'
Assert-Aspect $generatedInfo (64.0 / 35.0) 'Generated thinking eye atlas'

$outputRoot = Assert-WithinRoot (Join-Path $root 'eyes\thinking') $root 'Thinking output directory'
if (Test-Path -LiteralPath $outputRoot) { throw "Refusing to overwrite existing thinking output: $outputRoot" }
$sourceOriginalHash = Get-Sha256 $originalPath
$sourceUpperHash = Get-Sha256 $upperPath
$actualReferenceHash = Get-Sha256 $actualReferencePath
$halfSourceHashBefore = if ($null -ne $halfSourcePath) { Get-Sha256 $halfSourcePath } else { $null }
$generatedHash = Get-Sha256 $generatedFullPath
$standardHelper = Join-Path $PSScriptRoot 'SavePortraitRawOutput.ps1'
if (-not (Test-Path -LiteralPath $standardHelper -PathType Leaf)) { throw "Standard portrait helper missing: $standardHelper" }

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stageRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ('portrait-thinking-stage-' + [Guid]::NewGuid().ToString('N'))))
$tempPrefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $stageRoot.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Computed stage path escapes temp root: $stageRoot" }
$outputCreated = $false
$createdFiles = New-Object 'System.Collections.Generic.List[string]'
try {
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    Copy-Item -LiteralPath $originalPath -Destination (Join-Path $stageRoot 'original.png')
    Copy-Item -LiteralPath $layoutPath -Destination (Join-Path $stageRoot 'layout.json')
    Copy-Item -LiteralPath $actualReferencePath -Destination (Join-Path $stageRoot 'eyes-reference-grid.png')
    $stageRequest = [ordered]@{prompt=$request.prompt;output_hint=$request.output_hint}
    $stageRequestPath = Join-Path $stageRoot 'request.json'
    $stageRequest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $stageRequestPath -Encoding UTF8

    if ((Get-Sha256 (Join-Path $stageRoot 'original.png')) -ne $sourceOriginalHash) { throw 'Staged original hash mismatch' }
    if ((Get-Sha256 (Join-Path $stageRoot 'eyes-reference-grid.png')) -ne $actualReferenceHash) { throw 'Staged thinking grid hash mismatch' }
    if ((Get-Sha256 $originalPath) -ne $sourceOriginalHash -or (Get-Sha256 $upperPath) -ne $sourceUpperHash -or (Get-Sha256 $referenceSourcePath) -ne $referenceSourceHash -or ($null -ne $halfSourcePath -and (Get-Sha256 $halfSourcePath) -ne $halfSourceHashBefore)) { throw 'Portrait source files changed before helper call' }
    if ((Get-Sha256 $RequestJsonPath) -ne $requestSourceHash -or (Get-Sha256 $ArgumentsJsonPath) -ne $argumentsSourceHash) { throw 'Actual request or arguments changed before helper call' }

    & $standardHelper -PortraitRoot $stageRoot -Part eyes -Emotion neutral -GeneratedPath $generatedFullPath -RequestJsonPath $stageRequestPath | Out-Host
    $stageRaw = Join-Path $stageRoot 'eyes\neutral\raw.png'
    $stageProvenance = Join-Path $stageRoot 'eyes\neutral\provenance.json'
    if (-not (Test-Path -LiteralPath $stageRaw -PathType Leaf) -or -not (Test-Path -LiteralPath $stageProvenance -PathType Leaf)) { throw 'Standard helper did not create expected staged outputs' }
    if ((Get-Sha256 $stageRaw) -ne $generatedHash) { throw 'Standard helper staged raw hash differs from generated image' }

    if ((Get-Sha256 $originalPath) -ne $sourceOriginalHash -or (Get-Sha256 $upperPath) -ne $sourceUpperHash -or (Get-Sha256 $referenceSourcePath) -ne $referenceSourceHash -or (Get-Sha256 $actualReferencePath) -ne $actualReferenceHash -or ($null -ne $halfSourcePath -and (Get-Sha256 $halfSourcePath) -ne $halfSourceHashBefore)) {
        throw 'Portrait source files changed during helper call'
    }
    if ((Get-Sha256 $RequestJsonPath) -ne $requestSourceHash -or (Get-Sha256 $ArgumentsJsonPath) -ne $argumentsSourceHash) { throw 'Actual request or arguments changed during helper call' }
    if (Test-Path -LiteralPath $outputRoot) { throw "Refusing to overwrite existing thinking output: $outputRoot" }
    New-Item -ItemType Directory -Path $outputRoot | Out-Null
    $outputCreated = $true

    $copyMap = [ordered]@{
        'raw.png' = $stageRaw
        'provenance.json' = $stageProvenance
        'arguments.json' = $ArgumentsJsonPath
        'request.json' = $RequestJsonPath
        'eyes-reference-grid.png' = $actualReferencePath
    }
    foreach ($name in $copyMap.Keys) {
        $targetPath = Join-Path $outputRoot $name
        Copy-Item -LiteralPath $copyMap[$name] -Destination $targetPath
        $createdFiles.Add($targetPath)
    }
    if ((Get-Sha256 (Join-Path $outputRoot 'arguments.json')) -ne $argumentsSourceHash -or (Get-Sha256 (Join-Path $outputRoot 'request.json')) -ne $requestSourceHash) {
        throw 'Saved actual request or arguments differ from the exact input files'
    }

    $stagedProvenance = Get-Content -LiteralPath (Join-Path $outputRoot 'provenance.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($stagedProvenance.referenceGrid.path -ne 'eyes-reference-grid.png' -or $stagedProvenance.referenceGrid.sha256 -ne $actualReferenceHash) {
        throw 'Standard helper provenance does not identify staged thinking grid bytes'
    }
    $transparentBackgroundValue = $null
    if ($hasTransparencyOption) { $transparentBackgroundValue = $arguments.transparent_background }
    $wrapperReceipt = [ordered]@{
        schemaVersion = 1
        createdUtc = (Get-Date).ToUniversalTime().ToString('o')
        purpose = 'thinking-eye raw generation; standard helper receipt retained unchanged'
        emotionId = $emotion
        referenceSource = [ordered]@{file=$referenceSourceName;sha256=$referenceSourceHash;width=64;height=35}
        portraitRecord = $layout.portraitRecord
        bank = [string]$layout.bank
        actualReference = [ordered]@{path='../../thinking-reference-grid.png';absolutePath=$actualReferencePath;sha256=$actualReferenceHash;width=$referenceInfo.width;height=$referenceInfo.height}
        actualArguments = [ordered]@{path='arguments.json';sourcePath=[IO.Path]::GetFullPath($ArgumentsJsonPath);sha256=(Get-Sha256 (Join-Path $outputRoot 'arguments.json'));referencedImagePath=$argumentReference;normalizedReferencedImagePath=$argumentReferenceFull;transparentBackground=$transparentBackgroundValue;numLastImagesToInclude=$null}
        actualRequest = [ordered]@{path='request.json';sourcePath=[IO.Path]::GetFullPath($RequestJsonPath);sha256=(Get-Sha256 (Join-Path $outputRoot 'request.json'))}
        helperStage = [ordered]@{portraitRoot=$stageRoot;part='eyes';emotion='neutral';referencePath='eyes-reference-grid.png';referenceSha256=$actualReferenceHash;sourceOriginalSha256=$sourceOriginalHash;provenance='provenance.json'}
        transferred = [ordered]@{rawPath='raw.png';rawSha256=(Get-Sha256 (Join-Path $outputRoot 'raw.png'));rawSize=('{0}x{1}' -f $generatedInfo.width,$generatedInfo.height);helperProvenancePath='provenance.json';savedReferencePath='eyes-reference-grid.png'}
        helperProvenanceNote = 'provenance.json is the unchanged SavePortraitRawOutput receipt from the isolated helper stage. Its eyes/neutral labels and eyes-reference-grid.png path describe that stage; this wrapper maps those bytes to eyes/thinking and the actual thinking grid.'
    }
    if ($gazeLayout -eq 'horizontal-pair') {
        $wrapperReceipt['gazeLayout'] = 'horizontal-pair'
        $wrapperReceipt['referenceSources'] = @(
            [ordered]@{phase='open';file=$referenceSourceName;sha256=$referenceSourceHash;width=64;height=35},
            [ordered]@{phase='half';file=$halfSourceName;sha256=$halfSourceHash;width=64;height=35}
        )
    }
    $receiptPath = Join-Path $outputRoot 'reference-association.json'
    $wrapperReceipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    $createdFiles.Add($receiptPath)

    if ((Get-Sha256 (Join-Path $outputRoot 'raw.png')) -ne $generatedHash) { throw 'Transferred raw output differs from exact generated PNG' }
    if ((Get-Sha256 $originalPath) -ne $sourceOriginalHash -or (Get-Sha256 $upperPath) -ne $sourceUpperHash -or (Get-Sha256 $referenceSourcePath) -ne $referenceSourceHash -or (Get-Sha256 $actualReferencePath) -ne $actualReferenceHash -or ($null -ne $halfSourcePath -and (Get-Sha256 $halfSourcePath) -ne $halfSourceHashBefore)) {
        throw 'Portrait source files changed during transfer'
    }
    "SAVE_PORTRAIT_THINKING_PASS rawSha256=$generatedHash size=$($generatedInfo.width)x$($generatedInfo.height) gazeLayout=$gazeLayout referenceSha256=$actualReferenceHash halfSourceSha256=$halfSourceHash"
} catch {
    if ($outputCreated) {
        foreach ($createdPath in $createdFiles) {
            if (Test-Path -LiteralPath $createdPath -PathType Leaf) { Remove-Item -LiteralPath $createdPath -Force }
        }
        if ((Test-Path -LiteralPath $outputRoot -PathType Container) -and (@(Get-ChildItem -LiteralPath $outputRoot -Force).Count -eq 0)) {
            Remove-Item -LiteralPath $outputRoot
        }
    }
    throw
} finally {
    if (Test-Path -LiteralPath $stageRoot -PathType Container) {
        $stageFull = [IO.Path]::GetFullPath($stageRoot)
        if ($stageFull.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $stageFull -Recurse -Force }
    }
}
