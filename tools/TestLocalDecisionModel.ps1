$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')

$resolverPath = Join-Path $PSScriptRoot 'ResolveLocalDecisionModel.ps1'
. $resolverPath

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Assert-Equal($actual, $expected, [string]$message) {
    if ($actual -cne $expected) { throw "$message (expected '$expected'; found '$actual')" }
}

function Assert-Throws([scriptblock]$action, [string]$message) {
    $threw = $false
    try { & $action }
    catch { $threw = $true }
    if (-not $threw) { throw $message }
}

function Write-Selection([string]$path, [string]$json) {
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
}

function Assert-Profile($actual, [string]$profile, [string]$modelRelativePath,
    [string]$projectorRelativePath, [string]$modelHash, [string]$projectorHash,
    [long]$modelBytes, [long]$projectorBytes, [string]$runtimeRoot) {
    Assert-Equal $actual.Profile $profile "$profile profile name mismatch"
    Assert-Equal ([IO.Path]::GetFullPath([string]$actual.ModelPath)) `
        ([IO.Path]::GetFullPath((Join-Path $runtimeRoot $modelRelativePath))) "$profile model path mismatch"
    Assert-Equal ([IO.Path]::GetFullPath([string]$actual.ProjectorPath)) `
        ([IO.Path]::GetFullPath((Join-Path $runtimeRoot $projectorRelativePath))) "$profile projector path mismatch"
    Assert-Equal ([string]$actual.ModelHash).ToUpperInvariant() $modelHash "$profile model hash mismatch"
    Assert-Equal ([string]$actual.ProjectorHash).ToUpperInvariant() $projectorHash "$profile projector hash mismatch"
    Assert-Equal ([long]$actual.ModelBytes) $modelBytes "$profile model byte count mismatch"
    Assert-Equal ([long]$actual.ProjectorBytes) $projectorBytes "$profile projector byte count mismatch"

    $rootPrefix = [IO.Path]::GetFullPath($runtimeRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    foreach ($artifactPath in @([string]$actual.ModelPath, [string]$actual.ProjectorPath)) {
        $resolvedPath = [IO.Path]::GetFullPath($artifactPath)
        Assert-True $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) `
            "$profile registry path escaped the fake runtime root: $resolvedPath"
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('local-decision-model-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $runtimeRoot = Join-Path $tempRoot 'local-llm'
    New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
    $selectionPath = Join-Path $runtimeRoot 'active-decision-model.json'

    # Resolve registry metadata only: the fake runtime deliberately contains no model files.
    $mini = @{
        Profile = 'mini'
        ModelRelativePath = 'models/d3-mini/vllm-d3-mini-Q4_K_M.gguf'
        ProjectorRelativePath = 'models/d3-mini/mmproj-vllm-d3-mini-Q8_0.gguf'
        ModelHash = 'AB3D4378C268F7F3238A63607AF40C0CB516E4482278B01C2F48E9D0698CE522'
        ProjectorHash = 'BB79BA9F091DF890684388F559F3145649007B5715D03760F99B767DE41D9EDA'
        ModelBytes = 3707523552L
        ProjectorBytes = 366894720L
    }
    $nano = @{
        Profile = 'nano'
        ModelRelativePath = 'models/d3-nano/vllm-d3-nano-Q8_0.gguf'
        ProjectorRelativePath = 'models/d3-nano/mmproj-vllm-d3-nano-Q8_0.gguf'
        ModelHash = '4867D306FD22AEC8C5EBD1851B7F1074F480702655E8000CEEDF0C47C6A6F337'
        ProjectorHash = 'B778993A051D604757DDA94A554BA870BF6E685909C44E7399D71FFB73E0E58F'
        ModelBytes = 2552358464L
        ProjectorBytes = 364664448L
    }

    Remove-Item -LiteralPath $selectionPath -Force -ErrorAction SilentlyContinue
    $resolved = Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot
    Assert-Profile $resolved $mini.Profile $mini.ModelRelativePath $mini.ProjectorRelativePath `
        $mini.ModelHash $mini.ProjectorHash $mini.ModelBytes $mini.ProjectorBytes $runtimeRoot

    Write-Selection $selectionPath '{"profile":"nano"}'
    $resolved = Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot
    Assert-Profile $resolved $nano.Profile $nano.ModelRelativePath $nano.ProjectorRelativePath `
        $nano.ModelHash $nano.ProjectorHash $nano.ModelBytes $nano.ProjectorBytes $runtimeRoot

    $resolved = Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot -Profile 'mini'
    Assert-Profile $resolved $mini.Profile $mini.ModelRelativePath $mini.ProjectorRelativePath `
        $mini.ModelHash $mini.ProjectorHash $mini.ModelBytes $mini.ProjectorBytes $runtimeRoot
    Write-Selection $selectionPath '{"profile":"mini"}'
    $resolved = Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot -Profile 'nano'
    Assert-Profile $resolved $nano.Profile $nano.ModelRelativePath $nano.ProjectorRelativePath `
        $nano.ModelHash $nano.ProjectorHash $nano.ModelBytes $nano.ProjectorBytes $runtimeRoot

    Write-Selection $selectionPath '{"profile":"unknown"}'
    Assert-Throws { Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot } `
        'Unknown configured profile must be rejected.'
    Write-Selection $selectionPath '{"profile":"..\\outside"}'
    Assert-Throws { Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot } `
        'Path-like configured profile must be rejected.'
    Write-Selection $selectionPath '{"profile":17}'
    Assert-Throws { Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot } `
        'Non-string configured profile must be rejected.'
    Write-Selection $selectionPath '{"profile":"nano","modelPath":"..\\outside.gguf"}'
    Assert-Throws { Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot } `
        'Arbitrary model-path override in selection must be rejected.'
    Write-Selection $selectionPath '{"profile":"nano"'
    Assert-Throws { Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot } `
        'Malformed selection JSON must be rejected.'

    Assert-Throws { Resolve-LocalDecisionModel -RuntimeRoot $runtimeRoot -Profile '..\outside' } `
        'Path-like explicit profile override must be rejected.'

    Write-Output 'PASS local decision profile resolution: default, config selection, explicit override, invalid config, and registry path containment.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot -PathType Container) {
        $resolvedTempRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $tempRoot -ErrorAction Stop).Path)
        $separatorChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $resolvedTempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd($separatorChars)
        $actualParent = [IO.Path]::GetDirectoryName($resolvedTempRoot)
        $leafName = [IO.Path]::GetFileName($resolvedTempRoot)
        if (-not [string]::Equals($actualParent, $resolvedTempParent, [StringComparison]::OrdinalIgnoreCase) -or
            (-not ($leafName.StartsWith('local-decision-model-test-', [StringComparison]::Ordinal) -and (Test-HexValue $leafName.Substring(26) 32 -Lowercase)))) {
            throw "Refusing to remove unexpected local decision test directory: $resolvedTempRoot"
        }
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force -ErrorAction Stop
    }
}
