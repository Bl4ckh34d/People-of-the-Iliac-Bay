param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent), [switch]$QuestBindingProbe, [switch]$InteractiveOnly, [switch]$RetryBatch, [switch]$ReusePortraitExport)
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')

$ErrorActionPreference = 'Stop'
if (Get-Process DaggerfallUnity -ErrorAction SilentlyContinue) {
    throw 'Daggerfall Unity is running; build/install will not replace a loaded mod. Close the game first.'
}
dotnet build (Join-Path $PSScriptRoot '../src/PeopleOfTheIliacBay.QuestHooks/PeopleOfTheIliacBay.QuestHooks.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Quest offer hooks compilation failed.' }
& (Join-Path $PSScriptRoot 'Deploy.ps1') -DfuRoot $DfuRoot -ReusePortraitExport:$ReusePortraitExport
$editor = 'C:/Program Files/Unity/Hub/Editor/2019.4.40f1/Editor/Unity.exe'
$project = Join-Path $DfuRoot 'daggerfall-unity'
$log = Join-Path $DfuRoot 'work/peopleoftheiliacbay-build.log'
$repo = Split-Path $PSScriptRoot -Parent
$licenseReceiptPath = Join-Path $repo 'work/unity-batch-license-fallback-v1.json'
if (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq [System.IO.Path]::GetFullPath($editor) }) {
    throw 'Unity editor is already running; build will not compete with an open editor.'
}
$method = 'PeopleOfTheIliacBayBuild.VerifyAndBuild'
if ($QuestBindingProbe) {
    dotnet build (Join-Path $PSScriptRoot '../probes/quest-binding/QuestBindingProbe.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Quest binding probe compilation failed.' }
    $method = 'PeopleOfTheIliacBayBuild.VerifyAndBuildQuestBindingProbe'
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

$editorPath = [IO.Path]::GetFullPath($editor)
$projectPath = [IO.Path]::GetFullPath($project)
$editorSha256 = (Get-FileHash -LiteralPath $editorPath -Algorithm SHA256).Hash.ToLowerInvariant()
$skipKnownBatchFailure = $false
if (-not $InteractiveOnly -and -not $RetryBatch -and (Test-Path -LiteralPath $licenseReceiptPath -PathType Leaf)) {
    try {
        $receipt = Get-Content -LiteralPath $licenseReceiptPath -Raw | ConvertFrom-Json -ErrorAction Stop
        $skipKnownBatchFailure = $receipt.schemaVersion -eq 1 -and
            [string]::Equals([string]$receipt.editorPath, $editorPath, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$receipt.projectPath, $projectPath, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$receipt.windowsUser, [Environment]::UserName, [StringComparison]::Ordinal) -and
            $receipt.editorSha256 -eq $editorSha256
    }
    catch { $skipKnownBatchFailure = $false }
}
$attemptBatch = -not $InteractiveOnly -and -not $skipKnownBatchFailure
if ($skipKnownBatchFailure) { Write-Host 'Skipping the previously confirmed Unity batch-license failure; using the interactive editor path.' }
$body = Invoke-UnityBuild $attemptBatch
$batchLicenseRejected = $attemptBatch -and (-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_BUILD_SUCCESS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -and
    ((([string]$body).IndexOf('BatchMode: Unity has not been activated with a valid License',[StringComparison]::OrdinalIgnoreCase) -ge 0))
if ($batchLicenseRejected) {
    Write-Host 'Batch-mode license rejected despite Hub entitlement; retrying through the interactive editor path.'
    $body = Invoke-UnityBuild $false
}
if ((-not ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_BUILD_SUCCESS:',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or
    -not $body.Contains('Application will terminate with return code 0')) {
    throw "Unity build failed; inspect $log"
}
if ($batchLicenseRejected) {
    New-Item -ItemType Directory -Path (Split-Path $licenseReceiptPath -Parent) -Force | Out-Null
    $receipt = [ordered]@{ schemaVersion=1; editorPath=$editorPath; projectPath=$projectPath;
        windowsUser=[Environment]::UserName; editorSha256=$editorSha256 }
    [IO.File]::WriteAllText($licenseReceiptPath, ($receipt | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
elseif ($attemptBatch -and (Test-Path -LiteralPath $licenseReceiptPath)) {
    Remove-Item -LiteralPath $licenseReceiptPath -Force
}
Write-Output 'Unity build and integration checks passed; bundle installed.'
