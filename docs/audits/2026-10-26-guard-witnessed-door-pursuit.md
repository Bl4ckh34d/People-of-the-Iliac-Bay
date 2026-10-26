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
