# Daggerfall nightlife and the Lysandus haunting

## Verified content census, 15 October 2026

`DaggerfallCityAudit.Run` reads the installed original data through DFU's ContentReader without spawning actors or changing saves. Unity completed with exit 0; evidence: `work/DaggerfallCity-census.log`. Daggerfall city has map ID **1291010263**, location type **TownCity**, and 8×8 exterior blocks. There are 484 House1–House6 buildings containing 714 beds, plus 20 taverns containing 134 beds. These are content counts, not an allocated resident population: quest reservations, ownership and shop households still affect allocation. One separate HouseForSale is excluded from the house total. The audit does not prove usable wall refuges or route connectivity.

Current runtime admission gates remain in `StreetPopulation.SetTown` and `GuardPopulation.SetTown`; both admit only Gothway Garden. Guard allocation also hardcodes `SettlementScale.Town`. The narrow `KnownQuestBindings` Gothway quest gate must remain independent when city simulation is enabled. Read the authoritative curse flag through `GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0)` after player globals are initialized; do not cache it across loads or infer it from `LysandusSatisfied`.

Next acceptance work: city admission and scale classification; actual wall refuge survey; dedicated city save/reload and unresolved/resolved nighttime tests.

## Curse policy foundation, 15 October 2026

## Exterior wall refuge survey, 15 October 2026

`TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -RefugeProbe` loads only the dedicated exterior fixture, teleports that disposable session to Daggerfall, and performs the existing collision-checked TownGeometry route search. It does not save, enable city actors or modify quest globals. The live player completed successfully: 100 accepted ground-level Town23 entrances, all100 connected from another entrance within100 metres;59 other doors were rejected by the general entrance survey. This does not mean59 wall doors failed, nor does it prove paths from every city post. Evidence: `work/DaggerfallRefuge-exterior-pass.log`.

The original-content wall census records Town23 models444/445/446 with real interior records; the detailed candidate keys/models are in `work/DaggerfallCity-walls.log`. These model IDs and the Town23 type are evidence for this original layout, not a universal classification for city-overhaul mods. Next verify representative actual interiors, safe standing positions and physical guard arrival/retreat before using them as refuges.

The probe is opt-in and retains disabled test input/mouse capture. Owned test process34228 finished and was stopped by TownTest; no user session was interrupted. Unity build passed; latest development0.3.29 bundle hash is `E11E69AA2685B2E47AAC85461CCC164C5D6C68AB175D97CA5B5ECA4C7393B674`, superseding the policy-only build hash below. City admission remains disabled.

## Curse policy evidence

`SettlementHaunting.StreetsDangerous` now evaluates the verified city ID, current LiftedCurse flag and absolute game seconds. Tests cover exact 19:00/05:00 boundaries, midnight on distant days, immediate curse resolution, other settlements and invalid timestamps. Red run: 244/247 passed with three expected missing-implementation failures; green run: 247/247 passed. `GuardNightPatrols` reads the live player global each update and applies the policy before starting patrols; active patrols follow the existing physical return behavior when disabled. This return is to their existing posts, **not yet a wall refuge**.

Unity build/integration checks passed and installed a development rebuild retaining version0.3.29, SHA-256 `1549B2A6189E0A80B21E766ECBDA59722BA464F5AB31F576A453D4A712AEEC71`. Evidence: `work/Haunting-red.log`, `work/Haunting-green.log`, `work/Haunting-build.log`. Daggerfall remains excluded by runtime admission gates, so this is policy wiring, not verified city gameplay or completed haunting behavior. No live city quest-toggle/save test has run. Base-game tracked Assets/Scripts remains unchanged.

Verified shipped source: `Player/DaggerfallUnity_Data/StreamingAssets/Quests/S0000977.txt` (Curse of Daggerfall) ends on global `LiftedCurse`, table ID0 in `Tables/Quests-GlobalVars.txt`. Its daytime task is 05:00–19:00; haunting danger is 19:00–05:00. `LysandusSatisfied` is a different global and must not substitute for the curse gate. The player requests Daggerfall city as a second supported test settlement on 14 October2026; runtime enablement and disposable city saves remain pending.

Latest user decision,14 October2026: while the main quest's Lysandus haunting remains unresolved, Daggerfall city disables night guard patrols by default. This supersedes the earlier plan to keep regular night patrols running under fear. Existing exposed guards must flee a visible haunting threat into reachable city-wall refuges rather than fight the ghosts and accumulate corpses. Refuges require actual entrances and safe physical routes; never teleport a guard into a wall or invented building. Stationary duty, gates and indoor staffing remain separate from patrol enablement.

Town locals may use taverns in daylight and early evening, but visits and returns must finish before the actual nightly ghost activation boundary. No ordinary late-night party crowd or tavern-to-home traffic runs during the haunting. Existing paid traveler beds and indoor tavern services are not canceled merely because streets are dangerous. The actual main-quest state that resolves the haunting re-enables normal nightlife and guard patrols; do not infer resolution from date, player level, unrelated quest completion or temporary absence of ghosts.

City gameplay integration remains pending under M5/M7. Current runtime support is Gothway Garden only. Before enabling Daggerfall-city simulation, survey real wall refuges and test unresolved/resolved states, exact onset, family/traveler safety, guard flight, unloaded time and save/reload. The policy above is not an implemented visible-ghost detector.
