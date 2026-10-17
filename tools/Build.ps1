param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent), [switch]$QuestBindingProbe)
$ErrorActionPreference = 'Stop'
if (Get-Process DaggerfallUnity -ErrorAction SilentlyContinue) {
    throw 'Daggerfall Unity is running; build/install will not replace a loaded mod. Close the game first.'
}
dotnet build (Join-Path $PSScriptRoot '../src/LivingCities.QuestHooks/LivingCities.QuestHooks.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Quest offer hooks compilation failed.' }
& (Join-Path $PSScriptRoot 'Deploy.ps1') -DfuRoot $DfuRoot
$editor = 'C:/Program Files/Unity/Hub/Editor/2019.4.40f1/Editor/Unity.exe'
$project = Join-Path $DfuRoot 'daggerfall-unity'
$log = Join-Path $DfuRoot 'work/livingcities-build.log'
if (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq [System.IO.Path]::GetFullPath($editor) }) {
    throw 'Unity editor is already running; build will not compete with an open editor.'
}
$method = 'LivingCitiesBuild.VerifyAndBuild'
if ($QuestBindingProbe) {
    dotnet build (Join-Path $PSScriptRoot '../probes/quest-binding/QuestBindingProbe.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Quest binding probe compilation failed.' }
    $method = 'LivingCitiesBuild.VerifyAndBuildQuestBindingProbe'
}
function Invoke-UnityBuild([bool]$batch) {
    $arguments = '-quit -projectPath "' + $project + '" -executeMethod ' + $method + ' -logFile "' + $log + '"'
    if ($batch) { $arguments = '-batchmode -nographics ' + $arguments }
    $owned = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Host "Owned Unity build process: $($owned.Id); batch=$batch"
    while (-not $owned.HasExited) { Start-Sleep -Milliseconds 500 }
    $owned.WaitForExit()
    return Get-Content -LiteralPath $log -Raw
}

$body = Invoke-UnityBuild $true
if ($body -notmatch 'LIVING_CITIES_BUILD_SUCCESS:' -and $body -match 'BatchMode: Unity has not been activated with a valid License') {
    Write-Host 'Batch-mode license rejected despite Hub entitlement; retrying through the interactive editor path.'
    $body = Invoke-UnityBuild $false
}
if ($body -notmatch 'LIVING_CITIES_BUILD_SUCCESS:' -or
    $body -notmatch '(Application will terminate with return code 0|Exiting without the bug reporter\. Application will terminate with return code 0)') {
    throw "Unity build failed; inspect $log"
}
Write-Output 'Unity build and integration checks passed; bundle installed.'
