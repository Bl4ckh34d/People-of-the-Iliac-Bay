param(
    [Parameter(Mandatory = $true)] [string]$TrialRoot,
    [Parameter(Mandatory = $true)] [ValidateRange(1, 2147483647)] [int]$OwnedGameProcessId,
    [ValidateRange(30, 3600)] [int]$TimeoutSeconds = 300,
    [ValidateRange(1, 30)] [int]$PollSeconds = 1,
    [ValidateSet('npcvoice-01')] [string]$CaptureReferenceProfileId,
    [string]$TtsServerEndpoint,
    [switch]$ValidateOnly
)
. (Join-Path $PSScriptRoot '../TextProtocol.ps1')


$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($TtsServerEndpoint)) {
    $ttsEndpointUri = $null
    if (-not [Uri]::TryCreate($TtsServerEndpoint, [UriKind]::Absolute, [ref]$ttsEndpointUri) -or
        $ttsEndpointUri.Scheme -cne 'http' -or -not $ttsEndpointUri.IsLoopback -or
        $ttsEndpointUri.Port -ne 8094 -or $ttsEndpointUri.AbsolutePath -ne '/' -or
        $ttsEndpointUri.Query -or $ttsEndpointUri.Fragment) {
        throw 'TtsServerEndpoint must be the loopback warm server base URL, such as http://127.0.0.1:8094.'
    }
    $TtsServerEndpoint = $TtsServerEndpoint.TrimEnd('/')
}
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dfuRoot = Split-Path $repoRoot -Parent
$protocolHelpers = Join-Path $PSScriptRoot 'NativePilotAudioProtocol.ps1'
. $protocolHelpers
$dialogueRuntime = Join-Path $dfuRoot 'local-llm'
$serverPath = (Resolve-Path -LiteralPath (Join-Path $dialogueRuntime 'bin\llama-server.exe')).Path
$dialogueLauncher = Join-Path $repoRoot 'tools\StartLocalDialogue.ps1'
$decisionModel = (Resolve-Path -LiteralPath (Join-Path $dialogueRuntime 'models\d3-mini\vllm-d3-mini-Q4_K_M.gguf')).Path
$dialogueEndpoint = 'http://127.0.0.1:8080'
$decisionEndpoint = 'http://127.0.0.1:8093'
$expectedGameExe = (Resolve-Path -LiteralPath (Join-Path $dfuRoot 'Player\DaggerfallUnity.exe')).Path
$trialRootPath = [IO.Path]::GetFullPath($TrialRoot)
$requestRoot = Join-Path $trialRootPath 'requests'
$responseRoot = Join-Path $trialRootPath 'responses'
$measurementRoot = Join-Path $trialRootPath 'measurements'

$audioRoot = Join-Path $dfuRoot 'local-tts'
$audioRuntime = Join-Path $audioRoot 'runtime\audio-v0.9.2-drama3-base-small-cuda12.4'
$audioCli = (Resolve-Path -LiteralPath (Join-Path $audioRuntime 'audiocpp_cli.exe')).Path
$audioModel = (Resolve-Path -LiteralPath (Join-Path $audioRoot 'models\Breeze-TTS-2-GGUF\breeze-tts-2-q4_0.gguf')).Path
$audioSpec = (Resolve-Path -LiteralPath (Join-Path $audioRuntime 'model_specs\breeze_tts.json')).Path
$expectedAudioCliSha256 = 'ee1ae0b3dce0918f33dd9446e55c21f50120bcf4501a98a9a9a1ef4b7eaa91a9'
$expectedAudioSpecSha256 = 'f6e2587131fd2ca3ddc3e60b4450a58c685fcc7c1f6898748a5eab23003ca37e'
$audioManifestPath = Join-Path $audioRoot 'download-manifest.json'
$coordinator = Join-Path $PSScriptRoot 'coordinator.py'
$profileRoot = Join-Path $repoRoot 'work\breeze-tts-20261021\voices'
$profileFile = Join-Path $profileRoot 'profiles.json'
$voiceBindingsFile = Join-Path $profileRoot 'voice-bindings.json'
$pythonCommand = Get-Command python.exe -ErrorAction Stop | Select-Object -First 1
$pythonExe = $pythonCommand.Source

foreach ($requiredPath in @($dialogueLauncher, $serverPath, $decisionModel, $audioCli, $audioModel,
        $audioSpec, $audioManifestPath, $coordinator, $profileFile, $pythonExe)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Required file missing: $requiredPath" }
}
foreach ($requiredDirectory in @($trialRootPath, $requestRoot, $responseRoot, $measurementRoot)) {
    if (-not (Test-Path -LiteralPath $requiredDirectory -PathType Container)) {
        throw "Trial bridge directory missing: $requiredDirectory"
    }
}

$modelManifest = Get-Content -LiteralPath $audioManifestPath -Raw | ConvertFrom-Json
$modelArtifact = @($modelManifest.artifacts | Where-Object { $_.file -eq 'models/Breeze-TTS-2-GGUF/breeze-tts-2-q4_0.gguf' })
if ($modelArtifact.Count -ne 1 -or (-not (Test-HexValue $modelArtifact[0].sha256 64))) {
    throw 'Download manifest must pin exactly one Breeze Q4_0 model hash.'
}
$audioModelHash = [string]$modelArtifact[0].sha256
$audioCliHash = (Get-FileHash -LiteralPath $audioCli -Algorithm SHA256).Hash.ToLowerInvariant()
$audioSpecHash = (Get-FileHash -LiteralPath $audioSpec -Algorithm SHA256).Hash.ToLowerInvariant()
if ($audioCliHash -ne $expectedAudioCliSha256 -or $audioSpecHash -ne $expectedAudioSpecSha256) {
    throw 'Breeze CLI or model spec SHA-256 does not match the pinned trial build.'
}
$manifestModelPath = Join-Path $audioRoot (([string]$modelArtifact[0].file).Replace('/', '\'))
if (-not [string]::Equals((Resolve-Path -LiteralPath $manifestModelPath).Path, $audioModel,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Breeze model path does not match the pinned download manifest.'
}

. (Join-Path $repoRoot 'tools\ResolveLocalDialogueModel.ps1')
$selectionPath = Join-Path $dialogueRuntime 'active-model.json'
if (-not (Test-Path -LiteralPath $selectionPath -PathType Leaf)) {
    throw "Qwen active-model selection missing: $selectionPath"
}
$activeModelHash = (Get-FileHash -LiteralPath $selectionPath -Algorithm SHA256).Hash.ToLowerInvariant()
$selection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
$qwenModel = (Resolve-LocalDialogueModel -RuntimeRoot $dialogueRuntime -ModelPath $selection.modelPath)
$qwenModel = (Resolve-Path -LiteralPath $qwenModel).Path
$qwenExecutionDevice = 'CPU'
if ($selection.PSObject.Properties.Name -contains 'executionDevice' -and
    -not [string]::IsNullOrWhiteSpace([string]$selection.executionDevice)) {
    $qwenExecutionDevice = ([string]$selection.executionDevice).ToUpperInvariant()
}
if ($qwenExecutionDevice -notin @('CPU', 'GPU')) {
    throw "Qwen executionDevice must be CPU or GPU; found '$qwenExecutionDevice'."
}

$runRoot = Join-Path $trialRootPath 'operator-runner'
$logRoot = Join-Path $runRoot 'logs'
$eventPath = Join-Path $runRoot 'events.jsonl'
$eventWriter = $null
$script:runnerStartedUtc = [DateTime]::UtcNow
$script:gameIdentity = $null
$script:d3Identity = $null
$script:qwenIdentity = $null
$script:qwenStopped = $false
$script:qwenGpuLayers = 0
$script:coordinatorProcess = $null
$script:coordinatorRequestId = $null

function Write-RunnerEvent {
    param([Parameter(Mandatory = $true)] [string]$Name, [hashtable]$Data = @{})
    if (-not $eventWriter) { return }
    $row = [ordered]@{
        event = $Name
        utc = [DateTime]::UtcNow.ToString('o')
        monotonicTicks = [Diagnostics.Stopwatch]::GetTimestamp()
        monotonicFrequency = [Diagnostics.Stopwatch]::Frequency
        data = $Data
    }
    $eventWriter.WriteLine(($row | ConvertTo-Json -Depth 24 -Compress))
}

function Get-CimProcessIdentity {
    param([Parameter(Mandatory = $true)] [int]$ProcessId)
    $row = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    if (-not $row) { return $null }
    if ($row.CreationDate -is [DateTime]) {
        $created = $row.CreationDate.ToUniversalTime()
    } else {
        $created = [System.Management.ManagementDateTimeConverter]::ToDateTime([string]$row.CreationDate).ToUniversalTime()
    }
    [pscustomobject]@{
        pid = [int]$row.ProcessId
        executable = [string]$row.ExecutablePath
        commandLine = [string]$row.CommandLine
        createdUtc = $created.ToString('o')
    }
}

function Assert-SameProcess {
    param([Parameter(Mandatory = $true)]$Expected, [Parameter(Mandatory = $true)] [string]$Role)
    $actual = Get-CimProcessIdentity -ProcessId ([int]$Expected.pid)
    if (-not $actual -or $actual.createdUtc -ne $Expected.createdUtc -or
        -not [string]::Equals($actual.executable, $Expected.executable, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($actual.commandLine, $Expected.commandLine, [StringComparison]::Ordinal)) {
        throw "$Role process identity changed or exited; refusing to continue."
    }
    return $actual
}

function Get-ListenerIdentity {
    param([Parameter(Mandatory = $true)] [int]$Port, [Parameter(Mandatory = $true)] [string]$Role)
    $connections = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($connections.Count -ne 1) { throw "$Role must own exactly one listener on port $Port; found $($connections.Count)." }
    $connection = $connections[0]
    $address = [Net.IPAddress]::Parse([string]$connection.LocalAddress)
    if (-not [Net.IPAddress]::IsLoopback($address)) { throw "$Role listener on port $Port is not loopback." }
    $process = Get-CimProcessIdentity -ProcessId ([int]$connection.OwningProcess)
    if (-not $process) { throw "$Role listener owner process is unavailable on port $Port." }
    if (-not [string]::Equals($process.executable, $serverPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Role listener on port $Port uses an unexpected executable: $($process.executable)"
    }
    [pscustomobject]@{
        port = $Port
        localAddress = [string]$connection.LocalAddress
        process = $process
    }
}

function Assert-ListenerStable {
    param([Parameter(Mandatory = $true)]$Expected, [Parameter(Mandatory = $true)] [string]$Role)
    $actual = Get-ListenerIdentity -Port ([int]$Expected.port) -Role $Role
    if ($actual.localAddress -ne $Expected.localAddress -or
        $actual.process.pid -ne $Expected.process.pid -or
        $actual.process.createdUtc -ne $Expected.process.createdUtc -or
        -not [string]::Equals($actual.process.commandLine, $Expected.process.commandLine, [StringComparison]::Ordinal)) {
        throw "$Role listener identity changed on port $($Expected.port); refusing to continue."
    }
    return $actual
}

function Assert-GameAndDecisionService {
    $game = Assert-SameProcess -Expected $script:gameIdentity -Role 'Owned game'
    if (-not [string]::Equals($game.executable, $expectedGameExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Owned game PID $OwnedGameProcessId is not the expected DaggerfallUnity.exe."
    }
    $d3 = Assert-ListenerStable -Expected $script:d3Identity -Role 'D3'
    if (-not (Test-Path -LiteralPath $decisionModel -PathType Leaf)) { throw 'Pinned D3 model disappeared.' }
    return [pscustomobject]@{ game = $game; d3 = $d3 }
}

function Assert-SelectedQwenConfig {
    $currentHash = (Get-FileHash -LiteralPath $selectionPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($currentHash -ne $activeModelHash) { throw 'Qwen active-model configuration changed during the trial.' }
    $currentSelection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
    $currentPath = Resolve-LocalDialogueModel -RuntimeRoot $dialogueRuntime -ModelPath $currentSelection.modelPath
    if (-not [string]::Equals((Resolve-Path -LiteralPath $currentPath).Path, $qwenModel,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Qwen selected model changed during the trial.'
    }
}

function Assert-QwenCommandLine {
    param([Parameter(Mandatory = $true)]$Identity)
    $line = [string]$Identity.commandLine
    $quotedModel = '"' + $qwenModel + '"'
    if (-not $line.Contains($quotedModel) -and
        $line.IndexOf('-m ' + $qwenModel, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'Qwen command line does not contain the exact selected model path.'
    }
    foreach ($setting in @(@('-c','4096'),@('--host','127.0.0.1'),@('--port','8080'),@('-np','1'))) {
        if (-not (Test-NativeOption $line $setting[0] $setting[1])) { throw "Qwen command line misses required setting: $($setting[0])" }
    }
    return Assert-NativeQwenDeviceState -CommandLine $line -ExpectedMode $qwenExecutionDevice
}

function Get-QwenState {
    param([switch]$AllowBusy)
    Assert-SelectedQwenConfig
    $listener = Get-ListenerIdentity -Port 8080 -Role 'Qwen'
    if ($script:qwenIdentity -and
        ($listener.process.pid -ne $script:qwenIdentity.pid -or
         $listener.process.createdUtc -ne $script:qwenIdentity.createdUtc -or
         -not [string]::Equals($listener.process.executable, $script:qwenIdentity.executable, [StringComparison]::OrdinalIgnoreCase) -or
         -not [string]::Equals($listener.process.commandLine, $script:qwenIdentity.commandLine, [StringComparison]::Ordinal))) {
        throw 'Qwen listener no longer matches the recorded process identity.'
    }
    $deviceState = Assert-QwenCommandLine -Identity $listener.process
    $health = Invoke-RestMethod -Uri "$dialogueEndpoint/health" -TimeoutSec 3 -ErrorAction Stop
    if ([string]$health.status -ne 'ok') { throw 'Qwen /health did not report status=ok.' }
    $props = Invoke-RestMethod -Uri "$dialogueEndpoint/props" -TimeoutSec 3 -ErrorAction Stop
    if (-not [string]::Equals([string]$props.model_path, $qwenModel, [StringComparison]::OrdinalIgnoreCase) -or
        [int]$props.default_generation_settings.n_ctx -ne 4096 -or [int]$props.total_slots -ne 1) {
        throw 'Qwen /props does not match selected model, context 4096 and one slot.'
    }
    $slots = Invoke-RestMethod -Uri "$dialogueEndpoint/slots" -TimeoutSec 3 -ErrorAction Stop
    if ($slots -isnot [Array] -or $slots.Count -ne 1) {
        throw "Qwen /slots must report exactly one slot; found $($slots.Count)."
    }
    $idle = Test-QwenSlotIdle -Slots $slots -AllowBusy:$AllowBusy
    [pscustomobject]@{ listener = $listener; idle = $idle; modelPath = $qwenModel;
        executionDevice = $deviceState.mode; gpuLayers = $deviceState.gpuLayers; activeConfigSha256 = $activeModelHash }
}

function Wait-QwenIdle {
    param([DateTime]$Deadline)
    while ([DateTime]::UtcNow -lt $Deadline) {
        $null = Assert-GameAndDecisionService
        if (-not $script:qwenStopped) {
            # CPU Qwen is meant to remain resident while the independent Breeze GPU server runs.
            # Verify its original identity and configured execution mode during every TTS wait.
            $resident = Get-QwenState -AllowBusy
            if ($resident.executionDevice -cne $qwenExecutionDevice) {
                throw 'Qwen execution device changed while the native TTS request was running.'
            }
        }
        try {
            $state = Get-QwenState
            if ($state.idle) { return $state }
        } catch {
            if ($_.Exception.Message -notlike 'Qwen /slots does not prove*') { throw }
        }
        Start-Sleep -Seconds $PollSeconds
    }
    throw "Qwen did not become idle within $TimeoutSeconds seconds."
}

function Test-RequestCancelled {
    param([Parameter(Mandatory = $true)] [string]$RequestId)
    return Test-Path -LiteralPath (Join-Path $requestRoot ($RequestId + '.cancel.json')) -PathType Leaf
}

function Get-NextNativeRequest {
    $candidates = @(Get-ChildItem -LiteralPath $requestRoot -Filter '*.request.json' -File | Sort-Object Name)
    $pending = @()
    foreach ($file in $candidates) {
        $requestId = $file.Name.Substring(0, $file.Name.Length - '.request.json'.Length)
        if (Test-RequestCancelled -RequestId $requestId) { continue }
        $manifest = Join-Path $responseRoot ($requestId + '.manifest.json')
        if (Test-Path -LiteralPath $manifest -PathType Leaf) { continue }
        $pending += $file
    }
    if ($pending.Count -gt 1) { throw 'More than one uncancelled native request is pending; refusing to reorder prompts.' }
    if ($pending.Count -eq 0) { return $null }
    $request = Get-Content -LiteralPath $pending[0].FullName -Raw | ConvertFrom-Json
    $expectedId = $pending[0].Name.Substring(0, $pending[0].Name.Length - '.request.json'.Length)
    if ((-not (Test-ProtocolIdentifier $expectedId)) -or
        $request.schemaVersion -ne 1 -or $request.mode -ne 'full-wav-pilot' -or
        [string]$request.requestId -cne $expectedId -or [string]::IsNullOrWhiteSpace([string]$request.conversationToken) -or
        [string]::IsNullOrWhiteSpace([string]$request.personId) -or [string]::IsNullOrWhiteSpace([string]$request.text)) {
        throw "Pending native request $expectedId has invalid or mismatched content."
    }
    [pscustomobject]@{
        path = $pending[0].FullName
        id = $expectedId
        textLength = ([string]$request.text).Length
        textSha256 = Get-NativePilotTextSha256 -Text ([string]$request.text)
        conversationToken = [string]$request.conversationToken
        personId = [string]$request.personId
    }
}

function Wait-NextNativeRequest {
    param([DateTime]$Deadline)
    while ([DateTime]::UtcNow -lt $Deadline) {
        $null = Assert-GameAndDecisionService
        Assert-SelectedQwenConfig
        $request = Get-NextNativeRequest
        if ($request) { return $request }
        Start-Sleep -Seconds $PollSeconds
    }
    throw "Timed out after $TimeoutSeconds seconds waiting for next native finalized-text request."
}

function Stop-VerifiedQwen {
    param([Parameter(Mandatory = $true)]$Expected)
    if ($qwenExecutionDevice -cne 'GPU' -or $Expected.executionDevice -cne 'GPU' -or $Expected.gpuLayers -le 0) {
        throw 'Refusing Qwen stop unless active-model.json explicitly selects GPU and live Qwen proves positive GPU layers.'
    }
    $null = Assert-GameAndDecisionService
    $actual = Get-QwenState
    if (-not $actual.idle -or $actual.listener.process.pid -ne $Expected.listener.process.pid -or
        $actual.listener.process.createdUtc -ne $Expected.listener.process.createdUtc -or
        $actual.listener.process.commandLine -ne $Expected.listener.process.commandLine) {
        throw 'Qwen changed or became busy after idle verification; refusing to stop it.'
    }
    $qwenPid = [int]$actual.listener.process.pid
    $qwenHandle = Get-Process -Id $qwenPid -ErrorAction Stop
    if (-not [string]::Equals([string]$qwenHandle.Path, $serverPath, [StringComparison]::OrdinalIgnoreCase) -or
        [Math]::Abs(($qwenHandle.StartTime.ToUniversalTime() - [DateTime]::Parse($actual.listener.process.createdUtc).ToUniversalTime()).TotalMilliseconds) -gt 10) {
        throw 'Qwen process handle no longer matches verified executable and creation time; refusing to stop it.'
    }
    $script:qwenStopped = $true
    Write-RunnerEvent -Name 'qwen_stop_requested' -Data @{ process = $actual.listener.process; port = 8080 }
    $qwenHandle.Kill()
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $remaining = Get-CimProcessIdentity -ProcessId $qwenPid
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue)
        if (-not $remaining -and $listeners.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if ((Get-CimProcessIdentity -ProcessId $qwenPid) -or
        @(Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'Verified Qwen process or port 8080 listener did not stop cleanly.'
    }
    $script:qwenIdentity = $actual.listener.process
    Write-RunnerEvent -Name 'qwen_stopped' -Data @{ process = $actual.listener.process; port = 8080 }
}

function Restore-Qwen {
    if (-not $script:qwenStopped) { return }
    Assert-SelectedQwenConfig
    Write-RunnerEvent -Name 'qwen_restore_started' -Data @{ modelPath = $qwenModel; activeConfigSha256 = $activeModelHash }
    $script:qwenIdentity = $null
    & $dialogueLauncher -Background -ExecutionDevice GPU -GpuLayers $script:qwenGpuLayers -ContextSize 4096 -Port 8080 `
        -ReadinessTimeoutSeconds ([Math]::Min(90, $TimeoutSeconds)) -LogDirectory $logRoot -ModelPath $qwenModel
    $restored = Get-QwenState -AllowBusy
    $script:qwenIdentity = $restored.listener.process
    $script:qwenStopped = $false
    Write-RunnerEvent -Name 'qwen_restored' -Data @{ process = $restored.listener.process; port = 8080;
        modelPath = $qwenModel; contextSize = 4096; slots = 1; activeConfigSha256 = $activeModelHash }
}

function Wait-CoordinatorAndPreserveSafety {
    param([System.Diagnostics.Process]$Process)
    $stdoutTask = $Process.StandardOutput.ReadToEndAsync()
    $stderrTask = $Process.StandardError.ReadToEndAsync()
    while (-not $Process.WaitForExit(1000)) {
        $null = Assert-GameAndDecisionService
        if (-not $script:qwenStopped) {
            $residentQwen = Get-QwenState -AllowBusy
            if ($residentQwen.executionDevice -cne $qwenExecutionDevice -or
                $residentQwen.listener.process.pid -ne $script:qwenIdentity.pid -or
                $residentQwen.listener.process.createdUtc -cne $script:qwenIdentity.createdUtc -or
                $residentQwen.listener.process.commandLine -cne $script:qwenIdentity.commandLine) {
                throw 'Resident Qwen identity or execution mode changed during Breeze synthesis.'
            }
        }
        if (Test-RequestCancelled -RequestId $script:coordinatorRequestId) {
            Write-RunnerEvent -Name 'native_request_cancel_seen_during_synthesis' -Data @{ requestId = $script:coordinatorRequestId }
        }
    }
    $Process.Refresh()
    $Process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    return [pscustomobject]@{ exitCode = [int]$Process.ExitCode; stdout = $stdout; stderr = $stderr }
}

function Get-PlaybackPath {
    param([Parameter(Mandatory = $true)] [string]$RequestId)
    $path = Join-Path $measurementRoot ($RequestId + '.playback.json')
    if (Test-Path -LiteralPath $path -PathType Leaf) { return $path }
    return $null
}

function Wait-PlaybackCompleted {
    param([Parameter(Mandatory = $true)] [string]$RequestId, [DateTime]$Deadline)
    while ([DateTime]::UtcNow -lt $Deadline) {
        $null = Assert-GameAndDecisionService
        $path = Get-PlaybackPath -RequestId $RequestId
        if ($path) {
            if (Test-NativePlaybackCompleted -Path $path -RequestId $RequestId) {
                $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                Write-RunnerEvent -Name 'native_playback_completed' -Data @{ requestId = $RequestId; path = $path; record = $record }
                return
            }
        }
        Start-Sleep -Seconds $PollSeconds
    }
    throw "Timed out waiting for native playback completion for request $RequestId."
}

function Invoke-NativeAudioCycle {
    param([Parameter(Mandatory = $true)]$Request)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $requestId = [string]$Request.id
    Write-RunnerEvent -Name 'native_request_ready' -Data @{ requestId = $requestId; requestPath = $Request.path;
        textLength = $Request.textLength }
    $null = Assert-GameAndDecisionService
    # Independent CPU inference does not need to drain before GPU speech.
    # Only a GPU-resident Qwen must become idle before its guarded unload.
    $qwen = if ($qwenExecutionDevice -ceq 'CPU') {
        Get-QwenState -AllowBusy
    } else {
        Wait-QwenIdle -Deadline $deadline
    }
    if (Test-RequestCancelled -RequestId $requestId) {
        Write-RunnerEvent -Name 'native_request_cancelled_before_switch' -Data @{ requestId = $requestId }
        return
    }
    if ($qwen.executionDevice -eq 'GPU') {
        if ($qwenExecutionDevice -cne 'GPU') { throw 'Refusing Qwen stop without explicit GPU selection in active-model.json.' }
        Stop-VerifiedQwen -Expected $qwen
    } else {
        if ($qwenExecutionDevice -cne 'CPU' -or $qwen.gpuLayers -ne 0) {
            throw 'Refusing to run CPU-resident path with inconsistent Qwen device evidence.'
        }
        Write-RunnerEvent -Name 'qwen_kept_resident_cpu' -Data @{ process = $qwen.listener.process;
            modelPath = $qwenModel; executionDevice = 'CPU'; gpuLayers = 0 }
    }
    $script:coordinatorRequestId = $requestId
    $stdoutPath = Join-Path $logRoot ($requestId + '.coordinator.stdout.log')
    $stderrPath = Join-Path $logRoot ($requestId + '.coordinator.stderr.log')
    $coordinatorArgs = @('-u', $coordinator, '--cli', $audioCli, '--model', $audioModel,
        '--model-sha256', $audioModelHash, '--spec', $audioSpec, '--profiles', $profileFile,
        '--bindings', $voiceBindingsFile, '--voice-root', $profileRoot,
        '--root', $trialRootPath, '--backend', 'cuda', '--timeout', [string]$TimeoutSeconds)
    if (-not [string]::IsNullOrWhiteSpace($TtsServerEndpoint)) {
        $coordinatorArgs += @('--server-endpoint', $TtsServerEndpoint)
    }
    if ($CaptureReferenceProfileId) { $coordinatorArgs += @('--promote-reference-profile', $CaptureReferenceProfileId) }
    try {
        $pythonInfo = Get-CimProcessIdentity -ProcessId $PID
        Write-RunnerEvent -Name 'breeze_coordinator_starting' -Data @{
            requestId = $requestId; python = $pythonExe; coordinator = $coordinator; arguments = $coordinatorArgs
            backend = $(if ($TtsServerEndpoint) { 'server-sse' } else { 'cuda' });
            serverEndpoint = $TtsServerEndpoint; modelPath = $audioModel; modelSha256 = $audioModelHash
            cliPath = $audioCli; cliSha256 = $audioCliHash; specPath = $audioSpec; specSha256 = $audioSpecHash; activeQwen = $qwenModel
            qwenExecutionDevice = $qwen.executionDevice; qwenGpuLayers = $qwen.gpuLayers
            qwenStoppedProcess = $(if ($script:qwenStopped) { $qwen.listener.process } else { $null })
            qwenKeptResident = -not $script:qwenStopped; parentPowerShell = $pythonInfo
        }
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $pythonExe
        $startInfo.WorkingDirectory = $repoRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        if (-not ($startInfo.PSObject.Properties.Name -contains 'ArgumentList')) {
            throw 'This runner requires PowerShell 7 .NET ProcessStartInfo.ArgumentList for safe path quoting.'
        }
        foreach ($argument in $coordinatorArgs) { $startInfo.ArgumentList.Add([string]$argument) }
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $script:coordinatorProcess = $process
        $coordinatorIdentity = Get-CimProcessIdentity -ProcessId $process.Id
        Write-RunnerEvent -Name 'breeze_coordinator_started' -Data @{ requestId = $requestId; process = $coordinatorIdentity;
            stdout = $stdoutPath; stderr = $stderrPath }
        $processResult = Wait-CoordinatorAndPreserveSafety -Process $process
        $exitCode = [int]$processResult.exitCode
        [IO.File]::WriteAllText($stdoutPath, [string]$processResult.stdout, [System.Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($stderrPath, [string]$processResult.stderr, [System.Text.UTF8Encoding]::new($false))
        $script:coordinatorProcess = $null
        $summaryPath = Join-Path $measurementRoot ($requestId + '.evidence.json')
        $resultPath = Join-Path $responseRoot ($requestId + '.manifest.json')
        $failedPath = Join-Path $measurementRoot ($requestId + '.failed.json')
        $cancelled = Test-RequestCancelled -RequestId $requestId
        if ($cancelled) { throw "Native request $requestId was cancelled during synthesis." }
        $expectedTtsBackend = if ([string]::IsNullOrWhiteSpace($TtsServerEndpoint)) { 'cuda' } else { 'server-sse' }
        $result = Assert-NativeAudioCoordinatorResult -ManifestPath $resultPath -EvidencePath $summaryPath `
            -ResponseRoot $responseRoot -RequestId $requestId -ConversationToken $Request.conversationToken `
            -PersonId $Request.personId -TextSha256 $Request.textSha256 -ExpectedModelSha256 $audioModelHash `
            -ExpectedModelFile ([IO.Path]::GetFileName($audioModel)) -ExpectedBackend $expectedTtsBackend `
            -ExpectedServerEndpoint $TtsServerEndpoint -CoordinatorExitCode $exitCode
        Write-RunnerEvent -Name 'breeze_coordinator_exited' -Data @{ requestId = $requestId; exitCode = $exitCode;
            cancelled = $cancelled; result = $result; evidencePath = $summaryPath; stdout = $stdoutPath; stderr = $stderrPath }
    } finally {
        if ($script:coordinatorProcess) {
            $script:coordinatorProcess.WaitForExit()
            $script:coordinatorProcess.Refresh()
            $script:coordinatorProcess = $null
        }
        Restore-Qwen
    }
    Write-RunnerEvent -Name 'native_playback_wait_started' -Data @{ requestId = $requestId }
    Wait-PlaybackCompleted -RequestId $requestId -Deadline ([DateTime]::UtcNow.AddSeconds($TimeoutSeconds))
}

try {
    $script:gameIdentity = Get-CimProcessIdentity -ProcessId $OwnedGameProcessId
    if (-not $script:gameIdentity) { throw "Owned game PID $OwnedGameProcessId is not running." }
    if (-not [string]::Equals($script:gameIdentity.executable, $expectedGameExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Owned game PID $OwnedGameProcessId is not the expected executable: $($script:gameIdentity.executable)"
    }
    $d3 = Get-ListenerIdentity -Port 8093 -Role 'D3'
    if ((-not $d3.process.commandLine.Contains('"' + $decisionModel + '"')) -and
        ($d3.process.commandLine.IndexOf('-m ' + $decisionModel, [StringComparison]::OrdinalIgnoreCase) -lt 0)) {
        throw 'D3 listener command line does not contain the exact pinned model path.'
    }
    if (
        -not (Test-NativeOption $d3.process.commandLine '-c' '4096') -or
        -not (Test-NativeOption $d3.process.commandLine '--host' '127.0.0.1') -or
        -not (Test-NativeOption $d3.process.commandLine '--port' '8093') -or
        -not (Test-NativeOption $d3.process.commandLine '-np' '1')) {
        throw 'D3 listener command line does not match pinned model, loopback, port, context and slot configuration.'
    }
    $script:d3Identity = $d3
    $initialQwen = Get-QwenState
    $script:qwenIdentity = $initialQwen.listener.process
    if ($ValidateOnly) {
        @{
            validationOnly = $true
            game = $script:gameIdentity
            d3 = $script:d3Identity
            qwen = $script:qwenIdentity
            qwenModel = $qwenModel
            qwenExecutionDevice = $initialQwen.executionDevice
            qwenGpuLayers = $initialQwen.gpuLayers
            qwenContext = 4096
            qwenSlots = 1
            activeConfigSha256 = $activeModelHash
        } | ConvertTo-Json -Depth 12 -Compress | Write-Output
        return
    }
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $eventWriter = [System.IO.StreamWriter]::new($eventPath, $true, [System.Text.UTF8Encoding]::new($false))
    $eventWriter.AutoFlush = $true
    Write-RunnerEvent -Name 'runner_ready' -Data @{
        trialRoot = $trialRootPath; game = $script:gameIdentity; d3 = $script:d3Identity
        qwen = $script:qwenIdentity; qwenModel = $qwenModel; qwenContext = 4096; qwenSlots = 1
        qwenExecutionDevice = $initialQwen.executionDevice; qwenGpuLayers = $initialQwen.gpuLayers
        activeConfigSha256 = $activeModelHash; breezeModel = $audioModel; breezeModelSha256 = $audioModelHash
        audioCli = $audioCli; audioCliSha256 = $audioCliHash; audioSpec = $audioSpec; audioSpecSha256 = $audioSpecHash
        profiles = $profileFile; timeoutSeconds = $TimeoutSeconds
    }
    while ($true) {
        $request = Wait-NextNativeRequest -Deadline ([DateTime]::UtcNow.AddSeconds($TimeoutSeconds))
        Invoke-NativeAudioCycle -Request $request
    }
} catch {
    Write-RunnerEvent -Name 'runner_error' -Data @{ message = $_.Exception.Message }
    throw
} finally {
    try { Restore-Qwen } catch {
        Write-RunnerEvent -Name 'qwen_restore_failed' -Data @{ message = $_.Exception.Message }
        Write-Error "Qwen restore failed: $($_.Exception.Message)"
    }
    if ($eventWriter) { $eventWriter.Dispose() }
}
