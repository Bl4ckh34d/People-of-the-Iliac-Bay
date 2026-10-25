# Preserve the first observed native health change

This bounded repair implements the approved experience-event framework; it does not extend its semantic policy. Independent source review confirms that full-resource actors intentionally have no persisted vitals row. Native binding nevertheless applies and retains an exact baseline, so its first changed sample must not be mistaken for initial observation.

## Contract

Add `WorldStateStore.TryCaptureObservedNpcVitalsChange(object expectedEpoch, string id, NpcArchetype archetype, int previousHealth, int previousMagicka, int currentHealth, int currentMagicka, double capturedAt, long expectedRevision)`.

Only the bound native adapter supplies its retained previous native values. Require current epoch, identity, alive state, valid resources/time/archetype and exact vitals revision. Previous values must match initialized persisted vitals, or the canonical full-resource baseline when no initialized row exists. Reject mismatch without any state change. A changed health sample creates the same existing unknown-cause aggregate episode, including the first loss from full health. Magicka alone creates no health event. Ordinary `TryCaptureNpcVitals` remains an initial-snapshot API and must not infer damage from an initially low snapshot.

Reuse the existing capture reducer and atomic detached proposal; do not publish a temporary baseline row or add a schema field. No-change full-resource reads remain sparse. Keep skills, death handling, cause attribution, observer eligibility, receipt IDs and quiet-window scheduling unchanged. Sampling measures observed deltas; it cannot recover unobserved intermediate damage/healing.

## Verification and ownership

1. Core worker adds the entry point initially delegating to existing snapshot behavior plus focused tests. Parent runs red and retains actual first-change failure.
2. After parent confirms red, worker connects verified previous values to the existing reducer and updates only the native binding caller. Cover first damage, magicka-only first change, initialized-state match/mismatch, arbitrary low initial snapshots, epoch/revision rejection, no-change sparsity and save/reload once-only aggregate evidence.
3. Separate worker extends existing owned `NativeNpcVitalsProbe` to assert that first real nonlethal native damage on its previously unpersisted actor creates the exact unknown-cause pending health episode; preserve its existing stream teardown/disk rollback tests. No new model or broad test harness.
4. Parent owns Core runs, runtime/Unity build, native execution and settings/process lifecycle. Read-only reviewer checks source before native run. Preserve failures; no model rerolls are involved.
5. Update handoff and memory only from verified outcomes. Keep roadmap active and obey approximately40-percent quota pause/backup/shutdown instruction.
