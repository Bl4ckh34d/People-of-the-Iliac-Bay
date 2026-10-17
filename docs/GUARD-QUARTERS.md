# Gate sentry shifts and wall quarters

## Current requirement — supersedes sleeping below

16 October: user deferred guard beds. Outer sentries should cross before closure, enter the city-wall interior, patrol there until the next shift, and return in the morning. Inner sentries remain on duty. No automatic bed furniture is enabled. The experimental bed survey/probe is retained only as unfinished future work; isolated/loaded bed placement did not match and must not be used for assignments.

Implementation uses the persisted recall plus a wall-duty purpose on the existing doorway journey. The planned route follows the checked gate crossing, continues to a reachable wall door, and patrols back and forth along the final portion of a surveyed indoor route. Each living assigned guard reserves a separate wall interior for now. At dawn it finishes that circuit and reverses the entrance route to the inner staging point before daytime gate deployment. Fear-refuge release does not release this shift. Saved timing works without loaded exterior geometry.

Verification: 388/388 core tests pass, including JSON restoration, continuous indoor movement, dawn return, copied route isolation, invalid crossing speed and emergency priority without health reset. Native Daggerfall probe passes all eight finite outer shifts, pre-close crossing, next-day deployment, time advancement through the night while the player remains in an inn, a loaded wall guard moving along its patrol, and morning return. Final Unity build is installed. Evidence: `work/WallDuty-core.log`, `work/WallDuty-final-runtime-pass.log`, `work/WallDuty-build-editor.log`. Visually inspected `work/WallDuty-patrol.png` confirms the guard rendered in the wall interior. Earlier blank captures were test timing: native billboards need an Update after a sudden camera turn, and capture must finish before jumping to dawn. Native disk reload during a wall shift remains unverified; core JSON round-trip is covered.

Limitations: indoor patrol is a bounded back-and-forth route rather than a varied multi-room itinerary. Bed placement, sleeping and a broader shift rota remain deferred. If no reachable unreserved wall interior is found, the safe inner recall remains the fallback.

## Historical sleeping proposal (deferred)

User requirement, 16 October: outer gate sentries finish their shift before gate closure, enter city-wall quarters, and sleep in beds. The two inner sentries remain on duty. The current additional inner return points are staging destinations, not the final nighttime behavior.

## Verified constraints

`docs/data/bed-census.csv` contains 38 Town23 block/record layouts. Every one has zero bed models and zero rest markers, including WALLAA00–WALLAA11. Vanilla wall interiors therefore cannot provide the requested beds without mod-added furniture. Existing `RefugeGeometry` and Daggerfall refuge probes establish reachable floor routes, not sleeping accommodation.

Use existing classic bed models 41000–41002 as mod-added props. Do not edit game data or silently assign guards to invisible beds. Bed placement must preserve doors, stairs and walkable access. One physical bed holds one guard.

## Implementation sequence

1. Survey eligible Town23 interiors in isolation. Find deterministic floor footprints for beds, reject slopes, stairs, wall overlap and obstructed entrances, then validate a path from the entrance to each bedside after adding the prop collision geometry. Prefer quarters near the assigned gate. Cache the result by settlement/building and layout identity.
2. Persist each guard's quarter building, bed anchor, exterior route, interior route and shift times. Reserve beds across the finite roster; do not create extra guards to fill beds. Retain identity, health, effects and death state.
3. Extend the existing saved gate recall beyond its inner staging point into the selected quarters. Exterior and interior routes use separate coordinate origins with an explicit doorway transition, as in `GuardRefugeJourney`. Time advancement must work while the player is indoors or absent.
4. Add a rest purpose distinct from fear refuge. `ReleaseSafeRefuges` currently releases shelter when haunting becomes safe; it must not immediately wake ordinary off-duty guards. Sleep starts only after the guard reaches the assigned bed. Morning wake and the reverse journey precede the next gate shift.
5. Render the same guard asleep in the physical bed. Existing `SleepingPose.BeginNative` assumes a StaticNPC-style renderer/collider and cannot be applied blindly to GuardActor. Adapt presentation while retaining the guard entity, effects, damage and wake behavior.

## Acceptance

- Observe outer sentries crossing before closure, entering wall doors, reaching their own beds and sleeping when followed indoors.
- Inner sentries remain on duty; no duplicate guards or shared beds appear.
- Repeat with player waiting inside another building, saved mid-journey and saved asleep.
- Verify health/effects, death, emergency interruption, haunting and dawn return without teleportation.
- Test all distinct native wall layouts and reject unsafe bed placement rather than clipping furniture through walls or stairs.

Historical status at proposal time: saved pre-close recall and existing wall-refuge navigation were foundations. The sleeping sequence above is deferred by the current requirement.
