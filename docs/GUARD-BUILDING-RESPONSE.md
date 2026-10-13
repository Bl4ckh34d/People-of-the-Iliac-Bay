# Building guard response implementation

The approved behavior is physical reinforcement: shop alarms dispatch existing municipal guards, each travels to the shop door and enters on arrival. Distance produces the escape window. Saving, loading or entering another building must not reset travel, create guards, or move responders to a different incident.

## Persistence contract

The deterministic core now stores collision-checked exterior approaches, keyed by building, with an entrance and routes from guard posts. The scene adapter is responsible for surveying actual collision geometry. The core rejects unknown guard identities, mismatched origins or door endpoints, duplicate keys, invalid points and inconsistent active report destinations.

Building reports retain the destination and entrance independently of the cache. An incident cannot be redirected to another building. Repeated reports preserve responders and arrival times. Missing approaches dispatch nobody. Recruits can use a deceased guard's cached post route without inheriting its identity. Earlier schema-6 saves have an empty approach cache.

Core tests cover reload timing, defensive copying, missing routes, immutable destinations, malformed restoration and replacement identities. DFU's native FullSerializer round trip also retains the cache and nullable building key. This is not yet connected to indoor gameplay; the installed 0.3.5 preview remains unchanged.

## Remaining adapter work

1. Survey reachable building entrances and routes incrementally while exterior geometry is available. Retain the cache through interior loads; complete missing surveys outside. Avoid per-frame whole-town pathfinding.
2. Bind an indoor native crime report to the current building key. Dispatch against saved exterior approaches and suppress instant native reinforcements only when the replacement owns that building.
3. Maintain separate exterior and interior coordinates for each responder. Only materialize inside after the exterior arrival deadline, at a collision-checked doorway landing. Preserve health and identity through the transition.
4. Search the reported interior, pursue only observed suspects, return through the doorway and resume an exterior route. Handle player escape before arrival, repeated entry, death, paused time and loading directly inside.
5. Actual-player acceptance: native shop alarm, nearby and distant arrival times, doorway entry rather than player-centered appearance, no duplicated guards, save/load while en route and indoors, escape and return. Keep the exterior and civilian regressions green.

Patrol pairs, torches, wall posts, attacks against other enemies and broader settlement coverage remain required afterward.
