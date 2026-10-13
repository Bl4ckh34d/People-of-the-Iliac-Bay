# Building guard response implementation

## 0.3.7 gameplay integration

Verified checkpoint: 150 core tests, native build, GuardBuildingProbe, GuardControllerProbe and GuardActorProbe pass. Evidence is retained in work/guard-building-runtime-pass.log, work/guard-controller-runtime-pass.log and work/guard-actor-runtime-pass.log. Clearing the player's crime now explicitly stops indoor combat and starts the doorway return, preventing visible guards from extending hostility indefinitely. The building probe checks this without forcing the deadline.

Surveyed Gothway Garden buildings now use the finite roster for native indoor alarms. Guards retain their street travel deadline and materialize at the interior entry landing only after arrival. Native combat starts on sight. Building-local 3D positions, last-seen positions, health and the path travelled indoors persist through direct interior reloads. On search expiry, guards retrace that path to the entry and then reverse their exterior approach to return to the post. Missing surveys still leave the interior on vanilla guards; visit the exterior with the preview enabled first.

The guard clock now explicitly promotes whole seconds and fractional seconds to double before addition. The former ulong-plus-float expression rounded DFU's approximately 12-billion-second absolute timestamp to 1,024-second increments. StreetPopulation's parenthesized fractional expression was already double and is unchanged.

The new focused command is tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -GuardBuildingProbe. It drives the native SpawnCityGuards entry point inside shop 131342, checks no premature guard body, advances game time to arrival, checks the entry landing and native combat activation, saves a wounded responder to SAVE31 (Living Cities - shop guards), reloads directly inside, and checks eventual exterior return. The time jump accelerates the test; it is not a real-time travel benchmark or an inventory theft interaction test.

Remaining acceptance: more interior layouts, stairways and blocked door landings; whole-town survey/frame-time budgets; full arrest dialogue; player escape before arrival and repeated scene changes during pursuit; active room-search movement when no suspect is visible. Patrol pairs, wall posts, torches and defense against other enemies remain required.

## Earlier implementation notes

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
