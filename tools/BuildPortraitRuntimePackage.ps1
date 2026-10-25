param(
    [string]$ArtRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'art/portraits/mobile-mouths'),
    [string]$CatalogPath,
    [string]$SelectionPath = (Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'art/portraits/mobile-mouths') 'runtime-selection.json'),
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if ($PSBoundParameters.ContainsKey('CatalogPath') -and $PSBoundParameters.ContainsKey('SelectionPath')) {
    throw 'Specify either CatalogPath or SelectionPath, not both.'
}
$catalogMode = $PSBoundParameters.ContainsKey('CatalogPath')
if ($catalogMode -and [string]::IsNullOrWhiteSpace($CatalogPath)) { throw 'CatalogPath cannot be empty.' }
$repo = Split-Path $PSScriptRoot -Parent
$artRoot = [IO.Path]::GetFullPath($ArtRoot)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$artPrefix = $artRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$inputSnapshots = @{}
function Get-FileSha256([string]$path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Copy-FileIfDifferent([string]$source, [string]$destination, [string]$expectedSha256) {
    if ((Test-Path -LiteralPath $destination -PathType Leaf) -and (Get-FileSha256 $destination) -eq $expectedSha256) { return $false }
    Copy-Item -LiteralPath $source -Destination $destination -Force
    if ((Get-FileSha256 $destination) -ne $expectedSha256) { throw "Promoted file hash mismatch: $destination" }
    return $true
}
function Read-Utf8Text([string]$path) {
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($path))
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    return $text
}
function Add-InputSnapshot([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $inputSnapshots.ContainsKey($resolved)) { $inputSnapshots[$resolved] = Get-FileSha256 $resolved }
}
function Assert-SafeGeneratedTempPath([string]$path, [string]$prefix) {
    $resolved = [IO.Path]::GetFullPath($path)
    $expectedPrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar + $prefix
    if ($resolved -eq $tempRoot -or -not $resolved.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $resolved.Substring($expectedPrefix.Length).Length -ne 32) { throw "Generated temporary path is outside its task-owned temp root: $resolved" }
}

$selection = $null
$selectedEntries = $null
$catalog = $null
$catalogFullPath = $null
$catalogDirectory = $null
$catalogSha256 = $null
$animationPolicy = $null
$animationPolicySha256 = $null
$animationPolicySources = @()

$allowedEmotions = @('neutral', 'happy', 'angry', 'sad', 'afraid', 'guarded')
$allowedPhases = @('open', 'half', 'closed')
$allowedShapes = @('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'X')
$banks = @{
    'TFAC00I0.RCI' = @{ archive = 'CommonFaces'; token = 'common' }
}
$identities = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$sourcePaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$assetNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$runtimePortraits = New-Object 'System.Collections.Generic.List[object]'
$pendingCopies = New-Object 'System.Collections.Generic.List[object]'

function Resolve-ContainedFile([string]$baseDirectory, [string]$relativePath) {
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath)) { throw "Portrait path must be relative: $relativePath" }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $baseDirectory $relativePath))
    if (-not $fullPath.StartsWith($artPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Portrait path escapes art root: $relativePath" }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Portrait source file missing: $relativePath" }
    Assert-NoReparsePoint $fullPath
    return $fullPath
}

function Assert-NoReparsePoint([string]$fullPath) {
    $rootItem = Get-Item -LiteralPath $artRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Portrait art root is a reparse point.' }
    $relative = $fullPath.Substring($artPrefix.Length)
    $current = $artRoot
    $separators = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    foreach ($segment in $relative.Split($separators, [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Portrait path uses reparse point: $segment" }
    }
}

function Get-PixelFacts([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($path)
    try {
        $rgba = New-Object byte[] ($bitmap.Width * $bitmap.Height * 4)
        $offset = 0
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                $rgba[$offset++] = $pixel.R
                $rgba[$offset++] = $pixel.G
                $rgba[$offset++] = $pixel.B
                $rgba[$offset++] = $pixel.A
            }
        }
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($algorithm.ComputeHash($rgba)).Replace('-', '').ToLowerInvariant() }
        finally { $algorithm.Dispose() }
        return @{ width = $bitmap.Width; height = $bitmap.Height; rgbaSha256 = $digest }
    }
    finally { $bitmap.Dispose() }
}

function Get-ValidatedEyeEdgeBlend([object]$manifest, [string]$identity) {
    if ($null -eq $manifest.eyeEdgeBlend) { return $null }
    $profile = $manifest.eyeEdgeBlend
    if ($null -eq $profile.bottomRows -or [int]$profile.bottomRows -lt 1 -or [int]$profile.bottomRows -ne [double]$profile.bottomRows) {
        throw "Invalid eyeEdgeBlend.bottomRows: $identity"
    }
    if ($null -eq $manifest.eyeRegions -or $manifest.eyeRegions.Count -eq 0) {
        throw "eyeEdgeBlend requires eyeRegions: $identity"
    }
    $regions = New-Object 'System.Collections.Generic.List[object]'
    foreach ($region in $manifest.eyeRegions) {
        if ($null -eq $region -or $null -eq $region.x -or $null -eq $region.y -or $null -eq $region.width -or $null -eq $region.height) {
            throw "Invalid eye region: $identity"
        }
        $x = [int]$region.x; $y = [int]$region.y; $width = [int]$region.width; $height = [int]$region.height
        if ($x -ne [double]$region.x -or $y -ne [double]$region.y -or $width -ne [double]$region.width -or $height -ne [double]$region.height -or
            $x -lt 0 -or $y -lt 0 -or $width -le 0 -or $height -le 0 -or $x + $width -gt 64 -or $y + $height -gt 35 -or
            [int]$profile.bottomRows -gt $height) { throw "Eye region outside 64x35 frame or blend rows exceed region: $identity" }
        foreach ($prior in $regions) {
            if ($x -lt ($prior.x + $prior.width) -and $prior.x -lt ($x + $width) -and $y -lt ($prior.y + $prior.height) -and $prior.y -lt ($y + $height)) {
                throw "eyeRegions must not overlap: $identity"
            }
        }
        $regions.Add(@{ x = $x; y = $y; width = $width; height = $height })
    }
    return @{ bottomRows = [int]$profile.bottomRows; eyeRegions = @($regions.ToArray()) }
}

function Get-BitmapRgba([Drawing.Bitmap]$bitmap) {
    $rgba = New-Object byte[] ($bitmap.Width * $bitmap.Height * 4)
    $offset = 0
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $rgba[$offset++] = $pixel.R; $rgba[$offset++] = $pixel.G; $rgba[$offset++] = $pixel.B; $rgba[$offset++] = $pixel.A
        }
    }
    return ,$rgba
}

function Get-ByteSha256([byte[]]$bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Get-BlendedEyePng([string]$sourcePath, [string]$originalPath, [object]$blend) {
    $generated = [Drawing.Bitmap]::FromFile($sourcePath)
    $original = [Drawing.Bitmap]::FromFile($originalPath)
    $output = [Drawing.Bitmap]::new(64, 35, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stream = New-Object IO.MemoryStream
    try {
        if ($generated.Width -ne 64 -or $generated.Height -ne 35 -or $original.Width -ne 64 -or $original.Height -ne 64) {
            throw 'Eye edge blend requires 64x35 generated eyes and a 64x64 original portrait.'
        }
        for ($y = 0; $y -lt 35; $y++) {
            for ($x = 0; $x -lt 64; $x++) {
                $generatedPixel = $generated.GetPixel($x, $y)
                $red = $generatedPixel.R; $green = $generatedPixel.G; $blue = $generatedPixel.B
                foreach ($region in $blend.eyeRegions) {
                    $firstBlendedRow = [int]$region.y + [int]$region.height - [int]$blend.bottomRows
                    if ($x -lt [int]$region.x -or $x -ge ([int]$region.x + [int]$region.width) -or $y -lt $firstBlendedRow -or $y -ge ([int]$region.y + [int]$region.height)) { continue }
                    $row = $y - $firstBlendedRow
                    $generatedWeight = ([double]$blend.bottomRows - $row) / ([double]$blend.bottomRows + 1.0)
                    $originalWeight = 1.0 - $generatedWeight
                    $originalPixel = $original.GetPixel($x, $y)
                    $red = [int][Math]::Floor(($generatedPixel.R * $generatedWeight) + ($originalPixel.R * $originalWeight) + 0.5)
                    $green = [int][Math]::Floor(($generatedPixel.G * $generatedWeight) + ($originalPixel.G * $originalWeight) + 0.5)
                    $blue = [int][Math]::Floor(($generatedPixel.B * $generatedWeight) + ($originalPixel.B * $originalWeight) + 0.5)
                    break
                }
                $output.SetPixel($x, $y, [Drawing.Color]::FromArgb($generatedPixel.A, $red, $green, $blue))
            }
        }
        $rgba = Get-BitmapRgba $output
        $output.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $encoded = $stream.ToArray()
        return @{
            bytes = $encoded
            sha256 = Get-ByteSha256 $encoded
            rgbaSha256 = Get-ByteSha256 $rgba
            width = 64
            height = 35
        }
    }
    finally {
        $generated.Dispose(); $original.Dispose(); $output.Dispose(); $stream.Dispose()
    }
}

function Test-Sha256([string]$value) {
    if ($null -eq $value -or $value.Length -ne 64) { return $false }
    foreach ($character in $value.ToCharArray()) {
        if (($character -ge '0' -and $character -le '9') -or
            ($character -ge 'a' -and $character -le 'f') -or
            ($character -ge 'A' -and $character -le 'F')) { continue }
        return $false
    }
    return $true
}

function Test-PathWithin([string]$path, [string]$root) {
    $normalizedPath = $path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $normalizedRoot = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $normalizedPath.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $normalizedPath.StartsWith($normalizedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $normalizedPath.StartsWith($normalizedRoot + [IO.Path]::AltDirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoDestinationReparsePoints([string]$path) {
    $current = $path
    while (-not [string]::IsNullOrEmpty($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Package destination uses reparse point: $current" }
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Add-PortraitTexture([string]$fullPath, [string]$assetName, [int]$width, [int]$height, [string]$context, [object]$expectedSourceSha256, [object]$blend, [string]$originalPath) {
    if (-not $sourcePaths.Add($fullPath)) { throw "Duplicate portrait source image: $context" }
    Add-InputSnapshot $fullPath
    $facts = Get-PixelFacts $fullPath
    if ($facts.width -ne $width -or $facts.height -ne $height) { throw "Portrait texture must be ${width}x${height}: $context" }
    $sourceSha256 = Get-FileSha256 $fullPath
    if ($null -ne $expectedSourceSha256 -and [string]$expectedSourceSha256 -ne '' -and
        (-not (Test-Sha256 ([string]$expectedSourceSha256)) -or $sourceSha256 -ne ([string]$expectedSourceSha256).ToLowerInvariant())) {
        throw "Portrait manifest texture hash mismatch: $context"
    }
    $renderedSha256 = $sourceSha256
    $renderedRgbaSha256 = $facts.rgbaSha256
    $renderedBytes = $null
    if ($catalogMode -and $null -ne $blend) {
        $rendered = Get-BlendedEyePng $fullPath $originalPath $blend
        $renderedSha256 = $rendered.sha256
        $renderedRgbaSha256 = $rendered.rgbaSha256
        $renderedBytes = $rendered.bytes
    }

    if (-not $assetNames.Add($assetName)) { throw "Duplicate portrait package asset: $assetName" }
    $pendingCopies.Add(@{
        source = $fullPath
        assetName = $assetName
        sourceSha256 = $sourceSha256
        sourceRgbaSha256 = $facts.rgbaSha256
        sha256 = $renderedSha256
        rgbaSha256 = $renderedRgbaSha256
        renderedBytes = $renderedBytes
        width = $width
        height = $height
    })
    $descriptor = @{ assetName = $assetName; sha256 = $renderedSha256; rgbaSha256 = $renderedRgbaSha256; width = $width; height = $height }
    if ($catalogMode) {
        $descriptor.sourceSha256 = $sourceSha256
        $descriptor.sourceRgbaSha256 = $facts.rgbaSha256
    }
    return $descriptor
}

if (-not $catalogMode) {
    $selectionFullPath = [IO.Path]::GetFullPath($SelectionPath)
    Add-InputSnapshot $selectionFullPath
    $selection = Read-Utf8Text $selectionFullPath | ConvertFrom-Json
    if ($selection.schemaVersion -ne 1 -or $null -eq $selection.entries -or $selection.entries.Count -eq 0) {
        throw 'Portrait runtime selection schema or entries invalid.'
    }
    $selectedEntries = @($selection.entries)
}
else {
    $catalogFullPath = [IO.Path]::GetFullPath($CatalogPath)
    if (-not (Test-Path -LiteralPath $catalogFullPath -PathType Leaf) -or -not (Test-PathWithin $catalogFullPath $artRoot)) {
        throw "Portrait catalog must be a file inside the art root: $CatalogPath"
    }
    Assert-NoReparsePoint $catalogFullPath
    $catalogDirectory = Split-Path -Parent $catalogFullPath
    Add-InputSnapshot $catalogFullPath
    $catalogSha256 = Get-FileSha256 $catalogFullPath
    $catalog = Read-Utf8Text $catalogFullPath | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 1 -or $null -eq $catalog.entries -or $catalog.entries.Count -eq 0) {
        throw 'Portrait catalog schema or entries invalid.'
    }

    $policyPath = Resolve-ContainedFile $catalogDirectory 'portrait-behavior-policy.json'
    Add-InputSnapshot $policyPath
    $animationPolicySha256 = Get-FileSha256 $policyPath
    $animationPolicy = Read-Utf8Text $policyPath | ConvertFrom-Json
    $requiredPolicyFields = @(
        'schemaVersion', 'blinkDurationMs', 'blinkHalfMs', 'blinkClosedEndMs', 'blinkDoubleChance',
        'blinkDoubleStartMinFraction', 'blinkDoubleStartMaxFraction', 'blinkShortDelayMinMs',
        'blinkShortDelayMaxMs', 'blinkLongDelayMinMs', 'blinkLongDelayMaxMs', 'blinkSingleShortChance',
        'blinkDoubleShortChance', 'holdIntervalMinMs', 'holdIntervalMaxMs', 'holdHalfMinMs',
        'holdHalfMaxMs', 'holdSpeakingClosedChance', 'holdClosedMinMs', 'holdClosedMaxMs', 'holdReleaseMs',
        'gazeWindowMs', 'gazeBurstsMin', 'gazeBurstsMax', 'gazeDurationMinMs', 'gazeDurationMaxMs',
        'gazeAutoMinMs', 'gazeAutoMaxMs'
    )
    if ($null -eq $animationPolicy -or $animationPolicy.schemaVersion -ne 1) { throw 'Portrait animation policy schema invalid.' }
    foreach ($field in $requiredPolicyFields) {
        $property = $animationPolicy.PSObject.Properties[$field]
        if ($null -eq $property -or $property.Value -is [bool] -or $property.Value -isnot [ValueType] -or [double]::IsNaN([double]$property.Value) -or [double]::IsInfinity([double]$property.Value)) {
            throw "Portrait animation policy field invalid: $field"
        }
    }
    foreach ($chanceField in @('blinkDoubleChance', 'blinkSingleShortChance', 'blinkDoubleShortChance', 'holdSpeakingClosedChance')) {
        if ([double]$animationPolicy.$chanceField -lt 0.0 -or [double]$animationPolicy.$chanceField -gt 1.0) {
            throw "Portrait animation policy probability outside [0,1]: $chanceField"
        }
    }
    foreach ($bounds in @(
        @('blinkDoubleStartMinFraction', 'blinkDoubleStartMaxFraction'),
        @('blinkShortDelayMinMs', 'blinkShortDelayMaxMs'), @('blinkLongDelayMinMs', 'blinkLongDelayMaxMs'),
        @('holdIntervalMinMs', 'holdIntervalMaxMs'), @('holdHalfMinMs', 'holdHalfMaxMs'),
        @('holdClosedMinMs', 'holdClosedMaxMs'), @('gazeBurstsMin', 'gazeBurstsMax'),
        @('gazeDurationMinMs', 'gazeDurationMaxMs'), @('gazeAutoMinMs', 'gazeAutoMaxMs')
    )) {
        if ([double]$animationPolicy.($bounds[0]) -gt [double]$animationPolicy.($bounds[1])) {
            throw "Portrait animation policy bounds conflict: $($bounds[0])/$($bounds[1])"
        }
    }

    foreach ($sourceName in @(
        'preview.js', 'blink-policy.mjs', 'eye-hold-policy.mjs', 'idle-thinking-policy.mjs',
        'thinking-gaze-policy.mjs', 'portrait-behavior-policy.mjs', 'shared-voice-policy.mjs', 'eye-edge-blend-policy.mjs'
    )) {
        $sourcePath = Resolve-ContainedFile $catalogDirectory $sourceName
        Add-InputSnapshot $sourcePath
        $animationPolicySources += @{ name = $sourceName; sha256 = Get-FileSha256 $sourcePath }
    }
    $animationPolicySources += @{ name = 'portrait-behavior-policy.json'; sha256 = $animationPolicySha256 }

    $selectedEntries = New-Object 'System.Collections.Generic.List[object]'
    foreach ($entry in $catalog.entries) {
        if ($null -eq $entry -or [string]::IsNullOrWhiteSpace([string]$entry.animationManifest)) { continue }
        if ($null -eq $entry.record -or [int]$entry.record -lt 0 -or -not $banks.ContainsKey([string]$entry.bank)) {
            throw "Unsupported animated catalog identity: $($entry.id)"
        }
        $bank = $banks[[string]$entry.bank]
        $expectedId = $bank.token + '-' + ('{0:D3}' -f [int]$entry.record)
        if ([string]$entry.id -ne $expectedId) { throw "Catalog portrait id does not match its bank and record: $($entry.id)" }
        $manifestPath = Resolve-ContainedFile $catalogDirectory ([string]$entry.animationManifest)
        Add-InputSnapshot $manifestPath
        $manifestHash = Get-FileSha256 $manifestPath
        $manifest = Read-Utf8Text $manifestPath | ConvertFrom-Json
        $manifestDirectory = Split-Path -Parent $manifestPath
        $catalogOriginalPath = Resolve-ContainedFile $catalogDirectory ([string]$entry.original)
        $manifestOriginalPath = Resolve-ContainedFile $manifestDirectory ([string]$manifest.original)
        Add-InputSnapshot $catalogOriginalPath
        Add-InputSnapshot $manifestOriginalPath
        $sourceHash = Get-FileSha256 $catalogOriginalPath
        $manifestSourceHash = Get-FileSha256 $manifestOriginalPath
        if ($sourceHash -ne $manifestSourceHash) { throw "Catalog original and manifest original differ: $expectedId" }
        $sourceFacts = Get-PixelFacts $catalogOriginalPath
        if ($sourceFacts.width -ne 64 -or $sourceFacts.height -ne 64) { throw "Catalog original must be 64x64: $expectedId" }
        $selectedEntries.Add(@{
            manifestPath = $manifestPath
            manifest = $manifest
            sourceImagePath = $catalogOriginalPath
            sourceImageSha256 = $sourceHash
            nativeRgbaSha256 = $sourceFacts.rgbaSha256
            catalogId = [string]$entry.id
            catalogStatus = [string]$entry.status
            catalogRecord = [int]$entry.record
            catalogBank = [string]$entry.bank
            manifestSha256 = $manifestHash
        })
    }
    if ($selectedEntries.Count -eq 0) { throw 'Portrait catalog contains no animated entries.' }
}

foreach ($selected in $selectedEntries) {
    if ($null -eq $selected -or -not (Test-Sha256 $selected.sourceImageSha256) -or -not (Test-Sha256 $selected.nativeRgbaSha256)) {
        throw 'Portrait source hashes invalid.'
    }
    $manifestPath = if ($catalogMode) { [string]$selected.manifestPath } else { Resolve-ContainedFile $artRoot $selected.manifest }
    if (-not $catalogMode) { Add-InputSnapshot $manifestPath }
    $manifest = if ($catalogMode) { $selected.manifest } else { Read-Utf8Text $manifestPath | ConvertFrom-Json }
    $manifestLabel = if ($catalogMode) { [string]$selected.catalogId } else { [string]$selected.manifest }
    if ($manifest.schemaVersion -ne 2 -or $manifest.portraitRecord -lt 0 -or
        $manifest.width -ne 64 -or $manifest.height -ne 64 -or $manifest.splitY -ne 35 -or
        -not $banks.ContainsKey([string]$manifest.bank)) { throw "Unsupported split portrait manifest: $manifestLabel" }
    $bank = $banks[[string]$manifest.bank]
    if ($catalogMode -and ($selected.catalogRecord -ne [int]$manifest.portraitRecord -or $selected.catalogBank -ne [string]$manifest.bank)) {
        throw "Catalog identity and animation manifest differ: $($selected.catalogId)"
    }
    $identity = $bank.archive + '|' + $manifest.portraitRecord
    if (-not $identities.Add($identity)) { throw "Duplicate portrait identity: $identity" }

    $manifestDirectory = Split-Path -Parent $manifestPath
    $sourceImagePath = if ($catalogMode) { [string]$selected.sourceImagePath } else { Resolve-ContainedFile $manifestDirectory $manifest.original }
    if (-not $catalogMode) { Add-InputSnapshot $sourceImagePath }
    $actualSourceHash = (Get-FileHash -LiteralPath $sourceImagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSourceHash -ne $selected.sourceImageSha256.ToLowerInvariant()) { throw "Portrait source PNG hash mismatch: $identity" }
    $sourceFacts = Get-PixelFacts $sourceImagePath
    if ($sourceFacts.width -ne 64 -or $sourceFacts.height -ne 64 -or
        $sourceFacts.rgbaSha256 -ne $selected.nativeRgbaSha256.ToLowerInvariant()) { throw "Portrait native RGBA source mismatch: $identity" }
    $eyeBlend = if ($catalogMode) { Get-ValidatedEyeEdgeBlend $manifest $identity } else { $null }

    if ($null -eq $manifest.eyeSets -or $manifest.eyeSets.Count -ne $allowedEmotions.Count -or
        $null -eq $manifest.mouthSets -or $manifest.mouthSets.Count -ne $allowedEmotions.Count) {
        throw "Portrait requires six matching eye and mouth emotion sets: $identity"
    }
    $eyesByEmotion = @{}
    foreach ($set in $manifest.eyeSets) {
        if ($null -eq $set -or $allowedEmotions -cnotcontains [string]$set.id -or $eyesByEmotion.ContainsKey([string]$set.id)) {
            throw "Unsupported or duplicate eye emotion: $identity"
        }
        $eyesByEmotion[[string]$set.id] = $set
    }
    $mouthsByEmotion = @{}
    foreach ($set in $manifest.mouthSets) {
        if ($null -eq $set -or $allowedEmotions -cnotcontains [string]$set.id -or $mouthsByEmotion.ContainsKey([string]$set.id)) {
            throw "Unsupported or duplicate mouth emotion: $identity"
        }
        $mouthsByEmotion[[string]$set.id] = $set
    }

    $runtimeEmotions = New-Object 'System.Collections.Generic.List[object]'
    foreach ($emotionId in $allowedEmotions) {
        if (-not $eyesByEmotion.ContainsKey($emotionId) -or -not $mouthsByEmotion.ContainsKey($emotionId)) { throw "Missing emotion set: $emotionId" }
        $phaseMap = $eyesByEmotion[$emotionId].phases
        $eyeFrames = New-Object 'System.Collections.Generic.List[object]'
        foreach ($phase in $allowedPhases) {
            $frame = $phaseMap.$phase
            if ($null -eq $frame) { throw "Missing eye phase $emotionId/$phase" }
            $fullPath = Resolve-ContainedFile $manifestDirectory $frame.file
            $assetName = "portrait-$($bank.token)-$('{0:D3}' -f [int]$manifest.portraitRecord)-$emotionId-eye-$phase.png.bytes"
            $texture = Add-PortraitTexture $fullPath $assetName 64 35 $frame.file $frame.sha256 $eyeBlend $sourceImagePath
            $texture.frame = $phase
            $eyeFrames.Add($texture)
        }

        $runtimeGazes = New-Object 'System.Collections.Generic.List[object]'
        if ($catalogMode) {
            $gazeEntries = $manifest.thinkingEyeSetsByEmotion.$emotionId
            if ($null -eq $gazeEntries) { throw "Missing gaze emotion set: $identity/$emotionId" }
            $gazesById = @{}
            foreach ($gazeEntry in $gazeEntries) {
                $gazeId = [string]$gazeEntry.id
                if ($gazeId -notin @('left', 'right')) { continue }
                if ($gazesById.ContainsKey($gazeId)) { throw "Duplicate gaze id: $identity/$emotionId/$gazeId" }
                $gazesById[$gazeId] = $gazeEntry
            }
            foreach ($gazeId in @('left', 'right')) {
                if (-not $gazesById.ContainsKey($gazeId)) { throw "Missing required gaze: $identity/$emotionId/$gazeId" }
                $gazeEntry = $gazesById[$gazeId]
                $gazePhaseMap = $gazeEntry.phases
                $gazeFrames = New-Object 'System.Collections.Generic.List[object]'
                foreach ($phase in $allowedPhases) {
                    $frame = $gazePhaseMap.$phase
                    if ($null -eq $frame) { throw "Missing gaze eye phase $emotionId/$gazeId/$phase" }
                    $fullPath = Resolve-ContainedFile $manifestDirectory $frame.file
                    $assetName = "portrait-$($bank.token)-$('{0:D3}' -f [int]$manifest.portraitRecord)-$emotionId-gaze-$gazeId-eye-$phase.png.bytes"
                    $texture = Add-PortraitTexture $fullPath $assetName 64 35 $frame.file $frame.sha256 $eyeBlend $sourceImagePath
                    $texture.frame = $phase
                    $gazeFrames.Add($texture)
                }
                $runtimeGazes.Add(@{ id = $gazeId; eyes = @($gazeFrames.ToArray()) })
            }
        }

        $mouthFramesSource = $mouthsByEmotion[$emotionId].frames
        if ($null -eq $mouthFramesSource -or $mouthFramesSource.Count -ne $allowedShapes.Count) { throw "Incomplete mouth shapes for $emotionId" }
        $mouthsByShape = @{}
        foreach ($frame in $mouthFramesSource) {
            if ($null -eq $frame -or $allowedShapes -cnotcontains [string]$frame.shape -or $mouthsByShape.ContainsKey([string]$frame.shape)) {
                throw "Unsupported or duplicate mouth shape for $emotionId"
            }
            $mouthsByShape[[string]$frame.shape] = $frame
        }
        $mouthFrames = New-Object 'System.Collections.Generic.List[object]'
        foreach ($shape in $allowedShapes) {
            if (-not $mouthsByShape.ContainsKey($shape)) { throw "Missing mouth shape $emotionId/$shape" }
            $frame = $mouthsByShape[$shape]
            $fullPath = Resolve-ContainedFile $manifestDirectory $frame.file
            $assetName = "portrait-$($bank.token)-$('{0:D3}' -f [int]$manifest.portraitRecord)-$emotionId-mouth-$shape.png.bytes"
            $texture = Add-PortraitTexture $fullPath $assetName 64 29 $frame.file $frame.sha256 $null $sourceImagePath
            $texture.frame = $shape
            $mouthFrames.Add($texture)
        }
        $runtimeEmotion = @{ id = $emotionId; eyes = @($eyeFrames.ToArray()); mouths = @($mouthFrames.ToArray()) }
        if ($catalogMode) { $runtimeEmotion.gazes = @($runtimeGazes.ToArray()) }
        $runtimeEmotions.Add($runtimeEmotion)
    }

    $runtimePortrait = @{
        archive = $bank.archive
        bank = [string]$manifest.bank
        record = [int]$manifest.portraitRecord
        sourceImageSha256 = $actualSourceHash
        nativeRgbaSha256 = $sourceFacts.rgbaSha256
        width = 64
        height = 64
        splitY = 35
        emotions = @($runtimeEmotions.ToArray())
    }
    if ($catalogMode) {
        $runtimePortrait.catalogId = [string]$selected.catalogId
        $runtimePortrait.catalogStatus = [string]$selected.catalogStatus
        $runtimePortrait.animationManifestSha256 = [string]$selected.manifestSha256
    }
    $runtimePortraits.Add($runtimePortrait)
}

$registry = if ($catalogMode) {
    @{ schemaVersion = 2; animationPolicy = $animationPolicy; animationPolicySha256 = $animationPolicySha256; catalogSha256 = $catalogSha256; portraits = @($runtimePortraits.ToArray()) }
} else {
    @{ schemaVersion = 1; portraits = @($runtimePortraits.ToArray()) }
}
$json = ConvertTo-Json -InputObject $registry -Depth 12
$destinationPath = [IO.Path]::GetFullPath($Destination)
$destinationNormalized = $destinationPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$artNormalized = $artRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if ((Test-PathWithin $destinationNormalized $artNormalized) -or (Test-PathWithin $artNormalized $destinationNormalized)) {
    throw 'Package destination must be separate from the portrait source tree.'
}
Assert-NoDestinationReparsePoints $destinationPath
$stagingName = 'portrait-runtime-stage1-' + [Guid]::NewGuid().ToString('N')
$staging = Join-Path $tempRoot $stagingName
Assert-SafeGeneratedTempPath $staging 'portrait-runtime-stage1-'
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    foreach ($copy in $pendingCopies) {
        $stagedFile = Join-Path $staging $copy.assetName
        if ($null -ne $copy.renderedBytes) { [IO.File]::WriteAllBytes($stagedFile, [byte[]]$copy.renderedBytes) }
        else { Copy-Item -LiteralPath $copy.source -Destination $stagedFile }
        $stagedHash = (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash.ToLowerInvariant()
        $sourceHashAfterCopy = (Get-FileHash -LiteralPath $copy.source -Algorithm SHA256).Hash.ToLowerInvariant()
        $stagedFacts = Get-PixelFacts $stagedFile
        if ($stagedHash -ne $copy.sha256 -or $sourceHashAfterCopy -ne $copy.sourceSha256 -or
            $stagedFacts.width -ne $copy.width -or $stagedFacts.height -ne $copy.height -or
            $stagedFacts.rgbaSha256 -ne $copy.rgbaSha256) {
            throw "Portrait texture changed during package copy: $($copy.source)"
        }
    }
    foreach ($snapshotPath in $inputSnapshots.Keys) {
        if ((Get-FileSha256 $snapshotPath) -ne $inputSnapshots[$snapshotPath]) { throw "Portrait package input changed during build: $snapshotPath" }
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $staging 'portrait-split-manifest.json.bytes'), $json, $utf8)
    $reportName = if ($catalogMode) { 'portrait-runtime-catalog-build-report.json' } else { 'portrait-runtime-stage1-build-report.json' }
    $reportPath = Join-Path $staging $reportName
    if ($catalogMode) {
        $snapshotReport = @($inputSnapshots.Keys | Sort-Object | ForEach-Object { @{ path = $_; sha256 = $inputSnapshots[$_] } })
        $assetReport = @($pendingCopies | ForEach-Object {
            @{ assetName = $_.assetName; sourcePath = $_.source; sourceSha256 = $_.sourceSha256; renderedSha256 = $_.sha256; sourceRgbaSha256 = $_.sourceRgbaSha256; renderedRgbaSha256 = $_.rgbaSha256 }
        })
        $report = @{
            schemaVersion = 2
            sourceMode = 'catalog'
            portraitCount = $runtimePortraits.Count
            textureCount = $pendingCopies.Count
            registry = 'portrait-split-manifest.json.bytes'
            catalogPath = $catalogFullPath
            catalogSha256 = $catalogSha256
            animationPolicySha256 = $animationPolicySha256
            animationPolicySources = @($animationPolicySources)
            sourceSnapshots = $snapshotReport
            assetHashes = $assetReport
        }
    }
    else {
        $report = @{ schemaVersion = 1; portraitCount = $runtimePortraits.Count; textureCount = $pendingCopies.Count; registry = 'portrait-split-manifest.json.bytes'; sourceSelection = [IO.Path]::GetFullPath($SelectionPath) }
    }
    [IO.File]::WriteAllText($reportPath, (ConvertTo-Json -InputObject $report -Depth 8), $utf8)

    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    Assert-NoDestinationReparsePoints $destinationPath
    foreach ($obsolete in Get-ChildItem -LiteralPath $destinationPath -Filter 'portrait-*.png.bytes' -File) {
        if (-not $assetNames.Contains($obsolete.Name)) { Remove-Item -LiteralPath $obsolete.FullName -Force }
    }
    foreach ($copy in $pendingCopies) {
        [void](Copy-FileIfDifferent (Join-Path $staging $copy.assetName) (Join-Path $destinationPath $copy.assetName) $copy.sha256)
    }
    $stagedRegistry = Join-Path $staging 'portrait-split-manifest.json.bytes'
    [void](Copy-FileIfDifferent $stagedRegistry (Join-Path $destinationPath 'portrait-split-manifest.json.bytes') (Get-FileSha256 $stagedRegistry))
    $stagedReport = Join-Path $staging $reportName
    [void](Copy-FileIfDifferent $stagedReport (Join-Path $destinationPath $reportName) (Get-FileSha256 $stagedReport))
}
finally {
    Assert-SafeGeneratedTempPath $staging 'portrait-runtime-stage1-'
    Remove-Item -LiteralPath ([IO.Path]::GetFullPath($staging)) -Recurse -Force
}
Write-Output "Prepared $($runtimePortraits.Count) portrait entries and $($pendingCopies.Count) unique textures at $destinationPath"
