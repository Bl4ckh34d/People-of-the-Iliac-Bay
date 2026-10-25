param()

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dfuRoot = [IO.Path]::GetFullPath((Join-Path $repo '..'))
$packageTextures = Join-Path $dfuRoot 'daggerfall-unity/Assets/Game/Mods/PeopleOfTheIliacBay/Textures'
$registryPath = Join-Path $packageTextures 'portrait-split-manifest.json.bytes'
$nativeExport = Join-Path $repo 'work/portrait-native-bank-export'
$nativeExportManifestPath = Join-Path $nativeExport 'manifest.json'
$native77Path = Join-Path $nativeExport 'common-077.rgba'
$native76Path = Join-Path $nativeExport 'common-076.rgba'
$layoutPath = Join-Path $repo 'art/portraits/mobile-mouths/split-mask-prototype/layout.json'
$prototypeManifestPath = Join-Path $repo 'art/portraits/mobile-mouths/split-mask-prototype/manifest.json'
$prototypeOriginalPath = Join-Path $repo 'art/portraits/mobile-mouths/split-mask-prototype/original.png'
$runtimeDirectory = Join-Path $repo 'tools/RuntimeCompile/bin/Debug/net472'
$runtimeAssemblyPath = Join-Path $runtimeDirectory 'RuntimeCompile.dll'
$fullSerializerPath = Join-Path $runtimeDirectory 'FullSerializer - Unity.dll'
$runtimeProjectPath = Join-Path $repo 'tools/RuntimeCompile/RuntimeCompile.csproj'
$compositionSourcePath = Join-Path $repo 'mod/Scripts/SplitPortraitComposition.cs'
$manifestSourcePath = Join-Path $repo 'mod/Scripts/SplitPortraitManifest.cs'
$visemeSourcePath = Join-Path $repo 'mod/Scripts/PortraitVisemeTimeline.cs'
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$reportJsonPath = Join-Path $repo "work/portrait-runtime-stage2-real-art-$runId.json"
$reportMarkdownPath = Join-Path $repo "work/portrait-runtime-stage2-real-art-$runId.md"
$emotionIds = @('neutral', 'happy', 'angry', 'sad', 'afraid', 'guarded')
$eyeIds = @('open', 'half', 'closed')
$mouthIds = @('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'X')
$report = [ordered]@{
    schemaVersion = 1
    status = 'not-run'
    startedUtc = [DateTime]::UtcNow.ToString('o')
    completedUtc = $null
    combinationsExpected = 162
    combinationsCompared = 0
    exactOutputMatches = 0
    outsideFeaturePixelComparisons = 0
    outsideFeaturePixelMismatches = 0
    decodedAssets = 0
    decodedAssetHashMatches = 0
    rejectionChecks = [ordered]@{}
    inputsBefore = [ordered]@{}
    inputsAfter = [ordered]@{}
    error = $null
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Get-Sha256Bytes([byte[]]$bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $algorithm.ComputeHash($bytes)
        return ([BitConverter]::ToString($digest).Replace('-', '').ToLowerInvariant())
    } finally { $algorithm.Dispose() }
}

function Get-Sha256File([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ProtectedHashes([string[]]$paths) {
    $hashes = [ordered]@{}
    foreach ($path in ($paths | Sort-Object -Unique)) {
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Protected input missing: $path"
        $hashes[[IO.Path]::GetFullPath($path)] = Get-Sha256File $path
    }
    return $hashes
}

function Decode-PngRgba([byte[]]$pngBytes, [string]$assetName) {
    $stream = New-Object IO.MemoryStream(,$pngBytes)
    $bitmap = $null
    try {
        $bitmap = New-Object System.Drawing.Bitmap($stream)
        if ($bitmap.Width -ne 64 -or ($bitmap.Height -ne 35 -and $bitmap.Height -ne 29)) {
            throw "Unexpected PNG dimensions for $assetName`: $($bitmap.Width)x$($bitmap.Height)"
        }
        $rgba = New-Object byte[] ($bitmap.Width * $bitmap.Height * 4)
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $color = $bitmap.GetPixel($x, $y)
                $offset = ($y * $bitmap.Width + $x) * 4
                $rgba[$offset] = $color.R
                $rgba[$offset + 1] = $color.G
                $rgba[$offset + 2] = $color.B
                $rgba[$offset + 3] = $color.A
            }
        }
        return ,$rgba
    } finally {
        if ($null -ne $bitmap) { $bitmap.Dispose() }
        $stream.Dispose()
    }
}

function Convert-Registry([byte[]]$jsonBytes, [Reflection.Assembly]$runtimeAssembly, [Reflection.Assembly]$serializerAssembly) {
    $manifestType = $runtimeAssembly.GetType('PeopleOfTheIliacBay.DFU.SplitPortraitManifest', $true)
    $parserType = $serializerAssembly.GetType('FullSerializer.fsJsonParser', $true)
    $serializerType = $serializerAssembly.GetType('FullSerializer.fsSerializer', $true)
    $text = [Text.Encoding]::UTF8.GetString($jsonBytes)
    $parseMethod = $parserType.GetMethods() | Where-Object {
        $_.Name -eq 'Parse' -and $_.GetParameters().Length -eq 1 -and
        $_.GetParameters()[0].ParameterType -eq [string] -and $_.ReturnType.FullName -eq 'FullSerializer.fsData'
    } | Select-Object -First 1
    Assert-True ($null -ne $parseMethod) 'FullSerializer string parser unavailable.'
    $parsed = $parseMethod.Invoke($null, @($text))
    $serializer = [Activator]::CreateInstance($serializerType)
    $genericMethod = $serializerType.GetMethods() | Where-Object {
        $_.Name -eq 'TryDeserialize' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Length -eq 2
    } | Select-Object -First 1
    Assert-True ($null -ne $genericMethod) 'FullSerializer generic deserializer unavailable.'
    $deserializeMethod = $genericMethod.MakeGenericMethod(@($manifestType))
    $arguments = New-Object object[] 2
    $arguments[0] = $parsed
    $arguments[1] = $null
    $deserializeResult = $deserializeMethod.Invoke($serializer, $arguments)
    Assert-True $deserializeResult.Succeeded 'FullSerializer rejected deployed registry JSON.'
    return $arguments[1]
}

function New-DecodedAssetList([Reflection.Assembly]$runtimeAssembly, [object[]]$assets, [string]$corruptAssetName) {
    $assetType = $runtimeAssembly.GetType('PeopleOfTheIliacBay.DFU.SplitPortraitDecodedRgbaAsset', $true)
    $listDefinition = [Type]::GetType('System.Collections.Generic.List`1')
    $listType = $listDefinition.MakeGenericType([Type[]]@($assetType))
    $list = [Activator]::CreateInstance($listType)
    foreach ($sourceAsset in $assets) {
        $asset = [Activator]::CreateInstance($assetType)
        $encoded = [byte[]]$sourceAsset.encodedBytes.Clone()
        $rgba = [byte[]]$sourceAsset.rgba.Clone()
        if ($sourceAsset.assetName -eq $corruptAssetName) { $rgba[0] = $rgba[0] -bxor 255 }
        $assetType.GetField('assetName').SetValue($asset, [string]$sourceAsset.assetName)
        $assetType.GetField('width').SetValue($asset, [int]$sourceAsset.width)
        $assetType.GetField('height').SetValue($asset, [int]$sourceAsset.height)
        $assetType.GetField('encodedBytes').SetValue($asset, $encoded)
        $assetType.GetField('rgba').SetValue($asset, $rgba)
        $list.Add($asset)
    }
    return ,$list
}

function Invoke-CreateFace([Reflection.MethodInfo]$method, [object]$registry, [int]$record,
    [byte[]]$source, [object]$assets) {
    $arguments = New-Object object[] 8
    $arguments[0] = $registry
    $arguments[1] = 'CommonFaces'
    $arguments[2] = 'TFAC00I0.RCI'
    $arguments[3] = $record
    $arguments[4] = $source
    $arguments[5] = $assets
    $arguments[6] = $null
    $arguments[7] = $null
    $success = [bool]$method.Invoke($null, $arguments)
    return [pscustomobject]@{ Success = $success; Face = $arguments[6]; Error = $arguments[7] }
}

function Invoke-Compose([Reflection.MethodInfo]$method, [object]$face, [string]$emotion,
    [Type]$phaseType, [int]$phase, [Type]$shapeType, [int]$shape, [byte[]]$destination) {
    $arguments = New-Object object[] 6
    $arguments[0] = $emotion
    $arguments[1] = [Enum]::ToObject($phaseType, $phase)
    $arguments[2] = [Enum]::ToObject($shapeType, $shape)
    $arguments[3] = $destination
    $arguments[4] = $false
    $arguments[5] = $null
    $success = [bool]$method.Invoke($face, $arguments)
    return [pscustomobject]@{ Success = $success; UsedNeutralFallback = [bool]$arguments[4]; Error = $arguments[5] }
}

function Find-Frame([object[]]$frames, [string]$frame) {
    foreach ($candidate in $frames) {
        if ($candidate.frame -ceq $frame) { return $candidate }
    }
    throw "Manifest frame missing: $frame"
}

function Assert-BytesEqual([byte[]]$expected, [byte[]]$actual, [string]$context) {
    if ($expected.Length -ne $actual.Length) { throw "$context byte length mismatch." }
    for ($index = 0; $index -lt $expected.Length; $index++) {
        if ($expected[$index] -ne $actual[$index]) { throw "$context RGBA mismatch at byte $index." }
    }
}

function Test-IsFeaturePixel([int]$x, [int]$y, [object]$layout) {
    foreach ($region in $layout.eyeRegions) {
        if ($x -ge $region.x -and $x -lt ($region.x + $region.width) -and
            $y -ge $region.y -and $y -lt ($region.y + $region.height)) { return $true }
    }
    $mouth = $layout.mouthRegion
    return ($x -ge $mouth.x -and $x -lt ($mouth.x + $mouth.width) -and
        $y -ge $mouth.y -and $y -lt ($mouth.y + $mouth.height))
}

function Write-Reports([string]$status) {
    $report.status = $status
    $report.completedUtc = [DateTime]::UtcNow.ToString('o')
    $json = $report | ConvertTo-Json -Depth 30
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($reportJsonPath, $json + [Environment]::NewLine, $utf8)
    $lines = New-Object Collections.Generic.List[string]
    $lines.Add('# Split portrait Stage2 real-art acceptance')
    $lines.Add('')
    $lines.Add("Status: **$($report.status)**")
    $lines.Add('')
    $lines.Add("Compared combinations: **$($report.combinationsCompared)/$($report.combinationsExpected)** exact compositions.")
    $lines.Add("Outside-mask pixel comparisons: **$($report.outsideFeaturePixelComparisons)**; mismatches: **$($report.outsideFeaturePixelMismatches)**.")
    $lines.Add("Decoded assets with matching RGBA hashes: **$($report.decodedAssetHashMatches)/$($report.decodedAssets)**.")
    $lines.Add('')
    $lines.Add('Production implementation ran from `tools/RuntimeCompile/bin/Debug/net472/RuntimeCompile.dll` through reflection. Deployed manifest used FullSerializer. PNG parts decoded through System.Drawing in top-down RGBA order. No Unity editor, game, or model was started.')
    $lines.Add('')
    $lines.Add('Rejection checks:')
    foreach ($key in $report.rejectionChecks.Keys) { $lines.Add("- ${key}: $($report.rejectionChecks[$key])") }
    if ($report.error) { $lines.Add(''); $lines.Add("Failure: ``$($report.error)``") }
    $lines.Add('')
    $lines.Add('Input SHA-256 values and complete counts are in the JSON report.')
    [IO.File]::WriteAllText($reportMarkdownPath, ($lines -join [Environment]::NewLine) + [Environment]::NewLine, $utf8)
}

try {
    Assert-True (-not (Test-Path -LiteralPath $reportJsonPath)) 'Acceptance JSON already exists; refusing to overwrite evidence.'
    Assert-True (-not (Test-Path -LiteralPath $reportMarkdownPath)) 'Acceptance Markdown already exists; refusing to overwrite evidence.'
    Add-Type -AssemblyName System.Drawing
    $repoPrefix = [IO.Path]::GetFullPath((Join-Path $repo 'work')) + [IO.Path]::DirectorySeparatorChar
    foreach ($outputPath in @($reportJsonPath, $reportMarkdownPath)) {
        if (-not [IO.Path]::GetFullPath($outputPath).StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Acceptance output escaped work directory: $outputPath"
        }
    }

    $registryBytes = [IO.File]::ReadAllBytes($registryPath)
    $registryText = [Text.Encoding]::UTF8.GetString($registryBytes)
    $registryWire = ConvertFrom-Json -InputObject $registryText
    Assert-True ($registryWire.schemaVersion -eq 1 -and $registryWire.portraits.Count -eq 1) 'Unexpected deployed registry schema or entry count.'
    $wireEntry = $registryWire.portraits[0]
    Assert-True ($wireEntry.archive -ceq 'CommonFaces' -and $wireEntry.bank -ieq 'TFAC00I0.RCI' -and $wireEntry.record -eq 77) 'Deployed registry does not target CommonFaces record 77.'
    Assert-True ($wireEntry.emotions.Count -eq 6) 'Deployed registry does not contain six emotions.'
    Assert-True ((Get-Sha256File $prototypeOriginalPath) -ceq $wireEntry.sourceImageSha256) 'Prototype original PNG hash differs from deployed sourceImageSha256.'
    $assetNames = New-Object Collections.Generic.List[string]
    foreach ($emotion in $wireEntry.emotions) {
        foreach ($asset in @($emotion.eyes) + @($emotion.mouths)) { $assetNames.Add([string]$asset.assetName) }
    }
    Assert-True ($assetNames.Count -eq 72 -and ($assetNames | Select-Object -Unique).Count -eq 72) 'Expected 72 unique registered textures.'

    $layout = Get-Content -LiteralPath $layoutPath -Raw | ConvertFrom-Json
    Assert-True ($layout.portraitRecord -eq 77 -and $layout.splitY -eq 35) 'Prototype feature masks do not match record 77 split layout.'
    Assert-True ($layout.eyeRegions.Count -eq 2 -and $null -ne $layout.mouthRegion) 'Prototype feature masks are incomplete.'

    $nativeExportManifest = Get-Content -LiteralPath $nativeExportManifestPath -Raw | ConvertFrom-Json
    $native77Row = $nativeExportManifest.records | Where-Object record -eq 77 | Select-Object -First 1
    $native76Row = $nativeExportManifest.records | Where-Object record -eq 76 | Select-Object -First 1
    Assert-True ($null -ne $native77Row -and $null -ne $native76Row) 'Native export does not contain records 76 and 77.'
    $source77 = [IO.File]::ReadAllBytes($native77Path)
    $source76 = [IO.File]::ReadAllBytes($native76Path)
    Assert-True ($source77.Length -eq 16384 -and $source76.Length -eq 16384) 'Native RGBA source length is invalid.'
    $source77Hash = Get-Sha256Bytes $source77
    $source76Hash = Get-Sha256Bytes $source76
    Assert-True ($source77Hash -ceq $native77Row.rgbaSha256 -and $source77Hash -ceq $wireEntry.nativeRgbaSha256) 'Native record 77 hash does not match export and deployed registry.'
    Assert-True ($source76Hash -ceq $native76Row.rgbaSha256 -and $source76Hash -cne $source77Hash) 'Native record 76 is invalid or aliases record 77.'

    $runtimeBytes = [IO.File]::ReadAllBytes($runtimeAssemblyPath)
    $serializerBytes = [IO.File]::ReadAllBytes($fullSerializerPath)
    $runtimeAssemblyHash = Get-Sha256Bytes $runtimeBytes
    $serializerAssemblyHash = Get-Sha256Bytes $serializerBytes
    $inputPaths = @($registryPath, $native77Path, $native76Path, $nativeExportManifestPath,
        $layoutPath, $prototypeManifestPath, $prototypeOriginalPath, $runtimeAssemblyPath, $fullSerializerPath,
        $runtimeProjectPath, $compositionSourcePath, $manifestSourcePath, $visemeSourcePath, $PSCommandPath)
    foreach ($assetName in $assetNames) { $inputPaths += Join-Path $packageTextures $assetName }
    $report.inputsBefore = Get-ProtectedHashes $inputPaths

    $resolver = [ResolveEventHandler] {
        param($sender, $eventArgs)
        $simpleName = (New-Object Reflection.AssemblyName($eventArgs.Name)).Name + '.dll'
        $candidate = Join-Path $runtimeDirectory $simpleName
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
        return $null
    }
    [AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
    $runtimeAssembly = [Reflection.Assembly]::LoadFrom($runtimeAssemblyPath)
    $serializerAssembly = [Reflection.Assembly]::LoadFrom($fullSerializerPath)
    $compositionType = $runtimeAssembly.GetType('PeopleOfTheIliacBay.DFU.SplitPortraitComposition', $true)
    $assetType = $runtimeAssembly.GetType('PeopleOfTheIliacBay.DFU.SplitPortraitDecodedRgbaAsset', $true)
    $phaseType = $runtimeAssembly.GetType('PeopleOfTheIliacBay.DFU.SplitPortraitEyePhase', $true)
    $shapeType = $runtimeAssembly.GetType('PeopleOfTheIliacBay.DFU.PortraitMouthShape', $true)
    $createMethod = $compositionType.GetMethod('TryCreateFace')
    $composeMethod = $runtimeAssembly.GetType('PeopleOfTheIliacBay.DFU.SplitPortraitFace', $true).GetMethod('TryCompose')
    Assert-True ($null -ne $createMethod -and $null -ne $composeMethod) 'Compiled production API unavailable.'
    $manifest = Convert-Registry $registryBytes $runtimeAssembly $serializerAssembly

    $decodedAssets = New-Object Collections.Generic.List[object]
    $decodedByName = @{}
    foreach ($assetName in $assetNames) {
        $assetPath = Join-Path $packageTextures $assetName
        $encoded = [IO.File]::ReadAllBytes($assetPath)
        $rgba = Decode-PngRgba $encoded $assetName
        $asset = [pscustomobject]@{
            assetName = $assetName
            width = [int]($wireEntry.emotions | ForEach-Object { @($_.eyes) + @($_.mouths) } | ForEach-Object { $_ } | Where-Object assetName -CEQ $assetName | Select-Object -First 1).width
            height = [int]($wireEntry.emotions | ForEach-Object { @($_.eyes) + @($_.mouths) } | ForEach-Object { $_ } | Where-Object assetName -CEQ $assetName | Select-Object -First 1).height
            encodedBytes = $encoded
            rgba = $rgba
        }
        Assert-True ($rgba.Length -eq $asset.width * $asset.height * 4) "Decoded RGBA byte count invalid: $assetName"
        $frame = $wireEntry.emotions | ForEach-Object { @($_.eyes) + @($_.mouths) } | ForEach-Object { $_ } | Where-Object assetName -CEQ $assetName | Select-Object -First 1
        Assert-True ((Get-Sha256Bytes $encoded) -ceq $frame.sha256) "Encoded asset SHA-256 mismatch: $assetName"
        Assert-True ((Get-Sha256Bytes $rgba) -ceq $frame.rgbaSha256) "Decoded RGBA SHA-256 mismatch: $assetName"
        $decodedAssets.Add($asset)
        $decodedByName[$assetName] = $rgba
        $report.decodedAssets++
        $report.decodedAssetHashMatches++
    }
    $decodedAssetArray = $decodedAssets.ToArray()
    $validAssets = New-DecodedAssetList $runtimeAssembly $decodedAssetArray $null
    $created = Invoke-CreateFace $createMethod $manifest 77 $source77 $validAssets
    Assert-True $created.Success "Production TryCreateFace rejected deployed record 77: $($created.Error)"
    Assert-True ($created.Face.Record -eq 77 -and $created.Face.NativeRgbaSha256 -ceq $source77Hash) 'Production face identity differs from verified source.'

    foreach ($emotionName in $emotionIds) {
        $emotion = $wireEntry.emotions | Where-Object id -CEQ $emotionName | Select-Object -First 1
        Assert-True ($null -ne $emotion) "Emotion missing from deployed manifest: $emotionName"
        for ($phase = 0; $phase -lt $eyeIds.Count; $phase++) {
            $eyeFrame = Find-Frame $emotion.eyes $eyeIds[$phase]
            for ($shape = 0; $shape -lt $mouthIds.Count; $shape++) {
                $mouthFrame = Find-Frame $emotion.mouths $mouthIds[$shape]
                $output = New-Object byte[] 16384
                $compose = Invoke-Compose $composeMethod $created.Face $emotionName $phaseType $phase $shapeType $shape $output
                Assert-True $compose.Success "Composition rejected $emotionName/$($eyeIds[$phase])/$($mouthIds[$shape]): $($compose.Error)"
                Assert-True (-not $compose.UsedNeutralFallback) "Known emotion used fallback: $emotionName"

                $expected = New-Object byte[] 16384
                [Buffer]::BlockCopy($decodedByName[$eyeFrame.assetName], 0, $expected, 0, 64 * 35 * 4)
                [Buffer]::BlockCopy($decodedByName[$mouthFrame.assetName], 0, $expected, 64 * 35 * 4, 64 * 29 * 4)
                Assert-BytesEqual $expected $output "$emotionName/$($eyeIds[$phase])/$($mouthIds[$shape]) independent half decode"
                $report.exactOutputMatches++

                for ($y = 0; $y -lt 64; $y++) {
                    for ($x = 0; $x -lt 64; $x++) {
                        if (Test-IsFeaturePixel $x $y $layout) { continue }
                        $offset = ($y * 64 + $x) * 4
                        for ($channel = 0; $channel -lt 4; $channel++) {
                            if ($output[$offset + $channel] -ne $source77[$offset + $channel]) {
                                $report.outsideFeaturePixelMismatches++
                                throw "Outside-mask source pixel changed at x=$x y=$y channel=$channel for $emotionName/$($eyeIds[$phase])/$($mouthIds[$shape])."
                            }
                        }
                        $report.outsideFeaturePixelComparisons++
                    }
                }
                $report.combinationsCompared++
            }
        }
    }

    $neutralEmotion = $wireEntry.emotions | Where-Object id -CEQ 'neutral' | Select-Object -First 1
    $neutralEye = Find-Frame $neutralEmotion.eyes 'half'
    $neutralMouth = Find-Frame $neutralEmotion.mouths 'X'
    $unknownOutput = New-Object byte[] 16384
    $unknownResult = Invoke-Compose $composeMethod $created.Face 'surprised' $phaseType 1 $shapeType 8 $unknownOutput
    Assert-True ($unknownResult.Success -and $unknownResult.UsedNeutralFallback) 'Unknown emotion did not use same-face neutral fallback.'
    $neutralExpected = New-Object byte[] 16384
    [Buffer]::BlockCopy($decodedByName[$neutralEye.assetName], 0, $neutralExpected, 0, 64 * 35 * 4)
    [Buffer]::BlockCopy($decodedByName[$neutralMouth.assetName], 0, $neutralExpected, 64 * 35 * 4, 64 * 29 * 4)
    Assert-BytesEqual $neutralExpected $unknownOutput 'Unknown emotion same-face neutral X fallback'

    $requested76 = Invoke-CreateFace $createMethod $manifest 76 $source76 $null
    Assert-True (-not $requested76.Success -and $null -eq $requested76.Face) 'Requested record 76 silently selected record 77.'
    $wrongSource = Invoke-CreateFace $createMethod $manifest 77 $source76 $validAssets
    Assert-True (-not $wrongSource.Success -and $null -eq $wrongSource.Face) 'Record 77 accepted native record 76 pixels.'
    $corruptSource = [byte[]]$source77.Clone()
    $corruptSource[0] = $corruptSource[0] -bxor 255
    $corruptSourceResult = Invoke-CreateFace $createMethod $manifest 77 $corruptSource $validAssets
    Assert-True (-not $corruptSourceResult.Success -and $null -eq $corruptSourceResult.Face) 'Corrupt record 77 source RGBA was accepted.'
    $missingSourceResult = Invoke-CreateFace $createMethod $manifest 77 $null $validAssets
    Assert-True (-not $missingSourceResult.Success -and $null -eq $missingSourceResult.Face) 'Missing native source RGBA was accepted.'
    $unknownRecord = Invoke-CreateFace $createMethod $manifest 999 $source77 $null
    Assert-True (-not $unknownRecord.Success -and $null -eq $unknownRecord.Face) 'Unknown face record did not return native fallback.'

    $corruptAssets = New-DecodedAssetList $runtimeAssembly $decodedAssetArray $assetNames[0]
    $corrupt = Invoke-CreateFace $createMethod $manifest 77 $source77 $corruptAssets
    Assert-True (-not $corrupt.Success -and $null -eq $corrupt.Face) 'Corrupt decoded RGBA asset was accepted.'
    $missingAssets = New-DecodedAssetList $runtimeAssembly @($decodedAssets | Select-Object -Skip 1) $null
    $missing = Invoke-CreateFace $createMethod $manifest 77 $source77 $missingAssets
    Assert-True (-not $missing.Success -and $null -eq $missing.Face) 'Missing texture asset was accepted.'

    $badPhase = Invoke-Compose $composeMethod $created.Face 'neutral' $phaseType 99 $shapeType 0 (New-Object byte[] 16384)
    $badShape = Invoke-Compose $composeMethod $created.Face 'neutral' $phaseType 0 $shapeType 99 (New-Object byte[] 16384)
    $badBuffer = Invoke-Compose $composeMethod $created.Face 'neutral' $phaseType 0 $shapeType 0 (New-Object byte[] 16383)
    Assert-True (-not $badPhase.Success -and -not $badShape.Success -and -not $badBuffer.Success) 'Invalid phase, shape, or destination was accepted.'
    $report.rejectionChecks['record76-request'] = 'rejected'
    $report.rejectionChecks['record77-with-record76-pixels'] = 'rejected'
    $report.rejectionChecks['corrupt-record77-source-rgba'] = 'rejected'
    $report.rejectionChecks['missing-native-source-rgba'] = 'rejected'
    $report.rejectionChecks['unknown-record'] = 'rejected'
    $report.rejectionChecks['corrupt-decoded-texture'] = 'rejected'
    $report.rejectionChecks['missing-texture'] = 'rejected'
    $report.rejectionChecks['invalid-phase-shape-destination'] = 'rejected'
    $report.rejectionChecks['unknown-emotion'] = 'same-face neutral; X retained; eye phase retained'

    Assert-True ($report.combinationsCompared -eq 162 -and $report.exactOutputMatches -eq 162) 'Not all 162 real-art combinations passed.'
    Assert-True ($report.outsideFeaturePixelMismatches -eq 0) 'Outside-mask pixels changed.'
    $report.nativeIdentity = [ordered]@{
        archive = 'CommonFaces'
        bank = 'TFAC00I0.RCI'
        record77RgbaSha256 = $source77Hash
        record76RgbaSha256 = $source76Hash
        recordsDiffer = ($source76Hash -cne $source77Hash)
    }
    $report.artifactFacts = [ordered]@{
        registrySha256 = Get-Sha256Bytes $registryBytes
        source77RgbaSha256 = $source77Hash
        source76RgbaSha256 = $source76Hash
        sourceImageSha256 = $wireEntry.sourceImageSha256
        runtimeAssemblySha256 = $runtimeAssemblyHash
        fullSerializerAssemblySha256 = $serializerAssemblyHash
        compositionSourceSha256 = Get-Sha256File $compositionSourcePath
        manifestSourceSha256 = Get-Sha256File $manifestSourcePath
        runtimeProjectSha256 = Get-Sha256File $runtimeProjectPath
        toolSha256 = Get-Sha256File $PSCommandPath
        prototypeLayoutSha256 = Get-Sha256File $layoutPath
        prototypeManifestSha256 = Get-Sha256File $prototypeManifestPath
        prototypeOriginalSha256 = Get-Sha256File $prototypeOriginalPath
        decodedAssetSha256 = [ordered]@{}
    }
    foreach ($asset in $wireEntry.emotions) {
        foreach ($frame in @($asset.eyes) + @($asset.mouths)) { $report.artifactFacts.decodedAssetSha256[$frame.assetName] = $frame.rgbaSha256 }
    }
    $report.inputsAfter = Get-ProtectedHashes $inputPaths
    foreach ($path in $report.inputsBefore.Keys) {
        Assert-True ($report.inputsBefore[$path] -ceq $report.inputsAfter[$path]) "Input changed during acceptance: $path"
    }
    $report.unchangedInputCount = $report.inputsBefore.Count
    $report.status = 'pass'
} catch {
    $report.error = $_.Exception.Message
    $report.exceptionType = $_.Exception.GetType().FullName
    $report.scriptStackTrace = $_.ScriptStackTrace
    $report.errorPosition = $_.InvocationInfo.PositionMessage
    if ($report.inputsBefore.Count -gt 0) {
        try { $report.inputsAfter = Get-ProtectedHashes @($report.inputsBefore.Keys) } catch { $report.inputHashRecheckError = $_.Exception.Message }
    }
} finally {
    if ($report.status -eq 'not-run') {
        if ($report.error) { Write-Reports 'fail' } else { Write-Reports 'incomplete' }
    } elseif ($report.status -ne 'pass') {
        Write-Reports 'fail'
    } else {
        Write-Reports 'pass'
    }
}

if ($report.status -ne 'pass') { throw "Real-art portrait acceptance failed: $($report.error)" }
Write-Output "PASS: $($report.combinationsCompared)/162 combinations; $($report.outsideFeaturePixelComparisons) outside-mask pixel comparisons, zero mismatches; $($report.decodedAssetHashMatches)/$($report.decodedAssets) asset RGBA hashes; $($report.unchangedInputCount) protected inputs unchanged."
Write-Output "Report: $reportMarkdownPath"
