

. (Join-Path $PSScriptRoot 'TextProtocol.ps1')

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent

$dfuRoot = Split-Path $repoRoot -Parent

$externalLauncher = Join-Path $PSScriptRoot 'host/StartGame.ps1'

$workRoot = Join-Path $repoRoot 'work'

$testRoot = Join-Path $workRoot ('hybrid-game-launcher-selftest-' + [guid]::NewGuid().ToString('N'))

$resolvedWorkRoot = [IO.Path]::GetFullPath($workRoot).TrimEnd('\') + '\'

$fakeDfu = Join-Path $testRoot 'fake-dfu'

$fakeRepo = Join-Path $testRoot 'fake-repo'

$eventLog = Join-Path $testRoot 'events.jsonl'

$fakePlayer = Join-Path $fakeDfu 'Player\DaggerfallUnity.exe'

$fakeStartGame = Join-Path $fakeDfu 'tools\StartGame.ps1'

function Assert-True([bool]$condition, [string]$message) {

    if (-not $condition) { throw $message }

}

function Get-Events {

    if (-not (Test-Path -LiteralPath $eventLog)) { return @() }

    return @(Get-Content -LiteralPath $eventLog | Where-Object { $_ } | ForEach-Object { ConvertFrom-Json $_ })

}

function Reset-Case([bool]$existingGame = $false, [bool]$decisionFailure = $false, [bool]$dialogueFailure = $false) {

    Remove-Item -LiteralPath $eventLog -Force -ErrorAction SilentlyContinue

    $global:HybridLauncherExistingGame = $existingGame

    $global:HybridLauncherDecisionFailure = $decisionFailure

    $global:HybridLauncherDialogueFailure = $dialogueFailure

    $global:HybridLauncherSpeechFailure = $false

    $global:HybridLauncherDialogueOwned = $true

    $global:HybridLauncherSpeechOwned = $true

    $global:HybridLauncherProcesses = @{}

    $global:HybridLauncherStopEvents = @()

    if ($existingGame) {

        $started = [DateTime]::UtcNow.AddMinutes(-5)

        $global:HybridLauncherProcesses[4242] = [pscustomobject]@{

            ProcessId=4242; CreationDate=$started; ExecutablePath=$global:HybridLauncherPlayerPath

            CommandLine='"' + $global:HybridLauncherPlayerPath + '"'

        }

    }

}

function global:Add-HybridLauncherEvent([hashtable]$event) {

    $event | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:DFU_HYBRID_LAUNCHER_EVENT_LOG -Encoding Ascii

}

function global:Get-CimInstance {

    param([string]$ClassName, [string]$Filter, [string]$ErrorAction)

    if ($Filter -like "*Name='DaggerfallUnity.exe'*") {

        return @($global:HybridLauncherProcesses.Values | Where-Object { [string]::Equals([string]$_.ExecutablePath, $global:HybridLauncherPlayerPath, [StringComparison]::OrdinalIgnoreCase) })

    }

    if (($Filter.StartsWith('ProcessId=') -and (Test-DecimalValue $Filter.Substring(10)))) { return $global:HybridLauncherProcesses[[int]$Filter.Substring(10)] }

    throw "Unexpected CIM query in launcher test: $Filter"

}

function global:Start-Process {

    param(

        [string]$FilePath,

        [string]$WorkingDirectory,

        [string]$ArgumentList,

        [string]$WindowStyle,

        [switch]$PassThru

    )

    if ([IO.Path]::GetFileName($FilePath) -ieq 'powershell.exe') {

        Add-HybridLauncherEvent @{ Kind = 'watcher'; Arguments = $ArgumentList; WorkingDirectory = $WorkingDirectory; WindowStyle=$WindowStyle }

        return [pscustomobject]@{ Id=9001; HasExited=$false }

    }

    if ([IO.Path]::GetFileName($FilePath) -ine 'DaggerfallUnity.exe') {

        throw "Unexpected process launch in launcher test: $FilePath"

    }

    Add-HybridLauncherEvent @{ Kind = 'game'; Arguments = $ArgumentList; WorkingDirectory = $WorkingDirectory }

    $started = [DateTime]::UtcNow

    $commandLine = '"' + $FilePath + '" ' + [string]$ArgumentList

    $global:HybridLauncherProcesses[777] = [pscustomobject]@{ ProcessId=777; CreationDate=$started; ExecutablePath=$FilePath; CommandLine=$commandLine }

    return [pscustomobject]@{ Id = 777; HasExited=$false }

}

function global:Get-Process {

    param([int]$Id, [string]$ErrorAction)

    $process = $global:HybridLauncherProcesses[$Id]

    if (-not $process) { return $null }

    return [pscustomobject]@{ Id=$Id; Path=$process.ExecutablePath; StartTime=$process.CreationDate }

}

function global:Stop-Process {

    param([object]$InputObject, [switch]$Force, [string]$ErrorAction)

    $global:HybridLauncherStopEvents += $InputObject.Id

    Add-HybridLauncherEvent @{ Kind='stop'; ProcessId=$InputObject.Id }

    $global:HybridLauncherProcesses.Remove([int]$InputObject.Id)

}

try {

    if (-not (Test-Path -LiteralPath $externalLauncher -PathType Leaf)) { throw "Missing external launcher: $externalLauncher" }

    New-Item -ItemType Directory -Path (Join-Path $fakeDfu 'Player/PortableAppdata/Mods/GameData'), (Join-Path $fakeDfu 'tools'), (Join-Path $fakeRepo 'tools'), (Join-Path $fakeRepo 'mod') -Force | Out-Null

    Set-Content -LiteralPath $fakePlayer -Value '' -Encoding Ascii

    Copy-Item -LiteralPath $externalLauncher -Destination $fakeStartGame

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'host/LocalServiceLifecycle.ps1') -Destination (Join-Path $fakeDfu 'tools/LocalServiceLifecycle.ps1')

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'host/WatchGameOwnedServices.ps1') -Destination (Join-Path $fakeDfu 'tools/WatchGameOwnedServices.ps1')

    $global:HybridLauncherPlayerPath = $fakePlayer

    $env:DFU_HYBRID_LAUNCHER_EVENT_LOG = $eventLog

    $settingsDefinition = Get-Content -LiteralPath (Join-Path $repoRoot 'mod/modsettings.json') -Raw | ConvertFrom-Json

    $dialogueDefinition = @($settingsDefinition.Sections | Where-Object { $_.Name -eq 'Dialogue' })[0]

    $plannerDefinition = @($dialogueDefinition.Keys | Where-Object { $_.Name -eq 'EnableDecisionPlanner' })
    Assert-True ($plannerDefinition.Count -eq 0) 'Single-LLM settings must not expose the removed second-model planner.'

    @'

param([switch]$Background, [int]$Port, [int]$ContextSize, [int]$GpuLayers, [int]$ReadinessTimeoutSeconds, [string]$OwnershipReceiptPath)

Add-HybridLauncherEvent @{ Kind = 'qwen'; Port = $Port }

if ($OwnershipReceiptPath -and $global:HybridLauncherDialogueOwned) {

    $fakeServicePid = 7111

    $exe = 'C:\fake-local-llm\llama-server.exe'

    $started = [DateTime]::UtcNow

    $global:HybridLauncherProcesses[$fakeServicePid] = [pscustomobject]@{ ProcessId=$fakeServicePid; CreationDate=$started; ExecutablePath=$exe; CommandLine='"' + $exe + '" -m "C:\fake-models\gemma.gguf" --port ' + $Port }

    $config = Get-LocalServiceConfigIdentity ([ordered]@{ service='fake-dialogue'; model='C:\fake-models\gemma.gguf'; port=$Port; context=$ContextSize })

    $owner = New-LocalServiceOwnershipRecord -ProcessId $fakeServicePid -ExecutablePath $exe -ConfigIdentity $config

    Write-LocalServiceOwnershipReceipt -Path $OwnershipReceiptPath -Record $owner

}

if ($global:HybridLauncherDialogueFailure) { throw 'synthetic Qwen readiness failure' }

'@ | Set-Content -LiteralPath (Join-Path $fakeRepo 'tools\StartLocalDialogue.ps1') -Encoding Ascii

    @'

param([string]$DfuRoot, [string]$PersistentDataPath, [string]$OwnershipReceiptPath)

Add-HybridLauncherEvent @{ Kind = 'speech'; DfuRoot = $DfuRoot; PersistentDataPath = $PersistentDataPath }

if ($OwnershipReceiptPath -and $global:HybridLauncherSpeechOwned) {

    foreach ($service in @(

        @{ Pid=7121; Name='audiocpp_server.exe'; Config='b' * 64; Args='--config "C:\fake-tts\server.json"' },

        @{ Pid=7122; Name='python.exe'; Config='c' * 64; Args='"C:\fake-speech\coordinator.py" --ready-config-id ' + ('c' * 64) }

    )) {

        $exe = 'C:\fake-tts\' + $service.Name

        $started = [DateTime]::UtcNow

        $global:HybridLauncherProcesses[$service.Pid] = [pscustomobject]@{ ProcessId=$service.Pid; CreationDate=$started; ExecutablePath=$exe; CommandLine='"' + $exe + '" ' + $service.Args }

        $owner = New-LocalServiceOwnershipRecord -ProcessId $service.Pid -ExecutablePath $exe -ConfigIdentity $service.Config

        Write-LocalServiceOwnershipReceipt -Path $OwnershipReceiptPath -Record $owner

    }

}

if ($global:HybridLauncherSpeechFailure) { throw 'synthetic Breeze readiness failure' }

'@ | Set-Content -LiteralPath (Join-Path $fakeRepo 'tools\StartLocalSpeech.ps1') -Encoding Ascii

    $modGuid = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'

    @{ GUID = $modGuid } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fakeRepo 'mod/PeopleOfTheIliacBay.dfmod.json') -Encoding Ascii

    @(@{ FileName = 'peopleoftheiliacbay'; Enabled = $true }) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fakeDfu 'Player/PortableAppdata/Mods/GameData/Mods.json') -Encoding Ascii

    New-Item -ItemType Directory -Path (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid") -Force | Out-Null
    $fixtureSettings = Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json"
    @{ Values = @{ Dialogue = @{ EnableFreeText=$true; EnableNpcVoice=$false; EnableDecisionPlanner=$true } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $fixtureSettings -Encoding Ascii
    Reset-Case

    $warnings = @()

    & $fakeStartGame -RepositoryRoot $fakeRepo -ReadinessTimeoutSeconds 3 -WarningVariable warnings

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game,watcher') 'Normal launch must start one LLM, then game and owned-service cleanup despite the obsolete enabled setting.'

    Assert-True (-not $events[1].Arguments) 'Old enabled planner settings must not add second-model routing flags.'

    Assert-True ($events[2].Arguments -like '*WatchGameOwnedServices.ps1*' -and $events[2].WindowStyle -eq 'Hidden') 'Lifecycle watcher must run hidden with its manifest.'

    $manifestPaths = @(Get-NativeOptionValues ([string]$events[2].Arguments) '-ManifestPath')

    Assert-True ($manifestPaths.Count -eq 1 -and (Test-Path -LiteralPath $manifestPaths[0] -PathType Leaf)) 'Successful launch must preserve the watcher manifest after the launcher exits.'

    $manifest = Get-Content -LiteralPath $manifestPaths[0] -Raw | ConvertFrom-Json

    Assert-True (@($manifest.gameProcesses).Count -eq 1 -and @($manifest.ownedServices).Count -eq 1) 'Watcher manifest must contain exact game and newly owned service identities.'

    Write-Output 'PASS single LLM starts with no second-model routing despite obsolete enabled setting.'

    Reset-Case

    & $fakeStartGame -RepositoryRoot $fakeRepo -DevelopmentMode

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game') 'DevelopmentMode must leave launched model services warm without starting a watcher.'

    Assert-True ($global:HybridLauncherStopEvents.Count -eq 0) 'DevelopmentMode must not stop model services.'

    Write-Output 'PASS DevelopmentMode keeps model services warm and skips lifecycle supervision.'

    Reset-Case

    & $fakeStartGame -RepositoryRoot $fakeRepo -DialogueOnly

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game,watcher') 'DialogueOnly supervises the one dialogue service.'

    Assert-True (-not $events[1].Arguments) 'DialogueOnly game launch must have no decision flags.'

    Write-Output 'PASS DialogueOnly starts the one dialogue service and game.'

    Remove-Item -LiteralPath $fixtureSettings
    # Missing EnableFreeText must retain the legacy default of true while the mod is enabled.

    # The absent modsettings file also proves the fallback is not silently changed to AI-off.

    Reset-Case

    $warnings = @()

    & $fakeStartGame -RepositoryRoot $fakeRepo -ReadinessTimeoutSeconds 3 -WarningVariable warnings

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game,watcher') 'Missing EnableFreeText key must preserve its mod default true.'

    Write-Output 'PASS missing EnableFreeText key preserves the enabled mod default.'

    New-Item -ItemType Directory -Path (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid") -Force | Out-Null

    # Missing planner setting preserves the optional planner default of false.

    Reset-Case

    & $fakeStartGame -RepositoryRoot $fakeRepo

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game,watcher') 'Missing EnableDecisionPlanner key must preserve its default false.'

    Write-Output 'PASS missing EnableDecisionPlanner preserves the optional planner default.'

    @{ Values = @{ Dialogue = @{ EnableFreeText = $true; EnableNpcVoice = $false; EnableDecisionPlanner = $false } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case

    & $fakeStartGame -RepositoryRoot $fakeRepo

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game,watcher') 'Normal LLM launch supervises the one dialogue service.'

    Assert-True (-not $events[1].Arguments) 'LLM-on planner-off game launch must not receive D3 flags.'

    Write-Output 'PASS LLM-on planner-off starts Qwen and game without D3.'

    @{ Values = @{ Dialogue = @{ EnableFreeText = $false; EnableNpcVoice = 'True'; EnableDecisionPlanner = $true } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case -dialogueFailure $true

    & $fakeStartGame -RepositoryRoot $fakeRepo

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'speech,game,watcher') 'AI-off voice launch must skip Qwen and D3 while starting and supervising optional speech.'

    Assert-True ($events[0].PersistentDataPath -like '*PortableAppdata') 'Speech starter must receive the portable persistent-data root.'

    Write-Output 'PASS AI-off voice launches speech and game without Qwen or D3.'

    @{ Values = @{ Dialogue = @{ EnableFreeText = $true; EnableNpcVoice = $true; EnableDecisionPlanner = $false } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case

    $global:HybridLauncherSpeechFailure = $true

    try { & $fakeStartGame -RepositoryRoot $fakeRepo; throw 'Expected synthetic speech startup failure.' }

    catch { if (-not $_.Exception.Message.Contains('Optional NPC speech service')) { throw } }

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,speech,stop,stop,stop') 'Normal startup failure must clean only newly owned services.'

    Assert-True (($global:HybridLauncherStopEvents -join ',') -eq '7122,7121,7111') 'Startup cleanup must stop the coordinator, Breeze server, then Gemma server.'

    Write-Output 'PASS normal startup failure cleans newly owned Gemma/Breeze children in reverse launch order.'

    Reset-Case

    $global:HybridLauncherSpeechFailure = $true

    try { & $fakeStartGame -RepositoryRoot $fakeRepo -DevelopmentMode; throw 'Expected synthetic development speech startup failure.' }

    catch { if (-not $_.Exception.Message.Contains('Optional NPC speech service')) { throw } }

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,speech') 'DevelopmentMode startup failure must preserve newly started services.'

    Assert-True ($global:HybridLauncherStopEvents.Count -eq 0) 'DevelopmentMode must not clean services after startup failure.'

    Write-Output 'PASS DevelopmentMode preserves services when game startup fails.'

    @{ Values = @{ Dialogue = @{ EnableFreeText = $false; EnableNpcVoice = $false } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case -dialogueFailure $true

    & $fakeStartGame -RepositoryRoot $fakeRepo -ReadinessTimeoutSeconds 3

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'game') 'AI-off voice-off launch must not require Qwen, D3, or speech.'

    Write-Output 'PASS AI-off voice-off starts the base game with no local AI services.'

    @{ Values = @{ Dialogue = @{ EnableFreeText = $true; EnableNpcVoice = $true } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case -existingGame $true

    & $fakeStartGame -RepositoryRoot $fakeRepo

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'speech,watcher') 'Existing game may start newly enabled speech and supervise it, but must not restart Qwen, start D3, or launch a duplicate game.'

    Write-Output 'PASS existing game starts speech only and owns its AI services.'

    @{ Values = @{ Dialogue = @{ EnableFreeText = $false; EnableNpcVoice = 'False' } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    @{ Values = @{ Dialogue = @{ EnableFreeText = $true; EnableNpcVoice = 'False'; EnableDecisionPlanner = $true } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case -decisionFailure $true

    $warningOutput = @(& $fakeStartGame -RepositoryRoot $fakeRepo -ReadinessTimeoutSeconds 3 3>&1)

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game,watcher') 'Obsolete enabled planner setting must not require a second launcher file.'

    Assert-True (-not $events[1].Arguments) 'Obsolete planner setting must not add decision flags.'

    Assert-True (@($warningOutput | Where-Object { $_ -is [System.Management.Automation.WarningRecord] }).Count -eq 0) 'Retired planner service must not be contacted or emit a fallback warning.'

    Write-Output 'PASS retired planner cannot start a second service or trigger fallback warnings.'

    @{ Values = @{ Dialogue = @{ EnableFreeText = $true; EnableNpcVoice = $false; EnableDecisionPlanner = $false } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case

    $global:HybridLauncherDialogueOwned = $false

    & $fakeStartGame -RepositoryRoot $fakeRepo

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'qwen,game') 'Reused shared Gemma service must not gain a lifecycle receipt or watcher.'

    Assert-True ($global:HybridLauncherStopEvents.Count -eq 0) 'Reused shared Gemma service must not be stopped.'

    Write-Output 'PASS reused shared Gemma service is not claimed or stopped by the game launcher.'

    Reset-Case -existingGame $true

    & $fakeStartGame -RepositoryRoot $fakeRepo

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq '') 'Existing game with voice off must not start local services or a duplicate game.'

    Write-Output 'PASS existing game prevents service startup and duplicate game launch.'

    @(@{ FileName = 'peopleoftheiliacbay'; Enabled = $false }) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fakeDfu 'Player/PortableAppdata/Mods/GameData/Mods.json') -Encoding Ascii

    @{ Values = @{ Dialogue = @{ EnableFreeText = $true; EnableNpcVoice = $true } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fakeDfu "Player/PortableAppdata/Mods/GameData/$modGuid/modsettings.json") -Encoding Ascii

    Reset-Case -dialogueFailure $true

    & $fakeStartGame -RepositoryRoot $fakeRepo

    $events = Get-Events

    Assert-True (($events.Kind -join ',') -eq 'game') 'Disabled mod must not require or start any People of the Iliac Bay services.'

    Write-Output 'PASS disabled mod leaves base game launch independent of local AI and speech.'

}

finally {

    Remove-Item Env:\DFU_HYBRID_LAUNCHER_EVENT_LOG -ErrorAction SilentlyContinue

    Remove-Variable HybridLauncherExistingGame, HybridLauncherDecisionFailure, HybridLauncherDialogueFailure, HybridLauncherSpeechFailure, HybridLauncherDialogueOwned, HybridLauncherSpeechOwned, HybridLauncherProcesses, HybridLauncherStopEvents, HybridLauncherPlayerPath -Scope Global -ErrorAction SilentlyContinue

    Remove-Item Function:\Get-CimInstance, Function:\Start-Process, Function:\Get-Process, Function:\Stop-Process -ErrorAction SilentlyContinue

    if (Test-Path -LiteralPath $testRoot) {

        $resolvedTestRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $testRoot).Path)

        if (-not $resolvedTestRoot.StartsWith($resolvedWorkRoot, [StringComparison]::OrdinalIgnoreCase)) {

            throw "Refusing test cleanup outside repository work directory: $resolvedTestRoot"

        }

        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force

    }

}

