# Building guard response implementation

## 16 October: reacquisition and live recruitment

The user reported guards failing to resume pursuit after losing sight. Returning and posted guards were omitted from the controller's visibility-triggered engagement path. Guards now recognize a currently wanted player from their own prior player-crime response records, including after returning and save/load. Actual visibility and no invisibility are required. Uninformed guards and records of creature defense or neutral investigation do not grant this recognition. A posted guard gets a surveyed response route; a returning guard resumes from its current position. Cleared crimes do not trigger pursuit.

The full native controller regression passes with actual nearby recruitment, retained refusal during shared pursuit, wounded disk save/reload, search expiry, reacquisition after expiry and completed return, and posted self-defense (`work/GuardReacquire-runtime-pass.log`). The probe now preserves the current native surrender state on reacquisition rather than incorrectly assuming vanilla retains refusal after a complete disengagement/reload. Core 381/381 also passes (`work/GateReacquire-core.log`). This supersedes the pending live-recruitment status below. Dedicated gate validation remains separate.

## 16 October: nearby pursuit recruitment

User requirement: a guard chasing the player recruits other guards passed within roughly 10 metres, with slight variation. Loaded outdoor pursuit now checks contacts every 0.5 real seconds. Each candidate has a stable 8–12 metre radius derived from its saved appearance seed. Both guards must be living and outside; candidates must be available posted/patrolling guards, with no refuge or interior assignment. Creature-defense callers are excluded. Actual actor distance and visibility between guards prevent recruiting through walls. A surveyed path to the caller's last known suspect position is required.

Recruitment uses existing finite identities and native pursuit, preserves the caller's knowledge expiry, and does not reset the shared surrender flag. Callers are snapshotted to avoid instantaneous same-update cascades. Further pursuit can recruit more guards on later checks. The recruit receives the last known location rather than the player's hidden current position. Interior recruitment and a live passing-guard regression remain pending; the user's running game was not interrupted.

Verification: 379/379 core tests cover range boundaries, restore-stable radius, unavailable/dead candidates, expired pursuit, creature defense, finite identity count and saved pursuit knowledge/deadline. Unity build evidence: `work/PursuitRecruit-build.log`. This does not yet establish visual behavior in a live chase.

The native controller probe now exercises actual nearby recruitment after refused surrender and requires `LIVING_CITIES_PURSUIT_RECRUIT_PASS`. It checks inherited knowledge/deadline, finite identity count, native hostility and retained refusal. Initial dispatch assertions now count native reports separately from legitimate proximity recruitment. The repeated-attack loop excludes unassigned guards, because `BeginCombat` correctly rejects posted guards with no incident. Focused review identified that same fixture risk; the filter is included in the passing Unity build (`work/PursuitRecruitProbe-build.log`). The extended native probe is compiled but has not run while the user's game remains open.

Witness junction correction: surveyed polylines are split at existing route vertices and query points before shortest-path search. A branch touching the middle of a patrol segment now connects, and distances between interior points no longer detour through original endpoints. The regression failed before the fix and passes afterward (`work/WitnessJunction-red.log`, `work/WitnessJunction-core.log`). No arbitrary crossing point or unsurveyed shortcut is invented. Large-city query performance after this change still needs measurement.

## 16 October: surveyed witness routing

This supersedes the stationary-post-only routing limitation below. WitnessNavigation selects the shortest reachable route to an available post or current moving patrol position on cached approach/patrol paths. Escaped witnesses replan from their evaluated position every 24 game seconds until reporting, including when a target becomes unavailable. Dispatch still requires actual contact. Indoor escape preparation calculates this route only when an eligible witness needs it.

Verification: 377/377 core tests; native patrol selection/restore probe and full shop visitor/contact/inspection regression pass (`work/WitnessRouting-patrol-pass.log`, `work/WitnessRouting-shop-pass.log`). Final Unity build and integration checks pass and bundle is installed. One native route query measured approximately 6.6 ms. Full rendered moving-patrol interception, predictive meeting times, wall-separated contact and large-city performance remain unverified.

15 October witness retargeting foundation: CivilianEscape.RedirectExterior creates an isolated replacement street route from the witness's evaluated current position, preserves the original indoor exit and individual fleeing speed, and saves the redirect timestamp. It rejects pre-exit redirects and routes starting elsewhere. Historical saves default RedirectedAt to zero and retain original timing.362 core tests and Unity native serialization/build pass (`work/WitnessRedirect-core.log`, `work/WitnessRedirect-build-editor.log`, marker LIVING_CITIES_WITNESS_REDIRECT_SAVE_PASS). This API is not yet called by patrol targeting: predicted moving-guard interception, selection by reachable contact time, unavailable-target replanning and live contact validation remain pending. No live game test was run, following the user's deferral.

## Required patrol interception, 15 October 2026

Witnesses must compare reachable stationary guards with moving patrols and choose the earliest reachable contact. Use route travel time and the patrol's predicted motion rather than straight-line distance or its obsolete departure position. Re-evaluate when the target dies, enters another scene, becomes unavailable or cannot be reached. A report requires physical contact with a living guard; do not dispatch on an expired route timestamp alone. Persist witness identity, progress and target across scene transitions and saves. Required acceptance includes a closer patrol, a closer stationary post, an unreachable nearby guard, a moving-away patrol, target loss and indoor-player/offscreen contact. The current `WitnessRoute` explicitly excludes patrols and does not implement this yet.

## Witness timing clarification, 15 October 2026

`CivilianEscape` saves the indoor route to the exit, exterior route to a guard post, start time and individual fleeing speed. `ExitAt = start + indoorLength / speed * 12`; `ReportAt = ExitAt + outdoorLength / speed * 12`. The factor12 is the current simulation time conversion. New fleeing speeds are3.25–4.55m/s. Arrival must still find a living posted guard at the endpoint before a direct witness dispatches guards. Guards then traverse their own surveyed approach routes at2.8m/s with the same time conversion; interior response starts at physical entrance arrival. There is no separate conversation-duration delay.

Known limitation: `WitnessRoute` chooses routes to available stationary posts and excludes active patrols; it does not intercept moving patrols. Missing indoor or exterior routes prevent planning, and an absent guard at the endpoint prevents successful reporting. General alternative-guard replanning remains unfinished. Do not describe this as instantaneous reporting or completed dynamic guard discovery. Report flavor text is emitted only when dispatch succeeds, not when the attack occurs.

## 0.3.22 durable native building alarms

Native reported crimes now retain their original building when its approach is unavailable. `QueueBuildingReport` saves an explicit `AwaitingApproach` report with no entrance, route or responders. Save validation permits only an unassigned non-investigation building report in this state and rejects fabricated responders or partial destinations. Existing saves default the additive marker to false.

The active controller retries unassigned native building reports once per real second while the player's crime remains active. Once a surveyed approach and available guards exist, normal measured travel starts from the current game time. Reload or entering another building cannot retarget that saved report. Repeated callbacks reuse the saved pending or active report instead of issuing extra responder batches. Clearing the player's crime removes unassigned native reports; assigned guards retain their existing return behavior. Discovery scenes and civilian messenger knowledge retain their separate mechanisms.

This supersedes the earlier native missing-route report limitation. It does not add unsurveyed navigation, patrol pairs, torches, universal crime attribution or missing civilian escape routes.

Verification:198/198 core tests and Unity/native build pass. Actual-player GuardBuildingProbe tests repeated alarms without an approach, no immediate responders, cancellation after crime clears, real pending save/reload, another repeated alarm after reload, automatic route retry, timed doorway arrival, wounded guard persistence and return. `work/PendingGuard-reload-red.log` reproduces duplicate reports after reload before the reuse fix; `work/PendingGuard-0322-pass.log` records the final passing sequence. Read-only Luna/high review found that reload issue and found no remaining confirmed important issue after the fix.

## 0.3.21 ownership without a cached approach

An active finite roster retains ownership of its supported settlement's loaded building interiors before their exterior approaches are surveyed. Dungeons, other settlements and disabled controllers remain outside that ownership. Navigation readiness must not enable transient vanilla reinforcements or reveal an unwitnessed murder. Resident home deaths and registered native keeper workplace deaths use witness evidence and settlement ownership for concealment, preserving an existing unrelated crime.

Physical dispatch still needs an actual approach; no missing-route teleport or invented path was added. Departure-triggered concealed scenes can retry discovery as surveyed routes become available. Durable queuing/retrying of already reported non-murder crimes without a cached approach remains unfinished. This checkpoint does not claim that every known crime survives unavailable navigation.

The regression removes a real residential approach while indoors, tests concealment and ownership, restores the approach, then verifies timed arrival, cleanup, ten-second warning, native surrender/court, real save/load and one-hour expiry. A separate native sleeper regression removes its workplace approach during a lethal hit. Both probes restore temporary cache changes in `finally` even when assertions fail. The older missing-survey vanilla fallback statement below is superseded.

Verification: 196/196 core tests and the Unity build pass. `work/UncachedGuard-owner-red.log` reproduces lost ownership; `work/UncachedGuard-conceal-red.log` reproduces lost concealment after ownership alone was fixed. Final passing residential and native keeper evidence is in `work/UncachedGuard-0321-pass.log` and `work/Housing-0321-pass.log`. The latter also verifies the two-person/two-bed Kinghart household, preserved native head, native services, sleep/wake and real reload. Base DFU tracked `Assets/Scripts` remains unchanged.

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
# Repeated surrender fix, 16 October 2026

## Witness contact foundation, 16 October 2026

Exited witnesses are now eligible for contact checks along their exterior route, not only after its endpoint timestamp. `GuardRoster.GuardsInReportingRange` evaluates actual current guard positions (including patrols), within 0.75 metres, and excludes dead, indoor, sheltered or non-posted guards. Direct witnesses and neutral corpse observers use this check before dispatch. Hearing-only witnesses also use current positions rather than empty guard posts. New hearing reports persist the guard's contact position and validate it against the witness's saved route/time; legacy endpoint/post reports remain readable. Repeated neutral dispatch logging is suppressed after a populated report already exists.

Core 375/375 covers moving patrol versus empty post, guard death, roster restore, pre-endpoint hearing contact, report restore and corrupt contact rejection. Unity build, full inn alarm/inspection/replacement regression and full native keeper suite pass (`work/PatrolContact-core.log`, `work/PatrolContact-build-editor.log`, `work/PatrolContact-inn-regression-pass.log`, `work/PatrolContact-shop-regression-pass.log`). Those live tests verify existing reporting and cleanup paths; they do not demonstrate a rendered moving-patrol interception. Current `WitnessRoute` still selects a stationary posted destination. Closest-patrol route selection, prediction, retargeting after movement/death, robust wall-separated contact checks and complete offscreen encounter timing remain unfinished. This is contact handling, not completion of the requested moving-patrol routing system.

Opening the native surrender dialogue used to make `GuardPopulation.OwnsTown()` false because `IsPlayingGame()` excludes paused dialogue. The controller consequently hid its actors and called `EndCombat`. Vanilla then saw no hostile guards and reset `HaveShownSurrenderToGuardsDialogue`, reopening surrender after refusal. Ownership now depends on the loaded settlement, not dialogue pause; the existing update pause check freezes simulation without discarding actors or hostility. The shared vanilla surrender flag continues to govern all guards, without a per-guard replacement dialogue or base-game edit.

The real controller regression reproduces the old failure while the offer remains open, then passes with the fix: the actor stays active/hostile, No remains remembered across rendered frames, and repeated native attack calls from additional initialized guards do not reopen the offer. Health is replenished between test hits to exclude vanilla's separate lethal-hit forced-surrender policy. The complete controller suite also passes wounded save/reload, search expiry and posted self-defense. Evidence: `work/SurrenderRepeat-runtime-red.log`, `work/SurrenderRepeat-runtime-pass.log`, `work/SurrenderRepeat-build-editor.log`; core 372/372. The runner requires `LIVING_CITIES_SURRENDER_REFUSAL_PASS`.
