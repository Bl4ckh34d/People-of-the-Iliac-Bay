
. (Join-Path $PSScriptRoot '../TextProtocol.ps1')
function Get-NativePilotTextSha256 {
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string]$Text)
    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    $bytes = $encoding.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Test-QwenSlotIdle {
    param(
        [Parameter(Mandatory = $true)] [object[]]$Slots,
        [switch]$AllowBusy
    )
    if ($Slots.Count -ne 1) { throw "Qwen /slots must report exactly one slot; found $($Slots.Count)." }
    $slot = $Slots[0]
    $idle = $false
    $stateKnown = $false
    if ($slot.PSObject.Properties.Name -contains 'is_processing') {
        if ($slot.is_processing -isnot [bool]) { throw 'Qwen /slots is_processing must be boolean.' }
        $idle = -not [bool]$slot.is_processing
        $stateKnown = $true
    } elseif ($slot.PSObject.Properties.Name -contains 'state' -and
        [string]$slot.state -ieq 'idle') {
        $idle = $true
        $stateKnown = $true
    }
    if (-not $stateKnown -and -not $AllowBusy) {
        throw 'Qwen /slots does not prove that the single slot is idle.'
    }
    if (-not $idle -and -not $AllowBusy) {
        throw 'Qwen /slots does not prove that the single slot is idle.'
    }
    return $idle
}

function Get-NativeQwenDeviceState {
    param([Parameter(Mandatory = $true)] [string]$CommandLine)
    $deviceMatches = @(Get-NativeOptionValues $CommandLine '--device')
    $layerMatches = @(Get-NativeOptionValues $CommandLine '-ngl')
    if ($deviceMatches.Count -ne 1 -or $layerMatches.Count -ne 1) {
        throw 'Qwen command line must specify exactly one --device and -ngl setting.'
    }
    $device = $deviceMatches[0]
    if(-not (Test-DecimalValue $layerMatches[0])){throw 'Invalid GPU layer count.'}
    $gpuLayers = [int]$layerMatches[0]
    $hasNoKvOffload = (Test-NativeFlag $CommandLine '--no-kv-offload')
    $hasNoOpOffload = (Test-NativeFlag $CommandLine '--no-op-offload')
    $hasKvOffload = (Test-NativeFlag $CommandLine '--kv-offload')
    $hasOpOffload = (Test-NativeFlag $CommandLine '--op-offload')
    if ($device -ieq 'none' -and $gpuLayers -eq 0 -and $hasNoKvOffload -and $hasNoOpOffload) {
        return [pscustomobject]@{ mode = 'CPU'; device = $device; gpuLayers = $gpuLayers }
    }
    if (($device.StartsWith('CUDA',[StringComparison]::OrdinalIgnoreCase) -and (Test-DecimalValue $device.Substring(4))) -and $gpuLayers -gt 0 -and
        $hasKvOffload -and $hasOpOffload -and -not $hasNoKvOffload -and -not $hasNoOpOffload) {
        return [pscustomobject]@{ mode = 'GPU'; device = $device; gpuLayers = $gpuLayers }
    }
    throw 'Qwen device flags are mixed or incomplete; expected explicit CPU-offload flags or an explicit CUDA device with positive GPU layers.'
}

function Assert-NativeQwenDeviceState {
    param(
        [Parameter(Mandatory = $true)] [string]$CommandLine,
        [Parameter(Mandatory = $true)] [ValidateSet('CPU', 'GPU')] [string]$ExpectedMode
    )
    $state = Get-NativeQwenDeviceState -CommandLine $CommandLine
    if ($state.mode -cne $ExpectedMode) {
        throw "Qwen live device mode '$($state.mode)' does not match explicit active-model mode '$ExpectedMode'."
    }
    return $state
}

function Assert-NativeAudioCoordinatorResult {
    param(
        [Parameter(Mandatory = $true)] [string]$ManifestPath,
        [Parameter(Mandatory = $true)] [string]$EvidencePath,
        [Parameter(Mandatory = $true)] [string]$ResponseRoot,
        [Parameter(Mandatory = $true)] [string]$RequestId,
        [Parameter(Mandatory = $true)] [string]$ConversationToken,
        [Parameter(Mandatory = $true)] [string]$PersonId,
        [Parameter(Mandatory = $true)] [string]$TextSha256,
        [Parameter(Mandatory = $true)] [string]$ExpectedModelSha256,
        [Parameter(Mandatory = $true)] [string]$ExpectedModelFile,
        [ValidateSet('cuda', 'server-sse')] [string]$ExpectedBackend = 'cuda',
        [string]$ExpectedServerEndpoint,
        [Parameter(Mandatory = $true)] [int]$CoordinatorExitCode
    )
    if ($CoordinatorExitCode -ne 0) { throw "Breeze coordinator exited with code $CoordinatorExitCode." }
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "Breeze response manifest missing: $ManifestPath" }
    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) { throw "Breeze evidence missing: $EvidencePath" }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $expectedManifestFields = @('schemaVersion', 'requestId', 'conversationToken', 'personId', 'encoding',
        'wavFile', 'sampleRate', 'channels', 'sampleFrames') | Sort-Object
    $actualManifestFields = @($manifest.PSObject.Properties.Name | Sort-Object)
    if (Compare-Object -ReferenceObject $expectedManifestFields -DifferenceObject $actualManifestFields -CaseSensitive) {
        throw 'Breeze manifest fields do not match the native schema.'
    }
    if ($manifest.schemaVersion -ne 1 -or [string]$manifest.requestId -cne $RequestId -or
        [string]$manifest.conversationToken -cne $ConversationToken -or [string]$manifest.personId -cne $PersonId) {
        throw 'Breeze manifest does not match the exact native request identity.'
    }
    if ([string]$manifest.encoding -cne 'pcm_s16le' -or [string]$manifest.wavFile -cne ($RequestId + '.wav')) {
        throw 'Breeze manifest has an unexpected PCM format or WAV filename.'
    }
    if ([int]$manifest.sampleRate -lt 8000 -or [int]$manifest.sampleRate -gt 192000 -or
        [int]$manifest.channels -notin @(1, 2) -or [long]$manifest.sampleFrames -lt 1) {
        throw 'Breeze manifest has invalid sample rate, channel count, or frame count.'
    }

    $evidence = Get-Content -LiteralPath $EvidencePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    foreach ($field in @('schemaVersion', 'requestId', 'conversationToken', 'personId', 'finalizedTextSha256',
            'wavSha256', 'modelSha256', 'modelFile', 'backend', 'audioCppVersion', 'wavDurationSeconds')) {
        if (-not ($evidence.PSObject.Properties.Name -contains $field)) { throw "Breeze evidence misses '$field'." }
    }
    if ($evidence.schemaVersion -ne 1 -or [string]$evidence.requestId -cne $RequestId -or
        [string]$evidence.conversationToken -cne $ConversationToken -or [string]$evidence.personId -cne $PersonId -or
        [string]$evidence.finalizedTextSha256 -cne $TextSha256) {
        throw 'Breeze evidence does not match the exact native request or finalized text.'
    }
    $runtimeLines = @((([string][string]$evidence.audioCppVersion).Replace("`r",'').Split([char]10)) | ForEach-Object { $_.Trim() })
    if ([string]$evidence.modelSha256 -ine $ExpectedModelSha256 -or
        [string]$evidence.modelFile -cne $ExpectedModelFile -or [string]$evidence.backend -cne $ExpectedBackend) {
        throw 'Breeze evidence does not match the pinned model or expected backend.'
    }
    if ($ExpectedBackend -ceq 'cuda') {
        if ($runtimeLines.Count -lt 2 -or $runtimeLines[0] -cne 'audio.cpp 0.9.2-drama3-base-small' -or
            $runtimeLines[1] -cne 'git: 50749561 2026-10-18') {
            throw 'Breeze CLI evidence does not match the pinned runtime.'
        }
    } else {
        if ([string]::IsNullOrWhiteSpace([string]$evidence.audioCppVersion)) {
            throw 'Breeze server-SSE evidence must identify its server backend version policy.'
        }
        foreach ($field in @('backendKind', 'warmServerSse', 'serverSynthesisMs', 'serverFirstAudioMs',
                'serverAudioDeltaCount', 'serverEndpoint')) {
            if (-not ($evidence.PSObject.Properties.Name -contains $field)) { throw "Breeze server-SSE evidence misses '$field'." }
        }
        if ([string]$evidence.backendKind -cne 'audio.cpp-server-sse' -or $evidence.warmServerSse -cne $true -or
            [string]$evidence.serverEndpoint -cne $ExpectedServerEndpoint) {
            throw 'Breeze evidence does not match the requested warm loopback server endpoint.'
        }
        foreach ($metric in @('serverSynthesisMs', 'serverFirstAudioMs')) {
            $value = $evidence.$metric
            if ($value -is [bool] -or $value -isnot [ValueType] -or [double]$value -lt 0 -or [double]::IsNaN([double]$value) -or [double]::IsInfinity([double]$value)) {
                throw "Breeze server-SSE metric '$metric' must be finite and nonnegative."
            }
        }
        $deltaCount = $evidence.serverAudioDeltaCount
        if ($deltaCount -is [bool] -or $deltaCount -isnot [ValueType] -or
            [double]$deltaCount -lt 1 -or [double]::IsNaN([double]$deltaCount) -or
            [double]::IsInfinity([double]$deltaCount) -or [Math]::Truncate([double]$deltaCount) -ne [double]$deltaCount) {
            throw 'Breeze server-SSE audio delta count must be a positive integer.'
        }
    }
    if ((-not (Test-HexValue ([string]$evidence.wavSha256) 64))) { throw 'Breeze evidence WAV hash is invalid.' }

    $resolvedRoot = [IO.Path]::GetFullPath($ResponseRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $wavPath = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $manifest.wavFile))
    if (-not $wavPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($wavPath) -cne ($RequestId + '.wav') -or
        -not (Test-Path -LiteralPath $wavPath -PathType Leaf)) {
        throw 'Breeze WAV path is missing or outside the exact response root.'
    }
    $wavHash = (Get-FileHash -LiteralPath $wavPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($wavHash -ine [string]$evidence.wavSha256) { throw 'Published WAV does not match its evidence SHA-256.' }
    $duration = [double]([long]$manifest.sampleFrames) / [double]([int]$manifest.sampleRate)
    if ([Math]::Abs($duration - [double]$evidence.wavDurationSeconds) -gt 0.0011) {
        throw 'Breeze evidence duration does not match the manifest frame count.'
    }
    return [pscustomobject]@{
        status = 'ready'
        requestId = $RequestId
        encoding = [string]$manifest.encoding
        wavFile = [string]$manifest.wavFile
        sampleRate = [int]$manifest.sampleRate
        channels = [int]$manifest.channels
        sampleFrames = [long]$manifest.sampleFrames
        wavSha256 = $wavHash
        modelSha256 = [string]$evidence.modelSha256
        backend = [string]$evidence.backend
        evidencePath = $EvidencePath
        manifestPath = $ManifestPath
        wavPath = $wavPath
    }
}

function Test-NativePlaybackCompleted {
    param([Parameter(Mandatory = $true)] [string]$Path, [Parameter(Mandatory = $true)] [string]$RequestId)
    $record = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if (-not ($record.PSObject.Properties.Name -contains 'requestId') -or
        -not ($record.PSObject.Properties.Name -contains 'status')) {
        throw "Playback record lacks requestId or status: $Path"
    }
    if ([string]$record.requestId -cne $RequestId) { throw "Playback record requestId does not match $RequestId." }
    if ([string]$record.status -ceq 'failed' -or [string]$record.status -ceq 'cancelled' -or
        [string]$record.status -ceq 'canceled' -or [string]$record.status -ceq 'error') {
        throw "Native playback ended with status '$($record.status)' for request $RequestId."
    }
    return ([string]$record.status -ceq 'completed')
}
