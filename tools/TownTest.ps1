param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [ValidatePattern('^LivingCitiesTest-[a-f0-9]{8}$')][string]$FixtureCharacter)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$player = Join-Path $DfuRoot 'Player/DaggerfallUnity.exe'
$playerDir = Split-Path $player -Parent
$log = Join-Path $playerDir 'PortableAppdata/Player.log'
if (Get-Process DaggerfallUnity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $player }) { throw 'Isolated player already running; test will not interrupt it.' }
New-Item -ItemType Directory -Path "$repo/work" -Force | Out-Null
if (Test-Path -LiteralPath $log) {
    Move-Item -LiteralPath $log -Destination (Join-Path "$repo/work" ('player-before-town-' + [Guid]::NewGuid().ToString('N') + '.log'))
}
$started = Get-Date
# Saving uses DFU's rendered screenshot coroutine, so this test needs graphics.
$arguments = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -livingcities-town-test'
if ($FixtureCharacter) { $arguments += ' -livingcities-fixture-character ' + $FixtureCharacter }
$owned = Start-Process -FilePath $player -WorkingDirectory $playerDir -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Owned town-test process: $($owned.Id)"
try {
    $passed = $false
    $reported = [System.Collections.Generic.HashSet[string]]::new()
    while (((Get-Date) - $started).TotalSeconds -lt 180) {
        if ($owned.HasExited) { throw "Player exited early ($($owned.ExitCode))." }
        if (Test-Path -LiteralPath $log) {
            $body = Get-Content -LiteralPath $log -Raw
            foreach ($line in ($body -split "`n" | Where-Object { $_ -match 'LIVING_CITIES_TOWN_|LIVING_CITIES_TEST_SAVE:' })) {
                if ($reported.Add($line)) { Write-Output $line.Trim() }
            }
            if ($body -match 'error CS\d+|Exception:|LIVING_CITIES_TOWN_FAIL') { throw 'Town test failed; inspect work/town-test.log.' }
            if ($body -match 'LIVING_CITIES_TOWN_TEST_PASS:') { $passed = $true; break }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $passed) { throw 'Town test did not finish within 180 seconds.' }
} finally {
    try {
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination "$repo/work/town-test.log" -Force }
    } finally {
        if (-not $owned.HasExited) { Stop-Process -InputObject $owned }
    }
}
