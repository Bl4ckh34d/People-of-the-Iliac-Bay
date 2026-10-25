# Authoritative NPC Context Snapshot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each submitted NPC conversation one detached, source-backed context binding shared by routing, semantic classifiers, and social replies, with explicit unknowns and safe stale-result rejection.

**Architecture:** Core defines immutable snapshot data, binding validation, and purpose-specific text projections. A main-thread runtime adapter captures Unity facts once per message and carries the snapshot through existing production completion calls. Existing native reflection, action validation, quest-answer paths, and appointment HERE behavior remain authoritative.

**Tech Stack:** LivingCities.Core C#, Unity/DFU runtime C#, existing `tests/LivingCities.Tests` runner, RuntimeCompile, current `LocalDialogue.Request` and `LocalDialogue.Classify` production calls.

**Spec:** `docs/superpowers/specs/2026-10-19-authoritative-npc-context-design.md`; roadmap item IMM-02 in `docs/ROADMAP.md`; corrections and current-source map in `work/model-comparison/context-snapshot-design-review.md`.

## Status and prerequisite

23:37 whole-task experiment checkpoint: all 23 once-only v3 requests return complete valid JSON, but only 3/7 proposed plans on expected-current requests are exact, three additional proposals target ambiguous/non-request inputs, and the native Wait/Follow request still produces no plan. Willingness remains unscored. V2's pre-generation grammar failure and v3's single-quote correction are retained separately. This experiment is rejected for runtime integration; F0406C29 remains installed. See newest Testing experiment section for exact artifacts and limits.

19 October task-index checkpoint: 1181/1181 Core and clean RuntimeCompile; installed package F0406C29 with 492 matching archived executable/tool sources. A terminal task update now removes a resident's active index only if it indexes that same task. The regression covers delivery of A's old failure receipt while B is active, unchanged B state, sole pending-task lookup, and rejection of C. Native lifecycle and acknowledgment/source-provenance markers pass with fixture baseline restoration. Preserve the lifecycle wrapper's post-return unset-LASTEXITCODE error separately from the successful TownTest. Evidence: `imm02-native-20261019-231748/acceptance-scope.json` and latest Testing checkpoint. Whole-task semantic acceptance remains open; the new full-task protocol is an unshipped experiment.

23:04 source-provenance checkpoint: 1180/1180 Core and clean RuntimeCompile; final package 5E37011E has 492 matching archived source/tool files. Both focused native lifecycle and acknowledgment suites pass, including source-specific handling of unavailable UI/history rows. Failed replies remain in the visible transcript but do not create successful-exchange counts or NPC-speech recall. Full action acceptance and duration fidelity remain open. Matched 7B/9B template diagnostics do not fix the native task failure; the original 7B endpoint is restored. See the latest Testing checkpoint and `imm02-native-20261019-230048/acceptance-scope.json`.

Latest parent checkpoint (19 October, 22:29): 1171/1171 Core and clean RuntimeCompile; coherent installed package C3114B5A with 492 matching archived executable/tool sources. Both native snapshot/lifecycle and saved-state acknowledgment helper probes pass on this package. Full semantic action acceptance still fails at the first step of the two-action request. A task-step-specific prompt removes a verified whole-request policy conflict, but does not establish general native success. Duration and connector experiments retain failures and are not shipped. Full evidence and acceptance boundaries are in the newest `docs/TESTING.md` checkpoint; IMM-01/IMM-02 remain incomplete.

**IMM-01 baseline assessment is complete; this approved IMM-02 implementation can proceed.** Root retains 7B under the accepted protocol and has released the source freeze for approved IMM-02 work and evidence-based general repairs. IMM-01 is not marked complete: the full native action/assent probe failed because “Would you mind waiting here?” decomposed to no Wait task. Preserve that trace as a separate acceptance gap; it does not block this approved IMM-02 plan. This status update adds no user-approval gate and does not weaken native acceptance.

**Current implementation status (19 October, integration build):** The detached Core contract, sibling-checkpoint ancestry test, runtime capture/lifecycle adapter, route/action/home/appointment/browse snapshot handoff, immutable disposition successor, captured recall, and social reply projection are implemented. Parent independently ran 1158/1158 Core tests and RuntimeCompile with zero warnings/errors. Native snapshot and real lifecycle probes pass on package BDB7ED6E: close/reopen, same-branch native save/head advance, real disk-load invalidation, modal recovery and baseline restoration. Native action cancellation now succeeds, but its full suite and the wider native case list below remain open. Catalog-free routing is integrated with a separate offered-topic binder; captured-context controls pass 13/14, with one hypothetical social question still routed as factual. See `docs/TESTING.md` for exact hashes/logs and `work/model-comparison/imm02-native-coverage-map.md` for remaining gaps. IMM-01/IMM-02 completion remains unclaimed.

## Global constraints

- Keep implementation within the approved IMM-02 scope or evidence-based repairs. Preserve IMM-01's failed action/assent trace and do not claim full native action acceptance.
- Keep one original player message, actor binding, and request revision through every async stage.
- Keep snapshot data detached from Unity objects and immutable after capture.
- Represent unknown identity, role, location, activity, or source explicitly. Never infer a traveler from an unmanaged non-guard mobile actor.
- Keep player scene and `PlayerLocationAtSubmission` separate from NPC physical location and activity.
- Preserve existing `ConversationLocationSnapshot` as the fixed HERE source for appointments. Do not derive HERE again after async delay.
- If a committed tone effect changes effective disposition, create an immutable successor snapshot. Never mutate its parent.
- Invalidate pending work on conversation close/reopen, speaker change, save load/restore, restore failure, or superseding request.
- A same-branch save may advance archive head without invalidating the active conversation. Restore or branch switch invalidates it.
- Keep native action quote, consent, commit, and execution validation. Snapshot is not a reservation or authorization.
- Keep current reflection fields, native quest-answer APIs, action validators, `/completion` production transport, and `LocalDialogue` lifecycle behavior.
- Keep IMM-03 context-tool orchestration out of this plan. Add only the Core binding contract needed for later source revisions.

## Proposed Core contract

Names below are new plan-level proposals, not claims that these APIs already exist. Final field layout must match repository C# and serialization constraints during implementation.

- `ConversationContextBinding`: immutable identity for person ID or explicit ephemeral speaker token, actor kind, conversation session, request revision, runtime save/restore epoch, and active archive ancestry token. Use the current archive ID and head as provenance. Do not use head inequality alone to invalidate a same-branch pending turn.
- `ConversationContextFact<T>`: immutable status (`Unknown` or `Verified`), value, source category, and capture/observation game time. An unknown value carries no guessed display text.
- `ConversationContextSnapshot`: immutable capture time, binding, verified speaker identity/role, effective disposition, existing appointment HERE snapshot, player scene/place, NPC physical scene/activity evidence, bounded commitments, and owner/revision handles for existing read sources. The only revision-producing operation is a copy that records a committed state transition.
- `ConversationContextFormatter`: compact purpose-specific formatter for routing, semantic classification, and social reply. Include only fields verified for that stage. Keep prior player claims and prior model speech attributed. Do not accept hidden quest fields or disclose them through a shared formatter.

Do not store Unity `Transform`, `Resident`, `MobilePersonNPC`, `StaticNPC`, `TalkManager.ListItem`, native `List`, route geometry, full schedules, item payloads, raw quest state, or entire conversation journals in Core snapshot data.

## Exact file map

| File | Responsibility |
|---|---|
| `src/LivingCities.Core/ConversationContextSnapshot.cs` (new) | Immutable binding, fact status/provenance, detached snapshot values, successor revision, and mismatch checks. |
| `src/LivingCities.Core/ConversationContextFormatter.cs` (new) | Bounded routing/classifier/social-reply projections; unknowns and attributed sources remain truthful. |
| `src/LivingCities.Core/ConversationArchiveState.cs` (modify) | Add a small copied read-only accessor for current archive ID/head so runtime capture avoids copying the entire `WorldState`. Do not expose the mutable checkpoint. |
| `tests/LivingCities.Tests/ConversationContextSnapshotTests.cs` (new) | Core immutability, binding, unknown, successor, branch-token, and projection tests. |
| `tests/LivingCities.Tests/Program.cs` (modify) | Register the focused Core test group. |
| `mod/Scripts/LivingCitiesMod.ContextSnapshot.cs` (new) | Main-thread capture adapter, strict speaker identity validation, request revision, save/restore epoch, and current-snapshot matching. |
| `mod/Scripts/LivingCitiesMod.ConversationKnowledge.cs` (modify) | Capture once at `SubmitFreeText`; carry binding through route/scope callbacks and create successor after committed tone effects. |
| `mod/Scripts/LivingCitiesMod.Dialogue.cs` (modify) | Build final social reply from the captured/successor snapshot while retaining current request and failure handling. |
| `mod/Scripts/LivingCitiesMod.DialogueLife.cs` (modify) | Stop presenting player scene/weather as verified NPC outdoors/activity facts; use explicit unknown when no actor-specific evidence exists. |
| `mod/Scripts/LivingCitiesMod.DialogueActions.cs` (modify) | Give action classifiers the snapshot projection; preserve all current quotes and native acceptance checks. |
| `mod/Scripts/LivingCitiesMod.DialogueSemanticTasks.cs` (modify) | Carry the same snapshot binding through decomposition and sequential semantic classifiers. |
| `mod/Scripts/LivingCitiesMod.DialogueActionTargets.cs` (modify) | Carry the same snapshot binding through category selection and target choice. |
| `mod/Scripts/LivingCitiesMod.DialogueMemory.cs` (modify) | Retain current session guards and incorporate explicit request/save-restore epoch matching. |
| `mod/Scripts/LivingCitiesMod.cs` (modify) | Advance the runtime save/restore epoch at existing `BeforeLoad`, `RestoreSaveData`, and reset lifecycle boundaries. |
| `mod/Scripts/ConversationContextSnapshotProbe.cs` (new, probe-only) | Flag-gated native verification of capture, strict speaker binding, async invalidation, HERE, and prompt projection. |
| `tools/TownTest.ps1` (modify) | Expose only the new focused probe flag and required fixture arguments. |
| `docs/TESTING.md` (modify after acceptance) | Record exact native commands, logs, result scope, and limitations. |

Do not modify `mod/Scripts/LivingCitiesMod.ContextTools.cs`, `mod/Scripts/StreetPopulation.Actions*.cs`, `mod/Scripts/LivingCitiesMod.ConversationKnowledge.cs` native topic-answer code, `LivingCitiesMod.DialogueAppointments.cs`, or `ConversationLocationSnapshot.cs` unless a focused failing test proves a contract defect. IMM-03 owns tool orchestration; native action and quest paths remain their own authorities.

---

### Task 1: Define Core binding and immutability contract

**Files:**

- Create: `src/LivingCities.Core/ConversationContextSnapshot.cs`
- Create: `tests/LivingCities.Tests/ConversationContextSnapshotTests.cs`
- Modify: `tests/LivingCities.Tests/Program.cs`

**Interfaces:**

- Proposed Core types: `ConversationContextBinding`, `ConversationContextFact<T>`, `ConversationContextSnapshot`, `ConversationActorKind`, and `ConversationFactStatus`.
- Snapshot owns only detached immutable values. Provide a copy-producing successor operation for verified disposition changes. Do not put Unity or model-transport types in Core.

- [x] **Step 1: Write red tests.** Cover explicit unknown role/NPC position; managed, static, and ephemeral actor kinds; constructor copies of nested appointment place/commitment/source handles; accessor copies; rejection of empty stable IDs or invalid ephemeral bindings; mismatch rejection across actor, person, session, request, epoch, archive provenance, and scene binding; successor disposition updates without parent mutation.
- [x] **Step 2: Run focused tests and record expected failures.** Initial Core red runs recorded seven expected contract failures, followed by two scene-binding failures before the matching fields/checks were added.
- [x] **Step 3: Add the minimal immutable Core types.** Keep constructors validating null/length/range invariants. Keep unknown status explicit; never substitute empty string, zero, `traveler`, or current player place as a verified NPC value. Copy all mutable input and output values.
- [x] **Step 4: Re-run focused Core suite.** Parent-reported Core result: 1150/1150 passed, including existing save/memory tests.
- [x] **Step 5: Review contract surface.** The new Core contract contains no native type reference, mutable collection exposure, save-schema change, or model API.

### Task 2: Add purpose-specific formatter and branch provenance accessor

**Files:**

- Create: `src/LivingCities.Core/ConversationContextFormatter.cs`
- Modify: `src/LivingCities.Core/ConversationArchiveState.cs`
- Modify: `tests/LivingCities.Tests/ConversationContextSnapshotTests.cs`

**Interfaces:**

- Proposed `ConversationContextFormatter` methods produce bounded text for routing, semantic classifiers, and social replies from the same snapshot.
- Add a read-only `ConversationArchiveCheckpoint` copy accessor to `WorldStateStore`. It returns archive ID/head values without returning the store's mutable checkpoint or cloning all world state.

- [x] **Step 1: Add red projection tests.** Verify verified role and readable captured clock appear in projections; unknown NPC physical state produces no location claim; player scene and fixed/unknown player HERE are separately labelled; NPC weather requires an NPC-scene observation pinned to the captured scene revision; history stays attributed; nonallowlisted commitments are omitted; escaped fact rows and omission markers remain bounded.
- [x] **Step 2: Add red archive-accessor tests.** The focused tests prove a detached copied `(ArchiveId, Head)` view, snapshot-copy isolation, and two restored sibling checkpoints with independent heads and recall ancestry. Earlier captured views remain unchanged as each sibling advances. This is Core evidence, not a native disk-restore callback test.
- [x] **Step 3: Run failing tests.** The recorded initial Core red runs include expected missing-contract failures before implementation.
- [x] **Step 4: Implement compact formatters and accessor.** The Core formatter now uses purpose projections, commitment allowlists, scene-pinned NPC weather, atomic escaped rows, and an omission marker. The branch token remains provenance, not an automatic invalidation rule on same-branch head advancement.
- [x] **Step 5: Run Core suite.** Parent independently verified 1156/1156 passed after integration, including detached archive access and sibling ancestry. Task 6 native restore/head checks remain open.

### Task 3: Add runtime capture adapter and strict identity checks

**Files:**

- Create: `mod/Scripts/LivingCitiesMod.ContextSnapshot.cs`
- Modify: `mod/Scripts/LivingCitiesMod.DialogueMemory.cs`
- Modify: `mod/Scripts/LivingCitiesMod.cs`
- Create: `mod/Scripts/ConversationContextSnapshotProbe.cs`
- Modify: `tools/TownTest.ps1`

**Interfaces:**

- The adapter captures on Unity main thread and returns only the proposed Core snapshot type.
- Managed mobile identity comes from the live `ResidentActor` plus resident ID. Static identity comes from the current `StaticNPC.Data` map/building/hash key and must match at every callback. Unmanaged mobile identity is ephemeral and tied to that live speaker object plus session; never persist or cross-merge it.
- Effective disposition continues to use existing `reactionToPlayerField` reflection. Keep native field lookup, hooks, and tier adjustment unchanged.
- Runtime request revision increments at each accepted submitted message. Runtime save/restore epoch increments before loading, after restore/reset, and on restore failure boundaries. Do not increment it for ordinary same-branch save.

- [ ] **Step 1: Add flag-gated red native assertions.** The probe must fail if static ID is nonempty but belongs to a prior `StaticNPC`; if unmanaged mobile is labelled traveler; if player scene populates NPC scene; if a snapshot includes a Unity object; if request/session/restore token mismatch passes; or if same-branch save advancement invalidates a pending binding.
- [ ] **Step 2: Run only Core and RuntimeCompile red checks.** Run `dotnet run --project tests/LivingCities.Tests --no-restore` and `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -v:q`. Expected: Core remains green; RuntimeCompile fails until adapter references are defined. Do not launch Unity in this red-check step.
- [ ] **Step 3: Implement capture and lifecycle tokens.** Capture source-backed scalar facts only. Capture actual NPC scene/position only when the live actor and matching scene prove it; otherwise store unknown. Keep player scene and `ConversationLocationSnapshot` separate.
- [ ] **Step 4: Add current-binding validation.** Validate current manager actor type and identity key, session, request revision, and restore epoch before accepting any async result. Retain `CurrentMemorySession()` window/open/save-fault guards.
- [ ] **Step 5: Re-run Core and RuntimeCompile.** Expected: all Core tests pass and RuntimeCompile has zero warnings/errors. Keep the native probe dormant until implementation tasks are complete and the focused native checks below are ready.

### Task 4: Integrate captured snapshot into free-text route and social reply

**Files:**

- Modify: `mod/Scripts/LivingCitiesMod.ConversationKnowledge.cs`
- Modify: `mod/Scripts/LivingCitiesMod.Dialogue.cs`
- Modify: `mod/Scripts/LivingCitiesMod.DialogueLife.cs`
- Modify: `tests/LivingCities.Tests/ConversationContextSnapshotTests.cs`

**Interfaces:**

- `SubmitFreeText()` captures once before asynchronous route classification and retains `playerText`, history, `ConversationLocationSnapshot`, and binding in every closure.
- `SubmitSocialText()` accepts the captured snapshot or its explicit successor. It must not independently resolve a different speaker, role, or NPC location.
- Keep `ConversationLocationSnapshot.CapturedAt` and copied `AppointmentPlace` as appointment HERE source; preserve the existing appointment parser/booking seam unchanged.

- [ ] **Step 1: Add red Core projection lifecycle tests.** Verify `revision 0 -> committed tone effect -> revision 1 -> final reply` changes only disposition and revision. Rejected/unavailable effects produce no successor. Verify a route and reply for one binding cannot accept another binding.
- [ ] **Step 2: Run Core suite and record failures.** Run `dotnet run --project tests/LivingCities.Tests --no-restore`. Expected: successor lifecycle cases fail before adapter integration.
- [ ] **Step 3: Capture and pass initial snapshot.** Replace independent route-stage binding reads with one capture. Preserve current `CurrentMemorySession`/person checks, native hostility/disposition ordering, answer failure receipts, and original submitted text.
- [ ] **Step 4: Create successor only after committed disposition effect.** Use the actual resulting `CurrentDialogueDisposition()` after `ApplyConversationDisposition()` or `ApplyFamilyThreatDisposition()`. Pass successor into semantic routing/reply. Keep parent immutable. If no effect changes disposition, reuse original snapshot.
- [ ] **Step 5: Format social reply from snapshot.** Keep model history attributed and bounded. Use explicit unknown speaker facts; do not turn player exterior weather into NPC outdoors/mood. Keep response fallback/transcript semantics unchanged.
- [ ] **Step 6: Run Core and RuntimeCompile.** Run `dotnet run --project tests/LivingCities.Tests --no-restore` and `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -v:q`. Expected: green Core and zero-warning runtime compile.

### Task 5: Bind semantic classifiers to same snapshot and preserve rechecks

**Files:**

- Modify: `mod/Scripts/LivingCitiesMod.DialogueActions.cs`
- Modify: `mod/Scripts/LivingCitiesMod.DialogueSemanticTasks.cs`
- Modify: `mod/Scripts/LivingCitiesMod.DialogueActionTargets.cs`
- Modify: `tests/LivingCities.Tests/ConversationContextSnapshotTests.cs`

**Interfaces:**

- Every action/category/decomposition/offer classifier receives a purpose-specific projection from the current request snapshot/successor and preserves the same binding through chained calls.
- Native quote and accept methods continue to read live world state. Snapshot data cannot skip `TryQuoteSemanticShopPurchase`, `TryAcceptConversationAction`, executor target/route checks, or consent handling.

- [ ] **Step 1: Add red tests for chained binding.** Make category selection, decomposition step 2, and final action classifier reject a prior-step result after speaker/session/request binding changes. Verify actor role or action awareness is not reread from a different speaker.
- [ ] **Step 2: Run focused Core suite.** Run `dotnet run --project tests/LivingCities.Tests --no-restore`. Expected: new chained-binding cases fail before wiring.
- [ ] **Step 3: Pass one binding through semantic chains.** Replace each direct context reconstruction in these files with the corresponding formatter projection. For any committed effect, pass the explicit successor; do not capture a new actor implicitly.
- [ ] **Step 4: Preserve live commit checks.** Leave `StreetPopulation.Actions*.cs` unchanged. If a target, stock, actor state, wallet, alarm, or route changes after classification, current native validators must still reject it.
- [ ] **Step 5: Run Core and RuntimeCompile.** Expected: all tests pass; RuntimeCompile remains warning-free.

### Task 6: Prove stale-session, restore, and same-branch-save behavior in native runtime

**Files:**

- Modify: `mod/Scripts/ConversationContextSnapshotProbe.cs`
- Modify: `tools/TownTest.ps1`
- Modify: `docs/TESTING.md`

**Interfaces:**

- The flag-gated probe uses owned disposable fixtures and records exact captured values, projected prompts, callback acceptance/rejection, and displayed transcript result.
- It must not use hidden quest data or write an unrelated player save. Do not change the accepted model-comparison corpora or runner.

- [ ] **Step 1: Add red probe cases.** Cover same NPC close/reopen, speaker switch A to B, static actor identity mismatch, unmanaged ephemeral speaker, old request revision, pending completion across `BeforeLoad`, failed restore, restoring a sibling save branch, and same-branch save head advancement.
- [ ] **Step 2: Confirm sequencing prerequisite.** IMM-01 has been assessed, so this prerequisite is met. Build/install and launch the probe only after the Core/runtime implementation checks pass; preserve the existing model-server and user-game safeguards. The separate IMM-01 action/assent failure remains open and must not be represented as a pass.
- [ ] **Step 3: Run focused native probe after gate.** Use `tools/TownTest.ps1` with the new context-snapshot flag and disposable fixture. Verify stale callbacks cause no transcript, disposition, tool, appointment, or action effect; same-branch save remains valid; restore invalidates old requests.
- [ ] **Step 4: Add deterministic movement and weather cases.** Player exterior + stationary NPC interior, player interior + resident exterior, player in a different building, and unknown static/unmanaged actors. Verify exact prompt projection never confuses player/NPC scene and omits NPC weather mood when NPC outdoors is unknown.
- [ ] **Step 5: Capture appointment HERE regression.** Capture building and exterior points, move player before appointment completion, and verify booking still uses captured submission point/time. Compare exact stored `AppointmentPlace` and displayed result.
- [ ] **Step 6: Record evidence.** Add commands, disposable fixture identity, log paths, hashes, exact prompt/reply evidence, and test limits to `docs/TESTING.md`. Do not claim all authored actors are integrated.

### Task 7: Verify action and quest boundaries; close IMM-02 evidence

**Files:**

- Modify: `mod/Scripts/ConversationContextSnapshotProbe.cs`
- Modify: `tools/TownTest.ps1`
- Modify: `docs/TESTING.md`

- [ ] **Step 1: Add red native boundary cases.** Snapshot an available purchase/action, then invalidate target, stock, wallet, actor proximity, awake state, alarm state, or quest ownership before commit. Add native quest/person/regional topic cases and a prompt asking for a known-but-undisclosable quest fact.
- [ ] **Step 2: Run native boundary cases after the Task 6 snapshot probe.** Verify every changed action input is rejected by existing native validators and no resource mutates. Verify quest topic answers remain native-owned and hidden quest state never enters shared projections.
- [ ] **Step 3: Verify Core, runtime, and focused native checks.** Run `dotnet run --project tests/LivingCities.Tests --no-restore`, `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -v:q`, then the focused `TownTest.ps1` probes. Preserve full failure logs.
- [ ] **Step 4: Review scope.** Confirm no action validator, native answer getter, action schema, save schema, quest system, or context-tool orchestration changed as an accidental side effect.
- [ ] **Step 5: Update roadmap evidence only after acceptance.** Mark IMM-02 complete only after same-speaker/same-turn projection, stale-session rejection, branch isolation, HERE, actor-versus-player distinction, and native boundaries all pass. Leave IMM-03 incomplete until its own live tool orchestration acceptance.

## Acceptance checklist

- [x] Core snapshot is detached, immutable, explicitly represents unknowns, and has tested binding and successor semantics.
- [ ] Runtime capture identifies managed/static/ephemeral speakers correctly and strictly rechecks static actor identity.
- [ ] Route, semantic classifier, and reply projections share same binding. Disposition transitions use immutable successor revisions.
- [ ] Player scene/weather/HERE never masquerade as NPC location/activity/weather.
- [ ] Close/reopen, speaker switch, request supersession, load, restore failure, and old-branch callbacks fail closed.
- [ ] Same-branch save/head advancement does not invalidate a valid current turn.
- [ ] Action validations remain live and authoritative; native quest disclosure remains gated.
- [ ] Core tests, RuntimeCompile, focused native acceptance, exact prompt/reply captures, and `docs/TESTING.md` evidence pass.
- [x] IMM-01 baseline assessment precedes approved IMM-02 production Core/runtime implementation. IMM-01 completion remains separate and unchecked while the action/assent acceptance failure is open.

