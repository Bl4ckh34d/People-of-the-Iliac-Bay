# Generational NPC Memory Implementation Plan

> For agentic workers: use the installed `executing-plans` skill to implement this plan task by task. This preparation does not authorize new subagents or concurrent edits to the frozen V3 build.

**Goal:** Compress older NPC history according to personality and recorded emotion, then safely reduce active archive and save-ledger growth.

**Architecture:** Typed memory atoms and bounded evidence capsules support increasingly concise prose. Independent generations sever obsolete raw ancestry; save-root tracking and replay protection govern reclamation independently from summary quality.

**Tech Stack:** Existing C# Core, Unity adapter, FullSerializer save path, local Gemma 4 E4B Q4_K_M, Breeze-aware scheduling, .NET regression executables and Python fixture preflight. No new inference service.

**Spec:** `docs/superpowers/specs/2026-10-23-generational-npc-memory-design.md`.

## Global constraints and current status

- This plan and fixtures are prepared, not executed. The production feature is absent. Do not interpret fixture preflight as a model or runtime pass.
- Current V3 build owner controls Core/runtime/build/GPU/DFU. Start implementation only after that owner releases the frozen source checkpoint. Re-read current source and reconcile changes first.
- Use English model inputs. Use only active saved three-trait or verified authored characterization. Generic sidequest assignment does not replace traits.
- Normally compact sources strictly older than thirty game days. Under measured storage pressure, eligible sources strictly older than seven days may receive less aggressive summaries. Younger sources stay detailed. Personality and historical emotion affect both tiers.
- Byte values in `docs/test-fixtures/aged-memory-v1/policy-targets.json` are tuning proposals. Protected overflow is reported, not silently discarded. All storage figures include metadata and use encoded UTF-8 bytes.
- Never alter structured identity, quest state, obligations or relationship values through summary text. Never replay historic effects during migration or recall.
- No live archive deletion until independent generations, durable-save confirmation, root census, exact replay protection and portable export/import all pass.
- Keep scope-specific changes separate from the many unrelated dirty files. Stage exact owned files only after review; no blanket `git add`, reset or stash.

## Prepared assets and execution contract

`docs/test-fixtures/aged-memory-v1/semantic-cases.json` contains 32 development cases, including a matched personality pair. `protocol-cases.json` contains 40 state/storage/scheduler scenarios. Both preserve `execution_status=not_run`. Execution writes separate result records instead of changing oracle fixtures.

For each future attempt record `{caseId, sourceManifestHash, codeRevision, modelHash, templateHash, seed, attemptIndex, rawOutputPath, nativeValidation, semanticRubric, appliedEffects, reclaimedBytes, elapsedMs}`. Attempt zero plus at most two repairs; keep every output. Missing or unexecuted cases are blocked/not-run, never passes. Record protected-content fidelity separately from paraphrase quality and storage savings. These development fixtures do not constitute held-out evidence; create additional unseen cases before release.

## Task 1: age/pressure policy and immutable summary contract

**Create:** `src/PeopleOfTheIliacBay.Core/NpcAgedMemoryPolicy.cs`, `NpcAgedMemoryRecord.cs`; `tests/AgedMemory.Regression/AgedMemory.Regression.csproj`, `Program.cs`.

**Proposed interfaces:** `NpcAgedMemoryTier { Detailed, EarlySummary, MonthlySummary, Theme }`; `NpcAgedMemoryPolicy.Tier(double ageSeconds, bool pressure)` returns a tier and rejects negative/nonfinite age. Add a separate immutable pressure-state transition taking current state, per-NPC bytes and aggregate bytes; high/low thresholds come from a policy object. Record fields and limits are defined in the spec. Freeze/copy collection inputs and reject mismatched observer/source references.

- [ ] Add age boundary tests before implementation, then run them red:

```csharp
Check.Equal(NpcAgedMemoryTier.Detailed, NpcAgedMemoryPolicy.Tier(7 * 86400d, true));
Check.Equal(NpcAgedMemoryTier.EarlySummary, NpcAgedMemoryPolicy.Tier(7 * 86400d + 1, true));
Check.Equal(NpcAgedMemoryTier.Detailed, NpcAgedMemoryPolicy.Tier(8 * 86400d, false));
Check.Equal(NpcAgedMemoryTier.MonthlySummary, NpcAgedMemoryPolicy.Tier(30 * 86400d + 1, false));
Check.Equal(NpcAgedMemoryTier.Theme, NpcAgedMemoryPolicy.Tier(90 * 86400d + 1, false));
```

- [ ] Implement strict boundaries, pressure hysteresis and independent byte/count validation. Early summaries are less aggressive; age tier does not remove mandatory anchors.
- [ ] Add S01–S04/S33–S40 and malformed-ID/nonfinite-time/UTF-8 multibyte/duplicate-atom/defensive-copy cases. Persist and reload with FullSerializer, not only System.Text.Json.
- [ ] Run `dotnet run --project tests/AgedMemory.Regression/AgedMemory.Regression.csproj -- policy`; require all assertions pass. Review and commit only this slice.

## Task 2: durable historical emotion and replay-safe ledger retirement

**Create:** `src/PeopleOfTheIliacBay.Core/NpcHistoricalSalienceCapsule.cs`, `NpcExperienceReplayState.cs`.
**Modify:** `NpcExperienceInbox.cs`, `NpcExperienceCommit.cs`, `NpcRelationshipLedger.cs`, `ConversationMemorySalience.cs`, `WorldState.cs` and each native receipt producer identified by an explicit repository search for `TryEnqueueNpcExperience`/`TryEnqueueNpcObservedSpeech`.

**Interfaces:** capsule fields follow the spec and reference accepted receipts. `NpcExperienceReplayState` offers a pure admission check for `(producerId, incarnation, sequence)` plus immutable closed-epoch/watermark transitions. No sequence value may come from model text. Legacy exact IDs use a separate exact lookup; they cannot be silently converted into sequence watermarks.

- [ ] Write S23/S24/S32 first: apply one real receipt, persist a capsule, attempt retirement, reload, replay the receipt, assert unchanged mood/trust/inventory. Include pending/deferred gaps, out-of-order sequences and a branched older save.
- [ ] Inventory all dedupe consumers before deleting any row. Map them in a test fixture with producer, admission point, last possible replay and root lifetime. If a legacy producer cannot prove closure, retain exact replay keys and expose its bytes as residual growth.
- [ ] Capture nonspeech native outcomes as well as attributed speech. Preserve accepted/applicable effect masks and unknown responsibility. Retire full payload only after durable capsule plus replay protection is demonstrated.
- [ ] Run `dotnet run --project tests/AgedMemory.Regression/AgedMemory.Regression.csproj -- replay` and existing Core tests. Require zero duplicate effects and unchanged authoritative snapshots. Do not mark complete if only a recent-ID cache protects replay.

## Task 3: model summary proposal and strict publication gate

**Create:** `src/PeopleOfTheIliacBay.Core/NpcAgedMemoryDraft.cs`, `NpcAgedMemoryValidation.cs`, `NpcAgedMemoryPrompt.cs`.
**Modify:** `ConversationMemoryCompactionJob.cs`, `ConversationMemorySalience.cs`; keep the existing extractive cache working during migration.

**Interfaces:** immutable draft captures owner/save epoch, branch/head, NPC, source hash, active-profile/appraisal hash and policy. `NpcAgedMemoryValidation` returns either a validated record or explicit constraint failures; validation never applies state effects. Native typed atom references are separate from untrusted prose. The model proposes source-ranked atoms and an English summary, not archive paths or deletion instructions.

- [ ] Write rejection fixtures for M01–M17/M21–M32 plus malformed/oversized responses and S09–S12. Test unsupported actor substitution, lost negation, dropped retraction and quantity changes before accepting any draft.
- [ ] Implement source-visible candidate construction, mandatory anchor retention, bounded prompt paging and at most two targeted repairs. Failed drafts keep source rows. Use correct current Gemma template and queue ownership; do not change model residency.
- [ ] Run `dotnet run --project tests/AgedMemory.Regression/AgedMemory.Regression.csproj -- draft` using injected outputs first. Run actual Gemma cases only after GPU handoff. Report per-attempt fidelity and the remaining unverified semantic claims; a parse success is not a semantic pass.
- [ ] Compare M18/M19 under identical evidence and different profiles; compare early/monthly/theme output for S38. Expect emphasis/detail differences, never factual differences. Have a human review fresh unseen summaries before enabling destructive storage work.

## Task 4: independent generations and save transaction

**Create:** `src/PeopleOfTheIliacBay.Core/NpcMemoryGenerationManifest.cs`, `NpcMemoryGenerationStore.cs`.
**Modify:** `SharedConversationArchive.cs`, `ConversationArchiveState.cs`, `WorldState.cs`, `mod/Scripts/PeopleOfTheIliacBayMod.cs` (save hooks only).

**Interfaces:** a staged generation returns an immutable proposed checkpoint. Its manifest enumerates exact content files and protected references. `Stage -> VerifyReload -> PublishCheckpoint` never deletes existing chunks. Parent-generation audit hashes are not loader dependencies. Keep a legacy v1 reader and introduce an explicit new-format discriminator; do not reinterpret the old `.pim` magic.

- [ ] Start with S10/S11/S13–S17/S20: inject failure at each publication boundary; old roots must load. Test successful compacted reload in an empty directory containing only the new generation's closure.
- [ ] Write files to staging, flush content, validate manifest and complete reload, then publish through an actual save transaction. Confirm the native save-completion hook from DFU source; `GetSaveData` alone is insufficient. If no reliable confirmation exists, keep collection disabled.
- [ ] Keep ordinary pending appends bound to the new generation once committed; reject concurrent head changes in first version. No speculative rebase.
- [ ] Run `dotnet run --project tests/AgedMemory.Regression/AgedMemory.Regression.csproj -- generations`; require crash matrix and original-save hashes intact.

## Task 5: recall/export compatibility and safe reclamation

**Create:** `src/PeopleOfTheIliacBay.Core/NpcMemoryRootRegistry.cs`, `NpcMemoryReclamation.cs`.
**Modify:** `ConversationMemoryCompactionRecall.cs`, `ConversationArchiveState.cs`, `tools/MemoryArchiveTool/Program.cs`, `tools/TestPublishedMemoryArchiveTool.ps1`; update save-transfer callers found by their existing archive-export API use.

**Interfaces:** the root registry snapshots retained saves, active checkpoint, reader/export pins and recovery roots under a revision. Reclamation accepts only a complete verified root snapshot, computes reachability, rechecks the revision under the publication lock, quarantines unreachable files and then deletes only eligible exact paths. Export manifest v2 enumerates generation closure; v1 packages remain readable.

- [ ] Run S17–S22 first using temporary fake save roots. Include inaccessible directory, root added between mark and sweep, open export, copied old save, branch rewind, corrupt manifest, path traversal and a missing source capsule.
- [ ] Make recalled summary records distinguish remembered gist from verbatim quotations. Keep original timestamps and uncertainty; never present generated summary time as event time.
- [ ] Integrate archive verification/import/export before enabling reclamation. Test round-trip in an empty archive after eligible raw files are removed.
- [ ] Run `dotnet run --project tests/AgedMemory.Regression/AgedMemory.Regression.csproj -- storage` and the published archive-tool regression. Require every retained root loads and zero reachable files are reclaimed. Report old-save pinned bytes separately.

## Task 6: idle scheduling, foreground preemption and throughput

**Create:** `mod/Scripts/PeopleOfTheIliacBayMod.AgedMemory.cs` for the new runtime adapter; a focused native probe after source tests pass.
**Modify:** `PeopleOfTheIliacBayMod.MemoryCompaction.cs`, `tests/MemoryCompactionRuntime.Regression/Program.cs`.

**Interfaces:** immutable work snapshots enter the existing single Gemma queue. Native proximity facts are captured on Unity's thread. Workers process plain data; callbacks recheck all owner/epoch/head/context/foreground bindings. Runtime supplies actual spatial proximity for all relevant loaded NPCs, not just last speaker.

- [ ] Add S25–S29 and save/scene/critical-health cancellation cases to the injected scheduler harness. A pending model callback after cancellation must have zero publication effects.
- [ ] Add ten-second quiet hysteresis and pressure-aware fair scheduling. Keep a daily attempt budget that cannot reset on every new archive head. Break CPU and I/O into bounded work; measure, do not assume, the proposed 2 ms frame slice.
- [ ] Run `dotnet run --project tests/MemoryCompactionRuntime.Regression/MemoryCompactionRuntime.Regression.csproj`, then RuntimeCompile. After the owned handoff, measure real Gemma abort latency and Breeze contention in DFU.
- [ ] Record scene-wide NPC arrival, dialogue start, TTS, combat and save/load native traces. Quiet starvation must show a backlog metric, not permanent invisible suspension or foreground stalls.

## Task 7: soak, storage accounting and release evidence

**Create:** `tests/AgedMemory.Regression/Soak.cs`, `tools/RunAgedMemoryAcceptance.ps1`, `docs/2026-10-23-aged-memory-acceptance.md` when results exist.

- [ ] Run S05–S08/S30 plus repeated M05/M06/M13/M17/M26/M27 merges over one and ten simulated years at 100/1,000/10,000 NPCs. Include many branch roots and little quiet time.
- [ ] Measure actual encoded active generation, roots, capsules, replay index, consumed inbox and accepted-outcome ledgers; measure peak RAM/load time. Do not substitute policy arithmetic for byte measurements.
- [ ] Demonstrate steady state under a fixed encounter workload with enough idle capacity. Explicitly report protected overflow, legacy dedupe growth and storage pinned by retained saves; no universal hard-cap claim.
- [ ] Re-run Core, salience, compaction, scheduler, save portability and targeted native suites after integrating all slices. Fill the acceptance report with every attempted case, failures, blocked cases and source/package hashes.
- [ ] Update ROADMAP and Obsidian only with verified implementation/acceptance status. Production reclamation remains off until all destructive-release invariants pass.

## Preparation self-review

The seven tasks cover age/pressure policy, typed summaries, historical emotion, replay protection, source-backed semantic validation, independent generation storage, durable save confirmation, branch/root retention, portability, idle scheduling and capacity evidence. Proposed types are explicitly new. No runtime implementation or deletion is performed by this plan. Final test cardinality is 72 prepared cases; additional generated soak trials and unseen semantic cases are required during execution.
