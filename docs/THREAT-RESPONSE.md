# Existing hostile attackers and civilian flight

Installed development version: 0.3.33. Bundle SHA-256: `BAAC0B339EB36574DBBCF1E73BC0560263C8A1DF8CF8EBF3E1FC647CF02B1932`.

Living Cities reacts to existing vanilla, quest and mod attackers. It does not create a separate random-incursion spawner. This supersedes the original random-event generation proposal.

Guards now accept active hostile quest-owned enemy bodies even when the native quest's `IsAttackableByAI` flag is false. The scoped defender explicitly targets that enemy without changing the quest flag. Dead, pacified, player-allied and City Watch bodies remain excluded. Existing exterior finite dispatch, native combat, search and return behavior apply; this is not full interior enemy-defense integration.

Awake generated residents and registered native merchants/staff react to a hostile enemy visible within 20 metres. They flee using existing escape/home routes, independently of whether guard enemy defense is enabled. A direct witness can warn at most two other residents; those recipients cannot relay further. Quest-protected residents and sleeping residents are excluded from this visual threat scan. Not every vanilla stationary service NPC is registered yet.

Threat incidents have a separate persisted `ThreatId`, with no invented murder victim, corpse or crime scene. Citizen danger reports can alert nearby available exterior guards without accusing the player or erasing an existing crime. Native merchant reaction identities survive serialization. Expired repeated threats clear old native reaction links before creating a new episode. Indoor enemy reports do not yet dispatch guards into that building.

## Verification

- `dotnet run --project tests/LivingCities.Tests -c Release`: 279/279 pass, including threat flight, bounded warnings, malformed restore rejection, serialization and renewed native response links.
- Native `GuardDefenseProbe`: actual damage to a quest-tagged hostile human with the quest AI flag preserved, creature combat, save/reload, existing crime retention and player-assault takeover. Evidence: `work/Threat-defense-regression-pass.log`.
- Native `CitizenThreatProbe`: a registered shopkeeper and a generated street resident physically flee a controlled hostile enemy while guard defense is disabled. Player remains uncharged; the generated threat survives native serialization and validated restore. Evidence: `work/CitizenThreat-live-pass.log`.
- Native `IndoorNoiseProbe`: existing hearing, waking, escape, saved state and finite guard arrival regression. Evidence: `work/Threat-indoor-regression-pass.log`.
- Native `RefugeProbe`: haunting flight from combat and patrol, three wall interior models, city population/save/reload/movement and regional fixtures. Evidence: `work/Threat-refuge-city-regression-pass.log`.
- Native `CrowdProbe`: opposing resident bodies retain at least 0.82 metres separation; inserted-wall checks verify no capsule penetration, without claiming route replanning. Evidence: `work/Crowd-safety-pass.log`.

The hostile actors in these checks are disposable test fixtures. A quest-tagged fixture does not prove every shipped quest or third-party quest's progression semantics.

## Movement limits

Rendered resident placement now checks local crowd spacing and swept physical obstacles. Opposing walkers pass with at least 0.82 metres between managed body centers in the native street fixture. Local steering remains outside inserted walls; it does not guarantee a replacement route around a newly inserted obstruction. Waiting or oscillation at a blocked route remains possible. Static merchant flight still uses its existing checked route rather than the new crowd body registry. Guard integration was deferred after it prevented a wall-post combat fixture from activating; guards retain their existing tested placement and native combat movement.

Dense-city profiling, narrow-door queues, moving attackers across interior transitions, complete stationary NPC coverage, sleeping threat awareness, indoor guard enemy response and lawful-defense welfare/loot dialogue remain unfinished.

## Indoor combat adapter checkpoint, 15 October 2026

The guard actor now supports native enemy combat inside buildings and captures the enemy in interior coordinates, including vertical position and the return trail. A controlled `CitizenThreatProbe` creates a disposable indoor defender and verifies actual damage to a hostile thief, a saved interior target, and no player charge or player hostility. Expected pre-fix failure: `work/IndoorDefender-red.log`; passing run: `work/IndoorDefender-green.log`. This validates the actor adapter, not automatic witness dispatch or a complete indoor defense encounter.

Building defense now tolerates an unavailable roster without dereferencing a missing report. Regression covers zero allowed responders, busy guards, and a successful later retry. All 287 core tests pass (`work/IndoorDefender-core.log`), as do the Unity build and exterior enemy-defense regression (`work/IndoorDefender-exterior-pass.log`). Installed development0.3.34 bundle hash: `E270863B7B0252FC9525DD2B11720A13D7B5E62FE7195E40449052B5626299AC`.

The automatic dispatch gap is superseded by the checkpoint below.

## Automatic indoor enemy defense, 15 October 2026

An escaped registered keeper now contacts an available finite guard at its actual post. The report retains the hostile enemy identity and dispatches only contacted guards, without charging the player. The guard takes its surveyed exterior travel time, enters the reported building, searches toward the witness location, and uses native combat once it sees the attacker. Disabled defense, an invalid target, or search expiry ends the response; existing player crimes remain actionable.

The native shop fixture verifies keeper flight, guard contact, delayed arrival, actual enemy damage, no innocent-player hostility, and standing down when defense is disabled. Evidence: `work/IndoorResponse-clearance-pass.log` and final-bundle `work/IndoorResponse-final-native.log`. The latter combined invocation completed the citizen fixture but rejected the unexecuted doorway fixture: this harness branch returns early, so doorway regression must run separately.

Two routing defects were reproduced: alarm ground coordinates were treated as actor-origin coordinates, placing the destination one metre below the floor; then civilian clearance admitted a route too narrow for the native guard's 0.4-metre capsule radius. Alarm conversion now preserves the actor-origin convention. The interior survey accepts actor dimensions, and guard movement uses its actual ground offset and CharacterController. Default civilian survey dimensions are unchanged.

Core tests: 289/289 (`work/IndoorResponse-core-final.log`). Unity build: `work/IndoorResponse-final-build.log`. Installed development0.3.34 SHA-256: `9EFE142996512076627218F79A852B02DDBD9D738FEF280EEA4B8A78350ABA91`.

Separate native doorway regression passes (`work/IndoorResponse-door-regression-pass.log`): existing crime response, same wounded guard physically pursuing outside, player spell retained across exit, and actual effect save/reload. Tests use only the dedicated fixture character and hidden owned processes.

Broader interiors, dynamic obstruction replanning, enemies crossing scene boundaries, player-assault takeover during defense, and welfare/loot dialogue still need dedicated acceptance tests or implementation. This checkpoint does not establish complete indoor defense coverage.

## Guard spell effects across transitions

Superseded by the verified [guard effect persistence checkpoint](GUARD-EFFECTS.md). The paragraph below records the original gap.

Doorway transitions recreate guard actors and retain health, identity and pursuit, but do not capture or restore EntityEffectManager bundles. Active spell effects can be lost. Required follow-up: preserve native effect state and remaining duration across interior/exterior transitions and save/load, verified with real cast-effect probes. Offscreen time must not restart or indefinitely preserve timed effects. This is an acknowledged gap, not implemented behavior.

## Source-only indoor defense foundation (earlier checkpoint)

Building dispatch and queued building reports now accept a separate enemy target. Validated copy retains that target through exterior travel, delayed doorway entry, interior observations and physical return. A report cannot switch between criminal response and enemy defense or combine enemy defense with crime-scene investigation.281/281 core tests pass (`work/IndoorDefense-green.log`). This is not yet wired into the runtime or installed bundle; indoor guard enemy response remains unfinished.

## Daggerfall doorway-post report

The placement and shop doorway-pursuit corrections are now in development0.3.34; see [verified post and doorway behavior](GUARD-POSTS-AND-DOORWAYS.md). The following paragraph preserves the original diagnosis.

The player reported a guard standing facing a house door, then observed the guard run to city-wall shelter when a ghost appeared. SAVE50 stores guard `g:1291010263:n:4` as `Posted`, with no refuge/interior and post `(139.824996948242, 703.400024414063)`. The current player log later records that guard taking refuge in building67329. This supports a stationary doorway post followed by working ghost flight, rather than proving a stuck patrol. Current post selection samples resident commute starts, which are building entrances. Post selection needs an offset to reachable clear space; existing saved rosters need safe migration without renaming guards or rewriting active journeys. That placement correction is not implemented yet.
