
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$builder = Join-Path $PSScriptRoot 'BuildPortraitRuntimePackage.ps1'
$sync = Join-Path $PSScriptRoot 'SyncPortraitRuntimeCatalog.ps1'
if (-not (Test-Path -LiteralPath $builder)) { throw 'BuildPortraitRuntimePackage.ps1 is required.' }
if (-not (Test-Path -LiteralPath $sync)) { throw 'SyncPortraitRuntimeCatalog.ps1 is required.' }

$artRoot = Join-Path $repo 'art/portraits/mobile-mouths'
$catalogPath = Join-Path $artRoot 'split-mask-prototype/portrait-catalog.json'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$tempName = 'portrait-catalog-runtime-test-' + [Guid]::NewGuid().ToString('N')
$temp = Join-Path $tempRoot $tempName
$tempResolved = [IO.Path]::GetFullPath($temp)
$tempExpectedPrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar + 'portrait-catalog-runtime-test-'
if ($tempResolved -eq $tempRoot -or -not $tempResolved.StartsWith($tempExpectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $tempResolved.Substring($tempExpectedPrefix.Length).Length -ne 32) { throw "Generated test temp path is outside task-owned temp root: $tempResolved" }
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $output = Join-Path $temp 'package'
    & $sync -ArtRoot $artRoot -CatalogPath $catalogPath -Destination $output
    $registryPath = Join-Path $output 'portrait-split-manifest.json.bytes'
    if (-not (Test-Path -LiteralPath $registryPath)) { throw 'Catalog runtime manifest missing.' }
    $registry = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
    if ($registry.schemaVersion -ne 2) { throw "Expected runtime schema 2; got $($registry.schemaVersion)." }
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    $publishedEntries = @($catalog.entries | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.animationManifest) })
    if ($registry.portraits.Count -ne $publishedEntries.Count) {
        throw "Expected $($publishedEntries.Count) published animated portraits; got $($registry.portraits.Count)."
    }

    $requiredRecords = @($publishedEntries | ForEach-Object { [int]$_.record } | Sort-Object)
    $actualRecords = @($registry.portraits | ForEach-Object { [int]$_.record } | Sort-Object)
    if (Compare-Object $requiredRecords $actualRecords) { throw 'Catalog mode did not export every published animated portrait.' }

    foreach ($portrait in $registry.portraits) {
        if ($portrait.emotions.Count -ne 6) { throw "Portrait $($portrait.record) does not contain six emotions." }
        foreach ($emotion in $portrait.emotions) {
            if ($emotion.gazes.Count -ne 2) { throw "Portrait $($portrait.record)/$($emotion.id) must export only left and right gazes." }
            $gazeIds = @($emotion.gazes | ForEach-Object { [string]$_.id } | Sort-Object)
            if (Compare-Object @('left', 'right') $gazeIds) { throw "Portrait $($portrait.record)/$($emotion.id) has unexpected gaze ids." }
            foreach ($gaze in $emotion.gazes) {
                $phases = @($gaze.eyes | ForEach-Object { [string]$_.frame } | Sort-Object)
                if ($gaze.eyes.Count -ne 3 -or (Compare-Object @('closed', 'half', 'open') $phases)) {
                    throw "Portrait $($portrait.record)/$($emotion.id)/$($gaze.id) must contain open, half, and closed eyes."
                }
            }
            if ($emotion.mouths.Count -ne 9) { throw "Portrait $($portrait.record)/$($emotion.id) must contain nine mouth shapes." }
        }
    }

    $prototype = $registry.portraits | Where-Object { $_.record -eq 77 }
    if ($null -eq $prototype) { throw 'Catalog export omitted selected common-077 prototype.' }
    $legacyGazeIds = @($prototype.emotions[0].gazes | ForEach-Object { [string]$_.id } | Sort-Object)
    if (Compare-Object @('left', 'right') $legacyGazeIds) { throw 'Legacy nine-direction prototype must export LEFT and RIGHT only.' }
    if ($null -eq $registry.animationPolicy -or -not $registry.animationPolicySha256 -or $registry.animationPolicySha256.Length -ne 64) { throw 'Shared animation policy provenance missing.' }
    $pendingReviewCount = @($publishedEntries | Where-Object { ((([string][string]$_.status).IndexOf('pending',[StringComparison]::OrdinalIgnoreCase) -ge 0)) }).Count
    if ($pendingReviewCount -eq 0) { throw 'Test catalog must preserve its existing work-in-review statuses.' }
    foreach ($portrait in $registry.portraits) {
        $entry = $publishedEntries | Where-Object { [string]$_.id -eq [string]$portrait.catalogId }
        if ($null -eq $entry -or [string]$portrait.catalogStatus -ne [string]$entry.status) {
            throw "Catalog review status was not preserved for $($portrait.catalogId)."
        }
    }

    $prototypeEntry = $publishedEntries | Where-Object { [int]$_.record -eq 77 }
    $prototypeManifestPath = Join-Path (Split-Path -Parent $catalogPath) $prototypeEntry.animationManifest
    $prototypeManifest = Get-Content -LiteralPath $prototypeManifestPath -Raw | ConvertFrom-Json
    $prototypeNeutral = $prototype.emotions | Where-Object { $_.id -eq 'neutral' }
    $leftOpen = $prototypeNeutral.gazes | Where-Object { $_.id -eq 'left' } | ForEach-Object { $_.eyes | Where-Object { $_.frame -eq 'open' } }
    $sourceLeft = $prototypeManifest.thinkingEyeSetsByEmotion.neutral | Where-Object { $_.id -eq 'left' }
    $sourceEyePath = Join-Path (Split-Path -Parent $prototypeManifestPath) $sourceLeft.phases.open.file
    $sourceEye = [Drawing.Bitmap]::FromFile($sourceEyePath)
    $renderedEye = [Drawing.Bitmap]::FromFile((Join-Path $output $leftOpen.assetName))
    try {
        if ($null -eq $prototypeManifest.eyeEdgeBlend) {
            if ($renderedEye.GetPixel(15, 30).ToArgb() -ne $sourceEye.GetPixel(15, 30).ToArgb()) {
                throw 'Unblended catalog eye differs from its selected manifest source.'
            }
        }
        $untouched = $renderedEye.GetPixel(0, 0)
        $rawPixel = $sourceEye.GetPixel(0, 0)
        if ($untouched.ToArgb() -ne $rawPixel.ToArgb()) { throw 'Eye edge blend changed pixels outside configured regions.' }
    }
    finally { $sourceEye.Dispose(); $renderedEye.Dispose() }
    $reportPath = Join-Path $output 'portrait-runtime-catalog-build-report.json'
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $gallerySources = @('preview.js', 'blink-policy.mjs', 'eye-hold-policy.mjs', 'idle-thinking-policy.mjs',
        'thinking-gaze-policy.mjs', 'portrait-behavior-policy.mjs', 'shared-voice-policy.mjs',
        'eye-edge-blend-policy.mjs', 'portrait-behavior-policy.json')
    foreach ($sourceName in $gallerySources) {
        $modulePath = Join-Path (Split-Path -Parent $catalogPath) $sourceName
        $expectedModuleHash = (Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $moduleReport = $report.animationPolicySources | Where-Object { $_.name -eq $sourceName }
        $moduleSnapshot = $report.sourceSnapshots | Where-Object { $_.path -eq $modulePath }
        if ($null -eq $moduleReport -or $moduleReport.sha256 -ne $expectedModuleHash -or
            $null -eq $moduleSnapshot -or $moduleSnapshot.sha256 -ne $expectedModuleHash) {
            throw "Gallery behavior source provenance missing or stale: $sourceName"
        }
    }
    $policyPath = Join-Path (Split-Path -Parent $catalogPath) 'portrait-behavior-policy.json'
    if ($registry.animationPolicySha256 -ne (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Runtime policy hash does not match exact source JSON bytes.'
    }
    $eyeReport = $report.assetHashes | Where-Object { $_.assetName -eq $leftOpen.assetName }
    if ($null -eq $eyeReport) { throw 'Build report omitted the selected prototype eye.' }
    if ($null -eq $prototypeManifest.eyeEdgeBlend) {
        if ($eyeReport.sourceSha256 -ne $eyeReport.renderedSha256 -or
            $eyeReport.sourceRgbaSha256 -ne $eyeReport.renderedRgbaSha256) {
            throw 'Unblended catalog eye must preserve source bytes and pixels.'
        }
    }
    elseif ($eyeReport.sourceSha256 -eq $eyeReport.renderedSha256 -or
        $eyeReport.sourceRgbaSha256 -eq $eyeReport.renderedRgbaSha256) {
        throw 'Build report must distinguish gallery-blended eye source and rendered hashes.'
    }
    $rawMouth = $prototypeNeutral.mouths | Where-Object { $_.frame -eq 'A' }
    $rawMouthReport = $report.assetHashes | Where-Object { $_.assetName -eq $rawMouth.assetName }
    if ($null -eq $rawMouthReport -or $rawMouthReport.sourceSha256 -ne $rawMouthReport.renderedSha256) {
        throw 'Mouth source bytes must remain unmodified by catalog export.'
    }

    # Build one catalog-only fixture to prove new published entries flow through sync without
    # changing runtime-selection.json or treating review-pending as approval.
    $fixtureArtRoot = Join-Path $temp 'fixture-art'
    $fixtureCatalogDirectory = Join-Path $fixtureArtRoot 'split-mask-prototype'
    New-Item -ItemType Directory -Path $fixtureCatalogDirectory -Force | Out-Null
    $sourceEntry = $publishedEntries | Where-Object { $_.record -eq 72 }
    if ($null -eq $sourceEntry) { throw 'Test catalog is missing common-072 as the newly published fixture.' }
    $sourceManifestPath = Join-Path (Split-Path -Parent $catalogPath) $sourceEntry.animationManifest
    $sourceManifestDirectory = Split-Path -Parent $sourceManifestPath
    $sourceManifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
    $fixtureFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    [void]$fixtureFiles.Add((Join-Path (Split-Path -Parent $catalogPath) $sourceEntry.original))
    [void]$fixtureFiles.Add((Join-Path $sourceManifestDirectory $sourceManifest.original))
    foreach ($set in $sourceManifest.eyeSets) {
        foreach ($phase in @('open', 'half', 'closed')) { [void]$fixtureFiles.Add((Join-Path $sourceManifestDirectory $set.phases.$phase.file)) }
    }
    foreach ($set in $sourceManifest.mouthSets) {
        foreach ($frame in $set.frames) { [void]$fixtureFiles.Add((Join-Path $sourceManifestDirectory $frame.file)) }
    }
    foreach ($emotionProperty in $sourceManifest.thinkingEyeSetsByEmotion.PSObject.Properties) {
        foreach ($gaze in $emotionProperty.Value) {
            foreach ($phase in @('open', 'half', 'closed')) { [void]$fixtureFiles.Add((Join-Path $sourceManifestDirectory $gaze.phases.$phase.file)) }
        }
    }
    foreach ($sourceName in @('portrait-behavior-policy.json', 'preview.js', 'blink-policy.mjs', 'eye-hold-policy.mjs',
        'idle-thinking-policy.mjs', 'thinking-gaze-policy.mjs', 'portrait-behavior-policy.mjs', 'shared-voice-policy.mjs',
        'eye-edge-blend-policy.mjs')) {
        [void]$fixtureFiles.Add((Join-Path (Split-Path -Parent $catalogPath) $sourceName))
    }
    [void]$fixtureFiles.Add($sourceManifestPath)
    foreach ($sourcePath in $fixtureFiles) {
        $sourceFullPath = [IO.Path]::GetFullPath($sourcePath)
        $artPrefix = [IO.Path]::GetFullPath($artRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $sourceFullPath.StartsWith($artPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Fixture source escapes art root: $sourceFullPath" }
        $relativePath = $sourceFullPath.Substring($artPrefix.Length)
        $fixturePath = Join-Path $fixtureArtRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $fixturePath) -Force | Out-Null
        Copy-Item -LiteralPath $sourceFullPath -Destination $fixturePath -Force
    }
    $fixtureCatalogPath = Join-Path $fixtureCatalogDirectory 'portrait-catalog.json'
    @{ schemaVersion = 1; entries = @($sourceEntry) } | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $fixtureCatalogPath -Encoding UTF8
    $newPortraitPackage = Join-Path $temp 'new-published-portrait'
    & $sync -ArtRoot $fixtureArtRoot -CatalogPath $fixtureCatalogPath -Destination $newPortraitPackage
    $newPortraitRegistry = Get-Content -LiteralPath (Join-Path $newPortraitPackage 'portrait-split-manifest.json.bytes') -Raw | ConvertFrom-Json
    if ($newPortraitRegistry.schemaVersion -ne 2 -or $newPortraitRegistry.portraits.Count -ne 1 -or
        $newPortraitRegistry.portraits[0].record -ne 72 -or
        $newPortraitRegistry.portraits[0].catalogStatus -ne [string]$sourceEntry.status) {
        throw 'Catalog sync did not include the pending newly published portrait with its original review status.'
    }

    # A catalog reference that resolves outside ArtRoot must fail before writing a package.
    $badCatalogPath = Join-Path $fixtureCatalogDirectory 'bad-path-catalog.json'
    $badEntry = $sourceEntry | ConvertTo-Json -Depth 16 | ConvertFrom-Json
    $badEntry.animationManifest = '../../outside/manifest.json'
    @{ schemaVersion = 1; entries = @($badEntry) } | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $badCatalogPath -Encoding UTF8
    $badOutput = Join-Path $temp 'bad-path-package'
    $badPathRejected = $false
    try { & $sync -ArtRoot $fixtureArtRoot -CatalogPath $badCatalogPath -Destination $badOutput }
    catch { $badPathRejected = ((([string]$_.Exception.Message).IndexOf('escapes art root',[StringComparison]::OrdinalIgnoreCase) -ge 0)) }
    if (-not $badPathRejected -or (Test-Path -LiteralPath $badOutput)) { throw 'Catalog path escape was not rejected before package output.' }

    # Mutate a snapshotted input only after staging begins; the builder must reject the build
    # without replacing a pre-existing destination file.
    $conflictDestination = Join-Path $temp 'conflict-package'
    New-Item -ItemType Directory -Path $conflictDestination | Out-Null
    $sentinelPath = Join-Path $conflictDestination 'keep.txt'
    [IO.File]::WriteAllText($sentinelPath, 'preserve')
    $sentinelHash = (Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash
    $stageObserved = $false
    $conflictJob = $null
    $childStartedUtc = [DateTime]::UtcNow
    try {
        $conflictJob = Start-Job -ArgumentList @($builder, $fixtureArtRoot, $fixtureCatalogPath, $conflictDestination) -ScriptBlock {
            param($builderPath, $fixtureRoot, $catalogPath, $destinationPath)
            $ErrorActionPreference = 'Stop'
            & $builderPath -ArtRoot $fixtureRoot -CatalogPath $catalogPath -Destination $destinationPath
        }
        for ($attempt = 0; $attempt -lt 3000 -and $conflictJob.State -notin @('Completed', 'Failed', 'Stopped'); $attempt++) {
            $stage = Get-ChildItem -LiteralPath $tempRoot -Directory -Filter 'portrait-runtime-stage1-*' -ErrorAction SilentlyContinue |
                Where-Object { $_.CreationTimeUtc -ge $childStartedUtc } | Select-Object -First 1
            if ($null -ne $stage) {
                [IO.File]::AppendAllText((Join-Path $fixtureCatalogDirectory 'portrait-behavior-policy.json'), "`n")
                $stageObserved = $true
                break
            }
            Start-Sleep -Milliseconds 20
        }
        if (-not $stageObserved) {
            $jobReason = $conflictJob.ChildJobs[0].JobStateInfo.Reason
            throw "Could not observe staging directory for the input mutation test (state=$($conflictJob.State)): $jobReason"
        }
        $finishedJob = Wait-Job -Job $conflictJob -Timeout 60
        if ($null -eq $finishedJob) { throw 'Input mutation fixture did not finish in time.' }
    }
    finally {
        if ($null -ne $conflictJob) {
            if ($conflictJob.State -notin @('Completed', 'Failed', 'Stopped')) { Stop-Job -Job $conflictJob }
        }
    }
    $conflictOutput = @(Receive-Job -Job $conflictJob -ErrorAction SilentlyContinue 2>&1 | ForEach-Object { [string]$_ }) -join "`n"
    $conflictReason = [string]$conflictJob.ChildJobs[0].JobStateInfo.Reason
    $conflictState = [string]$conflictJob.State
    Remove-Job -Job $conflictJob -Force
    if ($conflictState -ne 'Failed' -or (-not ((([string]($conflictOutput + $conflictReason)).IndexOf('Portrait package input changed during build',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or
        (Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash -ne $sentinelHash -or
        (Test-Path -LiteralPath (Join-Path $conflictDestination 'portrait-split-manifest.json.bytes'))) {
        throw 'Input mutation did not fail safely before package promotion.'
    }

    Write-Output "PASS portrait catalog runtime package: $($registry.portraits.Count) portraits, schema $($registry.schemaVersion), $pendingReviewCount review-pending entries preserved."
}
finally {
    $cleanupTemp = [IO.Path]::GetFullPath($temp)
    if ($cleanupTemp -eq $tempRoot -or -not $cleanupTemp.StartsWith($tempExpectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $cleanupTemp.Substring($tempExpectedPrefix.Length).Length -ne 32) { throw "Refusing unsafe test temp cleanup: $cleanupTemp" }
    Remove-Item -LiteralPath $cleanupTemp -Recurse -Force
}
