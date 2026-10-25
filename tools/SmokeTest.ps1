param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent), [switch]$GuardProbe)
. (Join-Path $PSScriptRoot 'LogProtocol.ps1')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$player = Join-Path $DfuRoot 'Player/DaggerfallUnity.exe'
$playerDir = Split-Path $player -Parent
$log = Join-Path $playerDir 'PortableAppdata/Player.log'
if (Get-Process DaggerfallUnity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $player }) { throw 'Isolated player already running; smoke test will not interrupt it.' }
New-Item -ItemType Directory -Path "$repo/work" -Force | Out-Null
# DFU rotates and truncates this log itself. Move it first as well, so evidence
# freshness remains explicit if host logging changes in a future version.
if (Test-Path -LiteralPath $log) {
    $previousLog = Join-Path "$repo/work" ("player-before-smoke-" + [Guid]::NewGuid().ToString('N') + '.log')
    Move-Item -LiteralPath $log -Destination $previousLog
}
$started = Get-Date
$arguments = '-batchmode -nographics'
if ($GuardProbe) { $arguments += ' -peopleoftheiliacbay-guard-probe' }
$owned = Start-Process -FilePath $player -WorkingDirectory $playerDir -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    $passed = $false
    $readyAt = $null
    while (((Get-Date) - $started).TotalSeconds -lt 55) {
        if ($owned.HasExited) { throw "Player exited early ($($owned.ExitCode))." }
        if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).LastWriteTime -ge $started) {
            $body = Get-Content -LiteralPath $log -Raw
            if ((Test-NativeLogFailure $body)) { throw 'Player reported an exception or compile error; inspect preserved smoke log.' }
            $ready = ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_READY:',[StringComparison]::OrdinalIgnoreCase) -ge 0)) -and ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_SETTINGS_LOADED:',[StringComparison]::OrdinalIgnoreCase) -ge 0)) -and ((([string]$body).IndexOf('Finished mod init: BasicRoads',[StringComparison]::OrdinalIgnoreCase) -ge 0)) -and ((([string]$body).IndexOf('GameManager ready',[StringComparison]::OrdinalIgnoreCase) -ge 0))
            if ($GuardProbe) { $ready = $ready -and ((([string]$body).IndexOf('PEOPLE_OF_THE_ILIAC_BAY_GUARD_PROBE_PASS:',[StringComparison]::OrdinalIgnoreCase) -ge 0)) }
            if ($ready -and $null -eq $readyAt) { $readyAt = Get-Date }
            if ($null -ne $readyAt -and ((Get-Date) - $readyAt).TotalSeconds -ge 2) { $passed = $true; break }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $passed) { throw 'Startup markers missing after 55 seconds.' }
    Write-Output 'PASS: People of the Iliac Bay source compilation/init, Basic Roads init, GameManager ready. Gameplay not tested.'
    if ($GuardProbe) { Write-Output 'PASS: temporary guard interception probe; its patches removed.' }
} finally {
    try {
        $savedLog = if ($GuardProbe) { 'guard-probe-player.log' } else { 'player-smoke.log' }
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination "$repo/work/$savedLog" -Force }
    } finally {
        if (-not $owned.HasExited) { Stop-Process -InputObject $owned }
    }
}
