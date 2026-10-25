# Native Poison Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record one durable, observer-bound `StatusOnset` event when a living eligible resident receives an active native poison effect, with cause explicitly unknown.

**Verified probe correction after build 65:** The first native run restores its baseline but finds no resident satisfying the plan's native-level-above-one prerequisite. That prerequisite belongs to `FormulaHelper.InflictPoison` (its explicit `target.Level != 1` gate), not to `EntityEffectManager.CreatePoison`/`AssignBundle` or `PoisonEffect.Start`. The controlled probe below uses direct bundle assignment. For this probe, replace every level-above-one prerequisite with an existing living eligible resident, positive native health, the production observer, and its same-GameObject native effect manager. Record the actual level without changing it. This proves capture of a controlled accepted effect application; it does not prove ordinary weapon/combat poisoning can bypass DFU's level-one rule. Production poisoning rules remain unchanged. Preserve `native-poison-onset-65` as the failed prerequisite run, and use a fresh evidence directory for the corrected probe.

**Architecture:** A resident-local adapter listens to DFU's `EntityEffectManager.OnAssignBundle`, but accepts only the callback whose exact bundle remains active and contains a live `PoisonEffect`. It baselines effects present when the observer attaches, then asks Core to allocate a per-observer onset sequence and append the typed event to the existing experience inbox in one store mutation. The event then follows the existing appraisal path; it does not assert who poisoned the resident or alter state directly in the callback.

**Tech Stack:** C#, Unity resident components, DFU `EntityEffectManager`, the existing Core `WorldStateStore` and FullSerializer-backed world/inbox persistence, focused Core tests, disposable-character native probe.

**Spec:** `docs/superpowers/specs/2026-10-21-npc-experience-events.md`, especially its event contract, status events, source-bound evidence, unknown-culprit rule, and once-only effects.

## Global Constraints

- Capture only a native active poison effect for a living, known, adult, non-quest-protected resident.
- Use DFU's exact `OnAssignBundle` callback and verify the callback's same bundle reference is still in `EffectBundles` and has a live `PoisonEffect`.
- Set responsible-actor attribution to `Unknown`; do not infer a culprit from caster, damage, timing, speech, or effect text.
- Treat `DiseaseEffect` as unsupported: DFU ends it for every non-player host.
- Preserve the onset as history; do not claim resident active effects survive stream teardown or save/load.
- Do not use semantic regex, text classification, model calls on effect ticks, object hashes, or wall-clock time as onset identity.
- Do not apply relationship or mood changes inside the native callback; enqueue evidence for the existing appraisal/commit path.

---

### Task 1: Add a typed poison status payload and preserve old inbox rows

**Files:**
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceImpactDetails.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceInbox.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceAppraisal.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceImpactDetailsTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceInboxMigrationTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceAppraisalTests.cs`

**Interfaces:**
- Add `NpcExperienceImpactKind.StatusOnset = 4` and `NpcExperienceStatusKind.Poison = 1`; retain existing enum values.
- Add `NpcExperienceImpactDetails.ForStatusOnset(NpcExperienceStatusKind status, string nativeEffectKey, long onsetSequence)`.
- Add immutable copied fields `Status`, `NativeEffectKey`, and `StatusOnsetSequence`. Validate positive sequence, bounded nonempty key, no controls, and no transfer/health fields. Factory accepts the typed poison status; it does not parse or infer status from the key.
- Increase `NpcExperienceImpactDetails.VersionCurrent` to 4. Add flat inbox fields `StatusKind`, `StatusNativeEffectKey`, and `StatusOnsetSequence`, and round-trip them in `FromNativeEvent`, `ReadImpactDetails`, `CopyValidated`, and `ToEvent`.
- Increase `NpcExperienceInbox.VersionCurrent` to 3. Version-1/2 rows without typed status details migrate unchanged as unknown legacy evidence; migration must not derive status from old prose, key-like strings, or `EventKind` alone.
- Extend the appraisal event projection with `nativeStatus={kind=Poison,key=<exact native key>,onsetSequence=<sequence>,responsibleActor=Unknown}`. `StatusOnset` must not become a positive-trust event; an appraisal may propose a mood response, subject to the existing parser and commit bounds.

- [ ] **Step 1: Write failing Core tests.** Test typed poison impact copy/detachment, invalid status/sequence/key and transfer-only field rejection, event-kind matching, inbox round-trip, and legacy v1/v2 status rows staying without a typed status payload. Add an appraisal prompt test that exposes only the typed status, exact native key, sequence, and unknown attribution; a speech claim or old text-only row must not render as a verified poison event.

- [ ] **Step 2: Run the focused Core tests and verify the new assertions fail.**

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: failures identify the missing impact kind/factory and flattened inbox/prompt projection.

- [ ] **Step 3: Add the typed payload and migration.**

```csharp
var details = NpcExperienceImpactDetails.ForStatusOnset(
    NpcExperienceStatusKind.Poison, exactNativeEffectKey, onsetSequence);
var evidence = NpcExperienceEvent.FromNativeOutcome(
    observerId, observerId, receiptId, episodeId, outcomeId,
    NpcExperienceEventKind.StatusOnset, observedGameTime,
    NpcResponsibleActorAttribution.Unknown, null, details);
```

Factory validation accepts only the typed poison status, bounded key, and positive sequence. Do not infer poison from `nativeEffectKey`. Keep old inbox rows valid and unclassified.

- [ ] **Step 4: Run focused Core tests.**

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: new payload, persistence, migration, and prompt assertions pass; existing Core tests remain green.

### Task 2: Allocate sequence and enqueue atomically in Core

**Files:**
- Create: `src/PeopleOfTheIliacBay.Core/NpcStatusOnsets.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceInbox.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcStatusOnsetTests.cs`

**Interfaces:**
- Add `WorldStateStore.TryCaptureNpcPoisonOnset(object expectedVitalsEpoch, string observerId, string nativeEffectKey, double observedGameTime, out NpcExperienceEvent evidence)` in `NpcStatusOnsets.cs`.
- The method checks the current binding epoch, known/alive observer, finite nonnegative game time, status payload validity, and inbox validity. It computes `onsetSequence = 1 + max(saved poison StatusOnsetSequence for observer)` from the retained canonical inbox history, builds receipt/episode/outcome IDs from observer and sequence, and creates a native outcome with `SubjectPersonId == observerId`, `NpcResponsibleActorAttribution.Unknown`, and no responsible actor ID.
- Stage a detached inbox copy and new entry before publishing it. Append the entry and advance inbox revision as one assignment; any stale epoch, invalid input, duplicate, or copy failure leaves both sequence and inbox unchanged. Sequence is saved in each typed event row; current inbox history is retained, so deriving the next value from it does not require a second counter ledger.
- The runtime adapter may submit only after the exact DFU bundle callback has passed the native activity checks in Task 3. This Core method validates structure and identity; it does not independently prove DFU provenance.

- [ ] **Step 1: Write failing transaction tests.** Cover first sequence, next sequence, unknown cause, observer isolation, bad game time, unknown/dead observer, stale epoch, no partial inbox mutation on rejection, and FullSerializer save/restore followed by the next sequence. Verify a status event cannot grant positive trust and can proceed through the existing appraisal commit with a mood proposal while exact-once effects are recorded.

- [ ] **Step 2: Run the focused Core tests and verify failure.**

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: failures identify missing atomic capture and sequence behavior.

- [ ] **Step 3: Implement the single store transaction.** Build the event and detached inbox replacement first; publish only the completed replacement. Keep method free of model calls and relationship mutation.

- [ ] **Step 4: Re-run focused Core tests.**

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: accepted status event and sequence survive save/restore; all rejection cases preserve state.

### Task 3: Observe only newly active poison on resident actors

**Files:**
- Create: `mod/Scripts/NativeNpcPoisonOnsetBinding.cs`
- Modify: `mod/Scripts/ResidentActor.cs`
- Test: `mod/Scripts/NativeNpcPoisonExperienceProbe.cs`

**Interfaces:**
- Add `NativeNpcPoisonOnsetBinding.BindTo(GameObject owner, WorldStateStore world, Resident person)` and attach it from `ResidentActor.Initialize` next to `NativeNpcVitalsBinding.BindTo`.
- Keep the resident's `WorldStateStore` binding epoch. Subscribe to that actor's `EntityEffectManager.OnAssignBundle`; unsubscribe on component teardown and reject callbacks after the actor/store epoch is stale.
- On bind, record references to currently active poison bundles as baseline without enqueuing them. In the callback, require an eligible living resident, `bundle.bundleType == BundleTypes.Poison`, the callback's exact bundle reference in `manager.EffectBundles`, and at least one `PoisonEffect` whose `HasEnded` is false. Ignore the incumbent callback branch when its incoming bundle was not retained in the active list. Keep a transient reference set to prevent duplicate callbacks for the same live bundle; remove references on `OnRemoveBundle`.
- Read `PoisonEffect.Key` as the native key and use `DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()` as observed game time. Call `TryCaptureNpcPoisonOnset(writeEpoch, resident.Id, effect.Key, observedGameTime, out _)` once per newly active bundle. Never use the caster as proof of the poisoning actor.
- Do not subscribe to or emit disease onset. Do not capture general `EffectBundles`, all damage effects, or healing effects as status events.

- [ ] **Step 1: Add a native acceptance probe with strict assertions.** In the owned disposable character, wait for a real eligible resident with `EntityEffectManager` and native level greater than one. Assign a controlled classic poison using DFU `CreatePoison`/`AssignBundle`, then assert active callback, exact poison key, one inbox row, monotonic sequence 1, and unknown cause. Assign the same poison again while its incumbent remains; assert the unretained callback creates no second row. Remove the active bundle, assign it again, and assert sequence 2. Assign an active non-poison effect and assert no status row. Attach/baseline the observer while poison is already active and assert no onset replay. Stream the resident out and let production rebuild the same identity; assert the historical row remains once and that the probe does not claim the missing active effect survived actor recreation. Save/reload the disposable character, verify the persisted first onset remains once, then apply poison to a fresh eligible resident (or the same identity if available) and verify the next saved sequence advances rather than restarting at 1.

- [ ] **Step 2: Run the probe's offline/native preconditions before installing it.** Confirm the probe requires an explicit flag and disposable character, and does not alter the user's normal save or apply effects to the player.

- [ ] **Step 3: Implement the resident observer using exact bundle identity checks.** Subscribe only after the resident is initialized; keep capture and unsubscription on Unity's owner thread. Keep the baseline limited to the manager's currently active poison bundles.

- [ ] **Step 4: Run Core tests, runtime compile, and the owned native probe.**

Run Core: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Run runtime compile and `NativeNpcPoisonExperienceProbe` only through the repository's guarded `TownTest.ps1` disposable-character workflow.

Expected: controlled poison emits exactly two distinct typed onset receipts across remove/reapply; same-kind incumbent callback, unrelated effect, stale actor callback, and baseline effects emit none. Resident stream-out preserves the historical receipt but does not claim active-effect persistence; disk reload preserves the sequence and does not replay the old onset.

## Scope boundary

The DFU source has a stable native `PoisonEffect.Key`, active `EffectBundles`, `OnAssignBundle`, and `OnRemoveBundle`, but no stable bundle/onset identifier. Its assignment callback fires for both a retained bundle and an unretained incumbent; the active-reference test distinguishes them. `DiseaseEffect.Start` ends disease for non-player hosts, so disease status is excluded.

DFU serializes effect bundles for player/enemy save objects, but LivingCities resident actors are streamed `ResidentActor` objects whose `CaptureState` currently persists inventory and vitals only. This plan records a durable historical onset receipt; it does not preserve the active poison effect across actor streaming or save/load. Effect-state persistence for residents requires a separate design and native adapter before anyone describes current status as surviving reload.

The current appraisal pipeline may interpret the receipt-bound status and change mood once. Unknown cause must remain unknown; no trust credit is allowed from status alone. Do not claim model-quality or whole-game status support from Core fixtures or the controlled native probe.
