# NPC memory compaction design notes

Status: compaction remains design-only, 21 October 2026. A tested read-only scanning prerequisite exists; no archive summaries, history deletion, schema change, scheduler, or compaction model request is implemented.

Backlog: [idle background scheduling and age-based memory compaction](ROADMAP.md). This document records the requested future behavior, not an enabled maintenance job.

## Goal and decision

After one in-game calendar month, preserve each NPC's important shared events and information as short, source-linked bullets on disk. Run compaction only as cancellable background maintenance. Player dialogue, NPC conversation, combat, and other foreground model work always take priority.

Start with **semantic retrieval compaction**, not physical deletion. Store a validated summary for older history and use it to reduce prompt context. Keep the original immutable archive chunks. A summary chain alongside the raw chain does not save archive bytes; the raw head still pins every parent. A later compacted checkpoint can start a new base from the summary plus newer raw records, shrinking exports for that checkpoint while leaving older branches untouched. Deleting old chunks requires reference ownership across all save slots and exported backups.

## Current implementation and limits

The build-23 prerequisite adds `SharedConversationArchive.OpenScan(checkpoint).ReadNextBatch(maximumRecords)` and `ConversationMemoryCompactionEligibility.IsEligible(recordAt, now)`. Each scan call decodes at most one existing checksum-validated chunk (bounded to 2 MiB) and returns at most 128 detached entries. Stable addresses contain archive ID, pinned checkpoint head, chunk hash, record index and person ID. Scanning does not load every NPC's history or change an archive. A record becomes eligible strictly after 2,592,000 game seconds, matching the native 30-day month; equality remains in the recent window.

Scan order is newest archive chunk first and reverse stored record index, not global timestamp order. Journal grouping and backdated imports can place an older eligible row after a younger row. Future maintenance must age-check every record instead of stopping at its first ineligible row. Core 1485/1485 and the native build pass; focused tests cover boundary/time jumps, multiple chunks, detached rows, branch isolation, corrupt/missing reads and mixed-person backdated order. These are prerequisites, not compaction or storage savings.

`SharedConversationArchive` in `src/PeopleOfTheIliacBay.Core/SharedConversationArchive.cs` stores immutable gzip-compressed chunks. Each chunk contains up to 128 raw `NpcMemoryEntry` records, hashes its uncompressed bytes with SHA-256, and links to one parent hash. `ConversationArchiveCheckpoint` in each `WorldState` stores archive ID plus head. `SnapshotForSave()` appends pending records and saves the resulting checkpoint; loading follows only that checkpoint's ancestry. It cannot replace or rewrite an old chunk in place.

Raw entries currently carry game timestamp, displayed date, place, player text, NPC text, observation, and a coarse source (`native`, `model`, `action`, `observed`, or `unavailable`). Player text is a claim, not verified truth. NPC dialogue is evidence of what the NPC said, not proof that the described event occurred. `observed` records represent native recorded experience. The archive does not currently store a stable per-record event ID; a future summary needs a stable source address such as `(archiveId, chunkHash, recordIndex, personId)`.

`RecallConversation()` already limits retrieved prompt text and prefers relevance plus a recent meeting. That is retrieval truncation, not disk compaction. `Load()` still reads the full checkpoint ancestry into memory. Gzip reduces bytes without preserving or extracting meaning; it does not replace semantic summarization.

The current `MemoryArchiveTool` export manifest is format version 1. It lists the exact save checkpoint and ancestry, then relative path, byte length, and SHA-256 for every packaged file. Import requires the exact `.pim` ancestry set and rejects unlisted archive files. New summary files or a new checkpoint-root format therefore need explicit manifest/import changes. Existing exports and their ancestors must remain readable.

DFU's native calendar has 30 days per month and 360 days per year (`AppointmentCalendar`, `AppointmentChat`). Stored memory time uses game seconds, with 86,400 seconds per game day. Normal world time runs at 2× real time, so maintenance eligibility must use native game time, never elapsed wall time or a real-time timer. One calendar month is 30 game days; derive eligibility from each record's saved game timestamp and current native clock.

The approved idle-work item is in `docs/ROADMAP.md` under IMM-16: run small resumable batches only with no active player/NPC conversation, prefer no relevant nearby NPCs and spare shared-model capacity, and pause/cancel when foreground work needs resources. The repository has no production compaction scheduler or summary writer today.

## Summary record contract

Use a separate, versioned compaction record rather than disguising generated prose as an `observed` event. Recommended storage is a second immutable, content-addressed summary chain under the same `PeopleMemory/<archiveId>/` namespace. Add a `SummaryHead` to a version-2 checkpoint; each save then pins both raw-memory ancestry and summary ancestry. A summary chunk stores one NPC/month result and its exact source raw head. Keep the existing `.pim` encoding unchanged. Each summary covers one NPC and one closed game-time interval. Store:

- Stable person ID, calendar interval, source archive ID and checkpoint head, compaction policy version, and job revision.
- A bounded ordered list of short bullets. Each bullet has event time, place if known, participants, source kind, certainty, outcome/status, and one or more stable source addresses.
- Explicit attribution labels: `native_observed`, `player_claim`, `npc_said`, or `model_interpretation`. Preserve the original claimant/speaker identity when known. Unknown actor remains unknown.
- Separate fields for plan/promise, accepted commitment, completion, cancellation, refusal, breach, and uncertainty. Never rewrite a planned or promised act as completed.
- Source archive hashes and record indices sufficient to inspect the raw source. Do not cite display names as stable identity.

Include high-salience events such as family/relationship changes, explicit promises and their later outcomes, attendance/no-show, help or abandonment during danger, injury/healing, money or gifts, serious insult/threat, repeated conflict, confession, and stable preferences the NPC actually learned. Retain uncertainty and counterevidence. Omit low-value repeated greetings and transient wording when no durable information is added.

Compaction must never write authoritative state. Current appointments, relationships, crime, law enforcement, quest ownership, money, inventory, and task outcomes remain in their native ledgers. A summary may point to those records as source evidence; it cannot create, settle, pardon, or overwrite them. Retrieval filters secret or unauthorized facts before ranking summaries, just as it does raw memories.

## Minimal delivery phases

### Phase 1: eligibility and deterministic source scan

Add a game-time month-boundary helper using the native 30-day month and 86,400-second game day. Select only closed intervals at least 30 game days old. Keep the newest month raw for exact turns and recent continuity. Scan one person's checkpoint ancestry in bounded chunk/record slices; persist a cursor and source head so large histories do not require a full synchronous load.

Do not start work during an open Talk session, `conversationBusy`, combat, or another active model request. Preferably require no relevant NPC within a documented native proximity threshold. If the game cannot establish that condition reliably, defer rather than guess. Acquire a low-priority lease from the existing shared local-model service; never start a second Qwen instance. If the service has no priority/cancellation contract, add that scheduler seam before sending maintenance requests.

### Phase 2: bounded generation and validation

Send one NPC/month batch through the existing model service only when it has spare capacity. Keep prompt and output token budgets fixed and small. Pass source-labeled raw records, source IDs, and current authoritative-state references as data, not instructions. Request only the versioned bullet schema. Strictly reject malformed JSON, duplicate fields/IDs, missing source addresses, out-of-window records, unsupported attribution labels, unsupported claims, oversized output, and facts with no source. Model output remains untrusted until validated.

Keep in-progress output private to the job. Recheck foreground demand before every request and before publication. Cancel or abandon promptly when dialogue, NPC conversation, combat, or a higher-priority model request starts. A cancelled or failed job leaves no visible partial summary. Persist source head, interval, cursor, job revision, and cancellation state so reload can resume only when the source is still valid.

### Phase 3: atomic publish and retrieval

Publish a complete validated summary to the immutable summary chain. Bind publication to the exact raw source head and verify that the source chunks remain in the active checkpoint ancestry. Advance `SummaryHead` only after the full record and hash validate. If the save rolled back, switched branch, or changed the selected interval, discard and restart the job. Older save snapshots keep their previous `SummaryHead`, so they cannot see summaries from a later branch or future month.

Update retrieval to select source-linked summaries for compacted intervals while retaining recent raw exchanges, current authoritative context, and relevant exact records. Label summary bullets as summaries with their attribution and certainty. Allow raw-source fallback when a summary is missing, invalid, or filtered by disclosure rules. Never let a summary introduce another speaker's private memory or a hidden quest fact.

### Phase 4: compatibility and optional physical reclamation

Version the summary record format independently from `.pim` chunks. Extend the checkpoint format and increment the world-save schema. Bump portable manifest format to version 2 for packages with summary ancestry; include exact raw and summary ancestry plus relative paths, byte lengths, and hashes. Update `MemoryArchiveTool` import/export to enforce bounded sizes/counts and validate checkpoint/source references. Preserve manifest version-1 package imports. Older tools must reject version-2 packages clearly; they must not import them while silently dropping the summary.

Do not delete raw chunks in this feature. A future compacted checkpoint can point at a verified summary base plus later raw records, so its new portable export need not carry raw ancestry before that base. Old save slots keep their original heads. Exported ZIPs are independent ancestry roots and can outlive local saves; current tooling does not register every external copy or backup. A later disk-reclamation design needs explicit branch/export reference ownership, an opt-in retention policy, a verified compacted root, backup/recovery procedure, and tests proving every retained slot/package loads before deleting anything. Until those references are retired, old chunks remain pinned and total disk growth is expected.

## Acceptance and measurements

- Boundary tests prove a record becomes eligible only after one native calendar month, including month/year rollover, save/load, time skip, and pause. No real-time clock controls eligibility.
- Summary tests cover promises versus completed acts, player claims versus witnessed outcomes, unknown culprit, threats/insults, family facts, conflicting reports, source IDs, and no unsupported fact. Every bullet resolves to raw evidence.
- Save/branch tests compact a copy, restore old and new checkpoints independently, and prove rollback never sees future summaries. Missing/corrupt summary data falls back to raw history without changing authoritative ledgers.
- Package tests verify exact summary/source ancestry, version-1 compatibility, versioned-package rejection by old readers, file hashes, limits, and interrupted publication. No raw chunk is deleted.
- Scheduler tests start dialogue, nearby-NPC activity, combat, foreground Qwen work, reload, cancellation, and source revision changes during a job. Maintenance must yield or cancel; foreground request latency and frame time remain within an agreed measured budget. Test that one shared model instance serves foreground and maintenance work.
- Report prompt-token reduction separately from archive bytes saved. Phase 1 can reduce prompt size but cannot claim disk reclamation. Measure archive bytes, summary bytes, scans per frame, model request count/duration, foreground wait, frame-time impact, cancellation latency, resume correctness, and starvation.

## Source references

- `src/PeopleOfTheIliacBay.Core/SharedConversationArchive.cs`: immutable chunks, ancestry, gzip, SHA-256, commit/load.
- `src/PeopleOfTheIliacBay.Core/ConversationArchiveState.cs`: save checkpoint publication and archive restore.
- `src/PeopleOfTheIliacBay.Core/ConversationMemory.cs`: raw record fields, provenance, bounded retrieval, full journal loading.
- `src/PeopleOfTheIliacBay.Core/AppointmentCalendar.cs` and `AppointmentChat.cs`: native 30-day months and 360-day years.
- `tools/MemoryArchiveTool/Program.cs`: manifest version 1, exact ancestry export/import, relative file hashes and size limits.
- `docs/ROADMAP.md`: approved idle background compaction TODO under IMM-16; persistent-memory retrieval/disclosure requirements.
