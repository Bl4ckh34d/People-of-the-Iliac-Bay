param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [ValidatePattern('^LivingCitiesTest-[a-f0-9]{8}$')][string]$FixtureCharacter,
    [switch]$QuestProbe,
    [switch]$BedProbe,
    [switch]$SleepProbe,
    [switch]$ResponseProbe,
    [switch]$GuardActorProbe,
    [switch]$GuardControllerProbe,
    [switch]$GuardPatrolProbe,
    [switch]$GuardDefenseProbe,
    [switch]$GuardBuildingProbe,
    [switch]$GuardWitnessProbe,
    [switch]$HousingProbe,
    [switch]$KeeperResponseProbe,
    [switch]$IndoorNoiseProbe,
    [switch]$IndoorStairsProbe,
    [switch]$CrimeSceneProbe,
    [switch]$OutdoorSceneProbe,
    [switch]$InteriorEscapeProbe,
    [switch]$QuestBindingProbe,
    [switch]$DeliveryProbe,
    [switch]$RefugeProbe,
    [switch]$RegionalProbe,
    [switch]$ShopVisitProbe,
    [switch]$CrowdProbe,
    [switch]$CitizenThreatProbe,
    [switch]$DefenseLedgerProbe,
    [switch]$WelfareProbe)
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
if ($GuardPatrolProbe) { $arguments += ' -livingcities-guard-patrol-probe' }
if ($GuardDefenseProbe) { $arguments += ' -livingcities-guard-defense-probe' }
if ($GuardBuildingProbe) { $arguments += ' -livingcities-guard-building-probe' }
if ($GuardWitnessProbe) { $arguments += ' -livingcities-guard-witness-probe' }
if ($HousingProbe) { $arguments += ' -livingcities-housing-probe' }
if ($KeeperResponseProbe) { $arguments += ' -livingcities-keeper-response-probe' }
if ($IndoorNoiseProbe) { $arguments += ' -livingcities-indoor-noise-probe' }
if ($IndoorStairsProbe) { $arguments += ' -livingcities-indoor-stairs-probe' }
if ($CrimeSceneProbe) { $arguments += ' -livingcities-crime-scene-probe' }
if ($OutdoorSceneProbe) { $arguments += ' -livingcities-outdoor-scene-probe' }
if ($InteriorEscapeProbe) { $arguments += ' -livingcities-interior-escape-probe' }
if ($QuestBindingProbe) { $arguments += ' -livingcities-quest-binding-probe' }
if ($DeliveryProbe) { $arguments += ' -livingcities-delivery-probe' }
if ($RefugeProbe) {
    if (-not $FixtureCharacter) { throw 'Refuge survey requires a dedicated fixture character.' }
    $arguments += ' -livingcities-refuge-probe'
}
if ($RegionalProbe) {
    if (-not $FixtureCharacter) { throw 'Regional probe requires the dedicated fixture character.' }
    $arguments += ' -livingcities-regional-probe'
}
if ($ShopVisitProbe) {
    if (-not $FixtureCharacter) { throw 'Shop visit probe requires the dedicated fixture character.' }
    $arguments += ' -livingcities-shop-visit-probe'
}
if ($CrowdProbe) {
    if (-not $FixtureCharacter) { throw 'Crowd probe requires the dedicated fixture character.' }
    $arguments += ' -livingcities-crowd-probe'
}
if ($CitizenThreatProbe) {
    if (-not $FixtureCharacter) { throw 'Citizen threat probe requires the dedicated fixture character.' }
    $arguments += ' -livingcities-citizen-threat-probe'
}
if ($DefenseLedgerProbe) {
    if (-not $FixtureCharacter) { throw 'Defense ledger probe requires the dedicated fixture character.' }
    $arguments += ' -livingcities-defense-ledger-probe'
}
if ($WelfareProbe) {
    if (-not $FixtureCharacter) { throw 'Welfare probe requires a dedicated fixture character.' }
    $arguments += ' -livingcities-welfare-probe'
}
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
            if ($body -match 'error CS\d+|(?:^|[\r\n])(?:System\.)?\w*Exception\b|LIVING_CITIES_TOWN_FAIL') { throw 'Town test failed; inspect work/town-test.log.' }
            if ($body -match 'LIVING_CITIES_TOWN_TEST_PASS:') { $passed = $true; break }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $passed) { throw 'Town test did not finish within 180 seconds.' }
    if ($QuestProbe -and $body -notmatch 'LIVING_CITIES_TOWN_QUEST_PASS:') { throw 'Quest probe did not complete.' }
    if ($BedProbe -and $body -notmatch 'LIVING_CITIES_TOWN_BEDS_PASS:') { throw 'Bed probe did not complete.' }
    if ($ShopVisitProbe -and $body -notmatch 'LIVING_CITIES_SHOP_VISIT_PASS:') { throw 'Shop activity probe did not complete.' }
    if ($CrowdProbe -and $body -notmatch 'LIVING_CITIES_CROWD_PASS:') { throw 'Crowd movement probe did not complete.' }
    if ($CrowdProbe -and $body -notmatch 'LIVING_CITIES_CROWD_WALL_PASS:') { throw 'Crowd wall collision check did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_CITIZEN_THREAT_PASS:') { throw 'Citizen threat probe did not complete.' }
    if ($DefenseLedgerProbe -and $body -notmatch 'LIVING_CITIES_NATIVE_DEFENSE_LEDGER_PASS:') { throw 'Native defense ledger probe did not complete.' }
    if ($DefenseLedgerProbe -and $body -notmatch 'LIVING_CITIES_WELFARE_RECREATE_PASS:') { throw 'Welfare serialization and actor recreation probe did not complete.' }
    if ($DefenseLedgerProbe -and $body -notmatch 'LIVING_CITIES_DEFENSE_RELAY_PASS:') { throw 'Physical defense witness handoff probe did not complete.' }
    if ($WelfareProbe -and $body -notmatch 'LIVING_CITIES_WELFARE_CHAIN_PASS:') { throw 'Welfare encounter chain did not complete.' }
    if ($WelfareProbe -and $body -notmatch 'LIVING_CITIES_WELFARE_SLOT_PASS:') { throw 'Actual welfare approach save/load did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_THREAT_PASS:') { throw 'Keeper threat probe did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_DEFENDER_PASS:') { throw 'Indoor defender combat probe did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_DEFENSE_REPORT_PASS:') { throw 'Indoor witness defense report did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_DEFENSE_ARRIVAL_PASS:') { throw 'Indoor defense arrival did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_ASSAULT_PASS:') { throw 'Indoor player assault probe did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_ENEMY_DEATH_PASS:') { throw 'Indoor enemy death probe did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_GUARD_EFFECT_RECREATE_PASS:') { throw 'Guard effect recreation probe did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_GUARD_EFFECT_ELAPSED_PASS:') { throw 'Guard effect elapsed-time probe did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_GUARD_DOOR_EFFECT_PASS:') { throw 'Guard doorway effect probe did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_GUARD_EFFECT_LOAD_PASS:') { throw 'Guard effect game reload probe did not complete.' }
    if ($SleepProbe -and $body -notmatch 'LIVING_CITIES_TOWN_SLEEP_PASS:') { throw 'Sleep probe did not complete.' }
    if ($ResponseProbe -and $body -notmatch 'LIVING_CITIES_TOWN_RESPONSE_PASS:') { throw 'Response probe did not complete.' }
    if ($GuardActorProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_ACTOR_PASS:') { throw 'Guard actor probe did not complete.' }
    if ($GuardActorProbe -and $body -notmatch 'LIVING_CITIES_GUARD_TORCH_PASS:') { throw 'Guard torch probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_CONTROLLER_PASS:') { throw 'Guard controller probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_GUARD_TORCH_CONTROLLER_PASS:') { throw 'Guard torch controller probe did not complete.' }
    if ($GuardActorProbe -and $body -notmatch 'LIVING_CITIES_GUARD_QUIET_PASS:') { throw 'Peaceful guard audio probe did not complete.' }
    if ($GuardPatrolProbe -and $body -notmatch 'LIVING_CITIES_GUARD_PATROL_PASS:') { throw 'Guard patrol probe did not complete.' }
    if ($GuardDefenseProbe -and $body -notmatch 'LIVING_CITIES_GUARD_DEFENSE_PASS:') { throw 'Guard defense probe did not complete.' }
    if ($GuardDefenseProbe -and $body -notmatch 'LIVING_CITIES_OUTDOOR_WELFARE_PASS:') { throw 'Outdoor welfare encounter did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_BUILDING_PASS:') { throw 'Guard building probe did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_PUBLIC_POSTS_PASS:') { throw 'Public guard post check did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_GUARD_DOOR_PURSUIT_PASS:') { throw 'Doorway pursuit check did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_PENDING_GUARD_PASS:') { throw 'Pending native guard report probe did not complete.' }
    if ($GuardWitnessProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_WITNESS_PASS:') { throw 'Guard witness probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_HOUSING_LIVE_PASS:') { throw 'Housing probe did not complete.' }
    if ($HousingProbe -and ($body -notmatch 'LIVING_CITIES_ENTRY_PASS:' -or $body -notmatch 'LIVING_CITIES_ENTRY_GUARD_PASS:')) { throw 'Entry witness probes did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_ENTRY_MESSENGER_PASS:') { throw 'Entry messenger probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_IDENTITY_PASS:') { throw 'Keeper identity/bed reload probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_FAMILY_PASS:') { throw 'Keeper family/identity probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_RESPONSE_PASS:') { throw 'Keeper response/physical reporting probe did not complete.' }
    if ($KeeperResponseProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_RESPONSE_PASS:') { throw 'Keeper response probe did not complete.' }
    if ($IndoorNoiseProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_NOISE_PASS:') { throw 'Indoor hearing/waking probe did not complete.' }
    if ($IndoorStairsProbe -and $body -notmatch 'LIVING_CITIES_INTERIOR_STAIRS_PASS:') { throw 'Upper-floor escape probe did not complete.' }
    if ($IndoorStairsProbe -and $body -notmatch 'LIVING_CITIES_INTERIOR_STAIRS_MOVEMENT_PASS:') { throw 'Actual upper-floor actor movement did not complete.' }
    if ($IndoorNoiseProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_NOISE_REPORT_PASS:') { throw 'Indoor neutral reporting probe did not complete.' }
    if ($IndoorNoiseProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_NOISE_RELOAD_PASS:') { throw 'Actual indoor wake reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_KEEPER_WEAPON_PASS:') { throw 'Native weapon keeper witness probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_KEEPER_DEATH_PASS:') { throw 'Native keeper death/reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_KEEPER_SLEEP_PASS:') { throw 'Native keeper sleep/wake/reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_CIVILIAN_DOORS_PASS:') { throw 'Civilian door permission probe did not complete.' }
    if ($CrimeSceneProbe -and $body -notmatch 'LIVING_CITIES_TOWN_CRIME_SCENE_PASS:') { throw 'Crime scene probe did not complete.' }
    if ($CrimeSceneProbe -and $body -notmatch 'LIVING_CITIES_UNCACHED_GUARD_PASS:') { throw 'Uncached residential guard probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_UNCACHED_KEEPER_PASS:') { throw 'Uncached native keeper guard probe did not complete.' }
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
