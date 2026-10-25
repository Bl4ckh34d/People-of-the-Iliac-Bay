
. (Join-Path $PSScriptRoot '../TextProtocol.ps1')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativePilotAudioProtocol.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) ('native-pilot-audio-' + [Guid]::NewGuid().ToString('N'))
$responses = Join-Path $root 'responses'
$measurements = Join-Path $root 'measurements'
New-Item -ItemType Directory -Path $responses, $measurements -Force | Out-Null

function Assert-Throws {
    param([scriptblock]$Action, [string]$Label)
    $thrown = $false
    try { & $Action } catch { $thrown = $true }
    if (-not $thrown) { throw "Expected rejection: $Label" }
}

try {
    $cpuQwenCommand = 'llama-server.exe -m C:\models\dialogue.gguf -c 4096 --host 127.0.0.1 --port 8080 -np 1 --device none -ngl 0 --no-kv-offload --no-op-offload'
    $cpuQwen = Assert-NativeQwenDeviceState -CommandLine $cpuQwenCommand -ExpectedMode CPU
    if ($cpuQwen.mode -cne 'CPU' -or $cpuQwen.gpuLayers -ne 0) { throw 'Explicit CPU Qwen device flags did not classify as CPU.' }
    Assert-Throws { Assert-NativeQwenDeviceState -CommandLine $cpuQwenCommand -ExpectedMode GPU } 'CPU Qwen cannot enter legacy GPU cycle'
    Assert-Throws {
        Get-NativeQwenDeviceState -CommandLine 'llama-server.exe --device none -ngl 0 --no-kv-offload'
    } 'CPU Qwen requires explicit KV and op offload disabled'
    $gpuQwenCommand = 'llama-server.exe -m C:\models\dialogue.gguf -c 4096 --host 127.0.0.1 --port 8080 -np 1 --device CUDA0 -ngl 99 --kv-offload --op-offload'
    $gpuQwen = Assert-NativeQwenDeviceState -CommandLine $gpuQwenCommand -ExpectedMode GPU
    if ($gpuQwen.mode -cne 'GPU' -or $gpuQwen.gpuLayers -ne 99) { throw 'Explicit GPU Qwen device flags did not classify as GPU.' }
    Assert-Throws {
        Get-NativeQwenDeviceState -CommandLine ($gpuQwenCommand + ' --no-kv-offload')
    } 'mixed GPU and CPU-offload flags'
    Write-Output 'PASS explicit CPU/GPU Qwen device classification and mismatch rejection'

    $busySlot = [pscustomobject]@{ id = 0; is_processing = $true }
    if (Test-QwenSlotIdle -Slots @($busySlot) -AllowBusy) {
        throw 'Busy Qwen slot was incorrectly reported idle.'
    }
    Assert-Throws { Test-QwenSlotIdle -Slots @($busySlot) } 'busy Qwen slot before stop'
    $idleSlot = [pscustomobject]@{ id = 0; is_processing = $false }
    if (-not (Test-QwenSlotIdle -Slots @($idleSlot))) {
        throw 'Idle Qwen slot did not pass the pre-stop gate.'
    }
    Write-Output 'PASS busy restore and strict pre-stop Qwen slot checks'

    $requestId = 'offline-001'
    $token = 'conversation-offline-001'
    $person = 'npc-offline-001'
    $text = 'Exact finalized native text.'
    $modelHash = '56b3cc598b1f9220b6fb23dc9eaa098a9a1d0cc6604298a76faac79eef4824b8'
    $modelFile = 'breeze-tts-2-q4_0.gguf'
    $wavPath = Join-Path $responses ($requestId + '.wav')
    [IO.File]::WriteAllBytes($wavPath, [byte[]](0, 1, 2, 3, 4))
    $wavHash = (Get-FileHash -LiteralPath $wavPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $textHash = Get-NativePilotTextSha256 -Text $text

    $manifestPath = Join-Path $responses ($requestId + '.manifest.json')
    $evidencePath = Join-Path $measurements ($requestId + '.evidence.json')
    @{
        schemaVersion = 1; requestId = $requestId; conversationToken = $token; personId = $person
        encoding = 'pcm_s16le'; wavFile = ($requestId + '.wav'); sampleRate = 24000; channels = 1; sampleFrames = 24000
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    @{
        schemaVersion = 1; requestId = $requestId; conversationToken = $token; personId = $person
        finalizedTextSha256 = $textHash; wavSha256 = $wavHash; modelSha256 = $modelHash; modelFile = $modelFile
        backend = 'cuda'; audioCppVersion = "audio.cpp 0.9.2-drama3-base-small`ngit: 50749561 2026-10-18`nbuild: Release, msvc 19.44.35229.0, Windows AMD64`nbackends: cpu,cuda"; wavDurationSeconds = 1.0
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8

    $accepted = Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
        -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
        -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile -CoordinatorExitCode 0
    if ($accepted.status -cne 'ready' -or $accepted.sampleFrames -ne 24000 -or $accepted.wavSha256 -ne $wavHash) {
        throw 'Valid coordinator manifest/evidence did not pass exact request and audio checks.'
    }
    Write-Output 'PASS manifest_without_status_and_pinned_evidence'

    $wrongRuntime = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    $wrongRuntime.audioCppVersion = "audio.cpp 0.9.2-drama3-base-small`ngit: 00000000 2026-10-18"
    $wrongRuntime | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    Assert-Throws {
        Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
            -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
            -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile -CoordinatorExitCode 0
    } 'wrong runtime commit'
    $wrongRuntime.audioCppVersion = "audio.cpp 0.9.2-drama3-base-small`ngit: 50749561 2026-10-18`nbuild: Release, msvc 19.44.35229.0, Windows AMD64`nbackends: cpu,cuda"
    $wrongRuntime | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    Write-Output 'PASS pinned_runtime_version_and_commit'

    $serverEvidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    $serverEvidence.backend = 'server-sse'
    $serverEvidence.audioCppVersion = 'audio.cpp server SSE; server version not queried'
    $serverEvidence | Add-Member -NotePropertyName backendKind -NotePropertyValue 'audio.cpp-server-sse' -Force
    $serverEvidence | Add-Member -NotePropertyName warmServerSse -NotePropertyValue $true -Force
    $serverEvidence | Add-Member -NotePropertyName serverSynthesisMs -NotePropertyValue 235.4 -Force
    $serverEvidence | Add-Member -NotePropertyName serverFirstAudioMs -NotePropertyValue 18.2 -Force
    $serverEvidence | Add-Member -NotePropertyName serverAudioDeltaCount -NotePropertyValue 3 -Force
    $serverEvidence | Add-Member -NotePropertyName serverTiming -NotePropertyValue $null -Force
    $serverEvidence | Add-Member -NotePropertyName serverEndpoint -NotePropertyValue 'http://127.0.0.1:8094' -Force
    $serverEvidence | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    $serverAccepted = Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
        -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
        -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile `
        -ExpectedBackend server-sse -ExpectedServerEndpoint 'http://127.0.0.1:8094' -CoordinatorExitCode 0
    if ($serverAccepted.backend -cne 'server-sse') { throw 'Warm server-SSE evidence did not pass exact endpoint and backend checks.' }
    Write-Output 'PASS warm_server_sse_evidence_and_endpoint_validation'
    $serverEvidence.serverEndpoint = 'http://127.0.0.1:8095'
    $serverEvidence | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    Assert-Throws {
        Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
            -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
            -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile `
            -ExpectedBackend server-sse -ExpectedServerEndpoint 'http://127.0.0.1:8094' -CoordinatorExitCode 0
    } 'wrong server endpoint'
    $serverEvidence.serverEndpoint = 'http://127.0.0.1:8094'
    $serverEvidence.warmServerSse = $false
    $serverEvidence | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    Assert-Throws {
        Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
            -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
            -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile `
            -ExpectedBackend server-sse -ExpectedServerEndpoint 'http://127.0.0.1:8094' -CoordinatorExitCode 0
    } 'cold server evidence on warm SSE path'
    $serverEvidence.warmServerSse = $true
    $serverEvidence.serverAudioDeltaCount = 0
    $serverEvidence | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    Assert-Throws {
        Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
            -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
            -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile `
            -ExpectedBackend server-sse -ExpectedServerEndpoint 'http://127.0.0.1:8094' -CoordinatorExitCode 0
    } 'server evidence without audio deltas'
    $serverEvidence.serverAudioDeltaCount = 1.5
    $serverEvidence | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8
    Assert-Throws {
        Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
            -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
            -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile `
            -ExpectedBackend server-sse -ExpectedServerEndpoint 'http://127.0.0.1:8094' -CoordinatorExitCode 0
    } 'fractional server audio delta count'
    Write-Output 'PASS warm_server_sse_wrong_endpoint_cold_and_empty_audio_rejections'
    $evidence | ConvertTo-Json -Compress | Set-Content -LiteralPath $evidencePath -Encoding UTF8

    $pilot03Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\work\breeze-tts-20261021\native-pilot-03'))
    $pilot03Id = 'de3442d976d7478b856b46e7c0a02bb1'
    $pilot03Evidence = Join-Path $pilot03Root ('measurements\' + $pilot03Id + '.evidence.json')
    $pilot03Manifest = Join-Path $pilot03Root ('responses\' + $pilot03Id + '.manifest.json')
    if ((Test-Path -LiteralPath $pilot03Evidence -PathType Leaf) -and
        (Test-Path -LiteralPath $pilot03Manifest -PathType Leaf)) {
        $pilot03Record = Get-Content -LiteralPath $pilot03Evidence -Raw | ConvertFrom-Json
        $pilot03Accepted = Assert-NativeAudioCoordinatorResult -ManifestPath $pilot03Manifest -EvidencePath $pilot03Evidence `
            -ResponseRoot (Join-Path $pilot03Root 'responses') -RequestId $pilot03Id `
            -ConversationToken 'eab6683e2c1c48059864c9b527df2a38' -PersonId 's:206728581:h:131074:r:0' `
            -TextSha256 '79321b212551db2f479a928dc62bd9a14f8130c940b2825419fddaa91e4f7b21' `
            -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile -CoordinatorExitCode 0
        if ($pilot03Accepted.status -cne 'ready' -or (-not ([string]$pilot03Record.audioCppVersion).Replace("`r",'').Split([char]10).Contains('git: 50749561 2026-10-18'))) {
            throw 'Real pilot-03 evidence did not pass pinned runtime identity.'
        }
        $pilot03Playback = Join-Path $pilot03Root ('measurements\' + $pilot03Id + '.playback.json')
        if (-not (Test-NativePlaybackCompleted -Path $pilot03Playback -RequestId $pilot03Id)) {
            throw 'Real pilot-03 playback record is not exactly completed.'
        }
        Write-Output 'PASS real_pilot03_multiline_runtime_evidence_offline'
    } else {
        Write-Output 'SKIP real_pilot03_manifest_or_evidence_not_present'
    }

    Assert-Throws {
        Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
            -ResponseRoot $responses -RequestId $requestId -ConversationToken $token -PersonId $person `
            -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile -CoordinatorExitCode 2
    } 'nonzero coordinator exit'
    Assert-Throws {
        Assert-NativeAudioCoordinatorResult -ManifestPath $manifestPath -EvidencePath $evidencePath `
            -ResponseRoot $responses -RequestId 'wrong-request' -ConversationToken $token -PersonId $person `
            -TextSha256 $textHash -ExpectedModelSha256 $modelHash -ExpectedModelFile $modelFile -CoordinatorExitCode 0
    } 'wrong request identity'
    Write-Output 'PASS coordinator_exit_and_request_identity_rejections'

    $playbackPath = Join-Path $measurements ($requestId + '.playback.json')
    @{ requestId = $requestId; status = 'completed' } | ConvertTo-Json -Compress | Set-Content -LiteralPath $playbackPath -Encoding UTF8
    if (-not (Test-NativePlaybackCompleted -Path $playbackPath -RequestId $requestId)) {
        throw 'Exact completed playback record was not accepted.'
    }
    @{ requestId = $requestId; status = 'ended' } | ConvertTo-Json -Compress | Set-Content -LiteralPath $playbackPath -Encoding UTF8
    if (Test-NativePlaybackCompleted -Path $playbackPath -RequestId $requestId) {
        throw 'Playback status ended must not pass as completed.'
    }
    Write-Output 'PASS exact_playback_completion_only'

    $runnerPath = Join-Path $PSScriptRoot 'RunNativePilotAudio.ps1'
    $runnerTokens = $null
    $runnerErrors = $null
    $runnerAst = [System.Management.Automation.Language.Parser]::ParseFile($runnerPath, [ref]$runnerTokens, [ref]$runnerErrors)
    if ($runnerErrors.Count) { throw "Runner parse failed: $($runnerErrors[0].Message)" }
    $identityFunction = $runnerAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Get-CimProcessIdentity'
    }, $true)
    if (-not $identityFunction) { throw 'Get-CimProcessIdentity function not found in runner.' }
    function Get-CimInstance {
        param([string]$ClassName, [string]$Filter, $ErrorAction)
        [pscustomobject]@{
            ProcessId = 9876
            ExecutablePath = 'C:\mock\llama-server.exe'
            CommandLine = 'llama-server.exe --port 8080'
            CreationDate = [DateTime]::SpecifyKind([DateTime]::new(2026, 10, 21, 2, 3, 4, 123), [DateTimeKind]::Utc)
        }
    }
    Invoke-Expression $identityFunction.Extent.Text
    $identity = Get-CimProcessIdentity -ProcessId 9876
    if ($identity.pid -ne 9876 -or $identity.createdUtc -notlike '2026-10-21T02:03:04.123*Z' -or
        $identity.executable -cne 'C:\mock\llama-server.exe') {
        throw 'DateTime CIM creation time did not produce stable process identity.'
    }
    Write-Output 'PASS mocked CIM DateTime creation identity'

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.ArgumentList.Add('C:\folder with spaces\coordinator.py')
    $startInfo.ArgumentList.Add('--model')
    $startInfo.ArgumentList.Add('C:\model folder\breeze.gguf')
    if (@($startInfo.ArgumentList)[0] -cne 'C:\folder with spaces\coordinator.py' -or
        @($startInfo.ArgumentList)[2] -cne 'C:\model folder\breeze.gguf') {
        throw 'ProcessStartInfo.ArgumentList did not preserve arguments containing spaces.'
    }
    Write-Output 'PASS ProcessStartInfo.ArgumentList preserves spaced paths without launch'

    $getQwenState = $runnerAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Get-QwenState'
    }, $true)
    $restoreQwen = $runnerAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Restore-Qwen'
    }, $true)
    if (-not $getQwenState -or (-not ((([string]$getQwenState.Extent.Text).IndexOf('param([switch]$AllowBusy)',[StringComparison]::OrdinalIgnoreCase) -ge 0))) -or
        -not $restoreQwen -or (-not $restoreQwen.Extent.Text.Contains('Get-QwenState -AllowBusy'))) {
        throw 'Restore-Qwen must accept a busy Qwen slot through Get-QwenState -AllowBusy.'
    }
    Write-Output 'PASS Restore-Qwen opts into busy-slot readiness'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
