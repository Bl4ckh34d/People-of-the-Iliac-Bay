param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $DfuRoot 'daggerfall-unity/Assets/Game/Mods/LivingCities'
if (-not (Test-Path -LiteralPath (Join-Path $DfuRoot 'daggerfall-unity/ProjectSettings/ProjectVersion.txt'))) { throw 'DFU Unity project missing.' }
New-Item -ItemType Directory -Path "$destination/Core", "$destination/Scripts", "$destination/Editor" -Force | Out-Null
Get-ChildItem -LiteralPath "$repo/mod/Scripts" -Filter '*.cs' | Copy-Item -Destination "$destination/Scripts" -Force
Copy-Item -LiteralPath "$repo/mod/modsettings.json" -Destination "$destination/modsettings.json" -Force
Get-ChildItem -LiteralPath "$repo/src/LivingCities.Core" -Filter '*.cs' | Copy-Item -Destination "$destination/Core" -Force
Copy-Item -LiteralPath "$repo/tools/Editor/LivingCitiesBuild.cs" -Destination "$destination/Editor/LivingCitiesBuild.cs" -Force
$manifest = Get-Content -LiteralPath "$repo/mod/LivingCities.dfmod.json" -Raw | ConvertFrom-Json
$manifest.Files = @('Assets/Game/Mods/LivingCities/modsettings.json') + @(Get-ChildItem -LiteralPath "$destination/Scripts" -Filter '*.cs' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/LivingCities/Scripts/$($_.Name)" }) + @(Get-ChildItem -LiteralPath "$destination/Core" -Filter '*.cs' | Sort-Object Name | ForEach-Object { "Assets/Game/Mods/LivingCities/Core/$($_.Name)" })
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$destination/LivingCities.dfmod.json" -Encoding utf8
Write-Output "Deployed $($manifest.Files.Count) runtime assets to $destination"
