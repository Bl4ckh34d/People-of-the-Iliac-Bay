param([string]$DfuRoot = (Split-Path $PSScriptRoot -Parent | Split-Path -Parent),
    [ValidatePattern('^LivingCitiesTest-[a-f0-9]{8}$')][string]$FixtureCharacter,
    [ValidatePattern('^[A-Za-z0-9 _-]+$')][string]$FixtureSaveName,
    [switch]$QuestProbe,
    [switch]$FreshPreviews,
    [switch]$HauntingDawnProbe,
    [switch]$PerformanceProbe,
    [switch]$ResidentTripProbe,
    [switch]$CrossRegionTripProbe,
    [switch]$TavernSurveyProbe,
    [switch]$CityTravelerProbe,
    [switch]$GateGuardProbe,
    [switch]$GuardQuotaOnly,
    [switch]$WallQuarterProbe,
    [switch]$StationaryNpcProbe,
    [switch]$DialogueProbe,
    [switch]$MemoryArchiveProbe,
    [switch]$MemoryArchiveRead,
    [switch]$MovementClockProbe,
    [switch]$OccupiedApproachProbe,
    [switch]$BusinessIdentityProbe,
    [switch]$InnLodgerHomeProbe,
    [switch]$BedroomReplacementProbe,
    [switch]$BedProbe,
    [switch]$SleepProbe,
    [switch]$RunningPilotProbe,
    [switch]$InnAlarmProbe,
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
    [switch]$DailyItineraryProbe,
    [switch]$OutdoorArrivalProbe,
    [switch]$CitizenThreatProbe,
    [switch]$DefenseLedgerProbe,
    [switch]$WelfareProbe)
$ErrorActionPreference = 'Stop'
if ($GuardQuotaOnly -and -not $GateGuardProbe) { throw 'GuardQuotaOnly requires GateGuardProbe.' }
if ($RunningPilotProbe -and -not $FixtureCharacter) { throw 'Running pilot probe requires a disposable fixture character.' }
if ($InnAlarmProbe -and -not $FixtureCharacter) { throw 'Inn alarm probe requires a disposable fixture character.' }
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
if ($FreshPreviews) {
    if ($FixtureCharacter) { throw 'Fresh previews must start a new disposable character.' }
    $arguments += ' -livingcities-fresh-previews'
}
if ($FixtureCharacter) { $arguments += ' -livingcities-fixture-character ' + $FixtureCharacter }
if ($FixtureSaveName) {
    if (-not $FixtureCharacter -or -not ($PerformanceProbe -or $DailyItineraryProbe -or $OutdoorArrivalProbe -or $ResidentTripProbe -or $CrossRegionTripProbe -or $GateGuardProbe)) { throw 'FixtureSaveName requires FixtureCharacter and a city probe.' }
    $arguments += ' -livingcities-fixture-save "' + $FixtureSaveName + '"'
}
if ($QuestProbe) { $arguments += ' -livingcities-quest-probe' }
if ($PerformanceProbe) {
    if (-not $FixtureCharacter) { throw 'Performance probe requires a disposable fixture character.' }
    $arguments += ' -livingcities-performance-probe'
}
if ($HauntingDawnProbe) {
    if (-not $FixtureCharacter) { throw 'Haunting dawn probe requires a disposable fixture character.' }
    $arguments += ' -livingcities-haunting-dawn-probe'
}
if ($CrossRegionTripProbe) {
    if (-not $FixtureCharacter) { throw "Cross-region probe requires a disposable fixture character." }
    $arguments += " -livingcities-cross-region-trip-probe"
}
if ($ResidentTripProbe) {
    if (-not $FixtureCharacter) { throw 'Resident trip probe requires a disposable fixture character.' }
    $arguments += ' -livingcities-resident-trip-probe'
}
if ($InnAlarmProbe) { $arguments += ' -livingcities-inn-alarm-probe' }
if ($BedProbe) { $arguments += ' -livingcities-bed-probe' }
if ($SleepProbe) { $arguments += ' -livingcities-sleep-probe' }
if ($RunningPilotProbe) {
    $arguments += ' -livingcities-running-pilot-probe'
}
if ($ResponseProbe) { $arguments += ' -livingcities-response-probe' }
if ($GuardActorProbe) { $arguments += ' -livingcities-guard-actor-probe' }
if ($GuardControllerProbe) { $arguments += ' -livingcities-guard-controller-probe' }
if ($GuardPatrolProbe) { $arguments += ' -livingcities-guard-patrol-probe' }
if ($GuardDefenseProbe) { $arguments += ' -livingcities-guard-defense-probe' }
if ($TavernSurveyProbe) { $arguments += ' -livingcities-tavern-survey-probe' }
if ($CityTravelerProbe) { $arguments += ' -livingcities-city-traveler-probe' }
if ($GateGuardProbe) { $arguments += ' -livingcities-gate-guard-probe' }
if ($GuardQuotaOnly) { $arguments += ' -livingcities-guard-quota-only' }
if ($WallQuarterProbe) { $arguments += ' -livingcities-wall-quarter-probe' }
if ($StationaryNpcProbe) { $arguments += ' -livingcities-stationary-probe' }
if ($DialogueProbe) { $arguments += ' -livingcities-dialogue-probe' }
if ($OccupiedApproachProbe) {
    if (-not $FixtureCharacter) { throw 'Occupied approach probe requires a disposable character.' }
    $arguments += ' -livingcities-occupied-approach-probe'
}
if ($BusinessIdentityProbe) {
    if (-not $FixtureCharacter) { throw 'Business identity probe requires a disposable fixture character.' }
    $arguments += ' -livingcities-business-identity-probe'
}
if ($InnLodgerHomeProbe -or $BedroomReplacementProbe) {
    if (-not $FixtureCharacter) { throw 'Inn lodger home probe requires a disposable fixture character.' }
    $arguments += ' -livingcities-inn-lodger-home-probe'
}
if ($MovementClockProbe) {
    if (-not $FixtureCharacter) { throw 'Movement clock probe requires a disposable character.' }
    $arguments += ' -livingcities-movement-clock-probe'
}
if ($MemoryArchiveProbe) {
    if (-not $FixtureCharacter) { throw 'Memory archive probe requires a disposable character.' }
    $arguments += ' -livingcities-memory-archive-probe'
    if ($MemoryArchiveRead) { $arguments += ' -livingcities-memory-archive-read' }
}
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
if ($DailyItineraryProbe) {
    if (-not $FixtureCharacter) { throw 'Daily itinerary probe requires the dedicated fixture character.' }
    $arguments += ' -livingcities-daily-itinerary-probe'
}
if ($OutdoorArrivalProbe) {
    if (-not $FixtureCharacter) { throw 'Outdoor arrival probe requires a disposable fixture character.' }
    $arguments += ' -livingcities-outdoor-arrival-probe'
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
    $testTimeout = if ($ShopVisitProbe -or $FreshPreviews -or $DialogueProbe) { 600 } elseif ($GateGuardProbe -or $WallQuarterProbe -or $ResidentTripProbe -or $CrossRegionTripProbe -or $PerformanceProbe -or $DailyItineraryProbe -or $OutdoorArrivalProbe -or $TavernSurveyProbe -or $CityTravelerProbe) { 360 } else { 180 }
    while (((Get-Date) - $started).TotalSeconds -lt $testTimeout) {
        if ($owned.HasExited) { throw "Player exited early ($($owned.ExitCode))." }
        if (Test-Path -LiteralPath $log) {
            $body = Get-Content -LiteralPath $log -Raw
            foreach ($line in ($body -split "`n" | Where-Object { $_ -match 'LIVING_CITIES_TOWN_|LIVING_CITIES_TEST_SAVE:' })) {
                if ($reported.Add($line)) { Write-Output $line.Trim() }
            }
            if ($body -match 'error CS\d+|(?:^|[\r\n])(?:\w+\.)*\w*Exception\b|LIVING_CITIES_TOWN_FAIL|LIVING_CITIES_TRAVEL_PLANNING_FAILED') { throw 'Town test failed; inspect work/town-test.log.' }
            if ($body -match 'LIVING_CITIES_TOWN_TEST_PASS:') { $passed = $true; break }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $passed) { throw "Town test did not finish within $testTimeout seconds." }
    if ($OccupiedApproachProbe -and ($body -notmatch 'LIVING_CITIES_OCCUPIED_APPROACH_PASS:' -or $body -notmatch 'LIVING_CITIES_FREE_BROWSE_PASS:')) { throw 'Occupied approach and free browse checks did not finish.' }
    if ($BusinessIdentityProbe -and ($body -notmatch 'LIVING_CITIES_BUSINESS_IDENTITY_PASS:' -or $body -notmatch 'LIVING_CITIES_NO_INN_KEYS_PASS:' -or $body -notmatch 'LIVING_CITIES_ACTIVE_SPEAKER_PASS:')) { throw 'Business identity, active speaker and inn key checks did not finish.' }
    if (($InnLodgerHomeProbe -or $BedroomReplacementProbe) -and $body -notmatch 'LIVING_CITIES_BEDROOM_REPLACEMENT_PASS:') { throw 'Bedroom replacement checks did not finish.' }
    if ($MovementClockProbe -and $body -notmatch 'LIVING_CITIES_MOVEMENT_CLOCK_PASS:') { throw 'Physical NPC speed and 2x clock checks did not complete.' }
    if ($PerformanceProbe -and $body -notmatch 'LIVING_CITIES_PERFORMANCE_PASS:') { throw 'Performance samples did not complete.' }
    if ($PerformanceProbe -and $body -notmatch 'LIVING_CITIES_MURDER_WITNESS_PASS:') { throw 'Murder witness persistence probe did not complete.' }
    if ($HauntingDawnProbe -and $body -notmatch 'LIVING_CITIES_HAUNTING_DAWN_PASS:') { throw 'Haunting dawn lifetime check did not complete.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_CROSS_REGION_TRIP_PASS:') { throw 'Cross-region journey check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_STOP_SAVE_PASS:') { throw 'Intermediate road-stop serialization check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_STOP_RECEPTION_PASS:') { throw 'Intermediate road-stop reception check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_STOP_ADMISSION_PASS:') { throw 'Intermediate road-stop admission check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_STOP_REFUSAL_PASS:') { throw 'Intermediate road-stop refusal check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_SETTLEMENT_INDEX_PASS:') { throw 'Road settlement index check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_PASSAGE_SAVE_PASS:') { throw 'Through-town route serialization check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_INN_SURVEY_PASS:') { throw 'Real intermediate inn route survey did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_PASSAGE_PLAN_PASS:') { throw 'Real intermediate inn trip insertion did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_STOP_CRIME_PASS:') { throw 'Intermediate visitor crime identity check did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_KEEPER_PLAN_PASS:') { throw 'Intermediate inn keeper preparation did not pass.' }
    if ($CrossRegionTripProbe -and $body -notmatch 'LIVING_CITIES_INCOMING_DISCOVERY_PASS:') { throw 'Historical incoming discovery check did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_RESIDENT_TRIP_PASS:') { throw 'Resident trip identity and home absence check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_NETWORK_PASS:') { throw 'Basic Roads runtime network check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_SLICES_PASS:') { throw 'Sliced road search check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_ACTOR_PASS:') { throw 'Road resident actor check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_HOME_CONNECTOR_PASS:') { throw 'Resident home connector check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_JOIN_PASS:') { throw 'Resident road join check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_TRANSFER_PASS:') { throw 'Resident actor ownership transfer check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_DESTINATION_SAVE_PASS:') { throw 'Resident destination serialization check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_DESTINATION_CRIME_SAVE_PASS:') { throw 'Resident destination crime serialization check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_DESTINATION_ACTOR_PASS:') { throw 'Resident destination actor check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_DESTINATION_INTERIOR_PASS:') { throw 'Resident destination interior check did not complete.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_SERVICE_EXIT_PASS:') { throw 'Resident service exit check did not complete.' }
    if ($HauntingDawnProbe -and $body -notmatch 'LIVING_CITIES_HAUNTING_INN_DAWN_PASS:') { throw 'Haunting inn dawn check did not complete.' }
    if ($HauntingDawnProbe -and $body -notmatch 'LIVING_CITIES_HAUNTING_PENDING_PASS:') { throw 'Haunting delayed placement check did not complete.' }
    if ($CityTravelerProbe -and $body -notmatch 'LIVING_CITIES_CITY_TRAVELER_PASS:') { throw 'Daggerfall traveler gate test did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and $body -notmatch 'LIVING_CITIES_GATE_GUARD_PASS:') { throw 'Daggerfall gate sentry probe did not complete.' }
    if ($GateGuardProbe -and $body -notmatch 'LIVING_CITIES_GUARD_QUOTA_PASS:') { throw 'Guard quota and native restore check did not complete.' }
    if ($GateGuardProbe -and $body -notmatch 'LIVING_CITIES_GATE_GUARD_VIEW_PASS:') { throw 'Daggerfall gate sentry rendering check did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and $body -notmatch 'LIVING_CITIES_GATE_INDOOR_RECALL_PASS:') { throw 'Gate recall while inside an inn did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and $body -notmatch 'LIVING_CITIES_WALL_DUTY_PASS:') { throw 'Wall patrol and morning return did not complete.' }
    if ($GateGuardProbe -and -not $GuardQuotaOnly -and $body -notmatch 'LIVING_CITIES_WALL_SLICES_PASS:') { throw 'Wall survey equivalence and cancellation did not complete.' }
    if ($GateGuardProbe -and $body -notmatch 'LIVING_CITIES_GUARD_SETUP_SLICES_PASS:') { throw 'Guard setup survey equivalence and cancellation did not complete.' }
    if ($WallQuarterProbe -and $body -notmatch 'LIVING_CITIES_QUARTER_BEDS_PASS:') { throw 'Wall quarter furniture check did not complete.' }
    if ($CityTravelerProbe -and $body -notmatch 'LIVING_CITIES_TRAVELER_ACTOR_PASS:') { throw 'Daggerfall traveler actor test did not complete.' }
    if ($CityTravelerProbe -and $body -notmatch 'LIVING_CITIES_TRAVELER_ROOM_ARRIVAL_PASS:') { throw 'Traveler physical bedroom arrival test did not complete.' }
    if ($CityTravelerProbe -and $body -notmatch 'LIVING_CITIES_TRAVELER_REFUSAL_PASS:') { throw 'Daggerfall full-inn traveler departure test did not complete.' }
    if ($StationaryNpcProbe -and $body -notmatch 'LIVING_CITIES_STATIONARY_PASS:') { throw 'Ordinary stationary NPC test did not complete.' }
    if ($QuestProbe -and $body -notmatch 'LIVING_CITIES_TOWN_QUEST_PASS:') { throw 'Quest probe did not complete.' }
    if ($BedProbe -and $body -notmatch 'LIVING_CITIES_TOWN_BEDS_PASS:') { throw 'Bed probe did not complete.' }
    if ($ShopVisitProbe -and $body -notmatch 'LIVING_CITIES_SHOP_VISIT_PASS:') { throw 'Shop activity probe did not complete.' }
    if ($ShopVisitProbe -and ([regex]::Matches($body, 'LIVING_CITIES_SHOP_DEPARTURE_PASS:').Count -ne 2)) { throw 'Both shopper purchase modes must physically reach the exit.' }
    if ($ShopVisitProbe -and $body -notmatch 'LIVING_CITIES_FOREIGN_SHOP_PASS:') { throw 'Foreign shopper activity probe did not complete.' }
    if ($ShopVisitProbe -and $body -notmatch 'LIVING_CITIES_FOREIGN_SHOP_DEPARTURE_PASS:') { throw 'Foreign shopper physical departure did not complete.' }
    if ($ShopVisitProbe -and $body -notmatch 'LIVING_CITIES_FOREIGN_CORPSE_PASS:') { throw 'Foreign shopper corpse response check did not complete.' }
    if ($ShopVisitProbe -and $body -notmatch 'LIVING_CITIES_FOREIGN_REPORT_PASS:') { throw 'Foreign shopper physical report check did not complete.' }
    if ($ShopVisitProbe -and $body -notmatch 'LIVING_CITIES_FOREIGN_GUARDED_EXIT_PASS:') { throw 'Foreign guarded-shop exit check did not complete.' }
    if ($CrowdProbe -and $body -notmatch 'LIVING_CITIES_CROWD_PASS:') { throw 'Crowd movement probe did not complete.' }
    if ($CrowdProbe -and $body -notmatch 'LIVING_CITIES_COMFORT_AVOIDANCE_PASS:') { throw 'Early comfort avoidance did not complete.' }
    if ($DailyItineraryProbe -and $body -notmatch 'LIVING_CITIES_DAILY_ITINERARY_PASS:') { throw 'Generated daily itinerary probe did not complete.' }
    if ($OutdoorArrivalProbe -and $body -notmatch 'LIVING_CITIES_OUTDOOR_ARRIVAL_CONTINUITY_PASS:') { throw 'Physical outdoor arrival probe did not complete.' }
    if ($CrowdProbe -and $body -notmatch 'LIVING_CITIES_CROWD_WALL_PASS:') { throw 'Crowd wall collision check did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_CITIZEN_THREAT_PASS:') { throw 'Citizen threat probe did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_FLIGHT_SAFETY_PASS:') { throw 'Attacker-aware street flight did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_SHELTER_LOCK_PASS:') { throw 'Shelter lock/save/expiry check did not complete.' }
    if ($CitizenThreatProbe -and $body -notmatch 'LIVING_CITIES_RETREAT_CORRIDOR_PASS:') { throw 'Local retreat collision check did not complete.' }
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
    if ($RunningPilotProbe -and $body -notmatch 'LIVING_CITIES_RUNNING_PILOT_PASS:') { throw 'Running pilot preview probe did not complete.' }
    if ($InnAlarmProbe -and $body -notmatch 'LIVING_CITIES_INN_ALARM_PASS:') { throw 'Inn alarm probe did not complete.' }
    if ($InnAlarmProbe -and $body -notmatch 'LIVING_CITIES_INN_TRAVELER_CORPSE_PASS:') { throw 'Inn traveler corpse discovery probe did not complete.' }
    if ($InnAlarmProbe -and $body -notmatch 'LIVING_CITIES_WITNESSED_INSPECTION_PASS:') { throw 'Witnessed inn murder inspection did not complete.' }
    if ($ResponseProbe -and $body -notmatch 'LIVING_CITIES_TOWN_RESPONSE_PASS:') { throw 'Response probe did not complete.' }
    if ($GuardActorProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_ACTOR_PASS:') { throw 'Guard actor probe did not complete.' }
    if ($GuardActorProbe -and $body -notmatch 'LIVING_CITIES_GUARD_TORCH_PASS:') { throw 'Guard torch probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_CONTROLLER_PASS:') { throw 'Guard controller probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_SURRENDER_REFUSAL_PASS:') { throw 'Guard surrender refusal probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_PURSUIT_RECRUIT_PASS:') { throw 'Nearby guard pursuit recruitment probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_PURSUIT_REACQUIRE_PASS:') { throw 'Guard pursuit reacquisition probe did not complete.' }
    if ($InnAlarmProbe -and $body -notmatch 'LIVING_CITIES_REPLACEMENT_INSPECTOR_PASS:') { throw 'Replacement inspector probe did not complete.' }
    if ($GuardPatrolProbe -and $body -notmatch 'LIVING_CITIES_PATROL_WITNESS_ROUTE_PASS:') { throw 'Moving patrol witness route probe did not complete.' }
    if ($GuardControllerProbe -and $body -notmatch 'LIVING_CITIES_GUARD_TORCH_CONTROLLER_PASS:') { throw 'Guard torch controller probe did not complete.' }
    if ($GuardActorProbe -and $body -notmatch 'LIVING_CITIES_GUARD_QUIET_PASS:') { throw 'Peaceful guard audio probe did not complete.' }
    if ($GuardPatrolProbe -and $body -notmatch 'LIVING_CITIES_GUARD_PATROL_PASS:') { throw 'Guard patrol probe did not complete.' }
    if ($GuardDefenseProbe -and $body -notmatch 'LIVING_CITIES_GUARD_DEFENSE_PASS:') { throw 'Guard defense probe did not complete.' }
    if ($GuardDefenseProbe -and $body -notmatch 'LIVING_CITIES_OUTDOOR_WELFARE_PASS:') { throw 'Outdoor welfare encounter did not complete.' }
    if ($TavernSurveyProbe -and $body -notmatch 'LIVING_CITIES_TAVERN_SURVEY_PASS:') { throw 'Tavern interior survey did not complete.' }
    if ($TavernSurveyProbe -and $body -notmatch 'LIVING_CITIES_INN_SOCIAL_MOVEMENT_PASS:') { throw 'Rendered inn patron movement did not complete.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_TAVERN_PATRON_PASS:').Count -ne 3)) { throw 'Local patron routing checks did not pass in all three taverns.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_UNLOADED_SETTLEMENT_PASS:') { throw 'Unloaded settlement navigation comparison did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_UNLOADED_BOUNDARY_PASS:') { throw 'Unloaded settlement wilderness boundary comparison did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_UNLOADED_ROAD_ACCESS_PASS:') { throw 'Unloaded street-to-road access comparison did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_ROAD_BUILDING_ROUTE_PASS:') { throw 'Regional road-to-building route comparison did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_SERVICE_INN_ROUTE_PASS:') { throw 'Service-to-inn route comparison did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_OVERNIGHT_TIMING_PASS:') { throw 'Generated overnight timing did not pass native route and serialization checks.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_OVERNIGHT_ROUTE_PASS:') { throw 'Original-resident multi-day route generation did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_OVERNIGHT_CAPACITY_PASS:') { throw 'Remote household/inventory preparation and full-inn fallback did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_PLANNED_RECEPTION_PASS:') { throw 'Remote planned reception, native restore and delayed payment did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_ADMISSION_PASS:') { throw 'Dead remote keeper admission and departure did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_SAME_INN_STAY_PASS:') { throw 'Single-inn social overnight planning did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_LODGING_FIRST_PASS:') { throw 'Late-arrival lodging-first planning did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_RESIDENT_PLANNER_PASS:') { throw 'Original resident trip planner did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_TRAVEL_COMMIT_PASS:') { throw 'Resident travel commit lifecycle did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_ADJACENT_ROAD_SAVE_PASS:') { throw 'Adjacent road native persistence did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_DAILY_TRAVEL_BATCH_PASS:') { throw 'Live daily travel batch did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_AUTOMATIC_OVERNIGHT_PASS:') { throw 'Automatic multi-day trip check did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_DAY_VISIT_LODGING_PASS:') { throw 'Late day visit lodging check did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_RESERVED_HOUSEHOLD_PASS:') { throw 'Reserved traveler household check did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_UNVISITED_ORIGIN_PASS:') { throw 'Unvisited origin automatic departure check did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_VISITOR_ORIGINS_PASS:') { throw 'Visitor origin catalog and weighting check did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_WORK_STAGGER_PASS:') { throw 'Background work staggering did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_MODEL_CLEARANCE_PASS:') { throw 'Unloaded model approach collision comparison did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_MODEL_ROUTE_PASS:') { throw 'Unloaded model route collision comparison did not pass.' }
    if ($ResidentTripProbe -and $body -notmatch 'LIVING_CITIES_UNLOADED_GATES_PASS:') { throw 'Unloaded gate crossing comparison did not pass.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_RESIDENT_INN_ROOM_PASS:').Count -ne 3)) { throw 'Resident booked-room sleeping did not pass in all three inns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_VISIT_SETTLEMENT_SAVE_PASS:').Count -ne 6)) { throw 'Visit settlement persistence and legacy inference did not pass paid/refused cases in all three inns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_VISIT_ID_SAVE_PASS:').Count -ne 6)) { throw 'Stable visit ID persistence and legacy inference did not pass paid/refused cases in all three inns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_RESIDENT_INN_REFUSAL_PASS:').Count -ne 3)) { throw 'Resident refusal exits did not pass in all three inns.' }
    if ($TavernSurveyProbe -and $body -notmatch 'LIVING_CITIES_TAVERN_ROOMS_PASS:') { throw 'Stable tavern room partition did not complete.' }
    if ($TavernSurveyProbe -and $body -notmatch 'LIVING_CITIES_TAVERN_MARKER_PASS:') { throw 'Native rental-marker room mapping did not complete.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_NATIVE_RENTAL_PASS:').Count -ne 3)) { throw 'Native paid rental import did not pass in all three taverns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_UNRENTED_INVENTORY_PASS:').Count -ne 3)) { throw 'Unrented guest inventory did not pass in all three taverns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_UNLOADED_TAVERN_PASS:').Count -ne 3)) { throw 'Unloaded tavern inventory did not match all three actual interiors.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_SLICED_TAVERN_PASS:').Count -ne 3)) { throw 'Sliced tavern inventory did not match all three synchronous surveys.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_REMOTE_INN_PASS:').Count -ne 3)) { throw 'Remote inn records did not match all three native inventories.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_REMOTE_RECEPTION_PASS:').Count -ne 3)) { throw 'Remote keeper routes did not match all three loaded inns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_REMOTE_CLAIMS_PASS:').Count -ne 3)) { throw 'Remote native inn claims were not verified for all three inns.' }
    if ($TavernSurveyProbe -and $body -notmatch 'LIVING_CITIES_REMOTE_CANCEL_PASS:') { throw 'Remote survey cancellation and queue cleanup were not verified.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_REMOTE_RECEIPT_PASS:').Count -ne 3)) { throw 'Remote paid-room reconciliation was not verified for all three inns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_RESIDENT_ROOM_HOLD_PASS:').Count -ne 3)) { throw 'Multi-visit room ownership was not verified for all three inns.' }
    if ($TavernSurveyProbe -and $body -notmatch 'LIVING_CITIES_EXTERIOR_INVENTORY_PASS:') { throw 'Exterior inventory and paid receipt import did not complete.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_TRAVELER_ROUTE_PASS:').Count -ne 3)) { throw 'Physical traveler road routes did not reach all three taverns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_RENTAL_PAYMENT_PASS:').Count -ne 3)) { throw 'Rental payment/vacancy checks did not pass in all three taverns.' }
    if ($TavernSurveyProbe -and ([regex]::Matches($body, 'LIVING_CITIES_TAVERN_REST_PASS:').Count -ne 3)) { throw 'Occupied-bed rest checks did not pass in all three taverns.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_BUILDING_PASS:') { throw 'Guard building probe did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_PUBLIC_POSTS_PASS:') { throw 'Public guard post check did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_GUARD_DOOR_PURSUIT_PASS:') { throw 'Doorway pursuit check did not complete.' }
    if ($GuardBuildingProbe -and $body -notmatch 'LIVING_CITIES_PENDING_GUARD_PASS:') { throw 'Pending native guard report probe did not complete.' }
    if ($GuardWitnessProbe -and $body -notmatch 'LIVING_CITIES_TOWN_GUARD_WITNESS_PASS:') { throw 'Guard witness probe did not complete.' }
    if ($InteriorEscapeProbe -and $body -notmatch 'LIVING_CITIES_WITNESS_MOVING_ATTACKER_PASS:') { throw 'Moving attacker witness probe did not complete.' }
    if ($DialogueProbe -and ($body -notmatch 'LIVING_CITIES_DIALOGUE_FOCUS_PASS:' -or $body -notmatch 'LIVING_CITIES_GROUNDED_DIALOGUE_PASS:' -or $body -notmatch 'LIVING_CITIES_FLIRT_PASS:' -or $body -notmatch 'LIVING_CITIES_CHILD_DIALOGUE_REPORT_PASS:' -or $body -notmatch 'LIVING_CITIES_DIALOGUE_MEMORY_PASS:' -or $body -notmatch 'LIVING_CITIES_HOME_DIALOGUE_PASS:')) { throw 'Grounded dialogue, focus, flirt, child-report, memory or home checks did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_HOUSING_LIVE_PASS:') { throw 'Housing probe did not complete.' }
    if ($HousingProbe -and ($body -notmatch 'LIVING_CITIES_ENTRY_PASS:' -or $body -notmatch 'LIVING_CITIES_ENTRY_GUARD_PASS:')) { throw 'Entry witness probes did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_ENTRY_MESSENGER_PASS:') { throw 'Entry messenger probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_IDENTITY_PASS:') { throw 'Keeper identity/bed reload probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_FAMILY_PASS:') { throw 'Keeper family/identity probe did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_BEREAVEMENT_PASS:') { throw 'Bereavement dialogue check did not complete.' }
    if ($HousingProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_RESPONSE_PASS:') { throw 'Keeper response/physical reporting probe did not complete.' }
    if ($KeeperResponseProbe -and $body -notmatch 'LIVING_CITIES_KEEPER_RESPONSE_PASS:') { throw 'Keeper response probe did not complete.' }
    if ($IndoorNoiseProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_NOISE_PASS:') { throw 'Indoor hearing/waking probe did not complete.' }
    if ($IndoorStairsProbe -and $body -notmatch 'LIVING_CITIES_INTERIOR_STAIRS_PASS:') { throw 'Upper-floor escape probe did not complete.' }
    if ($IndoorStairsProbe -and $body -notmatch 'LIVING_CITIES_INTERIOR_STAIRS_MOVEMENT_PASS:') { throw 'Actual upper-floor actor movement did not complete.' }
    if ($IndoorNoiseProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_NOISE_REPORT_PASS:') { throw 'Indoor neutral reporting probe did not complete.' }
    if ($IndoorNoiseProbe -and $body -notmatch 'LIVING_CITIES_INDOOR_NOISE_RELOAD_PASS:') { throw 'Actual indoor wake reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_KEEPER_WEAPON_PASS:') { throw 'Native weapon keeper witness probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_KEEPER_DEATH_PASS:') { throw 'Native keeper death/reload probe did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_KEEPER_VISITOR_PASS:') { throw 'Dead keeper arriving-visitor regression did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_CORPSE_VISITOR_CONTACT_PASS:') { throw 'Corpse visitor physical guard-contact regression did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_NEUTRAL_INSPECTION_PASS:') { throw 'Neutral report corpse inspection did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_GUARDED_VISITOR_PASS:') { throw 'Guarded crime scene visitor rejection did not complete.' }
    if (($HousingProbe -or $KeeperResponseProbe) -and $body -notmatch 'LIVING_CITIES_ORDINARY_SHOP_DEATH_PASS:') { throw 'Ordinary shop NPC death/reload probe did not complete.' }
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
    if ($MemoryArchiveProbe -and $body -notmatch 'LIVING_CITIES_MEMORY_ARCHIVE_PASS:') { throw 'Shared memory archive probe did not complete.' }
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
