# Neutral ItemReceived Receipt Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task by task. Steps use checkbox syntax.

**Goal:** Record successful player-to-NPC item transfers as durable, neutral `ItemReceived` evidence without blocking transfers on optional appraisal.

**Architecture:** Persist a validated native transfer receipt on the already-completed `NpcActionJob`. Capture source, moved, and retained destination UIDs during the real transfer. A bounded optional appraisal worker later projects that durable receipt into the existing experience inbox using deduplicated event identity.

**Tech Stack:** C# Core, Unity runtime adapter, existing Core tests, existing owned-player `TownTest` probe.

**Spec:** `docs/ROADMAP.md` (“Inventory and action implementation plan”), current transfer transaction code in `src/PeopleOfTheIliacBay.Core/NpcActions.cs` and `src/PeopleOfTheIliacBay.Core/NpcInventory.cs`, and the reviewed neutral-item-receipt design from 21 October 2026.

## Global Constraints

- Record only completed `PlayerToNpc` native item transfers with counterparty `player` and resident recipient.
- Keep `GiftReceived` for confirmed gift-purpose evidence; `ItemReceived` means receipt only.
- Never infer gift intent, debt repayment, gratitude, trust, obligation, or motive from transfer alone.
- Set valuation to `Unknown` and value to zero until a native valuation source is proven.
- Appraisal availability, disabled AI, invalid inbox, and future inbox backpressure must not block a valid native transfer.
- Never enqueue transfer evidence inside the native transfer transaction callback.
- No extra unbounded queue. Keep receipt in existing `NpcActionJob` ledger, capped at 10,000 jobs.
- All added receipt fields must be serializable and validated on restore. Schema 38 jobs without receipts remain valid.
- Current native stack merge may destroy the moved object UID. Event must use retained destination UID; receipt must record moved and retained UIDs separately.

**Implementation status:** Receipt persistence, neutral `ItemReceived` evidence, schema-39 compatibility, bounded inbox projection, native transfer integration, and existing-probe coverage are implemented. Core passes 1477/1477 and runtime compilation passes with zero warnings/errors. Native editor/item API execution and loaded-player acceptance remain pending parent verification; no whole-framework completion is claimed.

---

### Task 1: Add durable transfer receipt to completed action

**Files:**
- Modify: `src/PeopleOfTheIliacBay.Core/NpcActions.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcActions.cs` (receipt DTO is colocated with action jobs)
- Modify: `src/PeopleOfTheIliacBay.Core/WorldState.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcActionExecutorTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcInventoryTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceInboxMigrationTests.cs`

**Interfaces:**
- Add `[Serializable] NpcActionTransferReceipt` with `TransactionId`, `SourceItemUid`, `MovedItemUid`, `RetainedDestinationItemUid`, `Quantity`, `RecipientId`, `CounterpartyId`, `ObservedGameTime`, `NativeValuationStatus`, and `NativeGoldValue`.
- Add nullable `NpcActionJob.ItemTransferReceipt`.
- Add `NpcActionEffectResult` with `Succeeded` and optional `TransferReceipt`.
- Add internal `TryCompleteNpcActionEffectWithReceipt(string id, long expectedRevision, double now, Func<NpcActionEffectResult> applyEffect)`. Keep existing public `Func<bool>` API unchanged.
- Define `NpcActionTransferReceipt.CreatePlayerToNpc(NpcActionJob job, string transactionId, ulong sourceItemUid, ulong movedItemUid, ulong retainedDestinationItemUid, int quantity, string recipientId, string counterpartyId, double observedGameTime, NpcExperienceNativeValuationStatus valuationStatus, long nativeGoldValue)`.
- Define `NpcActionEffectResult.Success(NpcActionTransferReceipt receipt)` and `NpcActionEffectResult.Failure()`; success result carries prevalidated receipt without later fallible validation.
- Receipt construction validates before inventory commit. Typed completion assigns already-validated receipt to detached completed job, then stores job. It must perform no rejecting receipt validation after callback succeeds.

- [x] **Step 1: Write Core tests.** Receipt copy/restore, invalid receipt rejection, schema-38 completed-action compatibility, later position/recovery update and restore, time-bound checks, merge UID, drain dedup and trust exclusion are covered.

- [x] **Step 2: Run focused tests and confirm failure.**

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: new receipt tests fail because receipt type and action field do not exist.

- [x] **Step 3: Add the receipt DTO and action completion result.** Creation binds to an executing player-to-NPC action and validates transaction identity, resident/counterparty, quantity, UIDs, time and valuation before commit.

- [x] **Step 4: Attach receipt without a post-commit rejection path.** The internal typed callback stores a detached receipt after native commit and adds no rejecting receipt check afterward.

- [x] **Step 5: Bump save version and preserve old jobs.** Schema 39 stores optional receipts; schema-38 completed actions restore without one. Later position/recovery timestamps may advance beyond the receipt time while accepted/deadline and reversed/future bounds remain enforced.

- [x] **Step 6: Run Core tests.** Core suite passes 1477/1477.

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: all existing and new Core tests pass.

---

### Task 2: Add neutral `ItemReceived` evidence

**Files:**
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceEvent.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceImpactDetails.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceInbox.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceAppraisal.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcRelationshipLedger.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceEventTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceImpactDetailsTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceInboxTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceAppraisalTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceCommitTests.cs`

**Interfaces:**
- Append `NpcExperienceEventKind.ItemReceived = 11`; do not renumber existing kinds.
- Add `NpcExperienceImpactDetails.ForReceivedItemTransfer(ulong retainedDestinationItemUid, long quantity, NpcExperienceNativeValuationStatus status, long nativeGoldValue)`.
- Add `NpcExperienceImpactDetails.RetainedDestinationItemUid`; bump `VersionCurrent` to 3 while preserving legacy flattened gift rows with UID zero.
- Add flat inbox field `ReceivedItemUid` and round-trip it through `FromNativeEvent`, `ReadImpactDetails`, `CopyValidated`, and `ToEvent`.
- Preserve existing `ForItemTransfer(...)` behavior for legacy `GiftReceived` evidence. New `ItemReceived` details require positive retained UID; legacy gift details may retain zero UID.

- [x] **Step 1: Write Core tests for neutral evidence.** Event round-trip, positive retained UID, legacy gift compatibility, restore and invalid evidence coverage pass in the Core suite.

- [x] **Step 2: Add appraisal and accepted-ledger tests.** Prompt identifies exact retained UID/quantity and unknown purpose; transfer evidence cannot change trust.

- [x] **Step 3: Run Core tests and confirm failure.**

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: new enum, impact, persistence, and policy assertions fail.

- [x] **Step 4: Add the neutral event kind and item identity.** `ItemReceived` is distinct from `GiftReceived`; retained UID is persisted and restored.

- [x] **Step 5: Add bounded appraisal instructions.** Appraisal context renders retained UID/quantity and forbids inferring purpose or trust.

- [x] **Step 6: Run Core tests.** Core suite passes 1477/1477.

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Expected: event, impact, inbox, appraisal, and existing gift tests pass.

---

### Task 3: Stage receipt from actual native transfer

**Files:**
- Modify: `mod/Scripts/NativeNpcInventoryBridge.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcActions.cs`
- Test: `mod/Scripts/NativeNpcInventoryProbe.cs`
- Modify: `tools/TownTest.ps1`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcActionExecutorTests.cs`

**Interfaces:**
- Extend `Execute(...)` with a staged receipt output and optional accepted action context. Non-action transfers keep existing behavior.
- Return `NpcActionEffectResult.Success(receipt)` only for validated `PlayerToNpc` item receive. Other directions return success without receipt.
- Use current transaction identity `job.Id + ":transfer"`.
- After merge, set `MovedItemUid` to transient moved object UID and `RetainedDestinationItemUid` to existing merged target UID. Require target still contains retained UID and expected new stack count before receipt validation.

- [x] **Step 1: Add native probe assertions.** Existing native inventory probe checks source, moved and retained destination UIDs, quantity, recipient, counterparty, transaction and merged target ownership.

- [x] **Step 2: Add failure assertions.** Rejected access/replay leave no new receipt; other transfer directions do not create an incoming receipt.

- [x] **Step 3: Capture actual stack identities before commit.** Native bridge records moved UID separately from retained merged target UID and verifies target UID/count before staging.

- [x] **Step 4: Validate receipt before `CommitNpcInventoryTransaction`.** Receipt factory validation precedes Core inventory commit; failure uses native rollback.

- [x] **Step 5: Return typed success with staged receipt.** Core attaches a detached validated receipt after commit; the transfer callback does not invoke the inbox.

- [x] **Step 6: Extend the existing owned-fixture probe.** Receipt assertions are in `NativeNpcInventoryProbe`; no new `TownTest` switch was added.

- [x] **Step 7: Run Core and runtime source checks.** Core passes 1477/1477; runtime compilation passes with zero warnings/errors. Native editor/item API execution and loaded-player acceptance remain pending.

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Run: `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release`

Expected: Core tests pass; RuntimeCompile reports zero warnings and zero errors. Parent runs owned-player probe separately with game closed before install and the required owned fixture save.

---

### Task 4: Drain completed receipts into optional appraisal inbox

**Files:**
- Modify: `src/PeopleOfTheIliacBay.Core/NpcActions.cs`
- Modify: `src/PeopleOfTheIliacBay.Core/NpcExperienceInbox.cs`
- Modify: `mod/Scripts/PeopleOfTheIliacBayMod.Experiences.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceInboxTests.cs`
- Test: `tests/PeopleOfTheIliacBay.Tests/NpcExperienceCommitTests.cs`

**Interfaces:**
- Add `WorldStateStore.DrainNpcItemTransferReceipts(int maxAttempts)` with strict maximum batch size 32.
- Derive event receipt ID from `ItemTransferReceipt.TransactionId`; use distinct IDs `job.Id + ":item-transfer"` and `job.Id + ":item-received"` for episode and outcome. `job.Id` max length is 220, keeping suffixes within 240-character event identity limits.
- Use a transient integer scan cursor, wrap at end, and scan at most 32 action-ledger rows per call. Cursor resets on restart; inbox dedup makes rescans safe.
- Call drain only from `TickNpcExperienceAppraisals()` after `ExperienceAppraisalRuntimeReady(...)` succeeds and before reading pending experience inbox entries.

- [x] **Step 1: Add handoff tests.** Cover successful drain, duplicate/restart dedup, no receipt, later action movement, and 40 receipts interleaved with nonreceipt actions across bounded scans.

- [x] **Step 2: Implement deterministic outbox projection.** Scan from a transient cursor with a 32-row cap, deduplicate through inbox outcome identity, and retain the durable action receipt when projection cannot enqueue.

- [x] **Step 3: Gate optional drain.** Drain runs only after appraisal readiness and busy/cancellation checks; disabled or unavailable appraisal does not change transfer outcome.

- [x] **Step 4: Run Core and runtime compile checks.** Core passes 1477/1477; runtime compilation passes with zero warnings/errors.

Run: `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`

Run: `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release`

Expected: all tests pass and RuntimeCompile reports zero warnings and zero errors.

- [x] **Step 5: Verify native editor/item API and loaded-player acceptance.** Parent verified the native editor inventory probe in earlier installed builds and the actual owned-player path in build 34. `work/breeze-tts-20261021/native-inbound-item-disk-34` passes real wait/approach/transfer, source/moved UID 33554496, retained receiver UID 33554495 (stack 2 to 3), exactly one neutral `ItemReceived` projection, real DFU disk save/load, strict receipt/inventory/inbox comparisons at native serialized precision, replay rejection, and exact baseline reload. AI is disabled; the existing receipt drain is invoked explicitly for this test, so this does not establish model-appraisal quality. The established `NpcActionProbe`/`NpcActionDiskProbe` switches are reused. RuntimeCompile has zero warnings/errors. Earlier red runs 32/33 are retained: a strict binary-double timestamp assertion was incompatible with DFU's native serialization precision; the correction compares against the actual native serializer roundtrip with no tolerance or production behavior change.

---
