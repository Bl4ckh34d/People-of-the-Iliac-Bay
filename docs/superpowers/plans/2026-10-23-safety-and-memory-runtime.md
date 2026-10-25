# Native safety goals and extractive memory maintenance

## Scope and authorization

The user requested implementation of remaining roadmap features without interfering with the independent Gemma game tests or portrait work. Gemma E4B and Breeze remain the selected models. STT is explicitly deferred. This work changes source and runs CPU checks; it does not rebuild the Unity bundle, deploy, run the game, or start model processes.

The current working tree contains concurrent work and is authoritative. Changes have exclusive file ownership. No reset, bulk staging, or commit is part of this task.

## Slice 1: native safety goal lifecycle

Use directly witnessed hostile threats and existing civilian flight as the source of a persisted Safety goal. Only eligible, living, awake, unprotected witnesses acquire it. The goal becomes fulfilled at actual refuge arrival, or abandoned when the threat expires or the resident dies. Starting a route is not fulfillment. Neither the producer nor completion infers a culprit or modifies crime and relationship state.

Reuse one goal slot per resident and the existing evidence-bound motivation store. Repeated polling and duplicate phases do not append receipts. New incidents replace the slot through validated transitions. The generic receipt cap remains unchanged; exhaustion fails closed rather than deleting replay protection.

Ownership: new Core bridge and focused tests; narrow native hooks in `CivilianResponses.cs` and `StreetPopulation.cs`.

## Slice 2: extractive memory maintenance

This first integrated version stores model-selected source references, not newly generated factual summaries. The model selects IDs from a bounded, completely scanned NPC/calendar-month group. Core validates selection and renders original entries with the existing speaker/source attribution. Raw immutable chunks and pending journal rows remain intact. Native calendar months are 30 days; individual records must be strictly older than one month.

A disposable derived cache binds each result to policy, person, native month, archive ID and exact source head. Missing, malformed, mismatched or otherwise unvalidated cache data uses raw recall. A new head invalidates old cached selections in this version. Portable export v1 stays unchanged; importing without this cache is supported and regenerates selections later. No archive storage saving is claimed.

The user additionally requested personality and emotional salience. Supply existing saved personality traits and, where the original source can be uniquely matched, accepted historical appraisal cues to selection. Do not use today's mood as an earlier event's emotion. Raw memory has no receipt ID: matching therefore requires exact observer, speaker, utterance and native time with an unambiguous source/receipt pair. Unmatched, ambiguous, pending or deferred appraisal evidence stays unknown. Positive mood impact is not automatically proof of joy. These cues influence selection, never source truth or attribution. A context fingerprint prevents a selection from silently carrying across changed personality/appraisal context at the same raw archive head. Real Gemma ranking quality requires separate model acceptance; CPU tests prove binding and contract behavior only.

Scanning progresses in bounded batches. Oversized groups remain raw instead of pretending that partial coverage is a complete month. Exact queries must still reach relevant omitted raw evidence; compaction must not make that evidence unavailable.

The runtime uses the existing low-priority shared dialogue client. Foreground dialogue, combat, pause, speech, save/load changes and shutdown invalidate or cancel its own ticket. It never cancels another request. Publication rechecks store ownership, save epoch, source head and idle conditions. Failed selections have bounded retries/backoff. Unfinished work may restart after reload; completed derived results are persisted atomically.

Ownership: new Core cache/job/recall files and isolated tests; new runtime scheduler with small Update/lifecycle and dialogue-recall hooks. The other chat retains `ConversationMemory.cs`, experience/speech classification, goal dialogue routing, and model transport/template code.

## Coordinated own-memory reads

The user approved structured authoritative storage plus bounded LLM-selected retrieval, with a future semantic index remaining optional. The other implementation chat owns selector/runtime read routing; this chat supplies the Core source receipt. The agreed contract adds at most one own-memory or mood-cause read per turn. Identity, time, archive and budgets come from runtime, never model output. A selected-read reply is buffered and its source receipt checked before final publication. `NONE` means no additional read; the bounded automatic context remains available. See [integrated architecture](../../2026-10-23-npc-memory-architecture.md) and [read-tool integration](../../2026-10-23-memory-read-tools.md).

## Deferred boundaries

Additional static role housing needs verified native actor/bed fixtures. Physical beds or workplace affiliation alone do not prove personal residence. Paid companion recruitment, escrow, party UI and progression remain separate features; these slices do not claim to finish the companion system. Free semantic memory summaries, descendant-head cache reuse, physical archive compaction and STT also remain deferred.

## Verification

Fresh starting Core baseline: 1,659 passed, 0 failed. Each slice starts with focused failing regression cases, then checks its actual implementation. Final integration runs the full Core executable, focused suites and the net472 runtime compile with separate artifact directories. Independent review checks stale callbacks, branch provenance, native lifecycle transitions, idempotency and module-off behavior. Native performance and in-game behavior require a later coordinated Unity build and game test; compilation is not presented as that validation.
