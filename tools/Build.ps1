param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Deploy.ps1') -DfuRoot $DfuRoot
$editor = 'C:/Program Files/Unity/Hub/Editor/2019.4.40f1/Editor/Unity.exe'
$project = Join-Path $DfuRoot 'daggerfall-unity'
$log = Join-Path $DfuRoot 'work/livingcities-build.log'
if (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq [System.IO.Path]::GetFullPath($editor) }) {
    throw 'Unity editor is already running; build will not compete with an open editor.'
}
$arguments = '-batchmode -nographics -quit -projectPath "' + $project + '" -executeMethod LivingCitiesBuild.VerifyAndBuild -logFile "' + $log + '"'
$owned = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Owned Unity build process: $($owned.Id)"
while (-not $owned.HasExited) { Start-Sleep -Milliseconds 500 }
$owned.WaitForExit()
$body = Get-Content -LiteralPath $log -Raw
if ($body -notmatch 'LIVING_CITIES_BUILD_SUCCESS:' -or $body -notmatch 'Application will terminate with return code 0') {
    throw "Unity build failed; inspect $log"
}
Write-Output 'Unity build and integration checks passed; bundle installed.'
