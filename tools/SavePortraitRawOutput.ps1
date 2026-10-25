[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PortraitRoot,
    [Parameter(Mandatory)][ValidateSet('eyes','mouth')][string]$Part,
    [Parameter(Mandatory)][ValidateSet('neutral','happy','angry','sad','afraid','guarded')][string]$Emotion,
    [Parameter(Mandatory)][string]$GeneratedPath,
    [Parameter(Mandatory)][string]$RequestJsonPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-PngInfo([string]$Path,[string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label file missing: $Path" }
    if ([IO.Path]::GetExtension($Path) -ine '.png') { throw "$Label must use .png extension: $Path" }
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 8) { throw "$Label is not a readable PNG: $Path" }
        $signature = New-Object byte[] 8
        $read = $stream.Read($signature,0,8)
        $expected = [byte[]](137,80,78,71,13,10,26,10)
        if ($read -ne 8) { throw "$Label is not a readable PNG: $Path" }
        for ($i=0; $i -lt 8; $i++) { if ($signature[$i] -ne $expected[$i]) { throw "$Label is not a PNG: $Path" } }
    } finally { $stream.Dispose() }
    $bitmap = [Drawing.Bitmap]::FromFile($Path)
    try {
        if ($bitmap.Width -le 0 -or $bitmap.Height -le 0) { throw "$Label has invalid dimensions: $Path" }
        return [ordered]@{width=$bitmap.Width;height=$bitmap.Height}
    } finally { $bitmap.Dispose() }
}

function Assert-AspectRatio([hashtable]$Info,[double]$Expected,[string]$Label) {
    $actual = [double]$Info.width / [double]$Info.height
    if ([Math]::Abs(($actual / $Expected) - 1.0) -gt 0.01) {
        throw "$Label aspect ratio must be within 1% of $Expected (actual $actual)"
    }
}

$root = [IO.Path]::GetFullPath($PortraitRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "PortraitRoot must exist: $root" }
$originalPath = Join-Path $root 'original.png'
$layoutPath = Join-Path $root 'layout.json'
$referenceName = if ($Part -eq 'eyes') { 'eyes-reference-grid.png' } else { 'mouth-reference-grid.png' }
$referencePath = Join-Path $root $referenceName
$originalInfo = Get-PngInfo $originalPath 'Source original'
if ($originalInfo.width -ne 64 -or $originalInfo.height -ne 64) { throw 'Source original must be native 64x64' }
if (-not (Test-Path -LiteralPath $layoutPath -PathType Leaf)) { throw "Portrait layout missing: $layoutPath" }
$layout = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([int]$layout.width -ne 64 -or [int]$layout.height -ne 64 -or [int]$layout.splitY -ne 35) { throw 'Portrait layout must declare 64x64 and splitY=35' }
if ($null -eq $layout.portraitRecord -or [string]::IsNullOrWhiteSpace([string]$layout.bank)) { throw 'Portrait layout must declare portraitRecord and bank' }
if ($Part -eq 'eyes' -and @($layout.eyeRegions).Count -ne 2) { throw 'Portrait layout must declare two eye regions' }
if ($Part -eq 'mouth' -and $null -eq $layout.mouthRegion) { throw 'Portrait layout must declare mouthRegion' }
$referenceInfo = Get-PngInfo $referencePath 'Reference grid'
if (-not [IO.Path]::IsPathRooted($GeneratedPath)) { throw 'GeneratedPath must be absolute' }
$generatedFullPath = [IO.Path]::GetFullPath($GeneratedPath)
$generatedInfo = Get-PngInfo $generatedFullPath 'Generated output'
$expectedAspect = if ($Part -eq 'eyes') { 64.0 / 35.0 } else { 64.0 / 29.0 }
Assert-AspectRatio $referenceInfo $expectedAspect 'Reference grid'
Assert-AspectRatio $generatedInfo $expectedAspect 'Generated output'
if (-not (Test-Path -LiteralPath $RequestJsonPath -PathType Leaf)) { throw "Request JSON missing: $RequestJsonPath" }
$request = Get-Content -LiteralPath $RequestJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($request.prompt -isnot [string] -or $request.output_hint -isnot [string]) { throw 'Request JSON prompt and output_hint must be JSON strings' }
$prompt = $request.prompt
$outputHint = $request.output_hint
if ([string]::IsNullOrWhiteSpace($prompt) -or [string]::IsNullOrWhiteSpace($outputHint)) { throw 'Prompt and output_hint must be nonempty strings' }
if (-not $outputHint.Contains($GeneratedPath)) { throw "GeneratedPath does not appear exactly in output_hint: $GeneratedPath" }

$emotionRoot = [IO.Path]::GetFullPath((Join-Path (Join-Path $root $Part) $Emotion))
$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $emotionRoot.StartsWith($rootPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Computed output directory escapes PortraitRoot' }
$rawPath = Join-Path $emotionRoot 'raw.png'
$provenancePath = Join-Path $emotionRoot 'provenance.json'
if (Test-Path -LiteralPath $rawPath) { throw "Refusing to overwrite existing raw output: $rawPath" }
if (Test-Path -LiteralPath $provenancePath) { throw "Refusing to overwrite existing provenance: $provenancePath" }

$sourceHash = Get-Sha256 $originalPath
$referenceHash = Get-Sha256 $referencePath
$generatedHash = Get-Sha256 $generatedFullPath
$rawCreated = $false
$provenanceCreated = $false
try {
    New-Item -ItemType Directory -Path $emotionRoot -Force | Out-Null
    Copy-Item -LiteralPath $generatedFullPath -Destination $rawPath
    $rawCreated = $true
    $rawInfo = Get-PngInfo $rawPath 'Copied raw output'
    $rawHash = Get-Sha256 $rawPath
    if ($rawHash -ne $generatedHash -or $rawInfo.width -ne $generatedInfo.width -or $rawInfo.height -ne $generatedInfo.height) {
        throw 'Copied raw output differs from generated PNG'
    }
    $provenance = [ordered]@{
        schemaVersion = 1
        createdUtc = (Get-Date).ToUniversalTime().ToString('o')
        portraitRecord = $layout.portraitRecord
        bank = [string]$layout.bank
        part = $Part
        emotion = $Emotion
        prompt = $prompt
        outputHint = $outputHint
        generatedPath = $generatedFullPath
        sourceOriginal = [ordered]@{path='original.png';sha256=$sourceHash;width=$originalInfo.width;height=$originalInfo.height}
        referenceGrid = [ordered]@{path=$referenceName;sha256=$referenceHash;width=$referenceInfo.width;height=$referenceInfo.height}
        generator = [ordered]@{path=$generatedFullPath;sha256=$generatedHash;width=$generatedInfo.width;height=$generatedInfo.height}
        raw = [ordered]@{path=($Part+'/'+$Emotion+'/raw.png');sha256=$rawHash;width=$rawInfo.width;height=$rawInfo.height}
    }
    $provenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $provenancePath -Encoding UTF8
    $provenanceCreated = $true
    "SAVE_PORTRAIT_RAW_PASS part=$Part emotion=$Emotion sha256=$rawHash size=$($rawInfo.width)x$($rawInfo.height)"
} catch {
    if ($provenanceCreated -and (Test-Path -LiteralPath $provenancePath)) { Remove-Item -LiteralPath $provenancePath -Force }
    if ($rawCreated -and (Test-Path -LiteralPath $rawPath)) { Remove-Item -LiteralPath $rawPath -Force }
    throw
}
