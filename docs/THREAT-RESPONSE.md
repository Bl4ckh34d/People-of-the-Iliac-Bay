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

## Source-only indoor defense foundation

Building dispatch and queued building reports now accept a separate enemy target. Validated copy retains that target through exterior travel, delayed doorway entry, interior observations and physical return. A report cannot switch between criminal response and enemy defense or combine enemy defense with crime-scene investigation.281/281 core tests pass (`work/IndoorDefense-green.log`). This is not yet wired into the runtime or installed bundle; indoor guard enemy response remains unfinished.

## Daggerfall doorway-post report

The player reported a guard standing facing a house door, then observed the guard run to city-wall shelter when a ghost appeared. SAVE50 stores guard `g:1291010263:n:4` as `Posted`, with no refuge/interior and post `(139.824996948242, 703.400024414063)`. The current player log later records that guard taking refuge in building67329. This supports a stationary doorway post followed by working ghost flight, rather than proving a stuck patrol. Current post selection samples resident commute starts, which are building entrances. Post selection needs an offset to reachable clear space; existing saved rosters need safe migration without renaming guards or rewriting active journeys. That placement correction is not implemented yet.
