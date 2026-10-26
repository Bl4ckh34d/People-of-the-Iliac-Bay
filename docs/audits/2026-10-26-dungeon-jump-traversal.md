# Bounded dungeon jump traversal — 26 October 2026

## Scope and status

Implemented a short physical jump fallback for a resident whose requested nearby dungeon destination has no complete walking path. This is one slice of roadmap step 5, **Navigation erweitern**. It does not complete dungeon navigation, climbing or the Privateer's Hold throne-platform request.

Source integration and CPU checks pass. **No native jump, obstacle collision, landing, dungeon block or moving platform has been accepted in this task.** No Unity or Player process was started, stopped, built, installed or controlled for these checks. The existing funded-family Inn acceptance session remained untouched.

## Implementation

- `NpcJumpTrajectory` solves the descending ballistic crossing for an actual horizontal distance and height. It refuses a height or horizontal speed beyond the supplied capability and rejects invalid numeric inputs.
- `NativeDungeonJumpTraversal` obtains the resident's live native Jumping skill, Athleticism, Improved Athleticism and enhanced-jumping effect. It uses `AcrobatMotor.defaultJumpSpeed`, `AcrobatMotor.defaultGravity` and the native multiplier formula. It deliberately omits the player's forward-input boost. The resident's requested walking speed limits horizontal velocity. The native `AcrobatMotor` component itself cannot be reused because it owns player input and player-only motors.
- The fallback applies only in the currently loaded dungeon with ready native navigation. It requires a living, non-quest-protected, non-paralyzed resident who is not seated, sleeping or conversing. It is limited to a nearby requested endpoint, at most a 0.5 m drop and a 1.5 second planned flight. It does not search for routes, build links or invent approach positions.
- Both endpoints require observed static dungeon floor support across the body footprint. The landing must also remain on the actual NavMesh. Moving action surfaces and other bodies cannot establish a valid landing. The existing capsule dimensions determine the full arc checks; incompatible replacement bodies are refused.
- Before launch, every bounded arc segment passes capsule overlap and sweep checks. Native mobile trigger capsules count as bodies; ordinary non-body triggers do not. Full query buffers fail closed.
- During flight the same body moves through capsule sweeps, in bounded substeps and at most 0.45 m per frame. There is no assignment to the requested landing point, warp or rescue teleport. An obstruction stops horizontal movement; gravity remains active. Another body or moving surface cannot produce a successful floor receipt. Native fall-damage threshold/rate applies if a newly intervening obstacle causes an unexpectedly long fall.
- `ResidentActor.Place` and `NativeResidentNavigation.TryMove` preserve the airborne movement owner until physical landing. A stationary navigation obstacle is disabled during flight. Ordinary walking resumes from the resulting body position. A changed target does not redirect an already launched jump.

The root agent added the small `NativeResidentNavigation` hook to its separately owned file. The jump helper pauses the walking proxy only after all launch checks pass.

## Verification performed

Six focused Core tests cover same-height landing, descending raised-floor arrival, unreachable height/range, higher launch capability, lower landing and invalid capability/coordinates. The initial minimal stub produced five expected positive-behavior failures; the rejection case already passed. One unrelated paid-Follow test failed during that initial shared-source run and was subsequently repaired by its owning agent.

Final commands:

```powershell
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj -c Release --no-restore
dotnet run --project tests/PeopleOfTheIliacBay.Tests/PeopleOfTheIliacBay.Tests.csproj --no-restore
```

Results: RuntimeCompile **0 warnings, 0 errors**; Core **2144/2144 passed**. Core output is `outputs/dungeon-jump-core-final.log`. These checks establish arithmetic behavior and compilation against installed native assemblies; they do not establish Unity physics behavior.

## Native acceptance prepared, not run

`NativeDungeonJumpProbe.Run()` is available through `POTIB_ROADMAP_PROBE=dungeon-jump`. It requires an already loaded owned `PeopleOfTheIliacBayTest-` dungeon fixture and `POTIB_DUNGEON_JUMP_FIXTURE` containing JSON with:

- `mapId`: the actual loaded dungeon ID.
- `start` and `end`: `{ "x": ..., "y": ..., "z": ... }` coordinates relative to that dungeon root, identified from actual floor geometry.
- `jumpingSkill`: an explicitly controlled native test skill from 0 to 100; default 50.

No endpoint coordinates are supplied here because none have yet been observed for this jump. The probe refuses a complete walking route and refuses unverified floor/capability/arc input. It creates one disposable resident at the verified test start, invokes ordinary `ResidentActor.Place`, and checks actual displacement, apex, bounded frame movement, final floor arrival and rejection of an unreachable command without body movement. Fixture placement is test setup, not a production traversal. Pass marker: `PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_JUMP_PROBE_PASS`.

Required next native checks:

1. Identify a real short dungeon gap or raised disconnected floor within a test resident's capability. Run the supplied probe with those measured endpoints.
2. Verify a low ceiling, wall, occupied landing, thin ledge and moving surface are refused before launch with no body movement.
3. Exercise an obstacle entering after launch, pause/resume, changed request and scene teardown. Ensure gravity and collision remain physical, with no false landing or stuck-overlap success. A body that remains underneath can keep the jump active; no unsticking or route recovery is implemented.
4. Check save/load and ordinary gameplay invocation separately. Existing ordinary Follow still refuses dungeon transitions; this change does not claim to make a companion enter a dungeon.

The throne platform needs its own identified native action object, dialogue/action binding, boarding, waiting and riding acceptance. This implementation excludes moving landing surfaces and does not claim any of those behaviors.
