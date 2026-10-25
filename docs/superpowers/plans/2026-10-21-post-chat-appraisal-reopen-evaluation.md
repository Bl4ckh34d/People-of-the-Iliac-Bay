# Post-Chat Appraisal and Reopen Evaluation Plan

> **For agentic workers:** This is a source-backed preparation plan. Do not run inference or build until the parent authorizes a fresh run.

**Goal:** Measure whether delayed appraisal of accepted player speech changes Core-owned relationship state that a later, separate conversation actually reads.

**Architecture:** Extend the existing opt-in `continue-social` evaluator to retain one `WorldStateStore` across three accepted dialogue turns, an idle appraisal phase, and one reopened D3/Qwen turn. Capture and commit each pending speech appraisal through existing Core APIs, serially, only after dialogue completes; stop and preserve evidence on the first failure.

**Tech Stack:** C#, `WorldStateStore`, `NpcExperienceAppraisalContract`, `LocalDialogueCompletion`, loopback Qwen `/completion`.

**Spec:** `docs/superpowers/specs/2026-10-21-npc-experience-events.md`; `docs/superpowers/specs/2026-10-21-npc-motivation-relationships-and-contracts.md`.

## Source-backed flow

The current dialogue loop commits each accepted turn through `WorldStateStore.TryCommitAcceptedSocialExchangeWithObservedSpeech`. That transaction persists the dialogue memory and mood, then appends one receipt-bound `AttributedSpeech` inbox row. The row stores the exact player text and uses `AlreadyAppliedEffectsMask.Mood`; it does not assert that any described world event happened. The same `WorldStateStore` must remain alive across all phases.

After turn three commits, impose an explicit idle barrier: no foreground request, no open talk window, no active chat callback, and no outstanding evaluator transport. In production, `TickNpcExperienceAppraisals` checks `LocalDialogueClient.IsBusy`, game/save readiness, conversation state, foreground talk, and voice workload before it captures work. The evaluator has no Unity client or talk window, so it must record this as an `idle_barrier_simulation`, not native scheduler acceptance.

Drain only this NPC's pending inbox, oldest event first. For each row, call `TryCaptureNpcExperienceAppraisal(personId, capturedGameTime, out ticket)`, build the exact completion with `LocalDialogueCompletion.BuildAppraisal`, send one request to the pinned local Qwen server, unwrap `content`, and pass the exact structured response to `TryCommitNpcExperienceAppraisal(ticket, response)`. The commit method rechecks branch/store ownership, inbox evidence and row revision, applied-effects mask, attitude, relationship-ledger revision, mood revision, motivation revision, knowledge revision, and the Core response parser. A successful commit atomically updates eligible mood/affinity/relationship state and marks the inbox row consumed with an `NpcExperienceAppliedAppraisal` record. Capture the next row only after that commit.

After all three rows commit, build a new model input from the same store, not from the fixture's later snapshots. Run one fresh D3 request and one mood-enabled Qwen social request for a caller-supplied, non-action follow-up. The request must show current mood, relationship trust-known state, trust, resentment, fear, and current effective disposition/affinity where the production context supports it. Do not update appraisal state during this reopened chat; its new observed-speech row stays pending for a later background phase.

## Important source contracts

- `TryCaptureNpcExperienceAppraisal` selects the oldest pending event and creates an owner/epoch-bound ticket. Its request binds `SaveBranchId`, evidence deduplication key, canonical evidence ID, relationship target, captured/observed time, applied-effects mask, and expected mood, affinity, relationship, motivation, and knowledge revisions.
- `TryCommitNpcExperienceAppraisal` rejects stale tickets or non-`NoAction` appraisals. A direct speech appraisal cannot grant positive trust. Severe negative trust, resentment, fear, and affinity depend on a direct threat/insult targeting the observer or a known family member. Already-applied mood must remain zero.
- Production dispatch uses `LocalDialogueClient.TryAppraiseNpcExperienceBackground`, `IsBackgroundCurrent(ticket)`, and `CancelBackground(ticket)`. Save/store change cancels the active background ticket; foreground work preempts it. Runtime retries failed events up to five attempts with backoff. This evaluation intentionally differs: one attempt per event, no retry, no reopen after any capture, transport, parse, or commit failure.
- The evaluator has no Unity dispatch ticket. Its serial transport must therefore own an abortable HTTP request; cancellation must abort the request and prevent any later parse/commit. Never commit a response after cancellation, even if the server completed concurrently.
- Record revisions before and after every phase: mood `(value, revision, acceptedTurnSequence)`, relationship-ledger revision and directed relationship `(trustKnown, trust, confidence, evidenceCount, resentment, fear, revision, lastGameTime)`, conversation-attitude row, inbox revision plus each entry `(deduplicationKey, status, rowRevision, effectsMask)`, and full store-state hash.

## Fixture gaps to resolve before live evaluation

The current three-turn `trust-help.verified` run is not a valid test of native help appraisal. Its “verified help” is oracle metadata only; `CreateSocialContinuationStore` does not enqueue a native help receipt. Its inbox contains only the player's three spoken turns. Speech cannot grant positive trust under the current Core parser, so this run cannot establish gratitude-based trust gain.

Use the existing `interaction.threat-quotation-and-repeated-abuse` / `speech.direct-threat-repeated` arm for a bounded relationship carry-forward probe. Its first two authored turns cover a direct threat and apology; append one explicit direct threat to the NPC as turn three so the event can target the observer even though the synthetic store contains only one resident and no known family member. Appraise all three inbox rows in order. Do not grade one exact numeric delta. Verify Core acceptance and source binding separately from a manual semantic review of the speech-act judgement.

The synthetic store currently seeds a resident, personality, directed relationship, and mood, but not `ConversationAttitude` and not authored `npcKnowledge` rows. It also does not project appraisal-updated disposition back into `BuildContinuationInput`; that helper currently refreshes mood and directed relationship only. Before claiming affinity/disposition carry-forward, add a validated synthetic attitude baseline mapping from the fixture and refresh the reopened snapshot from Core. Preserve source baseline separately from earned appraisal effects. Do not silently treat fixture `npc.affinity` as `ConversationAttitude.Offset` without confirming the units and native projection.

The runner must retain fixture v2's oracle boundary. Its `nativeFacts` never enter the seed or model input. Do not import later authored arm snapshots, accepted outcomes, or claims of delivered help. Current turn history must come from the store's committed transcript.

## Files and bounded implementation steps

**Files:** Modify `tools/NpcFactorEvaluation/Program.cs` for the phased loop and records; modify `tools/NpcFactorEvaluation/README.md` for explicit preflight/live commands and limitations; extend the existing fake transport checks in `Program.cs` or the focused evaluator tests only. Do not modify Core contracts or production runtime scheduling for this evaluation.

1. Add an offline self-test with three receipt-bound speech rows. Assert one persistent store instance, chronological capture, mood-zero enforcement for already-applied mood, consumed row state after each accepted commit, and unchanged state after a failed or stale commit.
2. Add an explicit opt-in command mode. Preflight checks exact Qwen model identity using the current `/health` and `/props` probe. Keep the existing three-turn D3/Qwen phase unchanged; write a phase-end idle barrier record before appraisal.
3. Capture, send, parse, and commit each appraisal once. Make the HTTP request abortable and gate commit on the still-current operation. Journal the exact completion body and hash, exact unwrapped model content and raw HTTP response, model metadata, elapsed time, captured request binding/revisions, parser result, commit result, and before/after state hashes. Stop on first failure; retain all prior records and make no retry.
4. Refresh mood, relationship, affinity/disposition, and history from the same store. Run one new D3/Qwen social turn with a unique request binding. Record its request context and final state; leave its observed-speech entry pending.
5. Manually review only whether the appraisal was semantically grounded in the exact observed utterance and whether reopened model context reflected committed state. Keep mechanical parser/commit acceptance separate from semantic quality. Do not use regex, keywords, exact prose, or one required delta.

## Evidence record contract

Use one append-only JSONL file created with `CreateNew`. Header records fixture SHA-256, arm, synthetic seed provenance, endpoints, expected model paths, server `/health` and `/props` bodies, `retryPolicy: none`, `oracleFieldsIncluded: false`, and `idleBarrier: simulated`.

Write a row for each dialogue turn, appraisal capture/attempt/commit, and reopened turn. Each appraisal row includes `personId`, `canonicalEvidenceId`, `evidenceDeduplicationKey`, `observedGameTime`, `capturedGameTime`, `alreadyAppliedEffectsMask`, expected revisions, `requestPromptSha256`, exact `requestJson` and base64 bytes, raw response and base64 bytes, model identity, HTTP status, elapsed milliseconds, `coreParserAccepted`, `coreCommitAccepted`, failure kind, and full serialized `stateBefore`/`stateAfter` with hashes. Record event `status`, row revision, applied proposal deltas, and actually applied deltas separately. Never label a fixture-only outcome as native evidence.

## Validation gate

Parent runs serial Core/tool build and self-test before any model call. First run must be a new output path with three accepted social turns, exactly three speech appraisal attempts, three consumed inbox rows, and one reopened turn. A failed stage must stop without retry and without the reopened request. The reopened Core request must show the same store's committed relationship and mood revisions; affinity/disposition carry-forward remains unclaimed until the baseline mapping and projection gap above is closed.
