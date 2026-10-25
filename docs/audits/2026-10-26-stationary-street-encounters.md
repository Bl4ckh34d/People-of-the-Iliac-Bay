# Native mobile-to-stationary street encounters

## Scope

`ProfileInEditor.ps1 -CountersOnly -ValidateStreetEncounter` runs a disposable copied city save through the owned Unity editor. It selects an existing adult resident whose current plan permits a pause and an existing awake stationary person. The ordinary seeded encounter probability and schedule-time checks must admit the pair. The fixture places the visitor at a surveyed, visible social position beside the stationary NPC; spontaneous route proximity is therefore not established by this test.

The ordinary `PrepareStreetChats` selector creates the encounter. Normal population updates own standing, expiration and route resumption. The player remains nearby so automatic player-facing would fail the heading assertion. Both current contexts must name the actual partner. A newly created shared event ID must appear in both memories; snapshot restore and recall must retain the participants' names.

## Evidence

- `street-encounter-01` verifies standing, partner-facing and both memories, but its departure measurement is invalid. The fixture advanced time to the end and accepted a 140 m reconstruction as movement. That result must not be used to claim continuous walking.
- `street-encounter-02` removes that time jump and allows the ordinary simulation to finish the encounter. Resident `s:1291010263:h:131842:r:0` speaks with stationary person `npc:1291010263:-1:2046650633`, retains partner-facing alignment 1, then walks 1.026 m with a maximum observed departure step of 0.0946 m. Both contexts, newly produced memories and restored recall pass. The editor exits with `result.txt=complete` and no exception entries.
- The final fixture additionally samples the exact frame that releases conversation ownership, so a jump on that boundary cannot escape the departure check.
- `street-encounter-03` passes that boundary check: 1.025 m resumption and maximum step 0.0667 m. After the acceptance marker, editor teardown exposes `TavernRoomSurvey.ReadRemoteSteps` calling `SetActive(false)` on an already-destroyed hidden owner. The inner temporary interior already had a Unity-lifetime check; the outer owner did not. The same check now protects the outer owner, while the remote-survey flag is still released.
- Final `street-encounter-04` additionally verifies that native navigation resumes the original travel destination within the ordinary 0.4 m tolerance. Actual resumption is 1.045 m, maximum frame step including the expiry boundary 0.0733 m, minimum partner-facing alignment 1. Both contexts, new shared-event memories and restored recall pass. `result.txt` is `complete`, the owned editor exits, and the log has no exception entries. The separate Unity singleton-cleanup warning remains; this is not a blanket shutdown-cleanliness claim.
- Core 2121/2121, RuntimeCompile with zero warnings/errors, source policy, 70 PowerShell syntax/no-regex checks, tooling protocols and whitespace checks pass.

## Limits

The tested native market sprite has no `DirectionalKeeperPose`: it remains at its post, but no new directional artwork or visible reciprocal turn is claimed. Where directional keeper art exists, production code turns it towards the visitor and restores its heading afterwards; that art path is not covered by this fixture.

The snapshot check does not replace a full scene unload/load or save-file round trip. Uncontrolled encounter frequency, occlusion/sleep interruption, conversations with quest actors and every market layout remain separate acceptance work. Production encounter selection is unchanged; the production repair concerns remote Inn-survey teardown.
