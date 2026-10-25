# Native scene observer visibility and restore boundaries

## Reproduced defect

The mobile `NativeNpcSceneObserver.BindTo` replaced its world-write epoch without discarding the old player-health observation. If a restore and rebind occurred before the next observer update noticed the invalid epoch, the next sample compared health across worlds. It could record an injury that the NPC had never witnessed.

`scene-observers-red-03` reproduces exactly this failure: the stationary observer passes, but the mobile observer reports `mobile fabricated an observed injury across a restore/rebind boundary`. The fix follows the existing stationary binding behavior: detach the old spell subscription and clear observation continuity when the binding changes. Rebinding the same body to the same current world epoch returns without clearing a valid observation.

## Native verification

Run the owned copied city fixture with `ProfileInEditor.ps1 -CountersOnly -ValidateSceneObservers`. It uses existing stationary and mobile bodies, native player health/concealment and actual Unity raycasts/colliders. The production observer's sample method is invoked at controlled instants; artificial results go into a detached world snapshot, not the world's event ledger. Player position, health and god-mode/concealment settings are restored afterwards.

Final `scene-observers-green` passes for stationary person `npc:1291010263:-1:2079293406` and resident `s:1291010263:h:131842:r:0`:

- Visible injury and healing create memories about the player without inventing a responsible person.
- A real collider blocks the observation. Returning into view does not create a retrospective injury event.
- Native invisibility, a target behind the observer, distance beyond the observation range and an inactive observer prevent the event.
- A controlled sleeping state blocks perception; waking establishes a new baseline rather than inventing an observation.
- An unchanged binding preserves a real observed health change.
- A restored world invalidates old observation authority. Rebinding establishes a fresh baseline, and subsequent visible changes are recorded normally.

The editor finishes with `result.txt=complete` and no exception entries. Core 2121/2121, RuntimeCompile without warnings/errors, source policy, PowerShell syntax/no-regex checks and tooling protocols pass.

## Fixture corrections and limits

Initial `scene-observers-red` failed because the shared harness enables god mode, which prevented native health changes. Run `red-02` then exposed a fixture-only missing reference: mobile `ResidentActor.Sleep` normally stores its lazily-created `SleepingPose`; the controlled sleep-state case must establish that reference as well. Neither failure established a production visibility defect. The later red/green pair uses the same corrected fixture.

This is a native perception and binding test, not a full save-file reload or a natural bed-entry test. Controlled samples bypass the one-second timer. Actual spell casts, theft, pursuit, distant scene identity and arbitrary authored/quest actors remain separate acceptance work; these health-event checks do not prove every event producer.
