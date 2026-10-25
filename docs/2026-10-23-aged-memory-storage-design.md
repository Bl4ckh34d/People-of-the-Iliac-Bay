# Aged NPC memory and idle maintenance — design, not implementation

The user requires genuine summaries of conversations and events older than weeks or months, with selection shaped by personality, relationships and recorded emotional significance. Maintenance should run outside conversations, preferably while the player is alone and no NPC is nearby. This supersedes keeping every original transcript forever as the final storage policy. The existing extractive cache remains an intermediate implementation.

## Verified storage boundary

`ConversationArchiveState.SnapshotForSave` commits pending entries into a shared archive and stores a checkpoint in the save. Individual saves therefore do not duplicate the complete transcript. However, `PrepareMemoryIndex` loads the checkpoint ancestry, and `SharedConversationArchive.ReadAncestry` follows every parent. GZip and the 2 MiB per-chunk limit do not bound total disk usage or loaded history. Deleting an old parent currently breaks descendant saves. `MemoryArchiveTool` also verifies and exports exact ancestry; its contract must change alongside storage, not afterwards.

## Proposed memory layers

1. Recent episodes retain bounded original excerpts, participants, time, source, and recorded appraisal. Age is measured in game time, not wall-clock time.
2. Older episodes become structured, bounded summaries: who, what happened, when, source/uncertainty, emotional significance, relationship consequences, unresolved commitments, and selected memorable details. Similar routine interactions can merge. High significance earns detail, not unlimited storage.
3. Older summaries merge into bounded themes and landmark memories. Budgets must limit total bytes and record counts per NPC, not merely each individual summary. The number of NPCs and retained save branches remains a separate source of growth.

The user clarified age policy: normally compact history approximately one month old or older; under high data volume from many/long conversations, start at approximately one week. Recent history keeps more detail; older history retains the most important points. Personality and recorded emotional significance determine importance at both levels. Byte budgets remain provisional tuning values. Pending commitments and authoritative household, identity, inventory, quest and relationship state remain in their structured stores. A summary must never rewrite them. Claims and rumors remain attributed; quoted threats, hypotheticals, negations and subsequent corrections retain their meaning. Known events about multiple NPCs use a shared event identity and participant-specific perspectives without revealing private knowledge.

Personality and recorded past emotion affect selection. Current fear must not manufacture past trauma, and positive mood alone does not prove a joyful event. Current mood can alter retrieval emphasis without repeatedly rewriting historical truth. Repeated summarization must preserve compact factual anchors and correction links; prose alone is not a safe source of truth.

## Idle scheduling

- Hard gates: no dialogue, queued foreground model request, TTS generation/playback, combat or critical save/load/scene transition. Foreground work always wins.
- Preferred gate: no relevant NPC within the actual spatial proximity query, with a short stable quiet interval. The last conversation partner alone cannot establish that the player is alone.
- Process a small bounded batch with one low-priority Gemma job; do not load another model. Bound CPU scanning, serialization and I/O too.
- When an NPC approaches or interaction starts, cancel/preempt model work and yield the model queue. If inference cannot resume, discard the incomplete draft and restart later. Do not block foreground interaction waiting for a summary.
- Pin world/store identity, NPC, branch checkpoint, input hashes and policy version. Reject stale results after load, rewind or conflicting changes. Save during an incomplete job uses the last valid committed generation.
- Use hysteresis/backoff to avoid repeated start/cancel loops. Long waits without solitude may delay maintenance; they must not cause foreground stutter or unbounded in-memory queues. Test whether real play provides sufficient idle capacity before choosing budgets.

## Publication and reclamation

A validated summary must be published into a new self-contained archive generation containing retained recent episodes and compacted older memory. Its ancestry must not require the raw chunks intended for deletion. Source hashes can remain as provenance, but cannot be required dependencies for normal loading after reclamation.

Write and flush the new generation, validate a complete reload, then atomically publish its checkpoint through a successfully saved state. Retain the previous generation through a crash-recovery window. Garbage collection only reclaims chunks unreachable from every retained save/checkpoint, active reader, export operation and recovery root. Incomplete root discovery or validation disables collection. Portable export/import must support the new generation format and preserve old-format readers.

Old saves pin old raw history. Preserving arbitrary old saves with exact original history and enforcing a hard global archive cap cannot both be guaranteed. First bound the active generation; report retained-save storage separately. A later explicit retention or branch-specific migration policy can release old roots. Never silently rewrite all branches to a newer summary or leak future events into an old save.

## Required acceptance tests

- Multi-week/month episodes, repeated mundane conversations, emotionally significant positive and negative events, conflicting claims, later corrections, quotation and hypothetical controls.
- Household and open promises survive; summary cannot invent spouse, children, locations or world facts. Unsupported details fail validation or are omitted. Validate semantic quality with held-out English fixtures and human review; hashes/schema alone cannot prove faithfulness.
- Repeated compaction over years of simulated play; measure drift, retained key facts, serialized size, loaded RAM and load latency. Test a constantly active player with little solitude and many encountered NPCs.
- Identical-source deterministic selection inputs; personality differences affect emphasis while facts remain consistent. Recorded emotions remain correctly attributed.
- Cancel on nearby NPC, conversation, TTS, combat, save/load and scene change; measure interruption latency, frame-time impact and model/VRAM contention in native DFU.
- Crash at every publish stage, disk-full/corrupt summaries, stale asynchronous completion, concurrent append, old-save rewind, multiple branches, and export/import round-trip.
- Reclamation never removes reachable chunks. After deleting eligible unreachable raw chunks, every retained save still loads and contains only its own historical knowledge.

## Coordination

This is a separate follow-up slice. The other implementation chat owns Core/Runtime/Compaction and the current build/install/native checkpoint. No storage format changes or raw-file deletions are part of this design-only handoff. The concrete follow-up is in `docs/superpowers/specs/2026-10-23-generational-npc-memory-design.md`, with English acceptance fixtures under `docs/test-fixtures/aged-memory-v1/`.
