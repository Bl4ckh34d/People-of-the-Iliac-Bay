# Building guard response implementation

The approved behavior is physical reinforcement: shop alarms dispatch existing municipal guards, each travels to the shop door and enters on arrival. Distance produces the escape window. Saving, loading or entering another building must not reset travel, create guards, or move responders to a different incident.

## Persistence contract

The deterministic core now stores collision-checked exterior approaches, keyed by building, with an entrance and routes from guard posts. The scene adapter is responsible for surveying actual collision geometry. The core rejects unknown guard identities, mismatched origins or door endpoints, duplicate keys, invalid points and inconsistent active report destinations.

Building reports retain the destination and entrance independently of the cache. An incident cannot be redirected to another building. Repeated reports preserve responders and arrival times. Missing approaches dispatch nobody. Recruits can use a deceased guard's cached post route without inheriting its identity. Earlier schema-6 saves have an empty approach cache.

Core tests cover reload timing, defensive copying, missing routes, immutable destinations, malformed restoration and replacement identities. DFU's native FullSerializer round trip also retains the cache and nullable building key.

Version 0.3.6 connects the exterior survey to GuardPopulation. It maps reachable entrances and calculates one guard-post approach per frame, using TownGeometry's existing collision checks. Only completed reachable building entries are persisted. The coroutine pauses outside its active exterior scene or while the game is paused, and Clear cancels it. Existing cached entries are retained after reload; newly recruited guards can reuse their post's route. This does not yet activate indoor dispatch or indoor guard actors.

The actual-player GuardControllerProbe checks that a reachable approach is produced and survives the wounded-guard save/load. It also places a temporary blocker overlapping a route start and requires route rejection. TownGeometry now checks waypoint overlaps as well as segment sphere casts. The probe does not yet certify a completed whole-town survey, frame-time budget, or interior doorway arrival. Initial door discovery is still synchronous and needs profiling.

## Remaining adapter work

1. Finish whole-town survey and scene-transition acceptance checks, including frame-time measurements. Initial incremental survey and save/load are connected.
2. Bind an indoor native crime report to the current building key. Dispatch against saved exterior approaches and suppress instant native reinforcements only when the replacement owns that building.
3. Maintain separate exterior and interior coordinates for each responder. Only materialize inside after the exterior arrival deadline, at a collision-checked doorway landing. Preserve health and identity through the transition.
4. Search the reported interior, pursue only observed suspects, return through the doorway and resume an exterior route. Handle player escape before arrival, repeated entry, death, paused time and loading directly inside.
5. Actual-player acceptance: native shop alarm, nearby and distant arrival times, doorway entry rather than player-centered appearance, no duplicated guards, save/load while en route and indoors, escape and return. Keep the exterior and civilian regressions green.

Patrol pairs, torches, wall posts, attacks against other enemies and broader settlement coverage remain required afterward.
