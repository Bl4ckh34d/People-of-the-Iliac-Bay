# Witnessed guard pursuit through a real building door

Date: 26 October 2026. Status: source implementation and regressions verified; full native entry/pursuit acceptance remains open. No Unity session, package build, installation, model call, commit, push, or vault write was performed by this worker.

## Implemented boundary

An already assigned guard can follow a managed resident through the exact building door only when the guard's loaded body sees that resident at the actual crossing. The adapter checks the existing target identity, active/alive body, invisibility, same scene, native guard field of view and sight radius, and existing collider line of sight. The target must be within 0.65 m of the native door's exterior ground. The native door centre is not its floor: the adapter reuses `StreetPopulation.FollowExteriorDoorGround`, including the real owner's transform and the lower edge of the door.

The observation is individual `GuardDoorPursuit` evidence. It does not modify the shared crime report's building or position, recruit another guard, or give other guards entry knowledge. Existing roster identities and the six-participant ceiling remain in use. The witness follows the existing surveyed exterior route from its actual captured position, with a surveyed return to its own post. The exact settlement/building/block/record/door identity must match again before entry. The ordinary building-entry API cannot bypass this exact-door check.

Only the matching loaded interior supplies a landing. No room position is stored in the doorway observation, and no unloaded interior is simulated. The guard uses existing native defense combat after actually seeing the same living target in that room. If the target is hidden, the guard has no new position; it can search only a point actually seen after entry. With no interior sighting, it waits at the known landing. A dead, removed, or otherwise ineligible native target releases combat and leaves only the bounded last-seen search. This does not infer an unseen death or create cleanup evidence.

The entry observation starts a 30-game-minute search window. A later actual indoor sighting restarts the guard's lost-track window. The saved entry observation itself remains historical evidence. Expiry returns the guard through its existing trail and surveyed exterior return. Wanted identity remains in the existing murder evidence. Native player assault detaches only the assaulted guard to its own existing-style assault report; it does not rewrite the other guards' suspect knowledge. No arrest, prison, transport, new role policy, or global police simulation was added.

## Resident refuge and context

The previous lawful-flight dictionary vanished when its exterior body disappeared. A real shelter arrival now records a small optional shelter on the existing `DefenseCombatant`, using the existing `AlarmPerson` position/reaction structure. This is the resident's private physical state, separate from guard knowledge. It records the exact door and whether a real loaded landing has been observed. Save/restore retains the refuge before landing and the actual local position after landing. It creates no alarm against a lawful guard and no guard report. A dead resident clears the shelter through the existing defense-death path.

An unobserved landing cannot be projected into another room or through another door. A restored sheltered resident with no visible pursuing guard holds its recorded ground and standing pose. It does not fall back to fleeing the innocent player's position. Visible pursuer checks use the resident's own existing `SeesSceneEvent` facing/LOS, independently of the guard's sight. A genuine known danger can still use existing indoor flight movement.

After the shelter window expires, an observed resident continues holding in that room rather than visibly jumping to a schedule location. Once that room is unobserved, the existing offscreen routine projection can resume. This slice does not add a new visible post-shelter exit/return journey. Both current-journey context and the legacy conversation plan describe the actual loaded refuge and interrupted routine. They state whether a pursuing guard is currently visible; they disclose no unseen guard position.

## Changed source

- New `src/PeopleOfTheIliacBay.Core/GuardDoorPursuit.cs`: individual exact-door evidence, approach/entry admission and per-guard assault reassignment.
- `Guards.cs`, `GuardInteriors.cs`, `GuardRefuges.cs`, `DefenseCorpseCleanup.cs`: saved validation, physical route/return lifecycle, observation deadline and clearing at actual ownership changes.
- New `src/PeopleOfTheIliacBay.Core/DefenseShelter.cs`; `DefenseIncidents.cs`, `DefenseSave.cs`: resident-owned refuge persistence and truthful activity text.
- New `mod/Scripts/GuardDoorPursuit.cs`; `GuardPopulation.cs`, `GuardEnemyCombat.cs`: native observation, exact door resolution, loaded landing and native combat integration.
- `StreetPopulation.cs`, `StreetPopulation.LawfulGuardFlight.cs`: real shelter arrival, resident continuity, actual landing and safe hold.
- `PeopleOfTheIliacBayMod.ContextSnapshot.cs`, `PeopleOfTheIliacBayMod.DialogueLife.cs`: reuse existing journey/plan context for the refuge.
- The cooperating Enter/Wait worker added the callback in `StreetPopulation.BuildingActions.cs`, after the transition and source position commit and before source-body removal. Optional guard observation contains its own exception boundary so it cannot strand a committed action.

## Verification

Commands run from `C:/Users/Daniel/Desktop/Code_Experiments/DFU/LivingCities`:

```powershell
dotnet run --project tests/GuardDoorPursuit.Regression/GuardDoorPursuit.Regression.csproj --no-restore
dotnet run --project tests/GuardDoorNative.Regression/GuardDoorNative.Regression.csproj --no-restore
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -v minimal
dotnet run --project tests/PeopleOfTheIliacBay.Tests/PeopleOfTheIliacBay.Tests.csproj --no-restore
```

- Core pursuit/refuge/context regression: **63 assertions pass**. Covers unseen/wrong identity rejection, unchanged other guard/report knowledge, actual approach progress, early/wrong-building/wrong-door entry, legacy API bypass rejection, no invented interior target position, 30-minute expiry, later actual sight, lasting murder identity, exterior/interior death, cleanup replacement, native assault reassignment, private refuge restore, exact loaded landing, held position and truthful context. It includes real FullSerializer JSON file round trips in its isolated output directory.
- Actual-source native-adapter fixture: **16 scenarios pass**. Roslyn structurally extracts the production observation, native ground helper, resident visibility, refuge hold and interior target-loss methods. Controlled native-shaped adapters supply a three-metre-tall door on an elevated translated owner, loaded entities, sight/obstruction facts, and routes. It covers backward/occluded/invisible observers, unknown door, remote entrant, unreachable route, malformed optional state, resident-versus-guard sight direction, invisible pursuer, restored hold, real-danger non-suppression, wrong room, and dead/removed target release. This is executable source regression, not a Unity physics or native animation acceptance claim.
- RuntimeCompile: **0 warnings, 0 errors**.
- Shared Core suite: **2147/2147 passed, 0 failed**.
- Scoped `git diff --check`: no whitespace errors; Git reports existing LF-to-CRLF normalization notices on touched files.

Observed RED results included the absent entry handoff, legacy entry API bypass, three-metre door-centre rejection, missing persisted refuge owner, a changed saved suspect identity, and the absent per-guard assault handoff. Each was followed by a passing regression after its production correction. Later observer/hold/target-loss cases exercise the corrected actual methods; they are not claimed as completed Unity reproductions.

## Bounded native acceptance procedure — not yet run

Use a disposable `PeopleOfTheIliacBayTest-*` character and a loaded settlement with a surveyed reachable public building. Do not mark this item accepted merely because the existing `guard-assault` probe passes: that probe establishes sourced native attacks and lawful flight but does not exercise this new door seam.

1. Establish a real managed resident assault and native guard pursuit using the existing assault/identity adapters. Retain the resident and guard IDs, shared report, wanted evidence and roster count. Choose the actual surveyed shelter door (or the already bound Enter action door), not a constructed room key.
2. Observe the resident physically reach that door. Keep one assigned guard facing the resident with unobstructed native LOS; keep a second assigned guard facing away or behind real geometry. Do not call the observation method remotely to manufacture a crossing. Check `PEOPLE_OF_THE_ILIAC_BAY_GUARD_WITNESSED_DOOR` identifies only the real witness and exact door. The other guard and shared report must gain no building/room position.
3. Sample the witnessing guard's actual body and saved route until door arrival. Require physical progress, no one-frame jump across the approach, the original guard identity, unchanged roster count and no seventh respondent. Check that the native door centre/floor difference does not reject entry.
4. Enter the same real building through the matching door. The resident's source body must be gone outside; resident and guard land only from the real interior geometry. Check that an unrelated building or another entrance does not materialize the pursuer. Check native collision/landing spacing, subsequent movement and animation.
5. Keep the resident visible inside and confirm existing native combat targets that resident and can cause actual damage. Then obstruct sight: no new target coordinates may appear. Remove or kill the target through native gameplay and verify combat stops pinning that target; any remaining search uses only the last actually seen point.
6. Repeat with an unseen exterior crossing. No guard enters or obtains an interior coordinate solely from the resident's saved shelter. While sheltered with no visible pursuer, observe standing/idle behavior and open Talk: actual refuge and interrupted routine must appear without claims about unseen guards or flight from the innocent player.
7. Save and reload once after exterior crossing but before the interior is loaded, and once after physical indoor movement. Check resident private refuge, exact witness evidence, stable identities, no exterior duplicate, and no invented room position. These native disk/body checks remain distinct from the FullSerializer regression.
8. Lose the trail for 30 game minutes. Check active search returns/ends while wanted identity survives; a later actual sighting may use the existing reacquisition adapter. While the refuge is watched after its window expires, check it holds safely. Leave the room and verify ordinary offscreen recovery. A new visible refuge-exit journey is outside this slice.

Required remaining evidence: native streamed doorway entry, actual loaded landing/spacing, real collision/FOV/occlusion, natural pursuit timing, native attack/death release, native disk reload, context in Talk, and search return/reacquisition. No such full native acceptance is claimed here.

## Opt-in native probe prepared — execution pending

Follow-up source: `mod/Scripts/GuardDoorNativeProbe.cs`, plus the two-line `guard-door` dispatch in `NativeNavigationTrial.cs`. Production pursuit behavior is unchanged. The probe is gated to the exact environment mode and an owned `PeopleOfTheIliacBayTest-*` character. The main agent owns all native runs and deployment.

The fixture requires a loaded exterior, two existing posted guards and an unoccupied living ordinary resident. It selects an existing surveyed primary shop, Tavern or Guild Hall door accessible at the current game time. It validates actual door floor/owner transform, native supported collision-clear staging points, LOS and surveyed exterior approach/return routes. It does not invent a door, force open a business, alter game time or spawn a replacement identity. If these prerequisites are missing, it fails rather than silently changing the scenario.

Controlled setup places the existing bodies, assigns the two guards to one shared exact-target report, and delivers a native resident-to-player 1 HP sourced hit followed by a native guard-to-resident 1 HP sourced hit. Both victims require actual health greater than one. These are staged source events, not naturally emergent aggression. Ordinary lawful-flight movement and the production shelter-arrival callback must produce the crossing. The probe never calls the doorway observation or Core pursuit admission methods directly. An early-update fixture-only safety component suppresses guard damage, archery and motor movement, including on the new streamed guard body; production exterior route placement and native senses remain in use. This proves native target assignment, not a subsequent native combat hit.

`witnessed` faces one assigned guard toward the entrant and the other away. `unwitnessed` faces both away. Each checks the actual native FOV/LOS condition before movement. The probe requires multiple moving samples, over one metre of resident travel, actual door-floor proximity, disappearance of the exterior body, exact individual evidence, unchanged shared-report room knowledge, and exactly one or zero following guards. In witnessed mode the guard's actual body must advance to the same doorway and become hidden only at arrival. Native door height, travel, largest sampled frame displacement, identities and the door ID are logged.

The fixture then uses the existing native player building transition. It checks actual matching resident/guard entry ground and absence of the other guard. To isolate indoor reacquisition from coincident entry bodies, the witnessed guard is explicitly staged on a collision-clear, visible, reachable native room anchor. Actual native sensing must reacquire the resident identity. A controlled resident-facing-away check verifies truthful refuge context and two seconds of stationary hold without fleeing the innocent player. Finally, deactivating the actual resident body must release native combat while preserving the bounded deadline and last seen point. These controlled indoor checks are not evidence of naturally emerged room positioning.

At the exterior and loaded-room stages, the probe calls the actual mod save capture, FullSerializer JSON round-trip, and restore into a detached `WorldStateStore`. It checks per-guard entry evidence and the resident's private refuge/landing. It never replaces the live world with that snapshot. This is **live save-copy evidence, not game disk save/reload**. Cleanup restores surviving health and changed player/controller/component settings, removes fixture flight/shelter and the fixture report, and restores only the two fixture guard records. It never resurrects a dead body. Actual sourced-event history can remain in this disposable run. Ordinary world records are not replaced wholesale.

Run through the existing launcher, once per mode, after the parent packages the frozen source. Use a daylight exterior fixture with at least two available guards; the fixture below is an existing launcher-supported disposable character/save pair, not a promise its current snapshot satisfies the prerequisites:

```powershell
$env:POTIB_NAVMESH_TRIAL = '1'
$env:POTIB_ROADMAP_PROBE = 'guard-door'
$env:POTIB_GUARD_DOOR_MODE = 'witnessed'
Remove-Item Env:POTIB_NAVMESH_INTERIOR -ErrorAction SilentlyContinue
& 'C:/Users/Daniel/Desktop/Code_Experiments/DFU/LivingCities/tools/TownTest.ps1' -OutdoorNavigationProbe -FixtureCharacter 'PeopleOfTheIliacBayTest-9a5f197b' -FixtureSaveName 'People - Gothway Garden'
# Separate fresh run with POTIB_GUARD_DOOR_MODE='unwitnessed'.
```

Required markers: `PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_EXTERIOR_PASS`, `PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_NATIVE_PASS` with the requested mode, cleanup marker, and outer `PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: roadmap=guard-door`. Preserve each run's full log separately. A stage or generic startup marker is insufficient.

Source verification after final probe edits:

```powershell
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -p:BaseIntermediateOutputPath=obj/guard-door-probe/ -p:OutputPath=bin/guard-door-probe/ -v minimal
git diff --check -- mod/Scripts/GuardDoorNativeProbe.cs mod/Scripts/NativeNavigationTrial.cs
```

Result: **0 warnings, 0 errors**; scoped whitespace check passes. No native probe execution by this worker. Outstanding beyond this bounded probe: naturally emerged pursuit and room movement, actual combat damage/death, physical occlusion/alternate-door rejection in Unity, Talk UI presentation, game disk reload, deadline return and later reacquisition. Native source freeze handed to the parent; no shared top-level documentation or Obsidian vault write. The reusable probe procedure is the capture candidate for parent-owned documentation/memory maintenance.
