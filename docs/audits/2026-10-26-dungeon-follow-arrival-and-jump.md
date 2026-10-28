# Dungeon Follow arrival and real jump acceptance — 26 October 2026

## Delivered source change

The existing dungeon Follow owner treated any player within 1.8 metres in air distance as reached. That stopped a follower on the opposite side of a short gap or wall before its existing native walking/jump adapter could run. It could also request a jump directly into the player's occupied body.

`NpcFollowArrival.CanHold` now requires both the existing body-distance limit and an actual complete walking connection no longer than 1.8 metres. `NativeDungeonFollowTarget.Resolve` obtains that connection from the loaded NavMesh. A long route around a nearby wall remains a walking route. A disconnected nearby destination can use the existing jump adapter only after it validates an actual clear static landing. Candidate landings stay in the player's same short walking-connected destination area. The target resolver does not move the body, invent floors or links, or take movement ownership away from `NativeResidentNavigation` and `NativeDungeonJumpTraversal`.

The existing jump adapter, source doorway continuation, paid contract, inventory, jump capability, capsule sweep, gravity, and physical frame bounds remain unchanged. This is a bounded Follow correction, not climbing or throne-platform implementation.

## CPU and source evidence

Three focused cases cover a disconnected short gap, a nearby wall requiring a long detour, and actual short walking arrival, including invalid measured distances. Before the correction, the first two cases fail against the old air-distance rule. Evidence: `outputs/dungeon-follow-arrival-red.log` records 2180/2182 with exactly those two failures. The corrected source passes 2182/2182 in `outputs/dungeon-follow-arrival-green.log`. Runtime compilation against installed DFU/Unity assemblies passes with zero warnings and errors in `outputs/dungeon-navigation-runtime.log`.

These CPU results establish the arrival policy and compilation. They do not establish native collision, real jump movement, paid dungeon entry/exit, or save-file reload.

After the native BuildB snapshot, the existing Follow-transition and jump cases run again against the exact deployed Core source set: **24/24 passed**, including the new arrival and invalid-distance assertions. `outputs/dungeon-final-focused.log` records this check. Later concurrent market source is deliberately excluded from that snapshot check.

## Existing native jump probe extended

The existing `dungeon-jump` probe can now survey actual installed dungeon NavMesh and static collision rather than require manually supplied coordinates. Its opt-in fixture is:

```json
{"mapId":187853213,"enterPrivateersHold":true,"discoverFloors":true,"jumpingSkill":100,"verifyFollowTarget":true}
```

`enterPrivateersHold` reuses the existing module probe's native location transport and native entrance. This is controlled test setup, not evidence of walking across the world. `discoverFloors` samples actual triangle centroids and inset edges, requires static native floor collision, and uses bounded local pairs. Complete walking routes are rejected. The existing jump adapter independently proves the selected floor footprint, actual skill, range, capsule occupancy and full arc. It does not create geometry or navigation links. The fixture body can be repositioned during this explicitly logged setup; measured movement starts only after the selected takeoff has been restored.

The default `verifyFollowTarget:true` invokes the same target resolver as the production dungeon follower. The physical probe then invokes ordinary `ResidentActor.Place`; no direct jump start is substituted. `verifyFollowTarget:false` retains the earlier direct-endpoint adapter check. A bounded survey failure is a missing test fixture, not evidence that no valid jump exists anywhere in the dungeon.

## Native geometry correction and measured limit

The first native run enters the real dungeon but fails before the jump survey because the starting RDB's supposedly static geometry fingerprint differs. `work/dungeon-jump-native-01-player.log` records `missing-block-geometry:S0000999.RDB`. The test deliberately forces modular loading, so a whole-dungeon capture cannot mask this mismatch.

The old 13-source S0000999 captures include eight spheres at the same X/Z but different Y values across captures. Native loot-container prefabs use non-physical sphere triggers, and native random loot artwork changes their heights. The static input collector excluded bodies and moving doors/platforms but retained interaction triggers. Those triggers cannot support or block physical walking, yet they made unchanged dungeon geometry fail compatibility checks.

`NativeNavigationGeometry.IsDynamicNavigationBody` now also excludes `Collider.isTrigger` from static navigation input and its bounds. Solid colliders remain subject to the existing static/dynamic rules. Capture and runtime hashing use that same filter. Native capture logs **9 observed triggers, zero captured triggers and 8 retained physical sources across five RDBs**. S0000999 changes from 13 to **4** sources: three meshes and the actual solid box. Its unreadable mesh's native provider digest remains unchanged; the other four RDB content hashes remain unchanged. New S0000999 content SHA is `30896f8857416c9ce2ff1b0f792da6b6e8b612e810ee9aacba6ae83a7fbae64b`.

Evidence: `work/dungeon-trigger-filter-capture-player.log`. The source-stable second build uses the exact deployed 20:36:29 snapshot, without copying later market work. It bakes two updated assets (whole dungeon and starting RDB), reuses 94, passes Unity integration and installs bundle `934D6E533BC7FB753F47CA2532A61293F57FE0920C342F48DC4A4DC2A33086B6`, with identical distribution/Player hashes. Build evidence: `work/dungeon-trigger-filter-build-{a,b}-unity.log`; all 1013 deployed Runtime/Core source hashes are in `work/dungeon-trigger-filter-deployed-source.json`. The bundled runtime DLL source is compiled only from those deployed files; its DLL SHA is `0A33D566A8CBC847A6ED8ABA67060238B5B18A5C4741954934C51402E60CAE0C`.

In a fresh native process, jump run02 now loads **five real RDB modules and eight seam links**, with `runtimeBake=false` and the live geometry/provider checks enabled. This verifies the repaired fingerprint across processes. Its survey inspects **4771 actual static floor anchors and 5669 capability-bounded pairs**. All selected pairs have a complete walking route: **zero disconnected pairs**. The probe therefore refuses to invent a gap or force a jump. **No physical jump has been accepted.** The survey is bounded and does not establish that no possible jump exists anywhere in Privateer's Hold.

Evidence: `work/dungeon-jump-native-02-player.log`. Both test Player processes exit. The existing paid Follow round-trip/Stop probe remains separate from this controlled standalone body and from normal traveler admission.

## Follow round-trip retry04

The one authorized retry uses that same installed package, the existing exact SAVE18/Dirty Goat197386 warmup, and normal production city navigation (`MODULES_ONLY` disabled). It ends before hiring or dungeon entry after the existing 180-second prerequisite deadline. The former route blocker is gone: 19/19 Inns have actual routes, the target has four routes, no route is empty, and the actual seeded Mage exists. Its admission remains Pending, with no bed, because native Inn197386 survey reports **`Lodging bed disconnected from its own floor.`**

No hire, paid contract, staging pass or dungeon pass marker occurs; no traveler identity, booking or substitute bed is injected. This is now a specific lodging-fixture prerequisite failure, not missing traveler routes and not evidence for or against actual dungeon following. The root agent's separately successful Inn263174 admission does not establish the failed Dirty Goat layout. No further run is performed. Evidence: `work/native-dungeon-follow-04-{launch,player}.log`. Owned Player26096 exits and the native slot returns to the root agent. Dungeon entry/movement/return/Stop and save-file reload acceptance remain open.

## Remaining scope

The Privateer's Hold throne lift is a moving native action surface. The current jump adapter deliberately refuses moving landing surfaces. Boarding requires the exact native platform object, target/dialogue binding, a real landing, waiting and physical riding after the player activates the lever. None of those behaviors is claimed here. Climbing and general remote approach/jump planning also remain open.

## Owned files and handoff

- `src/PeopleOfTheIliacBay.Core/NpcFollowArrival.cs` — measured arrival policy.
- `tests/PeopleOfTheIliacBay.Tests/NpcFollowTransitionTests.cs` — three focused arrival cases.
- `mod/Scripts/NativeDungeonFollowTarget.cs` — native route/landing resolution.
- `mod/Scripts/RoadTravelActors.DungeonFollow.cs` — uses that resolver; existing movement owner retained.
- `mod/Scripts/NativeNavigationGeometry.cs` — excludes non-physical trigger input.
- `mod/Scripts/NativeDungeonJumpProbe.cs` and `NativeDungeonJumpProbe.Discovery.cs` — actual native floor survey and production target-resolution check.
- `mod/Scripts/NativeDungeonModuleProbe.cs` — shared existing native entrance and actual static-input assertion.
- `mod/Scripts/NativeDungeonNavigationCapture.cs` — invokes that assertion before capture.
- This audit, the appended follow-up in `2026-10-26-dungeon-follow.md`, and `docs/superpowers/plans/2026-10-26-dungeon-follow-jump-acceptance.md`.

Generated canonical navigation capture updates live under `work/navigation-geometry`; the previous manifest is preserved as `work/dungeon-before-trigger-filter-manifest.json`. The actual S0000999 source and whole-dungeon source have new content names; other RDB assets are reused. No shared host hook, `NativeResidentNavigation`, roadmap, commit or push is changed by this slice. Obsidian captures the verified result and the separate retry04 limit. All owned native processes have exited; native ownership returns to the root agent.
