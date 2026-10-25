# Conversation Memory Index Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace repeated full-journal feature extraction and ranking with a compact, exact per-NPC recall index that preserves current recall output and stays within explicit memory and main-thread budgets.

**Architecture:** Keep canonical journal entries and shared archive persistence unchanged. Add an exact packed feature vocabulary and flat time-sorted postings owned by `WorldStateStore`; update them on append, invalidate them on state replacement, and retain no more than four active NPC indexes under a 20,000,000-byte aggregate cap. Build cold indexes cooperatively on the Unity main thread in bounded slices, while the current scan remains the correctness fallback until an index is complete.

**Tech Stack:** C# 7.3, `netstandard2.0` Core, `net472` Unity runtime, Unity 2019.4.40f1, .NET 8 Core tests.

**Spec:** `work/memory-index-benchmark/README.md`, `work/memory-index-benchmark/results.log`, and the retrieval behavior in `src/LivingCities.Core/ConversationMemory.cs` plus `src/LivingCities.Core/ConversationRetrieval.cs`.

## Global Constraints

- Core targets `netstandard2.0`, C# 7.3, with warnings treated as errors.
- Preserve per-person and save-branch isolation; never index future-dated entries for a query cutoff.
- Preserve current tokenization, stemming, concept features, weights, score ordering, tie ordering, pronoun-only follow-up, memory excerpts, role labels, and maximum-character behavior.
- Indexes are disposable derived data. Canonical journals, archive checkpoints, snapshots, and native save payloads remain authoritative and unchanged.
- No Unity API calls, Unity objects, worker threads, native inventory operations, or model requests inside Core index code.
- Retain no more than four active per-person indexes and no more than 20,000,000 accounted bytes across them; target at most 5,000,000 bytes for one 10,000-entry index, excluding existing journal strings.
- Never use hash equality as feature equality. Hash slots must verify the exact normalized feature bytes/chars before returning an ID.
- Cold construction must yield after at most 32 entries or 1.5 ms per slice, whichever comes first. Measure actual maximum slice time; do not infer smoothness from warm query latency.
- Do not silently return partial retrieval. While an index is incomplete or bypassed by the byte cap, use the existing full-scan behavior.

---

### Task 1: Freeze current recall semantics with scan-reference regressions

**Files:**
- Modify: `tests/LivingCities.Tests/ConversationRetrievalTests.cs`
- Modify: `tests/LivingCities.Tests/ConversationMemoryTests.cs`
- Modify: `tests/LivingCities.Tests/Program.cs`

**Interfaces:**
- Consumes: public `WorldStateStore.RememberConversation`, `RememberObservation`, `RecallConversation`, `Snapshot`, and `Restore`.
- Produces: deterministic assertions for selection order, exact role-labeled excerpt text, time cutoff, append, restore, branch, privacy, and maximum-character behavior. These remain the oracle while the production retrieval implementation changes.

- [ ] **Step 1: Add output-parity fixtures before changing Core.** Extend the current retrieval corpus with the same records queried at several cutoffs, including equal timestamps, out-of-order append timestamps, an observed event, future-only matching terms, an explicit query after a pronoun-only turn, and broad common terms. Capture expected rendered output or ranked record labels from the current implementation; include budgets 256, 1,000, 2,000, and 5,000 characters.

- [ ] **Step 2: Add branch and empty-result checks.** Assert that a snapshot recalled after append excludes the appended record, restore-to-base removes it, branch-B append does not leak branch-A text, Bob never sees Alice's entries, and an empty memory still returns the current bounded `MEMORY ABSENT` wording.

- [ ] **Step 3: Run the focused tests on the unchanged scan implementation.** Run: `dotnet run --project tests/LivingCities.Tests/LivingCities.Tests.csproj --configuration Release`. Expected: all tests pass; this records the baseline before index code exists.

- [ ] **Step 4: Verify the test corpus distinguishes unsafe ranking shortcuts.** Add one eligible older entry and enough future-dated entries to flip IDF ordering if future records are counted. Assert the current cutoff selects the same winner regardless of future records.

- [ ] **Step 5: Run the focused tests again.** Expected: the historical IDF case passes with future entries excluded and the same visible text order.

### Task 2: Add exact packed feature vocabulary and flat posting storage

**Files:**
- Create: `src/LivingCities.Core/ConversationMemoryIndex.cs`
- Modify: `src/LivingCities.Core/ConversationRetrieval.cs`
- Create: `tests/LivingCities.Tests/ConversationMemoryIndexTests.cs`
- Modify: `tests/LivingCities.Tests/Program.cs`

**Interfaces:**
- Consumes: validated `NpcMemoryEntry` records from one `NpcConversationMemory` journal, the existing feature extraction rules, and normalized entry index as the deterministic tie key.
- Produces: `ConversationMemoryIndex.BeginBuild(...)`, `AdvanceBuild(int maximumEntries, long maximumTicks)`, `IsComplete`, `OwnedBytes`, `Append(NpcMemoryEntry entry, int journalIndex)`, and `CreateQuery(string query, double now)`.
- `CreateQuery` returns a per-call query object that exposes scores and excerpts without retaining query state in the shared index.

- [ ] **Step 1: Add tests for the vocabulary before implementation.** Test exact equality for word, stem, and concept feature keys; force identical test hashes for distinct keys and assert they remain separate; test repeated feature interning returns one ID; test Unicode normalization follows the current tokenizer exactly.

- [ ] **Step 2: Run only the new index tests and confirm they fail.** Run: `dotnet run --project tests/LivingCities.Tests/LivingCities.Tests.csproj --configuration Release`. Expected: failures name the missing index/vocabulary behavior, not unrelated suite errors.

- [ ] **Step 3: Implement the exact compact vocabulary.** Store normalized feature text once in a packed `char[]` with per-feature offset and length arrays. Use an open-addressing table of feature hashes and IDs; resolve every occupied-slot match by comparing exact packed chars. Keep feature kind as a byte so score weights remain `word=1`, `stem=0.65`, `concept=0.5`. Do not retain `HashSet<string>` or a string key per journal-feature occurrence.

- [ ] **Step 4: Implement flat posting arrays.** Store postings as one flat `int[]` with per-feature start/count arrays, plus one `int[]` of journal indices sorted by `(At, journalIndex)`. Keep one query-scratch generation-mark array, one score array, and reusable candidate storage per index. Do not create one class, `List<int>`, or array per feature.

- [ ] **Step 5: Implement bounded building.** The build cursor processes no more than 32 journal records per call and stops sooner when the supplied elapsed-tick budget is reached. It creates no background task and touches no Unity API. It can resume at the next journal index without rescanning completed entries.

- [ ] **Step 6: Account owned capacity.** `OwnedBytes` includes all feature text buffers, hash slots, posting arrays, timestamp-order arrays, query scratch, append deltas, and their collection capacity. Exclude canonical journal strings and the existing journal object graph only. Add a test that constructs 10,000 representative entries, completes the index in slices, and asserts `OwnedBytes <= 5_000_000`.

- [ ] **Step 7: Run the focused tests.** Expected: exact collision test passes, index owns only primitive/packed arrays, and the representative 10k fixture meets the explicit byte bound.

### Task 3: Preserve scoring, historical IDF, ordering, and excerpt selection

**Files:**
- Modify: `src/LivingCities.Core/ConversationMemoryIndex.cs`
- Modify: `src/LivingCities.Core/ConversationRetrieval.cs`
- Modify: `tests/LivingCities.Tests/ConversationMemoryIndexTests.cs`
- Modify: `tests/LivingCities.Tests/ConversationRetrievalTests.cs`

**Interfaces:**
- Consumes: a complete index, query text, and absolute cutoff `now`.
- Produces: ranked eligible journal indices and the same scoring context used by `AppendMemory` and excerpt clause selection.

- [ ] **Step 1: Add time-cutoff regressions.** Cover cutoff before an entry, cutoff exactly at an entry's timestamp, same-time journal-order ties, and a future-only fact whose document frequencies would change the top match if counted.

- [ ] **Step 2: Run the new tests against the current full scan.** Expected: baseline passes; future additions do not alter recall before their timestamp.

- [ ] **Step 3: Implement per-query IDF from eligible posting prefixes.** Binary-search each queried feature posting at `now`; compute document frequency and population count from only entries with `At <= now`. The `now` value must be applied when the query context is created, not baked into the index.

- [ ] **Step 4: Preserve top-result and zero-score semantics.** Sum the current weighted IDF scores. Order by score descending, then `At` descending, then journal index descending. For score-zero entries, enumerate the timestamp-order array newest-first only as far as `maximumCharacters` can use them; do not assume the default 2,000-character budget is the only supported value. A query with matches may not drop tied results needed by a larger requested budget.

- [ ] **Step 5: Preserve implicit pronoun resolution.** When feature extraction returns no explicit topic and the current pronoun rule matches, derive follow-up features from the latest eligible non-observed entry, with timestamp and journal-index tie order matching current code.

- [ ] **Step 6: Preserve memory clause selection.** Route `AppendMemory`'s `Excerpt` through the same per-call query context so clause scores use the same query features, eligible population, time cutoff, and weights. Keep all quote labels, speaker attribution, truncation, and missing-answer wording unchanged.

- [ ] **Step 7: Compare scan and indexed output byte-for-byte.** For every Task 1 case, compare complete `RecallConversation` strings for all budgets and cutoffs. Expected: identical selected records, order, excerpt clauses, punctuation, and labels.

### Task 4: Add append deltas, four-slot cache, and restore invalidation

**Files:**
- Modify: `src/LivingCities.Core/ConversationMemoryIndex.cs`
- Modify: `src/LivingCities.Core/ConversationMemory.cs`
- Modify: `src/LivingCities.Core/ConversationArchiveState.cs`
- Modify: `src/LivingCities.Core/WorldState.cs`
- Modify: `tests/LivingCities.Tests/ConversationMemoryIndexTests.cs`
- Modify: `tests/LivingCities.Tests/ConversationMemoryTests.cs`

**Interfaces:**
- Consumes: the owning `WorldState` identity, person ID, current journal, and `Remember`/restore lifecycle.
- Produces: an MRU cache with `GetOrCreate(personId, stateIdentity, journal)`, `AdvanceBuild(...)`, `Append(personId, entry, journalIndex)`, and `Clear()`.

- [ ] **Step 1: Add failing cache lifecycle tests.** Assert cache size never exceeds four, fifth-person access evicts the least-recently-used person, an access refreshes recency, state identity replacement clears all slots, append updates an existing complete index, and appending after restore cannot expose the abandoned branch.

- [ ] **Step 2: Add byte-cap tests.** Create indexes with different feature densities; assert aggregate `OwnedBytes` never exceeds 20,000,000. If the next index would exceed the cap, assert it is not cached and recall falls back to the scan path without partial output.

- [ ] **Step 3: Run the cache tests and confirm RED.** Expected: current implementation has no bounded index cache and these tests fail for the intended missing behavior.

- [ ] **Step 4: Implement MRU eviction and ownership.** Store no more than four per-person indexes. Count index capacity bytes before admission and on growth. Evict least recently used indexes until a new index fits; if it still cannot fit alone, mark that person scan-only. Do not store copies of `WorldState`, `NpcConversationMemory`, or journal text in the cache.

- [ ] **Step 5: Integrate append updates.** After `Remember` adds an entry to the canonical in-memory journal, update an existing person's complete index by a compact append delta. If the index is still building, append the canonical new entry to its build queue and keep journal index order. Do not rebuild the full index per conversation.

- [ ] **Step 6: Integrate invalidation.** Clear all cache slots when `RestoreCore` replaces `state`; also clear them when `MemoryFor` detects a different `state` reference. Do not clear on `SnapshotForSave`, because save checkpointing commits and clears pending entries while the current merged memory journal remains the active branch. Never key cache validity by timestamp alone.

- [ ] **Step 7: Run cache, archive, and branch regressions.** Run: `dotnet run --project tests/LivingCities.Tests/LivingCities.Tests.csproj --configuration Release`. Expected: append and branch behavior match the scan oracle, cache stays within four slots and 20,000,000 bytes, and save snapshots do not invalidate correct active recall.

### Task 5: Drive cold builds cooperatively from the Unity main thread

**Files:**
- Modify: `src/LivingCities.Core/ConversationMemory.cs`
- Modify: `mod/Scripts/LivingCitiesMod.cs`
- Modify: `mod/Scripts/LivingCitiesMod.DialogueMemory.cs`
- Modify: `mod/Scripts/LocalDialogueClient.cs` only if dialogue dispatch must await warmup
- Modify: `tests/LivingCities.Tests/ConversationMemoryIndexTests.cs`

**Interfaces:**
- Core exposes `QueueConversationRecallIndex(personId)`, `AdvanceConversationRecallIndexes(maximumEntries: 32, maximumMilliseconds: 1.5)`, and `IsConversationRecallIndexReady(personId)`.
- Runtime queues only the currently addressed NPC and the most recently active NPCs; `LivingCitiesMod.Update` advances a bounded slice on the Unity main thread.
- Dialogue context uses indexed retrieval only after completion. Before completion or for an over-cap journal, it uses the unchanged scan path; it never presents partial indexed results.

- [ ] **Step 1: Add Core cooperative-build tests.** Advance a 10k-entry journal in repeated calls; assert each call processes at most 32 entries, no entry is processed twice, queue order is stable, append during a partial build is included once, and recall remains exactly equal to the scan while warming.

- [ ] **Step 2: Run the tests against the existing code.** Expected: these fail because no cooperative build API exists.

- [ ] **Step 3: Implement Core queue and bounded advancement.** Keep all index mutations synchronous and caller-thread-owned. Do not call `Task.Run`, locks, Unity APIs, filesystem APIs, or model APIs. Stop at either 32 entries or 1.5 ms measured by `Stopwatch`/ticks, whichever arrives first. Make no assumption that one call can finish an index.

- [ ] **Step 4: Queue active dialogue targets early.** At the earliest existing point when `memoryPersonId` is known—before `RecallConversation` is inserted into the model request—queue that person. If the index is still cold, let the current conversation proceed with exact scan output while subsequent frame slices build it; do not wait synchronously or lose the first prompt. Queueing and recall must remain person-specific.

- [ ] **Step 5: Advance from the existing mod update loop.** Call one bounded advance after `UpdateEmbeddedDialogue()` in `LivingCitiesMod.Update`; avoid scanning all saved NPCs or allocating a fresh `WorldState` snapshot per frame. Do not use `UpdatePopulation`'s slower refresh cadence as the only scheduler.

- [ ] **Step 6: Run Core suite and RuntimeCompile.** Run: `dotnet run --project tests/LivingCities.Tests/LivingCities.Tests.csproj --configuration Release`; then `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release`. Expected: all tests pass and runtime compile has zero warnings/errors.

### Task 6: Replace the throwaway benchmark with exact-layout performance acceptance

**Files:**
- Modify: `work/memory-index-benchmark/CompactIndexPrototype.cs`
- Modify: `work/memory-index-benchmark/Program.cs`
- Modify: `work/memory-index-benchmark/README.md`
- Modify: `work/memory-index-benchmark/results.log`

**Interfaces:**
- Consumes: real Core stores and the production index API.
- Produces: reproducible 1k/10k measurements for exact feature keys, build slices, retained bytes, total allocations, peak managed heap, incremental append, selective and broad queries, branch restore, and ranking parity.

- [ ] **Step 1: Keep the 1k/10k corpus and run it against exact packed keys.** Preserve paraphrase, implicit follow-up, future-only exclusion, temporal IDF rank flip, broad result, and another-person privacy cases. Add exact-key collision injection and the worst supported character budget.

- [ ] **Step 2: Measure cold build by slice.** Record every slice's entries processed, elapsed milliseconds, thread allocation bytes, max slice time, total build duration, total allocations, managed peak delta, `OwnedBytes`, and number of retained index arrays. Force full GC before retained-heap measurements; report accounted bytes separately from process heap noise.

- [ ] **Step 3: Measure append and branch restore.** Time one normal entry append and one append with new unique features; report allocated bytes. Restore a base state and branch B, then assert branch A text cannot be returned and index rebuild work starts from the restored state only.

- [ ] **Step 4: Measure worst broad query, not only rare terms.** For both implicit and broad phrase queries, record candidate count, median warm latency, average allocations, and complete output parity. The current prototype's 9,700-candidate cases at 10k records must remain present.

- [ ] **Step 5: Run the benchmark.** Run: `dotnet run --artifacts-path work/memory-index-benchmark/artifacts --project work/memory-index-benchmark/MemoryIndexBenchmark.csproj --configuration Release`. Expected: byte-for-byte visible recall parity; one index `OwnedBytes <= 5,000,000` at 10k records; aggregate cap invariant `<= 20,000,000`; no build slice processes more than 32 records; every measured slice stays at or below 1.5 ms on the named acceptance machine. If any memory target fails, stop and revise representation before native acceptance; do not restore the hash-only prototype.

### Task 7: Native dialogue/archive acceptance with isolated evidence

**Files:**
- Modify: `mod/Scripts/SharedMemoryRecallModelProbe.cs`
- Modify: `mod/Scripts/SharedMemoryArchiveProbe.cs`
- Modify: `tools/TownTest.ps1` only if a new explicit switch is required
- Modify: `docs/TESTING.md`

**Interfaces:**
- Consumes: an existing owned fixture and the actual save/load, archive retrieval, dialogue context, and local-model request path.
- Produces: separate markers for `INDEX_READY`, `RESTORED_ARCHIVE_CONTEXT`, `ROLE_ATTRIBUTION_CONTEXT`, `MODEL_ANSWER`, and per-slice timing. Context correctness is never inferred from a correct model answer.

- [ ] **Step 1: Define a restored-archive probe with an existing owned fixture.** Use an already-owned conversation record; do not edit or manufacture NPC memory in the native world. Save/load the same fixture and assert the restored branch selects an old paraphrased fact, excludes a future-only fact, and never returns another person's record.

- [ ] **Step 2: Assert prompt provenance before model execution.** Inspect the actual generated context and require both player and NPC claims to retain their correct role labels. Emit the context marker only after retrieval and attribution assertions pass.

- [ ] **Step 3: Exercise cooperative warmup with native timing.** Measure `LivingCitiesMod.Update` index slice duration and dialogue open-to-context latency for a cold restored archive and a warm repeat query. The first dialogue must use complete scan context while warming; no intermediate partial index may reach the model. Report index readiness separately from dialogue/model latency.

- [ ] **Step 4: Run the local model only after context assertions pass.** Use the already-configured local model via the existing probe. Ask a natural paraphrase, not a prompt containing the required answer. Record model answer correctness as a separate marker; do not treat model accuracy as evidence that retrieval selected the right memory.

- [ ] **Step 5: Repeat restore and branch isolation.** Load the base save, create a later branch using existing native flows, restore the base, and verify cache invalidation and no future branch leakage. Do not change fixture data by deleting journals or manufacturing route/NPC state.

- [ ] **Step 6: Record bounded native acceptance.** Update `docs/TESTING.md` with exact fixture/save slot, player build hash, slice max and total, archive context markers, answer markers, and failures. State whether only Core equivalence, native context, and model answer each passed; do not combine them into one claim.

## Acceptance Gates

- Core scan and indexed recall produce byte-identical text across the characterization corpus, including future cutoffs, same-time ties, branch restore, pronoun follow-up, clause excerpts, and every supported output budget.
- Exact packed features resolve collisions by content comparison; no hash-only equality is permitted.
- A 10,000-entry index accounts for no more than 5,000,000 bytes excluding canonical journal storage; active indexes never exceed four people or 20,000,000 aggregate bytes.
- Build slices process at most 32 entries and measure at most 1.5 ms on the selected acceptance machine; record both total cold build time and peak managed allocation. The 196 ms/153 MB hash prototype is not an acceptable implementation result.
- Append updates existing indexes without a full rebuild. Restore/state replacement drops all old branch indexes before the next recall. SnapshotForSave preserves correct active state.
- No partial retrieval reaches dialogue context; scan fallback remains available for warming and over-budget journals.
- Native acceptance separately proves restored archive selection, privacy, role attribution, timing and local-model answer behavior.
