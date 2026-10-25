# Stationary attack witnesses — 26 October 2026

## Reproduced defect and repair

The exterior static-witness adapter scanned all NPC flats and checked distance and line of sight, but not sleep or facing. It could therefore add a sleeping or backward-facing person as a direct witness, with a corresponding assault memory. The newer general scene observer already rejected those states.

`ObserveExteriorStaticWitnesses` now uses the existing ordinary stationary-observer registry and its awake, active, current-world, facing, range and native line-of-sight checks. Both victim and attacker must be visible; invisible or inactive attackers cannot establish direct evidence. The same adapter handles the player-default and explicitly supplied attacker paths. It preserves the actual exterior witness position. The existing registry excludes replaced/retired flats and authored quest/action objects; this does not add quest-NPC adoption or new hearing behavior. Cached participants also avoid a complete NPC hierarchy scan on every attack, although this change has not been performance-benchmarked.

## Native evidence

`ProfileInEditor.ps1 -CountersOnly -ValidateSceneObservers` now includes the production attack-witness adapter. The fixture uses an existing market NPC, actual native player concealment, controlled native sleep state, and a real Unity cube collider. It writes artificial outcomes into detached world snapshots and restores the original binding, position and concealment without yielding inside a case.

- `static-attack-red` was stopped after exposing a fixture readiness error: attack verification ran before a mobile victim was available and repeatedly retried the expensive health checks. It is not defect evidence.
- With prerequisites separated, `static-attack-red-02` passes visible assault and fails the sleeping case: `witness=True; memory=True; expected=False`.
- After the production fix, `static-attack-green` passes ten cases: visible attack, sleep, attacker behind, victim behind, range, invisible attacker, inactive observer, wall occlusion, visible explicit source, and sleeping with explicit source. Each checks both direct-witness admission and the remembered assault. Visible cases also retain exterior position metadata.
- The existing mobile/static health observation and restore/rebind checks pass in the same run. The result is `complete`; no exception entries were found. Existing DET custom-material errors remain in the startup log and are unrelated to this fixture.

The explicit-source case supplies the real player entity through the non-default parameter. It verifies that adapter branch, not a complete monster attack. Actual damage/death, hearing, guard interviews, natural sleeping, all other producers, full save-file reload and all NPC categories still require their own acceptance.

## Additional measurement

Both red startups report approximately 3.1 seconds under `population-families`. That timer includes `HousingSurvey.Read` and `GetOrCreateFamilies`; the individual cause is not yet isolated. This is startup evidence, not a demonstrated loitering stall or a before/after performance improvement. The worklist retains that investigation.
