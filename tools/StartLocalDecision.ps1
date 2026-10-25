param(
    [ValidateRange(1, 300)] [int]$ReadinessTimeoutSeconds = 90,
    [string]$LogDirectory,
    [ValidateSet('mini', 'nano')] [string]$Profile
)
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')


$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dfuRoot = Split-Path $repoRoot -Parent
$runtimeRoot = Join-Path $dfuRoot 'local-llm'
$serverPath = Join-Path $runtimeRoot 'bin\llama-server.exe'
. (Join-Path $PSScriptRoot 'ResolveLocalDecisionModel.ps1')
$modelArguments = @{ RuntimeRoot = $runtimeRoot }
if (-not [string]::IsNullOrEmpty($Profile)) { $modelArguments['Profile'] = $Profile }
$decisionModel = Resolve-LocalDecisionModel @modelArguments
$modelPath = $decisionModel.ModelPath
$projectorPath = $decisionModel.ProjectorPath
$modelHash = $decisionModel.ModelHash
$projectorHash = $decisionModel.ProjectorHash
$modelBytes = $decisionModel.ModelBytes
$projectorBytes = $decisionModel.ProjectorBytes
$port = 8093
$contextSize = 4096
$endpoint = "http://127.0.0.1:$port"
$wireEndpoint = "$endpoint/v1/systemone"
foreach ($artifact in @(
    @{ Path = $serverPath; Label = 'llama.cpp server' },
    @{ Path = $modelPath; Label = "D3-$($decisionModel.Profile) model" },
    @{ Path = $projectorPath; Label = "D3-$($decisionModel.Profile) projector" }
)) {
    if (-not (Test-Path -LiteralPath $artifact.Path -PathType Leaf)) {
        throw "Missing $($artifact.Label): $($artifact.Path)"
    }
}
$serverPath = (Resolve-Path -LiteralPath $serverPath).Path
$modelPath = (Resolve-Path -LiteralPath $modelPath).Path
$projectorPath = (Resolve-Path -LiteralPath $projectorPath).Path

if ((Get-Item -LiteralPath $modelPath).Length -ne $modelBytes -or
    (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ine $modelHash) {
    throw "D3-$($decisionModel.Profile) model size or SHA-256 does not match verified artifact."
}
if ((Get-Item -LiteralPath $projectorPath).Length -ne $projectorBytes -or
    (Get-FileHash -LiteralPath $projectorPath -Algorithm SHA256).Hash -ine $projectorHash) {
    throw "D3-$($decisionModel.Profile) projector size or SHA-256 does not match verified artifact."
}
if (-not $LogDirectory) { $LogDirectory = Join-Path $repoRoot 'work' }

function Get-ListenerRecords {
    foreach ($connection in @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)) {
        $ownerProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($connection.OwningProcess)" -ErrorAction SilentlyContinue
        [pscustomobject]@{
            LocalAddress = [string]$connection.LocalAddress
            ProcessId = [int]$connection.OwningProcess
            Process = $ownerProcess
        }
    }
}

function Test-LoopbackAddress([string]$address) {
    $parsed = $null
    return [System.Net.IPAddress]::TryParse($address, [ref]$parsed) -and [System.Net.IPAddress]::IsLoopback($parsed)
}

function Test-ExpectedCommandLine($process) {
    if (-not $process -or -not [string]::Equals([string]$process.ExecutablePath, $serverPath, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $line = [string]$process.CommandLine
    foreach ($required in @(
        $modelPath,
        $projectorPath,
        '--host 127.0.0.1',
        "--port $port",
        "-c $contextSize",
        '-ngl 99',
        '-np 1',
        '-b 2048',
        '-ub 2048',
        '-t 8'
    )) {
        if ($line.IndexOf($required, [StringComparison]::OrdinalIgnoreCase) -lt 0) { return $false }
    }
    return $true
}

function Test-EndpointCompatibility {
    try {
        $health = Invoke-RestMethod -Uri "$endpoint/health" -TimeoutSec 2 -ErrorAction Stop
        if ($health.status -ne 'ok') { return [pscustomobject]@{ Reachable = $true; Compatible = $false; Reason = 'health status mismatch' } }
        $props = Invoke-RestMethod -Uri "$endpoint/props" -TimeoutSec 3 -ErrorAction Stop
        $modelMatches = [string]::Equals([string]$props.model_path, $modelPath, [StringComparison]::OrdinalIgnoreCase)
        $contextMatches = [int]$props.default_generation_settings.n_ctx -eq $contextSize
        $slotsMatch = [int]$props.total_slots -eq 1
        if (-not $modelMatches -or -not $contextMatches -or -not $slotsMatch) {
            return [pscustomobject]@{ Reachable = $true; Compatible = $false; Reason = 'model, context, or slot count mismatch' }
        }
        return [pscustomobject]@{ Reachable = $true; Compatible = $true; Reason = 'ready' }
    } catch {
        return [pscustomobject]@{ Reachable = $false; Compatible = $false; Reason = 'health check unavailable' }
    }
}

function Get-ProcessStartedUtc($process) {
    if (-not $process -or -not $process.CreationDate) { throw 'Could not establish decision-server process creation time.' }
    $creation = $process.CreationDate
    if ($creation -isnot [datetime]) {
        try { $creation = [System.Management.ManagementDateTimeConverter]::ToDateTime([string]$creation) }
        catch { throw 'Could not parse decision-server process creation time.' }
    }
    return $creation.ToUniversalTime().ToString('o')
}

function New-ReadyMetadata($process, [string]$processStartedUtc, [bool]$startedByThisCall) {
    [pscustomobject]@{
        Ready = $true
        Profile = $decisionModel.Profile
        Endpoint = $wireEndpoint
        ModelId = $modelPath
        ModelPath = $modelPath
        ProjectorPath = $projectorPath
        ProcessId = [int]$process.ProcessId
        ProcessStartedUtc = $processStartedUtc
        StartedByThisCall = $startedByThisCall
        Port = $port
        ContextSize = $contextSize
        Slots = 1
        StandardOutputLog = $stdout
        StandardErrorLog = $stderr
    }
}

$listeners = @(Get-ListenerRecords)
$serverProcesses = @(Get-CimInstance Win32_Process -Filter "Name='llama-server.exe'" -ErrorAction SilentlyContinue |
    Where-Object { (Test-NativeOption $_.CommandLine '--port' $Port) })
$targetProcess = $null
$startedByThisCall = $false

if ($listeners.Count -gt 0) {
    $badListeners = @($listeners | Where-Object {
        -not (Test-LoopbackAddress $_.LocalAddress) -or -not (Test-ExpectedCommandLine $_.Process)
    })
    if ($badListeners.Count -gt 0) {
        throw "Port $port is occupied by an unrelated or externally bound listener; refusing to replace it."
    }
    $distinctOwners = @($listeners | Select-Object -ExpandProperty ProcessId -Unique)
    if ($distinctOwners.Count -ne 1) { throw "Port $port has multiple listener owners; refusing to reuse it." }
    $targetProcess = $listeners[0].Process
} elseif ($serverProcesses.Count -gt 0) {
    $matching = @($serverProcesses | Where-Object { Test-ExpectedCommandLine $_ })
    if ($matching.Count -ne 1) { throw "A llama-server process is starting on port $port with different arguments; refusing a duplicate." }
    $targetProcess = $matching[0]
} else {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $stdout = Join-Path $LogDirectory "local-decision-server-$port-$stamp.stdout.log"
    $stderr = Join-Path $LogDirectory "local-decision-server-$port-$stamp.stderr.log"
    $argumentLine = '-m "' + $modelPath + '" --mmproj "' + $projectorPath + '" --host 127.0.0.1 --port 8093 -c 4096 -ngl 99 -np 1 -b 2048 -ub 2048 -t 8'
    $launched = Start-Process -FilePath $serverPath -ArgumentList $argumentLine -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $targetProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($launched.Id)" -ErrorAction Stop
    $startedByThisCall = $true
}
$targetProcessId = [int]$targetProcess.ProcessId
$targetProcessStartedUtc = Get-ProcessStartedUtc $targetProcess

$deadline = [DateTime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
do {
    $listeners = @(Get-ListenerRecords)
    if ($listeners.Count -gt 0) {
        $badListeners = @($listeners | Where-Object {
            -not (Test-LoopbackAddress $_.LocalAddress) -or $_.ProcessId -ne [int]$targetProcess.ProcessId -or
                -not (Test-ExpectedCommandLine $_.Process)
        })
        if ($badListeners.Count -gt 0 -or $listeners.Count -ne 1) {
            throw "Port $port listener ownership changed during startup; refusing to use it."
        }
    }
    $status = Test-EndpointCompatibility
    if ($status.Compatible) {
        $readyListeners = @(Get-ListenerRecords)
        if ($readyListeners.Count -ne 1 -or $readyListeners[0].ProcessId -ne $targetProcessId -or
            -not (Test-LoopbackAddress $readyListeners[0].LocalAddress) -or
            -not (Test-ExpectedCommandLine $readyListeners[0].Process)) {
            throw "Port $port is not listening solely on loopback under the expected process; refusing to report ready."
        }
        $currentProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$targetProcessId" -ErrorAction Stop
        if (-not (Test-ExpectedCommandLine $currentProcess) -or
            (Get-ProcessStartedUtc $currentProcess) -ne $targetProcessStartedUtc) {
            throw 'Decision-server process identity changed during readiness; refusing to report ready.'
        }
        return New-ReadyMetadata $currentProcess $targetProcessStartedUtc $startedByThisCall
    }
    if ($status.Reachable) { throw "Local decision service is incompatible: $($status.Reason)." }
    $targetProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$targetProcessId" -ErrorAction SilentlyContinue
    if (-not $targetProcess) { throw "D3-$($decisionModel.Profile) exited before becoming ready." }
    Start-Sleep -Seconds 1
} while ([DateTime]::UtcNow -lt $deadline)

throw "Local decision service did not become ready within $ReadinessTimeoutSeconds seconds; owned process remains running for inspection."
