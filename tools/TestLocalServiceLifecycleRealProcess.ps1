$ErrorActionPreference = 'Stop'
$dfuTools = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools'
$lifecycle = Join-Path $dfuTools 'LocalServiceLifecycle.ps1'
$watcherScript = Join-Path $dfuTools 'WatchGameOwnedServices.ps1'
if (-not (Test-Path -LiteralPath $lifecycle -PathType Leaf)) { throw "Missing lifecycle helper: $lifecycle" }
if (-not (Test-Path -LiteralPath $watcherScript -PathType Leaf)) { throw "Missing lifecycle watcher: $watcherScript" }
. $lifecycle

$powershellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
$runRoot = Join-Path ([IO.Path]::GetTempPath()) ('local-service-lifecycle-real-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$serviceProcess = $null
$gameProcess = $null
$watcherProcess = $null
try {
    $sleeperArguments = '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"'
    $serviceProcess = Start-Process -FilePath $powershellExe -ArgumentList $sleeperArguments -WindowStyle Hidden -PassThru
    $gameProcess = Start-Process -FilePath $powershellExe -ArgumentList $sleeperArguments -WindowStyle Hidden -PassThru

    $serviceConfig = Get-LocalServiceConfigIdentity ([ordered]@{ purpose='benign-lifecycle-test-service'; processId=$serviceProcess.Id })
    $gameConfig = Get-LocalServiceConfigIdentity ([ordered]@{ purpose='benign-lifecycle-test-game'; processId=$gameProcess.Id })
    $serviceRecord = New-LocalServiceOwnershipRecord -ProcessId $serviceProcess.Id -ExecutablePath $powershellExe -ConfigIdentity $serviceConfig
    $gameRecord = New-LocalServiceOwnershipRecord -ProcessId $gameProcess.Id -ExecutablePath $powershellExe -ConfigIdentity $gameConfig

    $serviceCim = Get-CimInstance Win32_Process -Filter "ProcessId=$($serviceProcess.Id)" -ErrorAction Stop
    $serviceProcess.Refresh()
    $cimCreationUtc = ConvertTo-LocalServiceCreationUtc $serviceCim.CreationDate
    $managedStartUtc = ConvertTo-LocalServiceCreationUtc $serviceProcess.StartTime
    if ($cimCreationUtc -cne $managedStartUtc) { throw "CIM CreationDate and Process.StartTime differ: '$cimCreationUtc' vs '$managedStartUtc'." }
    if (-not (Test-LocalServiceOwnershipRecord -Record $serviceRecord)) { throw 'Real child process failed exact ownership identity validation.' }

    $manifestPath = Join-Path $runRoot 'game-services.json'
    Write-LocalServiceLifecycleManifest -Path $manifestPath -GameProcesses @($gameRecord) -OwnedServices @($serviceRecord)
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $watcherScript + '" -ManifestPath "' + $manifestPath + '"'
    $watcherProcess = Start-Process -FilePath $powershellExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 400
    $watcherProcess.Refresh()
    if ($watcherProcess.HasExited) { throw 'Real hidden watcher exited while exact game child was still running.' }

    $gameProcess.Kill()
    if (-not $gameProcess.WaitForExit(5000)) { throw 'Test game child did not exit after its exact process handle was killed.' }
    if (-not $watcherProcess.WaitForExit(15000)) { throw 'Real hidden watcher did not finish after game child exit.' }
    $serviceProcess.Refresh()
    if (-not $serviceProcess.HasExited) { throw 'Real hidden watcher left the exact owned test service child running.' }
    if (Test-Path -LiteralPath $manifestPath) { throw 'Real hidden watcher did not remove its completed manifest.' }

    Write-Output 'PASS real hidden watcher detected test game exit and stopped exact owned PowerShell child; CIM and Process.StartTime creation stamps matched.'
} finally {
    foreach ($process in @($watcherProcess, $gameProcess, $serviceProcess)) {
        if ($null -eq $process) { continue }
        try {
            $process.Refresh()
            if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) }
        } catch { }
    }
    if (Test-Path -LiteralPath $runRoot -PathType Container) {
        $resolvedRunRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $runRoot).Path)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        $resolvedRunParent = [IO.Path]::GetDirectoryName($resolvedRunRoot).TrimEnd('\')
        if (-not [string]::Equals($resolvedRunParent, $tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing test cleanup outside temporary directory: $resolvedRunRoot"
        }
        foreach ($path in @(Join-Path $runRoot 'game-services.json', (Join-Path $runRoot 'game-services.json.watcher-error.log'))) {
            if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
        }
        if (@(Get-ChildItem -LiteralPath $runRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $runRoot -Force }
    }
}
