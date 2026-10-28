<#
.SYNOPSIS
Start Daggerfall Unity with saved local dialogue services.

.PARAMETER DevelopmentMode
Keep newly started local model services warm after exit or startup failure. Use for gallery and test sessions.
#>
param(
    [ValidateRange(1, 300)] [int]$ReadinessTimeoutSeconds = 90,
    [string]$RepositoryRoot,
    [switch]$DialogueOnly,
    [switch]$DevelopmentMode
)

$ErrorActionPreference = 'Stop'
$dfuRoot = Split-Path $PSScriptRoot -Parent
$playerRoot = Join-Path $dfuRoot 'Player'
$player = Join-Path $playerRoot 'DaggerfallUnity.exe'
if (-not $RepositoryRoot) {
    $candidates = @(Get-ChildItem -LiteralPath $dfuRoot -Directory | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'mod/PeopleOfTheIliacBay.dfmod.json')
    })
    if ($candidates.Count -ne 1) { throw 'Specify -RepositoryRoot for the People of the Iliac Bay checkout.' }
    $RepositoryRoot = $candidates[0].FullName
}
$dialogueLauncher = Join-Path $RepositoryRoot 'tools/StartLocalDialogue.ps1'
$speechLauncher = Join-Path $RepositoryRoot 'tools/StartLocalSpeech.ps1'

function Convert-SavedToggle($RawValue, [bool]$DefaultValue, [string]$SettingName) {
    if ($null -eq $RawValue) { return $DefaultValue }
    if ($RawValue -is [bool]) { return $RawValue }
    $text = [string]$RawValue
    if ([string]::Equals($text, 'true', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ([string]::Equals($text, 'false', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    throw "Saved $SettingName setting is not a boolean: '$text'."
}

function Get-SavedDialogueLaunchSettings([string]$RepoRoot, [string]$PortableDataRoot) {
    $manifestPath = Join-Path $RepoRoot 'mod/PeopleOfTheIliacBay.dfmod.json'
    $modsPath = Join-Path $PortableDataRoot 'Mods/GameData/Mods.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $modsPath -PathType Leaf)) {
        return [pscustomobject]@{ ModEnabled = $false; EnableFreeText = $false; EnableNpcVoice = $false }
    }
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $modState = @(Get-Content -LiteralPath $modsPath -Raw | ConvertFrom-Json | Where-Object { $_.FileName -eq 'peopleoftheiliacbay' } | Select-Object -First 1)
        if (-not $manifest.GUID -or $modState.Count -ne 1 -or -not $modState[0].Enabled) {
            return [pscustomobject]@{ ModEnabled = $false; EnableFreeText = $false; EnableNpcVoice = $false }
        }
        $settingsPath = Join-Path $PortableDataRoot ("Mods/GameData/{0}/modsettings.json" -f $manifest.GUID)
        $settings = $null
        if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
            $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        }
        $dialogue = if ($settings -and $settings.Values) { $settings.Values.Dialogue } else { $null }
        # Preserve mod defaults when a setting file or individual key has not been written yet.
        return [pscustomobject]@{
            ModEnabled = $true
            EnableFreeText = Convert-SavedToggle $dialogue.EnableFreeText $true 'Dialogue.EnableFreeText'
            EnableNpcVoice = Convert-SavedToggle $dialogue.EnableNpcVoice $false 'Dialogue.EnableNpcVoice'
        }
    } catch {
        throw "Could not safely read saved dialogue launch settings: $($_.Exception.Message)"
    }
}

if (-not (Test-Path -LiteralPath $player)) { throw "Missing Daggerfall Unity player: $player" }
$portableDataRoot = Join-Path $playerRoot 'PortableAppdata'
$launchSettings = Get-SavedDialogueLaunchSettings -RepoRoot $RepositoryRoot -PortableDataRoot $portableDataRoot
$freeTextEnabled = $launchSettings.ModEnabled -and $launchSettings.EnableFreeText
$npcVoiceEnabled = $launchSettings.ModEnabled -and $launchSettings.EnableNpcVoice
$lifecycleHelper = Join-Path $dfuRoot 'tools/LocalServiceLifecycle.ps1'
$lifecycleRunRoot = $null
$dialogueOwnershipReceipt = $null
$speechOwnershipReceipt = $null
$lifecycleManifestPath = $null
$lifecycleArmed = $false

if (-not $DevelopmentMode) {
    if (-not (Test-Path -LiteralPath $lifecycleHelper -PathType Leaf)) { throw "Missing local-service lifecycle helper: $lifecycleHelper" }
    . $lifecycleHelper
    $lifecycleRunRoot = Join-Path $portableDataRoot ('LocalModelLifecycle/runs/' + [guid]::NewGuid().ToString('N'))
    $dialogueOwnershipReceipt = Join-Path $lifecycleRunRoot 'dialogue-owned.json'
    $speechOwnershipReceipt = Join-Path $lifecycleRunRoot 'speech-owned.json'
    $lifecycleManifestPath = Join-Path $lifecycleRunRoot 'game-services.json'
}

function Get-ExactGameOwnershipRecords([object[]]$Processes) {
    $records = @()
    foreach ($process in @($Processes)) {
        $configIdentity = Get-LocalServiceConfigIdentity ([ordered]@{
            schemaVersion = 1
            service = 'daggerfall-unity-game'
            executablePath = [string]$process.ExecutablePath
            commandLine = [string]$process.CommandLine
        })
        $records += New-LocalServiceOwnershipRecord -ProcessId ([int]$process.ProcessId) -ExecutablePath $player -ConfigIdentity $configIdentity
    }
    return $records
}

function Get-NewlyOwnedServiceRecords {
    $records = @()
    foreach ($receiptPath in @($dialogueOwnershipReceipt, $speechOwnershipReceipt)) {
        if (-not $receiptPath -or -not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { continue }
        $loaded = @(Read-LocalServiceOwnershipReceipt -Path $receiptPath)
        if ($loaded.Count -eq 0) { throw "Local service ownership receipt is invalid or empty: $receiptPath" }
        $records += $loaded
    }
    return $records
}

function Start-GameOwnedServiceMonitor([object[]]$GameProcesses) {
    $services = @(Get-NewlyOwnedServiceRecords)
    if ($services.Count -eq 0) { return }
    $gameRecords = @(Get-ExactGameOwnershipRecords -Processes $GameProcesses)
    if ($gameRecords.Count -eq 0) { throw 'Could not capture an exact game process identity for local-service cleanup.' }
    Write-LocalServiceLifecycleManifest -Path $lifecycleManifestPath -GameProcesses $gameRecords -OwnedServices $services
    $watcherScript = Join-Path $dfuRoot 'tools/WatchGameOwnedServices.ps1'
    if (-not (Test-Path -LiteralPath $watcherScript -PathType Leaf)) { throw "Missing local-service watcher: $watcherScript" }
    $powershellExe = Join-Path $PSHOME 'powershell.exe'
    if (-not (Test-Path -LiteralPath $powershellExe -PathType Leaf)) { $powershellExe = (Get-Command powershell.exe -ErrorAction Stop).Source }
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $watcherScript + '" -ManifestPath "' + $lifecycleManifestPath + '"'
    $watcherProcess = Start-Process -FilePath $powershellExe -WorkingDirectory $dfuRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 300
    if ($watcherProcess.HasExited) { throw 'The hidden local-service lifecycle watcher exited during startup.' }
    $script:lifecycleArmed = $true
    Write-Output "Armed hidden model-service cleanup for game PID(s) $(($gameRecords | ForEach-Object ProcessId) -join ', ')."
}

function Stop-NewlyOwnedServicesAfterStartupFailure {
    $allRecords = @()
    foreach ($receiptPath in @($dialogueOwnershipReceipt, $speechOwnershipReceipt)) {
        if (-not $receiptPath -or -not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { continue }
        $records = @(Read-LocalServiceOwnershipReceipt -Path $receiptPath)
        if ($records.Count -gt 0) { $allRecords += $records }
        else { Write-Warning "Could not validate ownership receipt after startup failure; left any service process running: $receiptPath" }
    }
    if ($allRecords.Count -gt 0) { $null = Stop-LocalServiceOwnershipRecords -Records $allRecords }
}

function Start-OptionalNpcVoiceService {
    if (-not $npcVoiceEnabled) { return }
    if (-not (Test-Path -LiteralPath $speechLauncher -PathType Leaf)) {
        throw "NPC voice is enabled in saved settings, but the speech launcher is missing: $speechLauncher"
    }
    try {
        $speechArguments = @{ DfuRoot=$dfuRoot; PersistentDataPath=$portableDataRoot }
        if (-not $DevelopmentMode) { $speechArguments.OwnershipReceiptPath = $speechOwnershipReceipt }
        & $speechLauncher @speechArguments
    } catch {
        throw "Optional NPC speech service could not be made ready: $($_.Exception.Message)"
    }
}

try {
    $runningPlayers = @(Get-CimInstance Win32_Process -Filter "Name='DaggerfallUnity.exe'" -ErrorAction SilentlyContinue |
        Where-Object { [string]::Equals([string]$_.ExecutablePath, $player, [StringComparison]::OrdinalIgnoreCase) })
    if ($runningPlayers.Count -gt 0) {
        # Voice may have been enabled while the game was already open. Warm only its
        # independent service; the running game owns dialogue and must not get a duplicate.
        Start-OptionalNpcVoiceService
        if (-not $DevelopmentMode) { Start-GameOwnedServiceMonitor -GameProcesses $runningPlayers }
        $ids = ($runningPlayers | ForEach-Object ProcessId) -join ', '
        Write-Output "Daggerfall Unity is already running (PID $ids); optional speech is ready when enabled; not starting dialogue services or a duplicate game process."
        return
    }

    if ($freeTextEnabled) {
        if (-not (Test-Path -LiteralPath $dialogueLauncher -PathType Leaf)) {
            throw "Free-text dialogue is enabled, but the local dialogue launcher is missing: $dialogueLauncher"
        }
        try {
            $dialogueArguments = @{ Background=$true; Port=8080; ContextSize=4096; ReadinessTimeoutSeconds=$ReadinessTimeoutSeconds }
            if (-not $DevelopmentMode) { $dialogueArguments.OwnershipReceiptPath = $dialogueOwnershipReceipt }
            & $dialogueLauncher @dialogueArguments
        } catch {
        $logDirectory = Join-Path $RepositoryRoot 'work'
        $errorLog = Join-Path $logDirectory 'local-dialogue-launcher-error.log'
        $detail = $_.Exception.Message
        $message = "Daggerfall Unity was not started because free-text dialogue is enabled but the local dialogue server is unavailable or incompatible.`r`n`r`n$detail`r`n`r`nSee $errorLog and the server stdout/stderr logs. Resolve the server error, then start the game again."
        try {
            New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
            Add-Content -LiteralPath $errorLog -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')] $detail" -Encoding UTF8
        } catch {
            Write-Error -Message "Could not write server startup error log: $($_.Exception.Message)" -ErrorAction Continue
        }
        Write-Error -Message $message -ErrorAction Continue
        try {
            Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
            [System.Windows.Forms.MessageBox]::Show(
                $message,
                'People of the Iliac Bay - dialogue server unavailable',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        } catch {
            Write-Error -Message "Could not display the server error dialog: $($_.Exception.Message)" -ErrorAction Continue
        }
            throw $message
        }
    }
    Start-OptionalNpcVoiceService

    $gameProcess = Start-Process -FilePath $player -WorkingDirectory $playerRoot -PassThru
    if ($freeTextEnabled) {
        Write-Output "Started Daggerfall Unity PID $($gameProcess.Id) after local dialogue server became ready."
    } else {
        Write-Output "Started Daggerfall Unity PID $($gameProcess.Id) with AI dialogue disabled."
    }
    if (-not $DevelopmentMode) {
        $launchedPlayers = @(Get-CimInstance Win32_Process -Filter "Name='DaggerfallUnity.exe'" -ErrorAction SilentlyContinue |
            Where-Object { [string]::Equals([string]$_.ExecutablePath, $player, [StringComparison]::OrdinalIgnoreCase) })
        if ($launchedPlayers.Count -eq 0) {
            $launchedPlayer = Get-CimInstance Win32_Process -Filter "ProcessId=$($gameProcess.Id)" -ErrorAction SilentlyContinue
            if ($launchedPlayer -and [string]::Equals([string]$launchedPlayer.ExecutablePath, $player, [StringComparison]::OrdinalIgnoreCase)) { $launchedPlayers = @($launchedPlayer) }
        }
        Start-GameOwnedServiceMonitor -GameProcesses $launchedPlayers
    }
} catch {
    if (-not $DevelopmentMode -and -not $lifecycleArmed) {
        try { Stop-NewlyOwnedServicesAfterStartupFailure }
        catch { Write-Warning "Could not safely clean newly owned local services after startup failure: $($_.Exception.Message)" }
    }
    throw
} finally {
    if (-not $DevelopmentMode -and $lifecycleRunRoot -and (Test-Path -LiteralPath $lifecycleRunRoot -PathType Container)) {
        try {
            $lifecycleRunsRoot = [IO.Path]::GetFullPath((Join-Path $portableDataRoot 'LocalModelLifecycle/runs')).TrimEnd('\')
            $resolvedRunRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $lifecycleRunRoot -ErrorAction Stop).Path).TrimEnd('\')
            $resolvedRunParent = [IO.Path]::GetDirectoryName($resolvedRunRoot).TrimEnd('\')
            if (-not [string]::Equals($resolvedRunParent, $lifecycleRunsRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing lifecycle cleanup outside exact run directory: $resolvedRunRoot"
            }
            $receiptPaths = @($dialogueOwnershipReceipt, $speechOwnershipReceipt)
            foreach ($receiptPath in $receiptPaths) {
                if ($receiptPath -and (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
                    $resolvedReceipt = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $receiptPath -ErrorAction Stop).Path)
                    if (-not [string]::Equals([IO.Path]::GetDirectoryName($resolvedReceipt).TrimEnd('\'), $resolvedRunRoot, [StringComparison]::OrdinalIgnoreCase)) {
                        throw "Refusing receipt cleanup outside exact run directory: $resolvedReceipt"
                    }
                    Remove-Item -LiteralPath $resolvedReceipt -Force -ErrorAction Stop
                }
            }
            if (-not $lifecycleArmed -and $lifecycleManifestPath -and (Test-Path -LiteralPath $lifecycleManifestPath -PathType Leaf)) {
                $resolvedManifest = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $lifecycleManifestPath -ErrorAction Stop).Path)
                if (-not [string]::Equals([IO.Path]::GetDirectoryName($resolvedManifest).TrimEnd('\'), $resolvedRunRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Refusing manifest cleanup outside exact run directory: $resolvedManifest"
                }
                Remove-Item -LiteralPath $resolvedManifest -Force -ErrorAction Stop
            }
            if (-not $lifecycleArmed -and @(Get-ChildItem -LiteralPath $resolvedRunRoot -Force -ErrorAction Stop).Count -eq 0) {
                Remove-Item -LiteralPath $resolvedRunRoot -Force -ErrorAction Stop
            }
        } catch {
            Write-Warning "Lifecycle scratch cleanup failed safely: $($_.Exception.Message)"
        }
    }
}
