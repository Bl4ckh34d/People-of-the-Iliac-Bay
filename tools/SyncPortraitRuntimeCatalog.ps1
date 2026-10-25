param(
    [string]$ArtRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'art/portraits/mobile-mouths'),
    [string]$CatalogPath,
    [string]$SelectionPath,
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'
if ($PSBoundParameters.ContainsKey('CatalogPath') -and $PSBoundParameters.ContainsKey('SelectionPath')) {
    throw 'Specify either CatalogPath or SelectionPath, not both.'
}
$repo = Split-Path $PSScriptRoot -Parent
$builder = Join-Path $PSScriptRoot 'BuildPortraitRuntimePackage.ps1'
if (-not (Test-Path -LiteralPath $builder -PathType Leaf)) { throw 'BuildPortraitRuntimePackage.ps1 is required.' }

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Test-PathWithin([string]$Path, [string]$Root) {
    $value = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $container = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $value.Equals($container, [StringComparison]::OrdinalIgnoreCase) -or
        $value.StartsWith($container + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Test-PortraitCache([string]$ReceiptPath, [string]$ResolvedArtRoot, [string]$ResolvedCatalog,
    [string]$ResolvedDestination) {
    try {
        if (-not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) { return $false }
        $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json -ErrorAction Stop
        if ($receipt.schemaVersion -ne 1 -or $receipt.sourceMode -ne 'catalog' -or
            -not [string]::Equals([string]$receipt.artRoot, $ResolvedArtRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$receipt.catalogPath, $ResolvedCatalog, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$receipt.destination, $ResolvedDestination, [StringComparison]::OrdinalIgnoreCase) -or
            $receipt.builderSha256 -ne (Get-Sha256 $builder) -or $receipt.syncSha256 -ne (Get-Sha256 $PSCommandPath) -or
            $null -eq $receipt.sourceSnapshots -or @($receipt.sourceSnapshots).Count -eq 0 -or
            $null -eq $receipt.assetHashes -or @($receipt.assetHashes).Count -eq 0) { return $false }
        $reportPath = Join-Path $ResolvedDestination 'portrait-runtime-catalog-build-report.json'
        $registryPath = Join-Path $ResolvedDestination 'portrait-split-manifest.json.bytes'
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf) -or (Get-Sha256 $reportPath) -ne $receipt.reportSha256 -or
            -not (Test-Path -LiteralPath $registryPath -PathType Leaf) -or (Get-Sha256 $registryPath) -ne $receipt.registrySha256) { return $false }
        foreach ($snapshot in @($receipt.sourceSnapshots)) {
            $path = [IO.Path]::GetFullPath([string]$snapshot.path)
            if (-not (Test-PathWithin $path $ResolvedArtRoot) -or -not (Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-Sha256 $path) -ne [string]$snapshot.sha256) { return $false }
        }
        $expected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($asset in @($receipt.assetHashes)) {
            $name = [string]$asset.assetName
            if ([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::GetFileName($name) -ne $name -or
                -not $name.StartsWith('portrait-', [StringComparison]::Ordinal) -or
                -not $name.EndsWith('.png.bytes', [StringComparison]::Ordinal) -or -not $expected.Add($name)) { return $false }
            $path = Join-Path $ResolvedDestination $name
            if (-not (Test-PathWithin $path $ResolvedDestination) -or -not (Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-Sha256 $path) -ne [string]$asset.renderedSha256) { return $false }
        }
        $actual = @(Get-ChildItem -LiteralPath $ResolvedDestination -Filter 'portrait-*.png.bytes' -File | ForEach-Object Name)
        if ($actual.Count -ne $expected.Count) { return $false }
        foreach ($name in $actual) { if (-not $expected.Contains($name)) { return $false } }
        return $true
    }
    catch { return $false }
}

$resolvedArtRoot = [IO.Path]::GetFullPath($ArtRoot)
$resolvedDestination = [IO.Path]::GetFullPath($Destination)
$cachePath = Join-Path $repo 'work/portrait-runtime-cache-v1.json'

if ($PSBoundParameters.ContainsKey('SelectionPath')) {
    & $builder -ArtRoot $ArtRoot -SelectionPath $SelectionPath -Destination $Destination
}
else {
    if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
        $CatalogPath = Join-Path $ArtRoot 'split-mask-prototype/portrait-catalog.json'
    }
    $resolvedCatalog = [IO.Path]::GetFullPath($CatalogPath)
    if (Test-PortraitCache $cachePath $resolvedArtRoot $resolvedCatalog $resolvedDestination) {
        $cached = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
        Write-Output "Portrait runtime cache hit: reused $($cached.portraitCount) portrait entries and $(@($cached.assetHashes).Count) unique textures at $resolvedDestination"
        return
    }
    & $builder -ArtRoot $ArtRoot -CatalogPath $CatalogPath -Destination $Destination
    $reportPath = Join-Path $resolvedDestination 'portrait-runtime-catalog-build-report.json'
    $registryPath = Join-Path $resolvedDestination 'portrait-split-manifest.json.bytes'
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -ErrorAction Stop
    if ($report.schemaVersion -ne 2 -or $report.sourceMode -ne 'catalog' -or @($report.sourceSnapshots).Count -eq 0 -or
        @($report.assetHashes).Count -eq 0 -or -not (Test-Path -LiteralPath $registryPath -PathType Leaf)) {
        throw 'Portrait package completed without a cacheable catalog report.'
    }
    $receipt = [ordered]@{
        schemaVersion = 1
        sourceMode = 'catalog'
        artRoot = $resolvedArtRoot
        catalogPath = $resolvedCatalog
        destination = $resolvedDestination
        builderSha256 = Get-Sha256 $builder
        syncSha256 = Get-Sha256 $PSCommandPath
        reportSha256 = Get-Sha256 $reportPath
        registrySha256 = Get-Sha256 $registryPath
        portraitCount = [int]$report.portraitCount
        sourceSnapshots = @($report.sourceSnapshots)
        assetHashes = @($report.assetHashes)
    }
    $cacheDirectory = Split-Path $cachePath -Parent
    New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
    $temporary = Join-Path $cacheDirectory ('.portrait-runtime-cache-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, ($receipt | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $cachePath -Force
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
