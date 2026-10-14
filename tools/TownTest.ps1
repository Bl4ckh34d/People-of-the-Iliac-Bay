param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [ValidatePattern('^LivingCitiesTest-[a-f0-9]{8}$')][string]$FixtureCharacter,
    [switch]$QuestProbe,
    [switch]$BedProbe,
    [switch]$SleepProbe,
    [switch]$ResponseProbe,
    [switch]$GuardActorProbe,
    [switch]$GuardControllerProbe,
    [switch]$GuardBuildingProbe,
    [switch]$GuardWitnessProbe,
    [switch]$HousingProbe,
    [switch]$KeeperResponseProbe,
    [switch]$CrimeSceneProbe,
    [switch]$OutdoorSceneProbe,
    [switch]$InteriorEscapeProbe,
    [switch]$QuestBindingProbe,
    [switch]$DeliveryProbe)
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
if ($QuestProbe) { $arguments += ' -livingcities-quest-probe' }
if ($BedProbe) { $arguments += ' -livingcities-bed-probe' }
if ($SleepProbe) { $arguments += ' -livingcities-sleep-probe' }
if ($ResponseProbe) { $arguments += ' -livingcities-response-probe' }
if ($GuardActorProbe) { $arguments += ' -livingcities-guard-actor-probe' }
if ($GuardControllerProbe) { $arguments += ' -livingcities-guard-controller-probe' }
if ($GuardBuildingProbe) { $arguments += ' -livingcities-guard-building-probe' }
if ($GuardWitnessProbe) { $arguments += ' -livingcities-guard-witness-probe' }
if ($HousingProbe) { $arguments += ' -livingcities-housing-probe' }
if ($KeeperResponseProbe) { $arguments += ' -livingcities-keeper-response-probe' }
if ($CrimeSceneProbe) { $arguments += ' -livingcities-crime-scene-probe' }
if ($OutdoorSceneProbe) { $arguments += ' -livingcities-outdoor-scene-probe' }
if ($InteriorEscapeProbe) { $arguments += ' -livingcities-interior-escape-probe' }
if ($QuestBindingProbe) { $arguments += ' -livingcities-quest-binding-probe' }
if ($DeliveryProbe) { $arguments += ' -livingcities-delivery-probe' }
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
    if ($QuestProbe -and $body -notmatch 'LIVING_CITIES_TOWN_QUEST_PASS:') { throw 'Quest probe did not complete.' }
    if ($BedProbe -and $body -notmatch 'LIVING_CITIES_TOWN_BEDS_PASS:') { throw 'Bed probe did not complete.' }
    if ($SleepProbe -and $body -notmatch 'LIVING_CITIES_TOWN_SLEEP_PASS:') { throw 'Sleep probe did not complete.' }
    if ($ResponseProbe -and $body -notmatch 'LIVING_CITIES_TOWN_RESPONSE_PASS:') { throw 'Response probe did not complete.' }
    if ($GuardActorProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_ACTOR_PASS:') { throw 'Guard actor probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_CONTROLLER_PASS:') { throw 'Guard controller probe did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_BUILDING_PASS:') { throw 'Guard building probe did not complete.' }
    if ($GuardWitnessProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_WITNESS_PASS:') { throw 'Guard witness probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_HOUSING_LIVE_PASS:') { throw 'Housing probe did not complete.' }
    if ($HousingProbe -and ($body -notmatch 'LIVING_CITIES_ENTRY_PASS:' -or $body -notmatch 'LIVING_CITIES_ENTRY_GUARD_PASS:')) { throw 'Entry witness probes did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_ENTRY_MESSENGER_PASS:') { throw 'Entry messenger probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_IDENTITY_PASS:') { throw 'Keeper identity/bed reload probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_RESPONSE_PASS:') { throw 'Keeper response/physical reporting probe did not complete.' }
    if ($KeeperResponseProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_RESPONSE_PASS:') { throw 'Keeper response probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_KEEPER_WEAPON_PASS:') { throw 'Native weapon keeper witness probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_CIVILIAN_DOORS_PASS:') { throw 'Civilian door permission probe did not complete.' }
    if ($CrimeSceneProbe -and $body -notmatch 'LIVING_CITIES_TOWN_CRIME_SCENE_PASS:') { throw 'Crime scene probe did not complete.' }
    if ($OutdoorSceneProbe -and $body -notmatch 'LIVING_CITIES_OUTDOOR_SCENE_PASS:') { throw 'Outdoor scene probe did not complete.' }
    if ($InteriorEscapeProbe -and $body -notmatch 'LIVING_CITIES_TOWN_INTERIOR_ESCAPE_PASS:') { throw 'Interior escape probe did not complete.' }
    if ($QuestBindingProbe -and $body -notmatch 'LIVING_CITIES_QUEST_BINDING_PASS:') { throw 'Quest binding probe did not complete.' }
    if ($QuestBindingProbe -and $body -notmatch 'LIVING_CITIES_TOWN_QUEST_LEDGER_PASS:') { throw 'Quest ledger save/load probe did not complete.' }
    if ($QuestBindingProbe -and $body -notmatch 'LIVING_CITIES_TOWN_QUEST_OFFER_PASS:') { throw 'Quest offer accept/reject probe did not complete.' }
    if ($QuestBindingProbe -and $body -notmatch 'LIVING_CITIES_TOWN_QUEST_RECIPIENT_PASS:') { throw 'Quest recipient activation probe did not complete.' }
    if ($DeliveryProbe -and $body -notmatch 'LIVING_CITIES_TOWN_DELIVERY_PASS:') { throw 'Vanilla delivery hand-in did not complete.' }
} finally {
    try {
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination "$repo/work/town-test.log" -Force }
    } finally {
        if (-not $owned.HasExited) { Stop-Process -InputObject $owned }
        if (-not $owned.WaitForExit(10000)) { throw 'Owned test player did not exit within 10 seconds.' }
        if ($QuestBindingProbe) {
            $probePackage = Join-Path $playerDir 'DaggerfallUnity_Data/StreamingAssets/Mods/livingcitiesquestbindingprobe.dfmod'
            if (Test-Path -LiteralPath $probePackage) { Remove-Item -LiteralPath $probePackage }
        }
    }
}
