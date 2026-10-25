
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')
$ErrorActionPreference = 'Stop'
$dfuTools = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools'
$lifecycle = Join-Path $dfuTools 'LocalServiceLifecycle.ps1'
if (-not (Test-Path -LiteralPath $lifecycle -PathType Leaf)) { throw "Missing lifecycle helper: $lifecycle" }

$script:FakeProcesses = @{}
$script:StopEvents = @()
function global:Get-CimInstance {
    param([string]$ClassName, [string]$Filter, [string]$ErrorAction)
    if (($Filter.StartsWith('ProcessId=') -and (Test-DecimalValue $Filter.Substring(10)))) { return $script:FakeProcesses[[int]$Filter.Substring(10)] }
    throw "Unexpected fake CIM query: $Filter"
}
function global:Get-Process {
    param([int]$Id, [string]$ErrorAction)
    $record = $script:FakeProcesses[$Id]
    if (-not $record) { return $null }
    return [pscustomobject]@{ Id=$Id; Path=$record.ExecutablePath; StartTime=$record.CreationDate }
}
function global:Stop-Process {
    param([object]$InputObject, [switch]$Force, [string]$ErrorAction)
    $script:StopEvents += [pscustomobject]@{ Id=$InputObject.Id; Path=$InputObject.Path; Force=[bool]$Force }
}

. $lifecycle
$dialogueLauncher = Join-Path $PSScriptRoot 'StartLocalDialogue.ps1'
$speechLauncher = Join-Path $PSScriptRoot 'StartLocalSpeech.ps1'
foreach ($launcherPath in @($dialogueLauncher, $speechLauncher)) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($launcherPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) { throw "Service launcher parse failed: $launcherPath ($($parseErrors[0].Message))" }
    $source = [IO.File]::ReadAllText($launcherPath)
    if ((-not ((([string]$source).IndexOf('$OwnershipReceiptPath',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$source).IndexOf('New-LocalServiceOwnershipRecord',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or (-not ((([string]$source).IndexOf('Write-LocalServiceOwnershipReceipt',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
        throw "Service launcher does not emit ownership receipts for newly started processes: $launcherPath"
    }
}
$dialogueSource = [IO.File]::ReadAllText($dialogueLauncher)
if ($dialogueSource.IndexOf('$serverProcess = Start-Process', [StringComparison]::Ordinal) -ge $dialogueSource.IndexOf('New-LocalServiceOwnershipRecord -ProcessId $serverProcessId', [StringComparison]::Ordinal)) {
    throw 'Dialogue launcher must record ownership immediately after starting its server.'
}
$speechSource = [IO.File]::ReadAllText($speechLauncher)
if ($speechSource.IndexOf('$startedServer = Start-Process', [StringComparison]::Ordinal) -ge $speechSource.IndexOf('New-LocalServiceOwnershipRecord -ProcessId $startedServer.Id', [StringComparison]::Ordinal) -or
    $speechSource.IndexOf('$coordinatorProcess = Start-Process', [StringComparison]::Ordinal) -ge $speechSource.IndexOf('New-LocalServiceOwnershipRecord -ProcessId $coordinatorProcess.Id', [StringComparison]::Ordinal)) {
    throw 'Speech launcher must record each new Breeze child immediately after starting it.'
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('local-service-lifecycle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $created = [DateTime]::SpecifyKind([DateTime]'2026-10-23T04:00:00Z', [DateTimeKind]::Utc)
    $script:FakeProcesses[41001] = [pscustomobject]@{
        ProcessId=41001
        CreationDate=$created
        ExecutablePath='C:\local-llm\llama-server.exe'
        CommandLine='"C:\local-llm\llama-server.exe" -m "C:\models\gemma.gguf" --port 8080'
    }
    $configIdentity = 'a' * 64
    $record = New-LocalServiceOwnershipRecord -ProcessId 41001 -ExecutablePath 'C:\local-llm\llama-server.exe' -ConfigIdentity $configIdentity
    if ($record.ProcessId -ne 41001 -or $record.ConfigIdentity -cne $configIdentity -or -not $record.CommandLine) {
        throw 'New ownership record omitted exact process/config identity.'
    }

    $receiptPath = Join-Path $testRoot 'owners.json'
    Write-LocalServiceOwnershipReceipt -Path $receiptPath -Record $record
    $loaded = @(Read-LocalServiceOwnershipReceipt -Path $receiptPath)
    if ($loaded.Count -ne 1 -or -not (Test-LocalServiceOwnershipRecord -Record $loaded[0])) {
        $details = if ($loaded.Count) { "properties=$(@($loaded[0].PSObject.Properties.Name) -join ','); stored=$($loaded[0].OwnershipFingerprint); calculated=$(Get-LocalServiceOwnershipFingerprint $loaded[0]); live=$(Test-LocalServiceOwnershipRecord $loaded[0])" } else { 'receipt decoder returned no records' }
        throw "Valid process ownership receipt did not round-trip ($details)."
    }

    $safeStop = Stop-LocalServiceIfOwned -Record $loaded[0]
    if (-not $safeStop.Stopped -or $script:StopEvents.Count -ne 1 -or $script:StopEvents[0].Id -ne 41001) {
        throw 'Exact matching owned process was not stopped through its validated process handle.'
    }

    $script:StopEvents = @()
    $script:FakeProcesses[41001] = [pscustomobject]@{
        ProcessId=41001
        CreationDate=$created.AddSeconds(1)
        ExecutablePath='C:\local-llm\llama-server.exe'
        CommandLine='"C:\local-llm\llama-server.exe" -m "C:\models\other.gguf" --port 8080'
    }
    $reusedPid = Stop-LocalServiceIfOwned -Record $loaded[0]
    if ($reusedPid.Stopped -or $script:StopEvents.Count -ne 0) {
        throw 'PID reuse with a different creation/config identity was stopped.'
    }

    $script:FakeProcesses[41001] = [pscustomobject]@{
        ProcessId=41001
        CreationDate=$created
        ExecutablePath='C:\local-llm\llama-server.exe'
        CommandLine='"C:\local-llm\llama-server.exe" -m "C:\models\other.gguf" --port 8080'
    }
    $changedCommand = Stop-LocalServiceIfOwned -Record $loaded[0]
    if ($changedCommand.Stopped -or $script:StopEvents.Count -ne 0) {
        throw 'A process whose command/configuration changed was stopped.'
    }
    $changedConfig = $loaded[0].PSObject.Copy()
    $changedConfig.ConfigIdentity = '9' * 64
    $configMismatch = Stop-LocalServiceIfOwned -Record $changedConfig
    if ($configMismatch.Stopped -or $script:StopEvents.Count -ne 0) {
        throw 'An ownership receipt with a changed config identity was stopped.'
    }

    $script:FakeProcesses[41001] = [pscustomobject]@{
        ProcessId=41001; CreationDate=$created; ExecutablePath='C:\local-llm\llama-server.exe'
        CommandLine='"C:\local-llm\llama-server.exe" -m "C:\models\gemma.gguf" --port 8080'
    }
    foreach ($gameId in @(42001, 42002)) {
        $gamePath = 'C:\DFU\Player\DaggerfallUnity.exe'
        $gameStart = $created.AddSeconds($gameId - 42000)
        $script:FakeProcesses[$gameId] = [pscustomobject]@{
            ProcessId=$gameId; CreationDate=$gameStart; ExecutablePath=$gamePath
            CommandLine='"' + $gamePath + '" -windowed'
        }
    }
    $gameRecords = @(
        New-LocalServiceOwnershipRecord -ProcessId 42001 -ExecutablePath 'C:\DFU\Player\DaggerfallUnity.exe' -ConfigIdentity ('1' * 64)
        New-LocalServiceOwnershipRecord -ProcessId 42002 -ExecutablePath 'C:\DFU\Player\DaggerfallUnity.exe' -ConfigIdentity ('2' * 64)
    )
    $manifestPath = Join-Path $testRoot 'game-services.json'
    Write-LocalServiceLifecycleManifest -Path $manifestPath -GameProcesses $gameRecords -OwnedServices $loaded
    $manifest = Read-LocalServiceLifecycleManifest -Path $manifestPath
    $script:StopEvents = @()
    $oneStillRunning = Invoke-LocalGameOwnedServiceMonitor -GameProcesses $manifest.GameProcesses -OwnedServices $manifest.OwnedServices -RunOnce
    if ($oneStillRunning.GameProcessesRemaining -ne 2 -or $oneStillRunning.ServicesStopped -or $script:StopEvents.Count -ne 0) {
        throw 'Owned service cleanup ran while exact game processes were still active.'
    }
    $script:FakeProcesses.Remove(42001) | Out-Null
    $oneStillRunning = Invoke-LocalGameOwnedServiceMonitor -GameProcesses $manifest.GameProcesses -OwnedServices $manifest.OwnedServices -RunOnce
    if ($oneStillRunning.GameProcessesRemaining -ne 1 -or $oneStillRunning.ServicesStopped -or $script:StopEvents.Count -ne 0) {
        throw 'Owned service cleanup ran while one exact game process was still active.'
    }
    $script:FakeProcesses.Remove(42002) | Out-Null
    $script:FakeProcesses[41001].CommandLine = '"C:\local-llm\llama-server.exe" -m "C:\models\other.gguf" --port 8080'
    $mismatchedService = Invoke-LocalGameOwnedServiceMonitor -GameProcesses $manifest.GameProcesses -OwnedServices $manifest.OwnedServices -RunOnce
    if ($mismatchedService.ServicesStopped -or $mismatchedService.ServiceStopFailures -ne 1 -or $script:StopEvents.Count -ne 0) {
        throw 'Watcher reported cleanup success although service identity had changed.'
    }
    $script:FakeProcesses[41001].CommandLine = '"C:\local-llm\llama-server.exe" -m "C:\models\gemma.gguf" --port 8080'
    $allGamesExited = Invoke-LocalGameOwnedServiceMonitor -GameProcesses $manifest.GameProcesses -OwnedServices $manifest.OwnedServices -RunOnce
    if (-not $allGamesExited.ServicesStopped -or $allGamesExited.ServicesStoppedCount -ne 1 -or $script:StopEvents.Count -ne 1 -or $script:StopEvents[0].Id -ne 41001) {
        throw 'Owned service cleanup did not run after all exact game processes exited.'
    }

    Write-Output 'PASS lifecycle receipts bind PID, creation time, executable, command line, and config; cleanup waits for every tracked game and fails closed on PID reuse or changed arguments.'
} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
    Remove-Item Function:\Get-CimInstance, Function:\Get-Process, Function:\Stop-Process -ErrorAction SilentlyContinue
}
