param(
    [string]$DfuRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$PersistentDataPath,
    [string]$LocalTtsRoot,
    [string]$Endpoint = 'http://127.0.0.1:8094',
    [ValidateRange(5,600)] [int]$ReadinessTimeoutSeconds = 120,
    [string]$OwnershipReceiptPath,
    [switch]$ValidateOnly,
    [switch]$RequireServerReady
)
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')


$ErrorActionPreference = 'Stop'
$DfuRoot = [IO.Path]::GetFullPath($DfuRoot)
if (-not $PersistentDataPath) { $PersistentDataPath = Join-Path $DfuRoot 'Player/PortableAppdata' }
if (-not $LocalTtsRoot) { $LocalTtsRoot = Join-Path $DfuRoot 'local-tts' }
$PersistentDataPath = [IO.Path]::GetFullPath($PersistentDataPath)
$LocalTtsRoot = [IO.Path]::GetFullPath($LocalTtsRoot)
if ($OwnershipReceiptPath) {
    . (Join-Path $DfuRoot 'tools/LocalServiceLifecycle.ps1')
}

function Require-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing ${Description}: $Path" }
    return (Resolve-Path -LiteralPath $Path).Path
}
function Test-OptionalPinnedArtifact([string]$Path, [long]$ExpectedBytes, [string]$ExpectedSha256, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{ Available=$false; Path=$null; Reason="$Description not installed" }
    }
    try {
        $resolved = (Resolve-Path -LiteralPath $Path).Path
        $actualBytes = (Get-Item -LiteralPath $resolved).Length
        if ($actualBytes -ne $ExpectedBytes) {
            return [pscustomobject]@{ Available=$false; Path=$null; Reason="$Description length mismatch ($actualBytes bytes; expected $ExpectedBytes)" }
        }
        $actualHash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $ExpectedSha256.ToLowerInvariant()) {
            return [pscustomobject]@{ Available=$false; Path=$null; Reason="$Description SHA-256 mismatch" }
        }
        return [pscustomobject]@{ Available=$true; Path=$resolved; Reason=$null }
    } catch {
        return [pscustomobject]@{ Available=$false; Path=$null; Reason="$Description verification failed: $($_.Exception.Message)" }
    }
}
function Get-CommandLineOptionValues([string]$CommandLine, [string]$Option) {
    return Get-NativeOptionValues $CommandLine $Option
}
function Test-CommandLineFlag([string]$CommandLine, [string]$Flag, [bool]$ExpectedPresent) {
    return (Test-NativeFlag $CommandLine $Flag) -eq $ExpectedPresent
}
function Get-CommandLineOptionNames([string]$CommandLine) {
    foreach($argument in @(Get-NativeCommandArguments $CommandLine)) {
        if($argument.StartsWith('--',[StringComparison]::Ordinal)){$argument.ToLowerInvariant()}
    }
}
function Test-CoordinatorLaunchConfiguration([string]$CommandLine, $Expected) {
    $optionProperties = [ordered]@{
        'cli'='Cli'; 'model'='Model'; 'model-sha256'='ModelSha256'; 'spec'='Spec'; 'backend'='Backend'
        'root'='Root'; 'server-endpoint'='ServerEndpoint'; 'profiles'='Profiles'; 'bindings'='Bindings'
        'voice-root'='VoiceRoot'; 'aligner-cli'='AlignerCli'; 'aligner-model'='AlignerModel'
        'aligner-model-sha256'='AlignerModelSha256'; 'rhubarb-cli'='RhubarbCli'; 'ready-file'='ReadyFile'
        'ready-config-id'='ReadyConfigId'
    }
    foreach ($name in $optionProperties.Keys) {
        $expectedValue = $Expected.($optionProperties[$name])
        $actualValues = @(Get-CommandLineOptionValues $CommandLine "--$name")
        if ($null -eq $expectedValue) {
            if ($actualValues.Count -gt 0) { return $false }
        } else {
            if ($actualValues.Count -ne 1) { return $false }
            if (-not [string]::Equals([string]$actualValues[0], [string]$expectedValue, [StringComparison]::OrdinalIgnoreCase)) {
                return $false
            }
        }
    }
    $expectedNames = @('--watch')
    foreach ($name in $optionProperties.Keys) {
        if ($null -ne $Expected.($optionProperties[$name])) { $expectedNames += "--$name" }
    }
    if ($Expected.PreparePresentation) { $expectedNames += '--prepare-presentation-before-playback' }
    if ($Expected.PerNpcVoices) { $expectedNames += '--per-npc-voices' }
    $expectedNames = @($expectedNames | Sort-Object)
    $actualNames = @(Get-CommandLineOptionNames $CommandLine | Sort-Object)
    if (($actualNames -join "`n") -cne ($expectedNames -join "`n")) { return $false }
    return (Test-CommandLineFlag $CommandLine '--watch' $true) -and
        (Test-CommandLineFlag $CommandLine '--await-native-root' $false) -and
        (Test-CommandLineFlag $CommandLine '--prepare-presentation-before-playback' ([bool]$Expected.PreparePresentation)) -and
        (Test-CommandLineFlag $CommandLine '--per-npc-voices' ([bool]$Expected.PerNpcVoices)) -and
        (Test-CommandLineFlag $CommandLine '--preview-alignment-before-playback' $false) -and
        (Test-CommandLineFlag $CommandLine '--preview-visemes-before-playback' $false)
}
function Get-ReadyConfigIdentity($Settings, [string]$CoordinatorScriptPath) {
    $sourceRoot = Split-Path -Parent $CoordinatorScriptPath
    $runtimeSources = @()
    foreach ($source in @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.py' -File |
            Where-Object { (-not (([string]$_.Name).StartsWith('test_',[StringComparison]::OrdinalIgnoreCase) -and ([string]$_.Name).EndsWith('.py',[StringComparison]::OrdinalIgnoreCase))) } | Sort-Object Name)) {
        $runtimeSources += [ordered]@{
            name = $source.Name
            sha256 = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    if ($runtimeSources.Count -eq 0) { throw "No coordinator runtime Python sources found under $sourceRoot" }
    $canonical = [ordered]@{
        schemaVersion = 2
        runtimeSources = $runtimeSources
        cli = $Settings.Cli
        model = $Settings.Model
        modelSha256 = ([string]$Settings.ModelSha256).ToLowerInvariant()
        spec = $Settings.Spec
        backend = $Settings.Backend
        preparePresentation = [bool]$Settings.PreparePresentation
        perNpcVoices = [bool]$Settings.PerNpcVoices
        root = $Settings.Root
        serverEndpoint = $Settings.ServerEndpoint
        profiles = $Settings.Profiles
        bindings = $Settings.Bindings
        voiceRoot = $Settings.VoiceRoot
        alignerCli = $Settings.AlignerCli
        alignerModel = $Settings.AlignerModel
        alignerModelSha256 = if ($Settings.AlignerModelSha256) { ([string]$Settings.AlignerModelSha256).ToLowerInvariant() } else { $null }
        rhubarbCli = $Settings.RhubarbCli
        rhubarbSha256 = $Settings.RhubarbSha256
    }
    $json = ConvertTo-Json -InputObject $canonical -Compress -Depth 5
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Test-CoordinatorReadyReceipt($Receipt, $Expected) {
    if ($null -eq $Receipt) { return $false }
    $requiredNames = @('schemaVersion', 'status', 'processId', 'root', 'modelSha256', 'serverEndpoint', 'configIdentity') | Sort-Object
    $actualNames = @($Receipt.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
    if (($actualNames -join "`n") -cne ($requiredNames -join "`n")) { return $false }
    if ($Receipt.schemaVersion -ne 1 -or $Receipt.status -cne 'ready') { return $false }
    $receiptProcessId = 0
    if (-not [int]::TryParse([string]$Receipt.processId, [ref]$receiptProcessId) -or $receiptProcessId -lt 1 -or
        $receiptProcessId -ne [int]$Expected.ProcessId) { return $false }
    try { $actualRoot = [IO.Path]::GetFullPath([string]$Receipt.root) }
    catch { return $false }
    if (-not [string]::Equals($actualRoot, [IO.Path]::GetFullPath([string]$Expected.Root), [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if (-not [string]::Equals([string]$Receipt.modelSha256, [string]$Expected.ModelSha256, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($null -eq $Expected.ServerEndpoint) {
        if ($null -ne $Receipt.serverEndpoint) { return $false }
    } elseif (-not [string]::Equals([string]$Receipt.serverEndpoint, [string]$Expected.ServerEndpoint, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    if ($null -eq $Expected.ReadyConfigId) { return $null -eq $Receipt.configIdentity }
    return [string]::Equals([string]$Receipt.configIdentity, [string]$Expected.ReadyConfigId, [StringComparison]::OrdinalIgnoreCase)
}
function Read-CoordinatorReadyReceipt([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $item = Get-Item -LiteralPath $Path
        if ($item.Length -lt 2 -or $item.Length -gt 16384) { return $null }
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch { return $null }
}
function Get-PostprocessCoordinatorArguments($Settings) {
    if (-not $Settings.RhubarbCli) {
        throw 'Production speech requires pinned Rhubarb viseme preparation.'
    }
    $hasAnyAligner = $Settings.AlignerCli -or $Settings.AlignerModel -or $Settings.AlignerModelSha256
    $hasFullAligner = $Settings.AlignerCli -and $Settings.AlignerModel -and $Settings.AlignerModelSha256
    if ($hasAnyAligner -and -not $hasFullAligner) {
        throw 'Optional CPU word alignment requires CLI, model, and model SHA-256 together.'
    }
    $arguments = @()
    if ($hasFullAligner) {
        $arguments += @('--aligner-cli', (Quote-Arg $Settings.AlignerCli),
            '--aligner-model', (Quote-Arg $Settings.AlignerModel),
            '--aligner-model-sha256', $Settings.AlignerModelSha256)
    }
    $arguments += @('--rhubarb-cli', (Quote-Arg $Settings.RhubarbCli),
        '--prepare-presentation-before-playback')
    return $arguments
}
function Assert-PreparedPresentationHelpers($AlignmentStatus, $RhubarbStatus) {
    if (-not $RhubarbStatus.Available) {
        throw ('Production Breeze speech cannot start; Rhubarb viseme preparation is unavailable: ' + $RhubarbStatus.Reason)
    }
}
function Assert-Sha256([string]$Path, [string]$Expected, [string]$Description) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected.ToLowerInvariant()) { throw "$Description SHA-256 mismatch at $Path" }
}
function Quote-Arg([string]$Value) { return '"' + $Value.Replace('"','\"') + '"' }
function Test-ReadyServer {
    param([string]$ServerExe, [string]$ConfigPath, [string]$ModelPath)
    $uri = [uri]$Endpoint
    if ($uri.Scheme -ne 'http' -or $uri.Host -ne '127.0.0.1' -or $uri.Port -ne 8094 -or $uri.AbsolutePath -ne '/') {
        throw 'TTS endpoint must be exactly http://127.0.0.1:8094.'
    }
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 8094 -ErrorAction SilentlyContinue)
    if ($listeners.Count -eq 0) { return $null }
    $ownerIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($ownerIds.Count -ne 1) { throw 'TTS port 8094 has multiple listener owners; refusing reuse.' }
    $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($ownerIds[0])" -ErrorAction SilentlyContinue
    if (-not $owner -or -not [string]::Equals([string]$owner.ExecutablePath,$ServerExe,[StringComparison]::OrdinalIgnoreCase) -or
        $owner.CommandLine.IndexOf($ConfigPath,[StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "TTS port 8094 is occupied by an unrelated or differently configured process (PID $($ownerIds[0])); refusing to stop or reuse it."
    }
    foreach ($listener in $listeners) {
        if (-not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse([string]$listener.LocalAddress))) {
            throw 'Existing TTS server listens beyond loopback; refusing reuse.'
        }
    }
    try {
        $health = Invoke-RestMethod -Uri "$Endpoint/health" -TimeoutSec 3
        $models = Invoke-RestMethod -Uri "$Endpoint/v1/models" -TimeoutSec 5
    } catch { return [pscustomobject]@{ ProcessId=[int]$owner.ProcessId; Ready=$false } }
    if ($health.status -ne 'ok') { return [pscustomobject]@{ ProcessId=[int]$owner.ProcessId; Ready=$false } }
    $ids = @($models.data | ForEach-Object { [string]$_.id })
    if ($ids -notcontains 'breeze') { return [pscustomobject]@{ ProcessId=[int]$owner.ProcessId; Ready=$false } }
    return [pscustomobject]@{ ProcessId=[int]$owner.ProcessId; Ready=$true }
}

$manifestPath = Require-File (Join-Path $LocalTtsRoot 'download-manifest.json') 'pinned local TTS download manifest'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.audioCppRelease -ne 'v0.9.2-drama3-base-small') { throw 'Unsupported local TTS download manifest.' }
$modelRecord = @($manifest.artifacts | Where-Object { $_.file -eq 'models/Breeze-TTS-2-GGUF/breeze-tts-2-q4_0.gguf' })
if ($modelRecord.Count -ne 1) { throw 'Pinned Q4 Breeze model is missing from the download manifest.' }
$model = Require-File (Join-Path $LocalTtsRoot $modelRecord[0].file) 'pinned Breeze GGUF model'
Assert-Sha256 $model $modelRecord[0].sha256 'Breeze model'
$runtimeMatches = @(Get-ChildItem -LiteralPath (Join-Path $LocalTtsRoot 'runtime') -Directory -Filter 'audio-v0.9.2-drama3-base-small-cuda12.4' -ErrorAction SilentlyContinue)
if ($runtimeMatches.Count -ne 1) { throw 'Pinned CUDA 12.4 audio.cpp runtime is missing or ambiguous.' }
$runtime = $runtimeMatches[0].FullName
$server = Require-File (Join-Path $runtime 'audiocpp_server.exe') 'pinned audio.cpp server'
$cli = Require-File (Join-Path $runtime 'audiocpp_cli.exe') 'pinned audio.cpp CLI'
$spec = Require-File (Join-Path $runtime 'model_specs/breeze_tts.json') 'pinned Breeze model spec'
Assert-Sha256 $cli 'ee1ae0b3dce0918f33dd9446e55c21f50120bcf4501a98a9a9a1ef4b7eaa91a9' 'audio.cpp CLI'
Assert-Sha256 $spec 'f6e2587131fd2ca3ddc3e60b4450a58c685fcc7c1f6898748a5eab23003ca37e' 'Breeze model spec'
$coordinator = Require-File (Join-Path $PSScriptRoot 'BreezeTtsTrial/coordinator.py') 'Breeze coordinator'
# Retain the explicit path for legacy developer probes. Normal per-NPC mode does
# not consume a shared registry and must also start on a fresh voice store.
$profiles = Join-Path $LocalTtsRoot 'profiles.json'
$bindings = Join-Path $LocalTtsRoot 'voice-bindings.json'
$voiceRoot = Join-Path $LocalTtsRoot 'voices'
$bridgeConfig = Join-Path $PersistentDataPath 'PeopleOfTheIliacBay/breeze-tts.json'
$bridgeRoot = Join-Path $PersistentDataPath 'PeopleOfTheIliacBay/BreezeTtsBridge'
$runRoot = Join-Path $PersistentDataPath 'PeopleOfTheIliacBay/local-tts/coordinator'
$generatedConfig = Join-Path $PersistentDataPath 'PeopleOfTheIliacBay/local-tts/server_breeze_streaming.json'
$pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
if (-not $pythonCommand) { $pythonCommand = Get-Command python -ErrorAction SilentlyContinue }
if (-not $pythonCommand) { throw 'Python is required to run the already-installed Breeze coordinator; install no packages through this launcher.' }
if (Test-Path -LiteralPath $bridgeConfig -PathType Leaf) {
    $cfg = Get-Content -LiteralPath $bridgeConfig -Raw | ConvertFrom-Json
    if ($cfg.schemaVersion -ne 1) { throw "Unsupported bridge config schema in $bridgeConfig" }
    if ($cfg.serverEndpoint) { $Endpoint = [string]$cfg.serverEndpoint }
    if ($cfg.bridgeRoot) { $bridgeRoot = [IO.Path]::GetFullPath([string]$cfg.bridgeRoot) }
}
$uri = [uri]$Endpoint
if ($uri.Scheme -ne 'http' -or $uri.Host -ne '127.0.0.1' -or $uri.Port -ne 8094 -or $uri.AbsolutePath -ne '/' -or
    -not [string]::IsNullOrEmpty($uri.UserInfo) -or -not [string]::IsNullOrEmpty($uri.Query) -or -not [string]::IsNullOrEmpty($uri.Fragment)) {
    throw 'TTS endpoint must be exactly http://127.0.0.1:8094.'
}
$Endpoint = $uri.GetLeftPart([UriPartial]::Authority)
$postprocessWorker = Join-Path $PSScriptRoot 'BreezeTtsTrial/postprocess_worker.py'
$alignerScript = Join-Path $PSScriptRoot 'BreezeTtsTrial/align_words.py'
$rhubarbScript = Join-Path $PSScriptRoot 'BreezeTtsTrial/rhubarb_visemes.py'
$alignerModelCandidate = Join-Path $LocalTtsRoot 'models/Qwen3-ForcedAligner-0.6B-GGUF/qwen3-forced-aligner-0.6b-q8_0.gguf'
$alignerModelSha256 = '75209490b11cec2b0db749ca5f4ff92266f58efd30f7fd04d9eb2a3ac9cc929f'
$alignerModelStatus = Test-OptionalPinnedArtifact -Path $alignerModelCandidate -ExpectedBytes 1129966496L `
    -ExpectedSha256 $alignerModelSha256 -Description 'Pinned Qwen3 forced-aligner model'
$alignerCliStatus = if ($alignerModelStatus.Available -and (Test-Path -LiteralPath $alignerScript -PathType Leaf) -and
    (Test-Path -LiteralPath $postprocessWorker -PathType Leaf)) {
    [pscustomobject]@{ Available=$true; Path=$cli; Reason=$null }
} elseif (-not $alignerModelStatus.Available) {
    $alignerModelStatus
} else {
    [pscustomobject]@{ Available=$false; Path=$null; Reason='alignment helper scripts not installed' }
}
# The upstream v1.14.0 release has no published digest. This SHA-256 pins the locally observed exe from that release package.
$rhubarbCandidate = Join-Path $LocalTtsRoot 'runtime/rhubarb-1.14.0/Rhubarb-Lip-Sync-1.14.0-Windows/rhubarb.exe'
$rhubarbSha256Pin = '9e289c6b5939ef6b306a61e8105ec721fd8d52b3ce950d08891ea3cc7df5718d'
$rhubarbStatus = Test-OptionalPinnedArtifact -Path $rhubarbCandidate -ExpectedBytes 2703360L `
    -ExpectedSha256 $rhubarbSha256Pin `
    -Description 'Pinned Rhubarb Lip Sync 1.14.0 executable'
if ($rhubarbStatus.Available -and (-not (Test-Path -LiteralPath $rhubarbScript -PathType Leaf) -or
    -not (Test-Path -LiteralPath $postprocessWorker -PathType Leaf))) {
    $rhubarbStatus = [pscustomobject]@{ Available=$false; Path=$null; Reason='viseme helper scripts not installed' }
}
Assert-PreparedPresentationHelpers -AlignmentStatus $alignerCliStatus -RhubarbStatus $rhubarbStatus
$postprocessExpected = [pscustomobject]@{
    AlignerCli = if ($alignerCliStatus.Available) { $cli } else { $null }
    AlignerModel = if ($alignerCliStatus.Available) { $alignerModelStatus.Path } else { $null }
    AlignerModelSha256 = if ($alignerCliStatus.Available) { $alignerModelSha256 } else { $null }
    RhubarbCli = if ($rhubarbStatus.Available) { $rhubarbStatus.Path } else { $null }
}
$readyFile = Join-Path $runRoot 'coordinator-ready.json'
$coordinatorExpected = [pscustomobject]@{
    ProcessId = $null
    Cli = $cli
    Model = $model
    ModelSha256 = ([string]$modelRecord[0].sha256).ToLowerInvariant()
    Spec = $spec
    Backend = 'cuda'
    Root = [IO.Path]::GetFullPath($bridgeRoot)
    ServerEndpoint = $Endpoint
    Profiles = $profiles
    Bindings = [IO.Path]::GetFullPath($bindings)
    VoiceRoot = [IO.Path]::GetFullPath($voiceRoot)
    AlignerCli = $postprocessExpected.AlignerCli
    AlignerModel = $postprocessExpected.AlignerModel
    AlignerModelSha256 = if ($postprocessExpected.AlignerCli) { $alignerModelSha256 } else { $null }
    RhubarbCli = $postprocessExpected.RhubarbCli
    RhubarbSha256 = if ($postprocessExpected.RhubarbCli) { $rhubarbSha256Pin } else { $null }
    PreparePresentation = $true
    PerNpcVoices = $true
    ReadyFile = [IO.Path]::GetFullPath($readyFile)
    ReadyConfigId = $null
}
$coordinatorExpected.ReadyConfigId = Get-ReadyConfigIdentity $coordinatorExpected $coordinator
$postprocessArgs = @(Get-PostprocessCoordinatorArguments $postprocessExpected)
Write-Output 'Prepared presentation enabled: pinned Rhubarb cues and CPU word alignment finish before playback.'
$serverConfig = @{ host='127.0.0.1'; port=8094; backend='cuda'; device=0; lazy_load=$false; max_loaded_models=1; idle_unload_ms=0; ui=$false; ui_management=$false; models=@(@{id='breeze';family='breeze_tts';path=$model;task='tts';mode='streaming'}) } | ConvertTo-Json -Depth 6
$coordinatorBaseArgs = @('-u', (Quote-Arg $coordinator), '--cli', (Quote-Arg $cli), '--model', (Quote-Arg $model), '--model-sha256', $coordinatorExpected.ModelSha256,
    '--spec', (Quote-Arg $spec), '--profiles', (Quote-Arg $profiles), '--bindings', (Quote-Arg $bindings), '--voice-root', (Quote-Arg $voiceRoot),
    '--root', (Quote-Arg $bridgeRoot), '--backend', 'cuda', '--server-endpoint', $Endpoint, '--watch',
    '--per-npc-voices',
    '--ready-file', (Quote-Arg $readyFile), '--ready-config-id', $coordinatorExpected.ReadyConfigId)
$coordinatorArgs = (@($coordinatorBaseArgs) + @($postprocessArgs)) -join ' '
$pythonProcesses = @(Get-CimInstance Win32_Process -Filter "Name='python.exe' OR Name='python3.exe' OR Name='pythonw.exe'" -ErrorAction SilentlyContinue)
$rootTargetMatches = @($pythonProcesses | Where-Object {
    $_.CommandLine -and $_.CommandLine.IndexOf($bridgeRoot,[StringComparison]::OrdinalIgnoreCase) -ge 0
})
$coordinatorMatches = @($pythonProcesses | Where-Object {
    $_.CommandLine -and $_.CommandLine.IndexOf($coordinator,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and
    $_.CommandLine.IndexOf($bridgeRoot,[StringComparison]::OrdinalIgnoreCase) -ge 0
})
if ($rootTargetMatches.Count -ne $coordinatorMatches.Count) {
    throw 'A Python process targets this bridge root without the pinned coordinator script; refusing duplicate startup or reuse.'
}
if ($coordinatorMatches.Count -gt 1) { throw 'Multiple Breeze coordinator processes already target this bridge root; refusing to start another.' }
$existingCoordinator = $null
$coordinatorState = 'not-running'
if ($coordinatorMatches.Count -eq 1) {
    $existingCoordinator = $coordinatorMatches[0]
    if (-not [string]::Equals([string]$existingCoordinator.ExecutablePath, [string]$pythonCommand.Source, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-CoordinatorLaunchConfiguration -CommandLine $existingCoordinator.CommandLine -Expected $coordinatorExpected)) {
        throw "Breeze coordinator PID $($existingCoordinator.ProcessId) uses outdated or incompatible coordinator code or launch settings; it was left running. Restart it through its owner to load current code and settings."
    }
    $coordinatorExpected.ProcessId = [int]$existingCoordinator.ProcessId
    $existingReceipt = Read-CoordinatorReadyReceipt $readyFile
    if (-not (Test-CoordinatorReadyReceipt -Receipt $existingReceipt -Expected $coordinatorExpected)) {
        throw "Breeze coordinator PID $($existingCoordinator.ProcessId) has no matching ready receipt at $readyFile; it was left running. Restart it through its owner."
    }
    $coordinatorState = "ready PID $($existingCoordinator.ProcessId)"
}
if ($ValidateOnly) {
    $serverValidation = Test-ReadyServer -ServerExe $server -ConfigPath $generatedConfig -ModelPath $model
    if ($RequireServerReady -and ((-not $serverValidation) -or (-not $serverValidation.Ready) -or ($coordinatorState -eq 'not-running'))) {
        throw 'ValidateOnly: compatible warm Breeze server and matching ready coordinator are required; no process was started.'
    }
    $serverState = if (-not $serverValidation) { 'not-running' } elseif ($serverValidation.Ready) { "ready PID $($serverValidation.ProcessId)" } else { "starting PID $($serverValidation.ProcessId)" }
    Write-Output "LOCAL_SPEECH_VALIDATE_ONLY_PASS: pinned assets and per-NPC launch configuration verified; server=$serverState; coordinator=$coordinatorState; no process started."
    return
}

New-Item -ItemType Directory -Path (Split-Path $generatedConfig -Parent), $bridgeRoot, $runRoot, $voiceRoot, (Split-Path $bindings -Parent) -Force | Out-Null
if (Test-Path -LiteralPath $generatedConfig -PathType Leaf) {
    $savedServerConfig = Get-Content -LiteralPath $generatedConfig -Raw | ConvertFrom-Json
    if ($savedServerConfig.host -ne '127.0.0.1' -or [int]$savedServerConfig.port -ne 8094 -or $savedServerConfig.backend -ne 'cuda' -or
        @($savedServerConfig.models).Count -ne 1 -or $savedServerConfig.models[0].id -ne 'breeze' -or
        -not [string]::Equals([string]$savedServerConfig.models[0].path,$model,[StringComparison]::OrdinalIgnoreCase) -or
        $savedServerConfig.models[0].mode -ne 'streaming') {
        throw "Existing local TTS server config is incompatible and was left unchanged: $generatedConfig"
    }
} else {
    $existingListeners = @(Get-NetTCPConnection -State Listen -LocalPort 8094 -ErrorAction SilentlyContinue)
    if ($existingListeners.Count -gt 0) { throw "Port 8094 is occupied but the launcher has no saved config to prove ownership; refusing to replace it." }
    [IO.File]::WriteAllText($generatedConfig,$serverConfig,[Text.UTF8Encoding]::new($false))
}
$serverStatus = Test-ReadyServer -ServerExe $server -ConfigPath $generatedConfig -ModelPath $model
if (-not $serverStatus) {
    $serverLog = Join-Path (Split-Path $generatedConfig -Parent) 'audio-server.log'
    $serverArgs = @('--config', (Quote-Arg $generatedConfig), '--log', '--log-file', (Quote-Arg $serverLog)) -join ' '
    $startedServer = Start-Process -FilePath $server -WorkingDirectory $runtime -ArgumentList $serverArgs -WindowStyle Hidden -PassThru
    if ($OwnershipReceiptPath) {
        $serverConfigIdentity = Get-LocalServiceConfigIdentity ([ordered]@{
            schemaVersion = 1
            service = 'local-breeze-server'
            executable = $server
            configPath = $generatedConfig
            configSha256 = (Get-FileHash -LiteralPath $generatedConfig -Algorithm SHA256).Hash.ToLowerInvariant()
            model = $model
            endpoint = $Endpoint
        })
        $ownedRecord = New-LocalServiceOwnershipRecord -ProcessId $startedServer.Id -ExecutablePath $server -ConfigIdentity $serverConfigIdentity
        Write-LocalServiceOwnershipReceipt -Path $OwnershipReceiptPath -Record $ownedRecord
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $serverStatus = Test-ReadyServer -ServerExe $server -ConfigPath $generatedConfig -ModelPath $model
        if ($serverStatus -and $serverStatus.Ready) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Started Breeze PID $($startedServer.Id), but compatible server readiness timed out." }
    } while ($true)
} elseif (-not $serverStatus.Ready) {
    $deadline = [DateTime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $serverStatus = Test-ReadyServer -ServerExe $server -ConfigPath $generatedConfig -ModelPath $model
        if ($serverStatus -and $serverStatus.Ready) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Existing Breeze PID $($serverStatus.ProcessId) did not become ready; it was left running." }
    } while ($true)
}
$existing = $existingCoordinator
if ($existing) {
    Write-Output "Local Breeze speech is ready: server PID $($serverStatus.ProcessId), existing coordinator PID $($existing.ProcessId), endpoint $Endpoint."
} else {
    $logStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    if (Test-Path -LiteralPath $readyFile -PathType Leaf) { Remove-Item -LiteralPath $readyFile -Force }
    $coordinatorProcess = Start-Process -FilePath $pythonCommand.Source -WorkingDirectory $PSScriptRoot -ArgumentList $coordinatorArgs -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runRoot "coordinator-$logStamp.stdout.log") -RedirectStandardError (Join-Path $runRoot "coordinator-$logStamp.stderr.log")
    if ($OwnershipReceiptPath) {
        $ownedRecord = New-LocalServiceOwnershipRecord -ProcessId $coordinatorProcess.Id -ExecutablePath $pythonCommand.Source -ConfigIdentity $coordinatorExpected.ReadyConfigId
        Write-LocalServiceOwnershipReceipt -Path $OwnershipReceiptPath -Record $ownedRecord
    }
    $coordinatorExpected.ProcessId = [int]$coordinatorProcess.Id
    $coordinatorDeadline = [DateTime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    $coordinatorReady = $false
    do {
        if ($coordinatorProcess.HasExited) { throw "Breeze coordinator exited before readiness (code $($coordinatorProcess.ExitCode)); inspect its timestamped stderr log under $runRoot." }
        $receipt = Read-CoordinatorReadyReceipt $readyFile
        if (Test-CoordinatorReadyReceipt -Receipt $receipt -Expected $coordinatorExpected) { $coordinatorReady = $true; break }
        if ([DateTime]::UtcNow -ge $coordinatorDeadline) { throw "Breeze coordinator PID $($coordinatorProcess.Id) did not publish a matching ready receipt before timeout; it was left running. Inspect its timestamped stderr log under $runRoot." }
        Start-Sleep -Milliseconds 100
    } while ($true)
    if (-not $coordinatorReady) { throw "Breeze coordinator PID $($coordinatorProcess.Id) is not ready; inspect $readyFile and its timestamped stderr log under $runRoot." }
    Write-Output "Local Breeze speech is ready: server PID $($serverStatus.ProcessId), coordinator PID $($coordinatorProcess.Id), endpoint $Endpoint."
}
