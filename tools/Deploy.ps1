param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent), [switch]$ReusePortraitExport)
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')

$ErrorActionPreference = 'Stop'
if (Get-Process DaggerfallUnity,Unity -ErrorAction SilentlyContinue) { throw 'Close the game and Unity before deploying runtime assets.' }
$repo = Split-Path $PSScriptRoot -Parent
& python (Join-Path $PSScriptRoot 'BuildCompletedNpcSprites.py') --check
if ($LASTEXITCODE -ne 0) { throw 'Completed NPC sprite selection changed or package is incomplete; rebuild and review its receipt before deployment.' }
$destination = Join-Path $DfuRoot 'daggerfall-unity/Assets/Game/Mods/PeopleOfTheIliacBay'
if (-not (Test-Path -LiteralPath (Join-Path $DfuRoot 'daggerfall-unity/ProjectSettings/ProjectVersion.txt'))) { throw 'DFU Unity project missing.' }
New-Item -ItemType Directory -Path "$destination/Core", "$destination/Scripts", "$destination/Editor", "$destination/Textures", "$destination/Libraries" -Force | Out-Null
foreach ($sourcePair in @(@('mod/Scripts','Scripts'), @('src/PeopleOfTheIliacBay.Core','Core'), @('tools/Editor','Editor'))) {
    $sourceDirectory = Join-Path $repo $sourcePair[0]
    $managedDirectory = (Resolve-Path -LiteralPath (Join-Path $destination $sourcePair[1])).Path
    $managedRoot = (Resolve-Path -LiteralPath $destination).Path
    if (-not $managedDirectory.StartsWith($managedRoot + [IO.Path]::DirectorySeparatorChar)) { throw 'Deployment cleanup escaped the managed mod directory.' }
    foreach ($obsolete in Get-ChildItem -LiteralPath $managedDirectory -Filter '*.cs' -File) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory $obsolete.Name))) {
            Remove-Item -LiteralPath $obsolete.FullName
        }
    }
}
Copy-Item -LiteralPath "$repo/work/Harmony/net472/0Harmony.dll" -Destination "$destination/Libraries/0Harmony.dll.bytes" -Force
Copy-Item -LiteralPath "$repo/src/PeopleOfTheIliacBay.QuestHooks/bin/Release/net472/PeopleOfTheIliacBay.QuestHooks.dll" -Destination "$destination/Libraries/PeopleOfTheIliacBay.QuestHooks.dll.bytes" -Force
Copy-Item -LiteralPath "$repo/mod/Notices/Harmony-LICENSE.txt" -Destination "$destination/Libraries/Harmony-LICENSE.txt" -Force
Get-ChildItem -LiteralPath "$repo/mod/Textures" -Filter '*.bytes' | Copy-Item -Destination "$destination/Textures" -Force
if ($ReusePortraitExport) {
    if (-not (Test-Path -LiteralPath "$destination/Textures/portrait-split-manifest.json.bytes" -PathType Leaf)) { throw 'No complete portrait export exists to reuse.' }
    Write-Output 'Reusing the existing portrait export; live gallery sources are not imported in this build.'
} else {
    & "$repo/tools/SyncPortraitRuntimeCatalog.ps1" -ArtRoot "$repo/art/portraits/mobile-mouths" -CatalogPath "$repo/art/portraits/mobile-mouths/split-mask-prototype/portrait-catalog.json" -Destination "$destination/Textures"
}
$sittingRoot = Join-Path $repo 'art/sitting'
if (Test-Path -LiteralPath $sittingRoot) {
    foreach ($appearance in Get-ChildItem -LiteralPath $sittingRoot -Directory) {
        if ((-not (Test-DecimalValue $appearance.Name))) { continue }
        $atlas = Join-Path $appearance.FullName 'atlas.png'
        $definition = Join-Path $appearance.FullName 'manifest.json'
        if (-not (Test-Path -LiteralPath $atlas) -or -not (Test-Path -LiteralPath $definition)) { continue }
        $sittingManifest = Get-Content -LiteralPath $definition -Raw | ConvertFrom-Json
        if ($sittingManifest.archive -ne [int]$appearance.Name -or $sittingManifest.frames.Count -ne 8) {
            throw "Invalid sitting atlas manifest: $definition"
        }
        Copy-Item -LiteralPath $atlas -Destination "$destination/Textures/sit-$($appearance.Name).png.bytes" -Force
        Copy-Item -LiteralPath $definition -Destination "$destination/Textures/sit-$($appearance.Name).json" -Force
    }
}
$idleRoot = Join-Path $repo 'art/standing/mobile'
$idleCoverage = Get-Content -LiteralPath "$idleRoot/idle-coverage.json" -Raw | ConvertFrom-Json
if ($idleCoverage.appearances.Count -ne 24) { throw 'Expected all 24 native mobile standing appearances.' }
foreach ($appearance in $idleCoverage.appearances) {
    $definition = @{ archive = $appearance.archive; nativeIdleSize = @($appearance.native_source_size); cellSize = @($appearance.atlas_cell_size) }
    $definition | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$destination/Textures/idle-$($appearance.archive).json" -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $idleRoot $appearance.atlas) -Destination "$destination/Textures/idle-$($appearance.archive).png.bytes" -Force
}
Get-ChildItem -LiteralPath "$repo/mod/Scripts" -Filter '*.cs' | Copy-Item -Destination "$destination/Scripts" -Force
Copy-Item -LiteralPath "$repo/mod/modsettings.json" -Destination "$destination/modsettings.json" -Force
Copy-Item -LiteralPath "$repo/mod/ItemTemplates.json" -Destination "$destination/ItemTemplates.json" -Force
Get-ChildItem -LiteralPath "$repo/src/PeopleOfTheIliacBay.Core" -Filter '*.cs' | Copy-Item -Destination "$destination/Core" -Force
Get-ChildItem -LiteralPath "$repo/tools/Editor" -Filter '*.cs' | Copy-Item -Destination "$destination/Editor" -Force
[IO.File]::WriteAllText("$destination/Editor/repository-root.txt", [IO.Path]::GetFullPath($repo))
$manifest = Get-Content -LiteralPath "$repo/mod/PeopleOfTheIliacBay.dfmod.json" -Raw | ConvertFrom-Json
$manifest.Files = @('Assets/Game/Mods/PeopleOfTheIliacBay/modsettings.json') + @(Get-ChildItem -LiteralPath "$destination/Scripts" -Filter '*.cs' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/PeopleOfTheIliacBay/Scripts/$($_.Name)" }) + @(Get-ChildItem -LiteralPath "$destination/Core" -Filter '*.cs' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/PeopleOfTheIliacBay/Core/$($_.Name)" })
$manifest.Files += 'Assets/Game/Mods/PeopleOfTheIliacBay/ItemTemplates.json'
$manifest.Files += @(Get-ChildItem -LiteralPath "$destination/Textures" -Filter '*.bytes' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/PeopleOfTheIliacBay/Textures/$($_.Name)" })
$manifest.Files += @(Get-ChildItem -LiteralPath "$destination/Textures" -Filter 'sit-*.json' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/PeopleOfTheIliacBay/Textures/$($_.Name)" })
$manifest.Files += @(Get-ChildItem -LiteralPath "$destination/Textures" -Filter 'idle-*.json' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/PeopleOfTheIliacBay/Textures/$($_.Name)" })
$manifest.Files += @('Assets/Game/Mods/PeopleOfTheIliacBay/Libraries/0Harmony.dll.bytes', 'Assets/Game/Mods/PeopleOfTheIliacBay/Libraries/PeopleOfTheIliacBay.QuestHooks.dll.bytes', 'Assets/Game/Mods/PeopleOfTheIliacBay/Libraries/Harmony-LICENSE.txt')
# Virtual editor mods need the same pre-baked navigation assets as the bundle.
# Runtime validates their geometry profile; deployment never silently rebakes.
$navigationSource = Join-Path $repo 'mod/Navigation'
if (Test-Path -LiteralPath (Join-Path $navigationSource 'navigation-manifest.json')) {
    New-Item -ItemType Directory -Path "$destination/Navigation" -Force | Out-Null
    Get-ChildItem -LiteralPath $navigationSource -File | Where-Object { $_.Extension -eq '.asset' } | Copy-Item -Destination "$destination/Navigation" -Force
    # A newer capture manifest is selected by NativeNavigationBake. Do not
    # replace an existing editor catalogue before that explicit build step.
    if (-not (Test-Path -LiteralPath "$destination/Navigation/navigation-manifest.json")) {
        Copy-Item -LiteralPath (Join-Path $navigationSource 'navigation-manifest.json') -Destination "$destination/Navigation/navigation-manifest.json"
    }
}
if (Test-Path -LiteralPath "$destination/Navigation/navigation-manifest.json") {
    $manifest.Files += 'Assets/Game/Mods/PeopleOfTheIliacBay/Navigation/navigation-manifest.json'
    $manifest.Files += @(Get-ChildItem -LiteralPath "$destination/Navigation" -Filter '*.asset' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/PeopleOfTheIliacBay/Navigation/$($_.Name)" })
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$destination/PeopleOfTheIliacBay.dfmod.json" -Encoding utf8
Write-Output "Deployed $($manifest.Files.Count) runtime assets to $destination"
