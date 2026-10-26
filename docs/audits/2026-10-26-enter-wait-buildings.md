# Exact public-building entry and Bank Enter/Wait acceptance

Date: 26 October 2026. This is a bounded extension of the existing roadmap adapter. Native Bank acceptance is still pending.

## Existing behavior and reproduced gap

The production adapter already admits public shops, taverns, temples, guild halls and banks through native `BuildingIsUnlocked`. It preserves the selected building, door and source/destination space through physical entry, timed Wait and routine recovery. This change does not add a second action or movement system.

`ResolveBuildingAction` validated the accepted `DoorId`, then used `geometry.Entrances[buildingKey]` to choose the walking target. That dictionary stores the building's current default entrance. An accepted door could therefore retain its original receipt identity while walking towards a different door after the default entrance changed. The resolver now obtains the exact accepted `StaticDoor`, uses the existing `FollowExteriorDoorGround`, and checks that the terrain endpoint is within the existing 0.4 m floor tolerance used by unplanned building return. The movement executor still requires physical arrival before committing the entry receipt. Unavailable or locked doors still fail the existing native check.

The separate witnessed-door agent requested one integration hook: after the entry receipt and source position are saved, but before removal of the exterior body, notify `GuardPopulation.ObserveResidentBuildingEntry` with the same actual door. That callback contains observation failures internally; it must not interrupt an already committed entry. Guard pursuit behavior and its verification belong to that agent's audit.

## Focused verification

Run from the LivingCities repository:

```powershell
dotnet run --project tests/NativeBuildingActionTarget.Regression/NativeBuildingActionTarget.Regression.csproj
```

The isolated test compiles the actual production `ResolveBuildingAction` method extracted structurally with Roslyn against controlled door/terrain adapters. It also compiles the actual Core source in its own output directory; it does not touch the shared Core build outputs. This test uses the installed .NET 9 SDK's Roslyn assemblies.

RED before the resolver change: three failures demonstrated using the wrong default door, retargeting after a default entrance refresh, and accepting an unsupported floor height. GREEN after the change: six resolver checks pass, including unavailable-door rejection, a small terrain-height difference and unchanged observed Exit behavior.

Three additional existing lifecycle scenarios pass against actual Core code: Wait completes only after the exact crossing time plus its duration; Stop before interior observation prevents a later Wait effect while retaining entry recovery; Stop during observed Wait preserves the exact building position for recovery. All three scenarios restore a real store snapshot and retain the original door receipt. These lifecycle checks characterize the reused contracts; they do not claim that the runtime physically walked a bank interior.

The integrated `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -v minimal` passes with zero warnings and zero errors, including the witnessed-door callback. The first compile identified a missing `DaggerfallWorkshop` import in the guard agent's changed `StreetPopulation.LawfulGuardFlight.cs`; that agent corrected it before the successful rerun.

No Unity editor or Player was launched by this task. No bundle was built or installed, no paid model call was made, and no commit or push was performed.

## Bank-specific native probe

The existing `-EnterWaitProbe` now requires a real native `Bank` which is not the resident's home. The fixture advances its owned disposable clock to noon, selects an ordinary living adult and uses the existing unlocked exact-door offer builder. A bank outside the bounded semantic catalogue can be offered directly by this controlled adapter fixture. The probe no longer pretends that passing a building name to the now text-independent candidate builder expands the catalogue. This tests native action execution, not free-language interpretation or willingness.

The accepted and completed receipt lines include `buildingType=Bank` and the real building key. An Inn, home or missing Bank cannot substitute. Failure to find a suitable Bank emits `PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_FIXTURE_GATE` and fails. Admission, physical routing, door completion, unchanged identity/inventory/vitals and the ten-game-second bound Wait remain required. The observer remains outside; no unseen interior coordinate is created.

After the coordinator builds and installs the frozen integrated sources, use the existing launcher:

```powershell
pwsh -NoProfile -File .\tools\TownTest.ps1 -EnterWaitProbe `
  -FixtureCharacter PeopleOfTheIliacBayTest-9a5f197b `
  -FixtureSaveName 'People - Morning daily routines'
```

Prerequisites: that owned disposable character/save exists; the save loads outdoors in a settled town with a surveyed, currently unlocked Bank and an eligible resident; the installed bundle contains this source revision. The fixture does not create a bank, resident, route or item. Its selection budget is 45 seconds, followed by a 180-second action budget. Allow about four minutes after town load; the existing launcher has a 600-second outer timeout for this mode. No extra probe flag, model setup or new harness is required.

Require all of the following from the same run:

- `PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_ACCEPTED` identifies the real Bank, exact door and task.
- `PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_ENTERED` records that exact door, physical crossing time and `playerOutside=true`.
- `PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_NATIVE_PASS` records `buildingType=Bank`, the same building/door, sufficient elapsed time and unchanged identity, inventory and vitals.
- The launcher exits successfully. A fixture gate, early exit or task failure is not acceptance.

## Remaining native checks

The automated probe covers offscreen waiting after physical Bank entry. For observed waiting and recovery, use one known willing ordinary adult and an actual named open Bank. Request entry and a five-minute wait through normal conversation. Confirm physical approach; enter that same door before the wait expires; confirm the same NPC lands at the surveyed interior entry ground and stays there. Its activity and commitment context must describe the current Wait and actual Bank, with no purchase, job, service or unseen room claim. Issue Stop, then observe physical exit and rejoining its routine; inventory and wallet must remain unchanged. Repeat with a closed/unavailable bank before acceptance and with interruption before entry. These native checks remain open.

Other public building types, different door geometries, raised entrances without compatible surveyed terrain, free-language candidate coverage, active family visits and disk reload during native execution are not accepted by this audit. No shared top-level documentation or Obsidian note was written by this subagent; the coordinator owns final integration and memory capture.

## Native Bank attempt and diagnostic follow-up

The coordinator ran the installed bundle beginning `639D5A2E7800` using the actual SAVE82 fixture `People - Morning daily routines`. The owned character name is 32 characters (`PeopleOfTheIliacBayTest-9a5f197b`); the coordinator fixed the launcher's previous 30-character validator separately. The earlier save-name example in this audit was stale and is corrected above.

`work/native-bank-enter-wait-02-player.log` preserves the Player log from this run; `work/native-bank-enter-wait-02.log` contains the launcher evidence. The adapter selected resident `s:1291010263:h:328960:r:0`, Bank `262414` (`The Bank of Daggerfall`), and exact door `door:1291010263:262414:769:14:0`. Enter failed with `The walking route remained blocked.` There was no crossing receipt and no Wait action. This is a failed acceptance run, not evidence that Bank waiting works.

Source review identifies two existing paths to that error: more than 20 seconds without 0.15 m body progress, or a graph waypoint without sufficient distance progress for 20 seconds. The old failure line did not distinguish these paths. The fixture also places the player at the selected resident's body position while gathering nearby conversation options. That is a concrete possible observer obstruction, but the original log contains no collider or distance proof of causation.

The diagnostic follow-up adds a read-only snapshot every five real seconds and at failure/timeout. Each snapshot includes actual foot and resolved/accepted target, current/nearest distance, total movement and maximum sampled step, player position and horizontal distance, pause/conversation state, native target/path/failure, nearby body colliders, and existing waypoint/crowd/support/clearance diagnostics. The existing MoveTo probe's diagnostic helper is reused, with native/graph flags and waypoint progress added. No actor, player, route, collision rule or arrival tolerance is changed. The coordinator must compile and rerun these diagnostic sources; they were not built or launched by this subagent during this follow-up.

## Explicit movement budget preservation

The coordinator's subsequent native runs separated two failures. Run03 demonstrated fixture-owned player overlap; the coordinator corrected observer placement using the existing `TryObserverGround`. Run04 then walked 257.322 m before its 300-game-second action deadline expired. Run05 requested 900 seconds for Enter in installed bundle `509FB25B9EEA`, but still failed at 150.502 real seconds after walking 258.157 m, 89.325 m short of the Bank. `work/native-bank-enter-wait-05-player.log` preserves that run. Its final progress snapshot shows no body overlap, active native navigation and no native failure. Wait never started.

The production task adapter discarded the requested budget in two places: task construction allocated 300 seconds for every untimed step, and dispatch reconstructed every untimed action with `DurationSeconds=300`. Changing only the probe's requested duration could therefore never extend the actual Enter action.

The bounded repair stores an optional `NpcTaskStep.ExecutionBudgetSeconds` for `MoveTo`, `Enter`, `Exit`, `Lead`, `Interact` and `ReportMurder`. Explicit values must be 10–900 seconds. Zero retains the existing 300-second adapter default. This field is separate from `DurationSeconds`, which continues to mean timed waiting, following, sitting or rest. Purchase, transfer, health and native-service timing remain unchanged. The task envelope now includes the selected movement budget, and dispatch preserves it. The existing eight-step and 86,400-second task caps remain in force.

Core copy and validation preserve the field. Dispatch and snapshot restoration require an explicitly budgeted movement action's deadline to equal `min(action.AcceptedAt + ExecutionBudgetSeconds, task.DeadlineAt)`. The existing conversation capability digest reflects public fields and therefore already binds the new budget; changing it after issuance invalidates admission. No new authority source, route estimator, universal timeout or save migration was added. The existing native action path still applies `ActionReturnFitsFixedCommitments` to the resulting action deadline before admission, so longer movement budgets remain subject to fixed appointments and protected sleep.

The isolated regression now extracts and executes the actual production `TryAcceptConversationTaskCore` and `DispatchConversationTaskStep` methods through Roslyn, alongside actual Core source. Only the final native action boundary, actor and geometry are controlled fixtures. RED reproduced Enter900 becoming300, acceptance of an invalid movement budget, and absence of the saved budget. GREEN passes six resolver checks, three lifecycle scenarios and four runtime timing scenarios. These verify:

- Enter900 dispatches with a 900-second deadline and no timed completion; a later Wait30 retains its own 30-second duration and existing grace.
- A physical crossing after 500 game seconds can complete within the accepted 900-second budget, then dispatch the observed Wait.
- Invalid explicit budgets are refused; zero retains the previous default; timed/service steps cannot acquire a movement budget; the overall task cap remains unchanged.
- Copy/restore preserve the budget; capability mutation fails; mismatched action deadlines fail both dispatch and restore.

Command: `dotnet run --project tests/NativeBuildingActionTarget.Regression/NativeBuildingActionTarget.Regression.csproj`. The isolated test passes. No shared Core build, runtime build, bundle, installation or Unity/native run was performed by this subagent for this timing change. Integrated compilation and native Bank acceptance remain with the coordinator.

### Observer obstruction confirmed

Native run `03` used installed diagnostic bundle `B15621A2C8AE`. Its preserved log, `work/native-bank-enter-wait-03-player.log`, shows the player and resident at the same horizontal position, zero movement throughout, `nativeFailure=occupied`, and `PlayerAdvanced<CharacterController>` as the overlapping body. This confirms that the fixture itself blocked the route before any Bank crossing.

The fixture now reuses the existing MoveTo probe's reachable observer point, 2–4 metres from the actual resident. Native collision and movement rules are unchanged. Bundle `FCE340795270` passed Unity compilation and package checks on 26 October at 15:46. Native run `04` starts 2.5 metres apart and walks 257.322 metres before its 300-game-second deadline expires, still 90.851 metres from the door. The complete log is `work/native-bank-enter-wait-04-player.log`.

The next diagnostic bundle, `509FB25B9EEA` at 15:51, requests 900 game seconds for Enter and allows 480 real seconds of observation. Run `05` nevertheless fails after 150.5 real seconds and 258.157 metres. `StreetPopulation.Actions.Tasks.cs` discards untimed `DurationSeconds` both when budgeting the task and when reconstructing its action, always using 300. This is a production task-timing gap, not another observer obstruction. Its repair and full Bank acceptance remain open. The complete log is `work/native-bank-enter-wait-05-player.log`.

## Native Bank acceptance after the budget repair

Root run `06` completes on installed bundle `E31822DEE3D6` (26 October16:22),
using the same owned character and `People - Morning daily routines` fixture.
The resident `s:1291010263:h:328960:r:0` physically reaches exact Bank door
`door:1291010263:262414:769:14:0`; closest sampled distance is0.212m, inside the
accepted0.250m arrival threshold. The actual crossing receipt completes Enter,
then the task runs Wait for10.006 game seconds. The player remains outside.
Identity, inventory and vitals are unchanged. Both native PASS and outer
TownTest PASS markers are present; the owned process and launcher terminate.

Evidence: `work/native-bank-enter-wait-06-player.log` (entry line1912 and final
pass line1914) and `work/native-bank-enter-wait-06-launch.log`. This proves the
same long native approach now survives the earlier five-minute task deadline,
crosses its bound Bank door and performs the subsequent offscreen Wait. It is
not an observed interior walking/Talk test, all-building acceptance, or a game
disk reload. The separate guard pursuit probe remains unaccepted.
