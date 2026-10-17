param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $DfuRoot 'daggerfall-unity/Assets/Game/Mods/LivingCities'
if (-not (Test-Path -LiteralPath (Join-Path $DfuRoot 'daggerfall-unity/ProjectSettings/ProjectVersion.txt'))) { throw 'DFU Unity project missing.' }
New-Item -ItemType Directory -Path "$destination/Core", "$destination/Scripts", "$destination/Editor", "$destination/Textures", "$destination/Libraries" -Force | Out-Null
Copy-Item -LiteralPath "$repo/work/Harmony/net472/0Harmony.dll" -Destination "$destination/Libraries/0Harmony.dll.bytes" -Force
Copy-Item -LiteralPath "$repo/src/LivingCities.QuestHooks/bin/Release/net472/LivingCities.QuestHooks.dll" -Destination "$destination/Libraries/LivingCities.QuestHooks.dll.bytes" -Force
Copy-Item -LiteralPath "$repo/mod/Notices/Harmony-LICENSE.txt" -Destination "$destination/Libraries/Harmony-LICENSE.txt" -Force
Get-ChildItem -LiteralPath "$repo/mod/Textures" -Filter '*.bytes' | Copy-Item -Destination "$destination/Textures" -Force
Get-ChildItem -LiteralPath "$repo/mod/Scripts" -Filter '*.cs' | Copy-Item -Destination "$destination/Scripts" -Force
Copy-Item -LiteralPath "$repo/mod/modsettings.json" -Destination "$destination/modsettings.json" -Force
Copy-Item -LiteralPath "$repo/mod/ItemTemplates.json" -Destination "$destination/ItemTemplates.json" -Force
Get-ChildItem -LiteralPath "$repo/src/LivingCities.Core" -Filter '*.cs' | Copy-Item -Destination "$destination/Core" -Force
Get-ChildItem -LiteralPath "$repo/tools/Editor" -Filter '*.cs' | Copy-Item -Destination "$destination/Editor" -Force
$manifest = Get-Content -LiteralPath "$repo/mod/LivingCities.dfmod.json" -Raw | ConvertFrom-Json
$manifest.Files = @('Assets/Game/Mods/LivingCities/modsettings.json') + @(Get-ChildItem -LiteralPath "$destination/Scripts" -Filter '*.cs' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/LivingCities/Scripts/$($_.Name)" }) + @(Get-ChildItem -LiteralPath "$destination/Core" -Filter '*.cs' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/LivingCities/Core/$($_.Name)" })
$manifest.Files += 'Assets/Game/Mods/LivingCities/ItemTemplates.json'
$manifest.Files += @(Get-ChildItem -LiteralPath "$destination/Textures" -Filter '*.bytes' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/LivingCities/Textures/$($_.Name)" })
$manifest.Files += @('Assets/Game/Mods/LivingCities/Libraries/0Harmony.dll.bytes', 'Assets/Game/Mods/LivingCities/Libraries/LivingCities.QuestHooks.dll.bytes', 'Assets/Game/Mods/LivingCities/Libraries/Harmony-LICENSE.txt')
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$destination/LivingCities.dfmod.json" -Encoding utf8
Write-Output "Deployed $($manifest.Files.Count) runtime assets to $destination"
