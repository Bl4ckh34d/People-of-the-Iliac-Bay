param(
    [ValidateRange(1024, 16384)] [int]$ContextSize = 4096,
    [ValidateRange(1, 200)] [Nullable[int]]$GpuLayers,
    [ValidateSet('CPU', 'GPU')] [string]$ExecutionDevice,
    [ValidateRange(1024, 65535)] [int]$Port = 8080,
    [switch]$Background,
    [ValidateRange(1, 300)] [int]$ReadinessTimeoutSeconds = 90,
    [string]$LogDirectory,
    [string]$ModelPath,
    [string]$OwnershipReceiptPath
)
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')


$ErrorActionPreference = 'Stop'
$peopleOfTheIliacBayRoot = Split-Path $PSScriptRoot -Parent
$dfuRoot = Split-Path $peopleOfTheIliacBayRoot -Parent
$runtimeRoot = Join-Path $dfuRoot 'local-llm'
$server = Join-Path $runtimeRoot 'bin\llama-server.exe'
. (Join-Path $PSScriptRoot 'ResolveLocalDialogueModel.ps1')
$model = Resolve-LocalDialogueModel -RuntimeRoot $runtimeRoot -ModelPath $ModelPath
$selectionPath = Join-Path $runtimeRoot 'active-model.json'
if (-not $ExecutionDevice) {
    $ExecutionDevice = 'CPU'
    if (Test-Path -LiteralPath $selectionPath) {
        $selection = Get-Content -LiteralPath $selectionPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($selection.PSObject.Properties.Name -contains 'executionDevice' -and
            -not [string]::IsNullOrWhiteSpace([string]$selection.executionDevice)) {
            $configuredDevice = ([string]$selection.executionDevice).ToUpperInvariant()
            if ($configuredDevice -notin @('CPU', 'GPU')) { throw "executionDevice in $selectionPath must be CPU or GPU; found '$configuredDevice'." }
            $ExecutionDevice = $configuredDevice
        }
    }
}
if ($ExecutionDevice -eq 'CPU' -and $null -ne $GpuLayers) {
    throw 'GpuLayers is valid only with -ExecutionDevice GPU. Use -ExecutionDevice GPU to opt into GPU inference.'
}
if ($ExecutionDevice -eq 'GPU' -and $null -eq $GpuLayers) { $GpuLayers = 99 }
$deviceArguments = if ($ExecutionDevice -eq 'CPU') {
    @('--device', 'none', '-ngl', '0', '--no-kv-offload', '--no-op-offload')
} else {
    @('--device', 'CUDA0', '-ngl', [string]$GpuLayers, '--kv-offload', '--op-offload')
}
$endpoint = "http://127.0.0.1:$Port"

if (-not (Test-Path -LiteralPath $server)) { throw "Missing llama.cpp server: $server" }
if (-not $LogDirectory) { $LogDirectory = Join-Path $peopleOfTheIliacBayRoot 'work' }

if ($OwnershipReceiptPath) {
    . (Join-Path $dfuRoot 'tools/LocalServiceLifecycle.ps1')
    $dialogueConfigIdentity = Get-LocalServiceConfigIdentity ([ordered]@{
        schemaVersion = 1
        service = 'local-dialogue'
        executable = $server
        model = $model
        executionDevice = $ExecutionDevice
        gpuLayers = if ($ExecutionDevice -eq 'GPU') { [int]$GpuLayers } else { $null }
        contextSize = $ContextSize
        port = $Port
        host = '127.0.0.1'
        slots = 1
        deviceArguments = @($deviceArguments)
    })
}

$server = (Resolve-Path -LiteralPath $server).Path
$model = (Resolve-Path -LiteralPath $model).Path

function Get-ListenerRecords {
    $connections = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    foreach ($connection in $connections) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($connection.OwningProcess)" -ErrorAction SilentlyContinue
        [pscustomobject]@{ LocalAddress = [string]$connection.LocalAddress; Process = $owner }
    }
}

function Get-ServerProcessesForPort {
    Get-CimInstance Win32_Process -Filter "Name='llama-server.exe'" -ErrorAction SilentlyContinue |
        Where-Object { (Test-NativeOption $_.CommandLine '--port' $Port) }
}

function Test-EndpointCompatibility {
    $result = [pscustomobject]@{ Reachable = $false; Compatible = $false; Reason = 'health check unavailable' }
    try {
        $health = Invoke-RestMethod -Uri "$endpoint/health" -TimeoutSec 2 -ErrorAction Stop
        if ($health.status -ne 'ok') {
            $result.Reason = "health status is '$($health.status)'"
            return $result
        }
        $result.Reachable = $true
        $props = Invoke-RestMethod -Uri "$endpoint/props" -TimeoutSec 3 -ErrorAction Stop
        $nCtx = $props.default_generation_settings.n_ctx
        $modelMatches = [string]::Equals([string]$props.model_path, $model, [StringComparison]::OrdinalIgnoreCase)
        if (-not $modelMatches) { $result.Reason = "server model_path is '$($props.model_path)'"; return $result }
        if ([int]$nCtx -ne $ContextSize) { $result.Reason = "server context is '$nCtx', expected $ContextSize"; return $result }
        if ([int]$props.total_slots -ne 1) { $result.Reason = "server has '$($props.total_slots)' slots, expected 1"; return $result }
        $listener = @(Get-ListenerRecords | Where-Object { $_.Process -and [string]::Equals([string]$_.Process.ExecutablePath, $server, [StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
        if (-not $listener -or -not (Test-ExecutionDeviceCommandLine ([string]$listener.Process.CommandLine))) {
            $result.Reason = "server command line does not match requested $ExecutionDevice execution mode"
            return $result
        }
        $result.Compatible = $true
        $result.Reason = 'compatible'
    } catch {
        $result.Reason = $_.Exception.Message
    }
    return $result
}

function Test-ExecutionDeviceCommandLine([string]$commandLine) {
    if ($ExecutionDevice -eq 'CPU') {
        return ((Test-NativeOption $commandLine '--device' 'none') -and
            (Test-NativeOption $commandLine '-ngl' '0') -and
            ((Test-NativeFlag $commandLine '--no-kv-offload') -or (Test-NativeFlag $commandLine '-nkvo')) -and
            (Test-NativeFlag $commandLine '--no-op-offload'))
    }
    return ((Test-NativeOption $commandLine '--device' 'CUDA0') -and
        (Test-NativeOption $commandLine '-ngl' ([int]$GpuLayers)) -and
        (Test-NativeFlag $commandLine '--kv-offload') -and
        (Test-NativeFlag $commandLine '--op-offload'))
}

function Format-ListenerOwner($processes) {
    if (-not $processes -or $processes.Count -eq 0) { return 'owner unavailable' }
    return (($processes | ForEach-Object {
        $item = $_
        if ($item.Process) { "PID $($item.Process.ProcessId) $($item.Process.ExecutablePath) at $($item.LocalAddress)" }
        else { "PID unknown at $($item.LocalAddress)" }
    }) -join '; ')
}

function Quote-Argument([string]$value) {
    return '"' + $value.Replace('"', '\"') + '"'
}

$owners = @(Get-ListenerRecords)
$pendingServers = @(Get-ServerProcessesForPort)
if ($owners.Count -gt 0) {
    $foreignOwners = @($owners | Where-Object {
        -not $_.Process -or -not [string]::Equals([string]$_.Process.ExecutablePath, $server, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($foreignOwners.Count -gt 0) {
        throw "Port $Port is already owned by an unrelated process ($(Format-ListenerOwner $owners)); refusing to launch a duplicate server."
    }
    $nonLoopbackListeners = @($owners | Where-Object {
        $address = [System.Net.IPAddress]::Parse($_.LocalAddress)
        -not [System.Net.IPAddress]::IsLoopback($address)
    })
    if ($nonLoopbackListeners.Count -gt 0) {
        throw "The llama.cpp process on port $Port listens beyond loopback ($(Format-ListenerOwner $nonLoopbackListeners)); refusing to reuse an externally reachable server."
    }
    $expectedOwner = $owners | Where-Object {
        $_.Process -and [string]::Equals([string]$_.Process.ExecutablePath, $server, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    $initial = Test-EndpointCompatibility
    if ($initial.Reachable -and -not $initial.Compatible) {
        throw "Port $Port is served by llama.cpp but configuration is incompatible: $($initial.Reason)."
    }
    if ($initial.Compatible) {
        Write-Output "Local dialogue server is already ready and compatible at $endpoint. Reusing PID $($expectedOwner.Process.ProcessId)."
        return
    }
    $serverProcessId = [int]$expectedOwner.Process.ProcessId
    Write-Output "Waiting for existing llama.cpp process $serverProcessId to become ready."
} elseif ($pendingServers.Count -gt 0) {
    $expectedPending = $pendingServers | Where-Object {
        [string]::Equals([string]$_.ExecutablePath, $server, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine.IndexOf($model, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        (Test-NativeOption $_.CommandLine '-c' $ContextSize) -and
        (Test-NativeOption $_.CommandLine '--host' '127.0.0.1') -and
        (Test-NativeOption $_.CommandLine '-np' '1') -and
        (Test-ExecutionDeviceCommandLine ([string]$_.CommandLine))
    } | Select-Object -First 1
    if (-not $expectedPending) {
        $ownersText = Format-ListenerOwner $pendingServers
        throw "A llama-server process is already starting on port $Port with a different executable or configuration ($ownersText); refusing a duplicate."
    }
    $serverProcessId = [int]$expectedPending.ProcessId
    Write-Output "Waiting for existing llama.cpp process $serverProcessId to bind port $Port and become ready."
} elseif ($Background) {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $stdout = Join-Path $LogDirectory "local-dialogue-server-$Port-$stamp.stdout.log"
    $stderr = Join-Path $LogDirectory "local-dialogue-server-$Port-$stamp.stderr.log"
    $argumentLine = '-m ' + (Quote-Argument $model) + ' ' + ($deviceArguments -join ' ') + " -c $ContextSize --host 127.0.0.1 --port $Port -np 1"
    $serverProcess = Start-Process -FilePath $server -ArgumentList $argumentLine -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $serverProcessId = $serverProcess.Id
    if ($OwnershipReceiptPath) {
        $ownedRecord = New-LocalServiceOwnershipRecord -ProcessId $serverProcessId -ExecutablePath $server -ConfigIdentity $dialogueConfigIdentity
        Write-LocalServiceOwnershipReceipt -Path $OwnershipReceiptPath -Record $ownedRecord
    }
    Write-Output "Started local dialogue server PID $serverProcessId. Logs: $stdout ; $stderr"
} else {
    & $server -m $model @deviceArguments -c $ContextSize --host 127.0.0.1 --port $Port -np 1
    return
}

$deadline = [DateTime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
do {
    $status = Test-EndpointCompatibility
    if ($status.Compatible) {
        Write-Output "Local dialogue server ready and compatible at $endpoint (PID $serverProcessId)."
        return
    }
    if ($status.Reachable) {
        throw "Local dialogue server at $endpoint has incompatible configuration: $($status.Reason)."
    }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$serverProcessId" -ErrorAction SilentlyContinue
    if (-not $process) {
        $owner = Format-ListenerOwner @(Get-ListenerRecords)
        throw "Local dialogue server PID $serverProcessId exited before becoming ready. Last health error: $($status.Reason). Listener: $owner"
    }
    Start-Sleep -Seconds 1
} while ([DateTime]::UtcNow -lt $deadline)

throw "Local dialogue server did not become compatible within $ReadinessTimeoutSeconds seconds. Last health error: $($status.Reason). Process $serverProcessId remains running for inspection."
