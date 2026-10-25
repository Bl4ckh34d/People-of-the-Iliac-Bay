$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$builder = Join-Path $PSScriptRoot 'BuildPortraitRuntimePackage.ps1'
if (-not (Test-Path -LiteralPath $builder)) { throw 'BuildPortraitRuntimePackage.ps1 is required.' }

$artRoot = Join-Path $repo 'art/portraits/mobile-mouths'
$selection = Get-Content -LiteralPath (Join-Path $artRoot 'runtime-selection.json') -Raw | ConvertFrom-Json
$nativeHash = $selection.entries[0].nativeRgbaSha256
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$tempName = 'portrait-runtime-stage1-test-' + [Guid]::NewGuid().ToString('N')
$temp = Join-Path $tempRoot $tempName
$tempResolved = [IO.Path]::GetFullPath($temp)
$tempExpectedPrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar + 'portrait-runtime-stage1-test-'
if ($tempResolved -eq $tempRoot -or -not $tempResolved.StartsWith($tempExpectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $tempResolved.Substring($tempExpectedPrefix.Length).Length -ne 32) { throw "Generated test temp path is outside task-owned temp root: $tempResolved" }
New-Item -ItemType Directory -Path $temp | Out-Null
$junctionPath = $null
$destinationJunction = $null
try {
    $fixtureArt = Join-Path $temp 'art'
    $fixtureManifestRel = [string]$selection.entries[0].manifest
    $sourceManifestPath = Join-Path $artRoot $fixtureManifestRel
    $sourceManifestDirectory = Split-Path -Parent $sourceManifestPath
    $fixtureManifestPath = Join-Path $fixtureArt $fixtureManifestRel
    $manifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
    $fixtureManifest = $manifest | ConvertTo-Json -Depth 32 | ConvertFrom-Json
    $fixtureOriginal = Join-Path $sourceManifestDirectory $manifest.original
    $fixtureOriginalPath = Join-Path (Split-Path -Parent $fixtureManifestPath) $manifest.original
    New-Item -ItemType Directory -Path (Split-Path -Parent $fixtureOriginalPath) -Force | Out-Null
    Copy-Item -LiteralPath $fixtureOriginal -Destination $fixtureOriginalPath
    $sourceFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($set in $manifest.eyeSets) {
        foreach ($phase in @('open', 'half', 'closed')) { [void]$sourceFiles.Add([string]$set.phases.$phase.file) }
    }
    foreach ($set in $manifest.mouthSets) {
        foreach ($frame in $set.frames) { [void]$sourceFiles.Add([string]$frame.file) }
    }
    foreach ($relative in $sourceFiles) {
        $source = Join-Path $sourceManifestDirectory $relative
        $target = Join-Path (Split-Path -Parent $fixtureManifestPath) $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    $fixtureManifest | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $fixtureManifestPath -Encoding UTF8
    $selectionPath = Join-Path $artRoot 'runtime-selection.json'
    $output = Join-Path $temp 'valid'
    & $builder -ArtRoot $fixtureArt -SelectionPath $selectionPath -Destination $output
    $textures = @(Get-ChildItem -LiteralPath $output -Filter 'portrait-common-077-*.png.bytes' -File)
    if ($textures.Count -ne 72) { throw "Expected 72 selected texture files; got $($textures.Count)." }
    $registryPath = Join-Path $output 'portrait-split-manifest.json.bytes'
    if (-not (Test-Path -LiteralPath $registryPath)) { throw 'Runtime manifest missing.' }
    $registry = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
    if ($registry.schemaVersion -ne 1 -or $registry.portraits.Count -ne 1 -or $registry.portraits[0].record -ne 77) {
        throw 'Runtime manifest identity or schema mismatch.'
    }
    $listed = @($registry.portraits[0].emotions | ForEach-Object { @($_.eyes) + @($_.mouths) })
    if ($listed.Count -ne 72) { throw "Expected 72 manifest texture references; got $($listed.Count)." }

    $failedCases = New-Object 'System.Collections.Generic.List[string]'
    function Assert-PackageFails([string]$name, [object]$selected, [string]$selectionOverride = $null, [string]$destinationOverride = $null) {
        $caseSelection = Join-Path $temp ($name + '.json')
        if ($selectionOverride) { $selectionOverride | Set-Content -LiteralPath $caseSelection -Encoding UTF8 }
        else { $selected | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $caseSelection -Encoding UTF8 }
        $caseOutput = if ($destinationOverride) { $destinationOverride } else { Join-Path $temp ($name + '-output') }
        $outputExistedBefore = Test-Path -LiteralPath $caseOutput
        $existingPortraitFiles = @()
        if ($outputExistedBefore -and (Test-Path -LiteralPath $caseOutput -PathType Container)) {
            $existingPortraitFiles = @(Get-ChildItem -LiteralPath $caseOutput -Filter 'portrait-*' -File | ForEach-Object { $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
        }
        try {
            & $builder -ArtRoot $fixtureArt -SelectionPath $caseSelection -Destination $caseOutput
            throw "Invalid package case accepted: $name"
        }
        catch { if ($_.Exception.Message -eq "Invalid package case accepted: $name") { throw } }
        if (-not $outputExistedBefore -and (Test-Path -LiteralPath $caseOutput)) { throw "Invalid package case wrote output: $name" }
        if ($outputExistedBefore -and (Test-Path -LiteralPath $caseOutput -PathType Container)) {
            $existingPortraitFilesAfter = @(Get-ChildItem -LiteralPath $caseOutput -Filter 'portrait-*' -File | ForEach-Object { $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
            if (Compare-Object $existingPortraitFiles $existingPortraitFilesAfter) { throw "Invalid package case changed source destination files: $name" }
        }
        $failedCases.Add($name)
    }
    function Save-FixtureManifest([object]$value) { $value | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $fixtureManifestPath -Encoding UTF8 }
    function Fresh-Manifest() { return ($manifest | ConvertTo-Json -Depth 32 | ConvertFrom-Json) }

    $wrongHash = $selection | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $wrongHash.entries[0].sourceImageSha256 = '0' * 64
    Assert-PackageFails 'wrong-hash' $wrongHash
    $wrongNativeHash = $selection | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $wrongNativeHash.entries[0].nativeRgbaSha256 = '0' * 64
    if ($wrongNativeHash.entries[0].nativeRgbaSha256 -eq $nativeHash) { throw 'Native hash fixture must differ from source identity.' }
    Assert-PackageFails 'wrong-native-rgba-hash' $wrongNativeHash
    $outside = $selection | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $outside.entries[0].manifest = '../outside.json'
    Assert-PackageFails 'out-of-root' $outside
    $sourceOverlap = Join-Path $fixtureArt 'must-not-be-created'
    Assert-PackageFails 'destination-inside-source' $selection $null $sourceOverlap
    if (Test-Path -LiteralPath $sourceOverlap) { throw 'Invalid destination created files inside source tree.' }
    Assert-PackageFails 'destination-is-source-root' $selection $null $fixtureArt

    $bad = Fresh-Manifest
    $bad.splitY = 34
    Save-FixtureManifest $bad
    Assert-PackageFails 'unsupported-split' $selection
    $bad = Fresh-Manifest
    $bad.eyeSets[0].phases.open.file = 'eyes/neutral/missing.png'
    Save-FixtureManifest $bad
    Assert-PackageFails 'missing-image' $selection
    $bad = Fresh-Manifest
    $bad.eyeSets[0].phases.half.file = $bad.eyeSets[0].phases.open.file
    Save-FixtureManifest $bad
    Assert-PackageFails 'duplicate-image' $selection
    $bad = Fresh-Manifest
    $bad.eyeSets[0].phases.open.file = '../../../outside.png'
    Save-FixtureManifest $bad
    Assert-PackageFails 'out-of-root-image' $selection
    $bad = Fresh-Manifest
    $bad.eyeSets[0].phases.open.file = $bad.mouthSets[0].frames[0].file
    Save-FixtureManifest $bad
    Assert-PackageFails 'wrong-image-size' $selection

    $outsideDirectory = Join-Path $temp 'outside-target'
    New-Item -ItemType Directory -Path $outsideDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $fixtureManifestPath) $manifest.eyeSets[0].phases.open.file) -Destination (Join-Path $outsideDirectory 'open.png')
    $junctionPath = Join-Path (Split-Path -Parent $fixtureManifestPath) 'linked-outside'
    $junctionCreated = $false
    try {
        New-Item -ItemType Junction -Path $junctionPath -Target $outsideDirectory -ErrorAction Stop | Out-Null
        $junctionCreated = $true
    }
    catch { $junctionPath = $null }
    if ($junctionCreated) {
        $bad = Fresh-Manifest
        $bad.eyeSets[0].phases.open.file = 'linked-outside/open.png'
        Save-FixtureManifest $bad
        Assert-PackageFails 'reparse-point-escape' $selection

        $destinationJunction = Join-Path $temp 'linked-destination'
        New-Item -ItemType Junction -Path $destinationJunction -Target $outsideDirectory -ErrorAction Stop | Out-Null
        Assert-PackageFails 'destination-reparse-point' $selection $null (Join-Path $destinationJunction 'package')
    }

    $report = @{
        status = 'pass'
        command = 'powershell -NoProfile -ExecutionPolicy Bypass -File tools/TestPortraitRuntimePackage.ps1'
        packagedTextureCount = $textures.Count
        registryReferenceCount = $listed.Count
        rejectedCases = @($failedCases.ToArray())
        reparsePointCase = $(if ($junctionCreated) { 'rejected' } else { 'skipped-unavailable' })
    }
    $reportPath = Join-Path $repo 'work/portrait-runtime-stage1-package-report.json'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $reportPath)) | Out-Null
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Output "PASS portrait runtime package: $($textures.Count) textures; rejected $($failedCases.Count) invalid fixtures."
}
finally {
    if ($junctionPath -and (Test-Path -LiteralPath $junctionPath)) { [IO.Directory]::Delete($junctionPath, $false) }
    if ($destinationJunction -and (Test-Path -LiteralPath $destinationJunction)) { [IO.Directory]::Delete($destinationJunction, $false) }
    $cleanupTemp = [IO.Path]::GetFullPath($temp)
    if ($cleanupTemp -eq $tempRoot -or -not $cleanupTemp.StartsWith($tempExpectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $cleanupTemp.Substring($tempExpectedPrefix.Length).Length -ne 32) { throw "Refusing unsafe test temp cleanup: $cleanupTemp" }
    Remove-Item -LiteralPath $cleanupTemp -Recurse -Force
}
