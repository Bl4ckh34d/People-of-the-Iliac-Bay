# Indoor witness escape and reporting

This implements the already approved physical civilian flight/reporting behavior. A direct fleeing generated witness uses a collision-checked interior route to the entry landing, followed by a previously surveyed exterior route to a living posted guard. Preserve identity, game-time progress and the original crime building across save/load. Dispatch the existing finite roster only after a living witness reaches a guard; never infer the player's new location from the report.

The current entry-connected interior survey becomes a navigation tree. It provides routes back to the doorway, rejecting unsupported floors, blocked segments and disconnected rooms. Missing routes retain existing conservative room retreat and indoor native alarm behavior. Unwitnessed murders retain that existing behavior until the separate discovery/occupation system replaces it.

Implementation sequence:

1. Add and test a serializable civilian escape plan with absolute timing, interior and exterior coordinates, and dead-witness suppression.
2. Retain the interior survey's parent links and expose validated exit paths.
3. Connect direct witnesses, delayed guard reporting and scene-specific actor placement. Use cached exterior paths while inside.
4. Add an actual-player probe: native kill, no premature response, physical interior escape, reload continuity, delayed guard report and original building destination. Re-run existing civilian and building guard regressions.

The first implementation uses entry-connected floors and existing sprites. Multi-level disconnected spaces, movable obstructions, original stationary NPCs, and corpse discovery remain explicit follow-up work. Do not claim all-interior coverage from one test house.

## Implemented behavior (0.3.9)

Direct fleeing witnesses receive a persistent `CivilianEscape` containing interior XYZ waypoints, an exterior route to a living posted guard, and absolute departure time. The scene survey retains its navigation tree; exit paths follow its checked segments. Outdoor paths reverse the existing guard-post-to-building survey. Actors retain their identity across the door transition. The same timeline continues while that scene is unloaded and after a native save/load. Dead witnesses do not move or report. Witnesses wait at the post once they arrive; automatic return home after the incident expires remains a later refinement.

For generated indoor victims with a valid fleeing witness plan, the scoped weapon hook suppresses the immediate native alarm. Reports use the original building key and existing finite roster. All other indoor alarm cases keep the previous behavior, including unwitnessed kills and missing routes. This is deliberately not the finished corpse discovery policy. A busy or dead destination guard cannot receive the report; the witness waits for an available guard at that post during the remaining reporting window. Retargeting another post is not implemented.

The base alarm window stays 900 game seconds. An escape plan extends effective activity through its arrival plus 300 seconds, preventing a distant guard route from expiring halfway there. The actual test house required about 916 game seconds. Validated copies cache route lengths; ordinary alarm queries do not repeatedly allocate street-path arrays.

Core coverage: interior/exterior interpolation and timing, defensive copies, native-data validation, persisted progress, dead witness suppression, long journeys and expiry. The actual-player probe rejects a temporary overlapping obstacle, uses a native weapon kill in house 1, checks no immediate response, observes interior movement, saves/reloads SAVE32, follows the same named NPC outside, observes exterior movement, and verifies a bounded report to the original building. Travel deadlines are accelerated in the test; this is not a full real-time benchmark or a multi-layout certification.

Outstanding: stair/door coverage, dynamic obstructions, alternative guard targets, catch-up of newly dispatched guard reports after a whole-town absence, return-to-schedule transitions, unwitnessed discovery, six-hour crime-scene occupation, original stationary actors and remaining guard behaviors. No new art was generated.

## Verified release evidence

The final 0.3.9 bundle passes 154/154 core tests, native build/settings/serializer checks, and separate actual-player InteriorEscapeProbe, ResponseProbe, GuardBuildingProbe and GuardWitnessProbe runs. Logs are `work/<ProbeName>-039-pass.log`. The indoor escape probe writes SAVE32; existing regression probes update only their dedicated SAVE29/SAVE31 fixtures. A test harness race was fixed by waiting for the owned player process to exit before starting the next probe. Read-only Luna/high review identified a malformed-save gap for a fighting escape witness; restore now rejects that state, with a regression assertion. Upstream tracked source remains unchanged.

Installed bundle SHA-256: `C6A5D3105F03CAA727D0C205A99A6E25C6F81CE4BFACCFEE0C23748CCEB01283`. The full mod goal remains active.
