# Shelter replanning under pursuit — source checkpoint, 24 October 2026

## Behavior

A fleeing NPC can change course when an already known pursuer gets close and gains ground. Sight and facing are not prerequisites: the pursuer may be behind the NPC. This does not identify an unrelated nearby player as an attacker; the source must belong to the active alarm.

The current balance uses a 12 m near range and at least 0.5 m/s closing speed, with immediate danger inside 6 m. Samples are spaced by 0.5 real seconds and emergency searches by 2 seconds. NPC movement toward a stationary threat does not masquerade as pursuit. Source changes discard the previous motion sample. Pausing the game does not advance these decisions.

An emergency search considers accessible doors within 30 m. Destinations must lie roughly along the escape heading and never toward the pursuer. The existing whole-route safety test also rejects paths through the attacker. A broad forward cone permits street-side doors and bends; an actor facing toward the attacker falls back to the direction away from the attacker. The NPC retains a safe current route unless an alternative saves more than 1.5 m. If none is suitable, ordinary safe flight continues. Global and per-actor budgets prevent every NPC from performing expensive path searches every frame.

The new route starts from the current saved position. It does not teleport the NPC or lock a building merely because a route was selected. The existing refuge-arrival path seals the chosen building for one game hour, with the established earlier release when the murder is resolved and its corpse cleared.

Timed interior-witness routes to a guard also support diversion after reaching the exterior. `CivilianEscape.ShelterBuildingKey` preserves this route's destination through snapshots. The guard-route planner cannot overwrite a shelter diversion, and arrival at the door never counts as guard contact. At actual route completion, the NPC reaches the selected refuge and the lock is recorded. The original crime and witness evidence remain in the incident.

## Implementation

- Core: `ShelterPursuit.cs` and the shelter destination in `CivilianEscape.cs`.
- Runtime: `CivilianShelters.cs`, exterior escape placement in `StreetPopulation.cs`, and guard-report exclusions in `GuardPopulation.cs`.
- Existing arrival and door-lock integration: `ShelterLocks.cs` and `HouseholdDoors.cs`.
- Regression cases: `tests/PeopleOfTheIliacBay.Tests/PursuitShelterTests.cs`.

## Verification

Thirteen new pursuit/shelter cases pass. They cover rapid approach from behind, stationary/slow/distant/unidentified threats, source changes, cooldown, forward-door choice, safe rearward detours that must still be rejected, corner heading, route-copy ownership, lock only after arrival, guard-route diversion, no invented guard report, saved shelter destination and historical FullSerializer data without the additive field.

The current combined Core suite passes 1739/1739. RuntimeCompile and QuestHooks compile with zero warnings/errors. The temporary shared-build blocker from an unfinished dialogue class resolved when that parallel file became available; no dialogue behavior was changed for this feature.

No new bundle was installed and no native in-game pass is claimed. Native acceptance should cover a pursuer behind the fleeing actor, a faster approaching pursuer, doors on the wrong side, closed/inaccessible doors, actual exterior motion into the chosen refuge, the locked door, a fleeing interior witness and save/reload during the diverted route.
