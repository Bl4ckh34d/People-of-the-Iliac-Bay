param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent))
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')

$ErrorActionPreference = 'Stop'
# Validate the new core with DFU's own serializer without replacing the playable bundle.
dotnet build (Join-Path $PSScriptRoot '../src/PeopleOfTheIliacBay.QuestHooks/PeopleOfTheIliacBay.QuestHooks.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Guard hook compilation failed.' }
& (Join-Path $PSScriptRoot 'Deploy.ps1') -DfuRoot $DfuRoot
$editor = 'C:/Program Files/Unity/Hub/Editor/2019.4.40f1/Editor/Unity.exe'
if (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq [IO.Path]::GetFullPath($editor) }) {
    throw 'Unity editor is already running; validation will not compete with it.'
}
$project = Join-Path $DfuRoot 'daggerfall-unity'
$log = Join-Path $DfuRoot 'work/guards-persistence.log'
$arguments = '-batchmode -nographics -quit -projectPath "' + $project + '" -executeMethod PeopleOfTheIliacBayBuild.VerifyGuardPersistence -logFile "' + $log + '"'
$owned = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Owned guard-validation process: $($owned.Id)"
while (-not $owned.HasExited) { Start-Sleep -Milliseconds 500 }
$owned.WaitForExit()
$body = Get-Content -LiteralPath $log -Raw
if ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARDS_PERSISTENCE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$body).IndexOf('Application will terminate with return code 0',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
    throw "Guard persistence validation failed; inspect $log"
}
Write-Output 'PASS: native guard persistence checks. Installed player bundle unchanged.'
