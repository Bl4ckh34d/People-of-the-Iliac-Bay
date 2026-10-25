# Generational NPC memory — preparation specification

Status: prepared during the V3 build freeze. No Core/runtime changes, model calls, archive migration or deletion. This elaborates `docs/2026-10-23-aged-memory-storage-design.md`; it does not claim that storage is already bounded.

## Decision and alternatives

Use structured memory atoms plus short model-written summaries in self-contained archive generations. Personality and accepted historical emotion determine emphasis, not truth. Keep current native identity, obligations and relationship stores authoritative.

Keeping only an extractive cache cannot reclaim raw history. Replacing history with free prose alone loses attribution, corrections and replay protection. The selected design retains bounded factual/source capsules with prose, and separates archival reclamation from semantic compression. A source hash proves integrity, not meaning.

## Verified integration points and gaps

- `ConversationArchiveState.SnapshotForSave` commits pending rows and returns a checkpoint. `PrepareMemoryIndex` loads full checkpoint ancestry. `SharedConversationArchive` uses immutable parent-linked `.pim` chunks; deleting a parent breaks descendants. Individual saves avoid duplicating complete transcripts, but shared disk usage and loaded history still grow.
- `ConversationMemoryCompactionJob` currently selects up to four of eight source rows; it does not generate summaries or reclaim bytes. Its 3,000-byte prompt and 128-token response limits are selection limits, not suitable summary defaults.
- `ConversationMemorySalience` matches accepted player-speech appraisals to archive rows by exact provenance/text/floored time and rejects ambiguity. Native nonspeech events need their own typed memory source path. Persist historical salience before any consumed receipt is pruned.
- `NpcExperienceInbox.Entries` and `NpcRelationshipLedger.AcceptedOutcomes` persist growing lists. They also prevent duplicate effects. Replacing transcript storage alone does not bound saves; deleting these lists carelessly permits replayed mood/trust effects.
- `GetSaveData` is called before confirmed disk-save completion. It is not a garbage-collection commit acknowledgement. Until a reliable save-success hook and complete root census are established, reclamation stays disabled.
- Current nearby deferral tracks the last conversation actor. A real proximity query must include all relevant loaded NPCs, scene changes, disabled/despawned actors and NPCs behind the player.

## Memory policy

Age uses native game seconds. The user's explicit policy is normal compaction at approximately **one month**, or already after **one week** when many/long conversations produce storage pressure. Sources younger than seven days remain detailed. Sources strictly older than seven days may receive a detail-preserving early summary under pressure; without pressure they remain detailed until strictly older than thirty days. After ninety days, related summaries may merge into more concise themes. Seven/thirty days encode the user's approximate thresholds; ninety days is a provisional tuning choice. Do not remove an open obligation just because it is old.

Personality and recorded emotional meaning affect selection at **every** age tier. Early summaries retain more specific interactions and memorable details; old themes keep core events, relational turning points and a few landmarks. Age reduces ordinary detail, not the truth of the event or the importance of a still-open promise.

Initial pressure tuning: enter at 64 KiB recent serialized UTF-8 data for an NPC or 16 MiB aggregate recent data across the active branch. Release only below 48 KiB per affected NPC and 12 MiB aggregate. These are provisional high/low watermarks, not measured capacity guarantees. Global pressure fairly schedules eligible sources across NPCs, starting with older routine-heavy data; it does not override the seven-day floor. Measure bytes including metadata before compression so gzip ratios do not arbitrarily change memory policy. Persistent protected overflow remains visible.

Per-NPC active-memory targets, including metadata and UTF-8 encoding: recent episodes 64 KiB; up to 16 summaries totaling 16 KiB; up to 8 landmark memories totaling 8 KiB; detached provenance/emotion capsules 8 KiB. Total target 96 KiB. These are targets, not unconditional caps: pending obligations, missing evidence or insufficient idle throughput can cause reported protected overflow. Never silently discard protected data to make a graph look bounded. Current identity and other gameplay ledgers are outside this memory estimate and must be measured separately.

Bound both count and encoded bytes. A landmark does not gain infinite space merely because it is emotional. Preserve its central meaning and key participants while discarding minor wording. Repeated routine greetings can become one pattern; repetition is not independent corroboration. Use stable source order for ties; do not use random selection on each maintenance pass.

Retention priority:
1. Mandatory anchors: open commitments, unresolved correction links, active quest disclosure rules and dependencies required by authoritative state. Keep references to the authoritative objects; do not duplicate them as a second truth store.
2. Landmarks: strongly evidenced relational changes, danger, help, betrayal, reconciliation and other personally significant events, positive and negative.
3. Themes: repeated interactions and relationship history, preserving turning points and contradictions.
4. Routine detail: summarize and reduce first under pressure.

Use only the saved active three-trait profile (or verified authored persona). Missing traits remain absent. Gemma receives candidates and accepted appraisal cues; it ranks relevance within the nonnegotiable protected set. Do not implement keyword rules such as "greedy means retain every sentence containing gold". Proposed importance must cite source IDs and a short reason. Test identical events with different profiles; facts must remain identical even when emphasis differs.

Historical cues retain their recorded deltas, subject, responsible actor/unknown status and receipt IDs. Current fear cannot invent past trauma. Positive mood is not proof of love or joy. Current mood may influence retrieval but must not repeatedly rewrite historical content. Once the accepted appraisal is detached into a durable capsule, future compaction must not depend on an inbox row scheduled for removal.

## Proposed record contract

All types below are new proposed contracts, not existing public APIs. Schema version is independent from world-save and portable-package versions.

`NpcMemoryGenerationManifest`: formatVersion, archiveId, generationId, parentGenerationAuditHash (optional and not a load dependency), sourceCheckpoint, cutoffGameTime, policyVersion, sorted contentFile hashes/lengths, per-person roots, protected references, summary counts/bytes, replay-protection root. One manifest describes a complete loadable generation plus its bounded append tail.

`NpcAgedMemoryRecord`: recordId, observerId, originalStart/EndGameTime, generatedAtGameTime, tier, englishSummary, atoms, salienceCapsules, commitmentRefs, sharedEventRefs, sourceSetDigest, coverageCount, policyVersion. `atoms` hold subject/participants, predicate/value, polarity, modality, evidenceKind, assertingSpeaker, visibility, sourceCapsuleId and supersedesAtomId. Plain prose never becomes an inventory, identity or quest update.

`NpcMemorySourceCapsule`: capsuleId, original receipt/event identity, observer, asserting speaker, original time/range, evidence kind, uncertainty, optional bounded verbatim excerpt, content digest. A selected atom must have enough retained evidence to interpret qualifiers after raw deletion; a hash alone is insufficient. Keep a bounded subset of detailed citations and one aggregate digest/count, not an ever-growing array of all historic hashes. The digest is an audit commitment, not permission to recover deleted detail.

`NpcHistoricalSalienceCapsule`: sourceCapsuleId, acceptedAppraisalId, acceptedAt, recorded mood/fear/trust/resentment/affinity deltas, applied-effect mask, responsibility category, personalityVersionAtAppraisal where known. Preserve unknown values explicitly. Do not replay deltas when reading or summarizing.

`NpcAgedMemoryDraft`: captured owner/save epoch/branch/head, NPC ID, source-set hash, personality/appraisal-context hash, policy version, selected source IDs, proposed atoms and prose. Reject references outside the captured candidate set. Response limits apply before deserialization. Compact batches follow topic/event boundaries; large months paginate rather than becoming permanently ineligible.

## Validation and correction

Native validation checks identifiers, person/branch/time boundaries, allowed evidence kinds, provenance, byte/count bounds, protected references, current snapshot fingerprint and absence of state mutation. Validate cross-NPC visibility before prompting, not only after response.

Semantic validation checks polarity, quote/hypothetical scope, unknown responsibility, quantities, subsequent corrections and whether prose says more than retained atoms support. Schema checks cannot prove these properties. Measure Gemma with the prepared English corpus and fresh held-out cases; use human review for destructive-release acceptance. A separate validation pass may share correlated model errors and is not an oracle.

Allow at most two targeted repairs, each with concrete failed constraints and unchanged original evidence. A failed or uncertain draft keeps the originals. Deterministic extractive capsules are an interim fallback, not proof of full semantic compression or a hard byte cap. Only approved records enter a generation; failed records remain raw and appear in backlog metrics.

Long-term retelling uses atoms and capsules as semantic anchors, not a chain of summaries alone. Do not multiply uncertainty into certainty after repeated merges. Household truth remains in its identity store. An accepted fictional past wedding may be retained, but does not invent a current temple, spouse or shared knowledge. Shared biography synchronization is a separate open feature; compaction must preserve existing event IDs and participant views without creating it implicitly.

## Save-safe generation publication

Prepare a new independent generation in a staging directory; never mutate active chunks in place. Copy retained recent rows and accepted records. Include source capsules needed for meaning without requiring raw parent chunks. New normal appends attach to this generation, not the old raw parent chain.

Publication state machine: `Captured -> Drafted -> Validated -> Staged -> ReloadVerified -> SaveReferenced -> DurableSaveConfirmed -> ReclaimEligible`. At every failure before durable confirmation, old roots remain valid. Atomic rename alone is not proof of a successful game save. Write/flush content before manifest, validate a clean load from staging, then publish a checkpoint through the save path. Reject stale head or owner; first version restarts rather than attempting an untested rebase.

Reclamation marks reachable files from every retained local save, active world, in-flight reader/export, recovery root and uncommitted save transaction. Scan under a publication/root-registry lock; recheck generation before sweep to close the mark/sweep race. Incomplete/inaccessible/unknown roots disable sweeping. Use a quarantine/recovery window; never recursively delete the archive root. Report reclaimed bytes only after verified deletion and reloading all retained test roots.

Exact arbitrary old saves can pin all their raw history. A hard global cap while preserving unlimited old saves is impossible. First bound the active generation under a fixed workload, report old-root storage separately, and leave any later save-retention decision explicit. A self-contained portable export pins and includes its exact dependency closure; offline backups containing only saves are not discoverable and cannot be silently assumed safe.

## Replay protection and ledger growth

Before pruning a consumed inbox or accepted-outcome row, prove that its salience capsule is durable and its effect cannot be applied again. New native producers should use trusted durable `(producer, incarnation, sequence)` identities. A closed producer epoch can be represented by a watermark plus bounded gap ranges only after pending/deferred rows and out-of-order receipts are resolved. The admission boundary must reject closed-epoch receipts before applying any effect. Loading an older save restores that save's watermarks; never share future watermarks across branches.

Legacy arbitrary receipt IDs need exact replay protection until their producer lifecycle is migrated and formally closed. Do not replace exact dedupe with a Bloom filter or bounded recent-ID cache. Keeping exact hashes reduces row payload but still grows with event count; report that honestly. Full save-size bounding cannot be marked complete while legacy histories remain unbounded. Inventory, commitment and relationship authority are not reconstructed by replaying summaries.

## Scheduling and capacity

Gemma 4 E4B Q4_K_M remains the sole model; no embedding model or service. One background request at a time. Start after ten seconds of quiet, preferably with no relevant NPC within an initial eight-metre tuning radius. Cancel/yield on dialogue, TTS generation/playback, combat, critical health event, save/load, scene transition or approaching NPC. Missing proximity state defers work. Check foreground ownership again before submission and before publication.

CPU scanning, source matching, encoding and I/O need bounded steps, not only low-priority inference. Start with a 2 ms main-thread maintenance slice as a measured target; never claim it without profiling. Snapshot native facts on Unity's thread; workers handle immutable plain data and file work only. Inference cancellation latency must be measured separately from UI cancellation. Existing twelve-attempt/day scheduler can seed policy, but checkpoint appends must not repeatedly reset a global background-work budget.

Continuous interaction can starve maintenance. Report backlog age/bytes and producer rates. Overflow does not license stutter or silent loss. Fixed-workload soak acceptance requires maintenance throughput above mean eligible input rate; otherwise reduce retained routine detail or defer the feature, without claiming bounded saves.

## Acceptance and release gates

Prepared fixtures: `docs/test-fixtures/aged-memory-v1/semantic-cases.json` (32) and `protocol-cases.json` (40). All are explicitly `not_run`. They include seven-day/thirty-day boundaries, pressure hysteresis and personality/emotion at both early and late detail levels. They are development cases, not a held-out benchmark. The preflight checks fixture integrity only.

Required gates: every protected invariant passes after initial output or at most two repairs; unfaithful drafts are rejected with zero deletion; repeated merges retain correction/qualifier meaning; personality changes emphasis without changing facts; record bytes stay within configured targets or emit explicit protected overflow; safe-root fixtures never lose reachable files; duplicate effects remain impossible; v1/v2 import/export and branch rewinds preserve branch knowledge; native quiet/preemption performance is measured with Breeze and DFU.

Soak matrix: 100/1,000/10,000 NPCs; one/ten simulated game years; routine-heavy and landmark-heavy workloads; many saves/branches; constant interaction with little solitude. Record active-generation bytes, retained-root bytes, pending/consumed inbox bytes, accepted-outcome bytes, replay-index bytes, peak RAM, load latency, frame-time percentiles, model time, cancellation latency, unresolved semantic failures and forgotten-detail rates separately. Storage arithmetic is not a measurement.
