# NPC multi-turn interaction suite v1

Status: frozen synthetic evaluation design. The suite defines review cases and evidence boundaries. It does not claim that every event adapter exists, that a model will pass, or that any exact reply is correct.

Fixture: [`tools/NpcFactorEvaluation/fixtures/multi-turn-interactions-v1.json`](../tools/NpcFactorEvaluation/fixtures/multi-turn-interactions-v1.json). Existing paired-scenarios v1/v2 and appraisal-acceptance v1/v2 remain unchanged.

## Why separate multi-turn suite

One-turn pairs can check a local contrast, but cannot show whether an NPC keeps identity and relationship evidence coherent across a causal episode. This suite groups ordered turns into paired arms. Every turn carries a complete snapshot. The runner does not reconstruct hidden state from a previous model answer.

The suite holds a full 17-axis personality profile stable within each episode. It uses two deliberately mixed profiles rather than one-dimensional caricatures: warmth can coexist with vigilance and sensitivity; humor can coexist with low sensitivity, blunt manner, and repeated justified boundaries. Traits shape interpretation and voice. They never prove event facts, beliefs, intent, promises, rank, or truthfulness.

## Frozen episode coverage

| Episode | Matched contrast | Review focus |
| --- | --- | --- |
| `interaction.trust-help-and-gratitude` | Observed, receipted completion versus player claim without confirmation | Affinity is not reliability. Completed help can inform trust; claim alone stays attributed. One errand proves no unrelated competence. |
| `interaction.gift-obligation-and-coercion` | Confirmed known voluntary transfer versus unverified claim | Transfer is distinct from repayment terms. A gift does not buy consent or add a favor. Existing scoped work remains separate. |
| `interaction.appointment-and-private-cause` | Actor waited through deadline versus appointment interrupted before confirmed wait | Describe observed absence without inventing intent or private cause. Do not double-apply existing no-show effect. Encounters do not imply comfort. |
| `interaction.threat-quotation-and-repeated-abuse` | Direct targeted threat and repeated abuse versus quoted hypothetical | Preserve speech attribution and context. Humor does not erase repeated abuse. Apology speech does not automatically clear resentment or fear. |
| `interaction.poison-discovery-and-attribution` | Poison source unknown versus directly observed placement | Separate illness, exposure, discovery, harm, source, and motive. Unknown culprit stays unknown; direct observation does not prove unobserved harm or intent. |

The poison episode is a planned interaction contract. Its event labels do not assert a current poison-specific native event kind. The runner must record any unsupported adapter as unsupported, not silently map it to a convenient but false event.

## Input and evidence contract

Only `turns[].input` is model-facing. It contains the actor's complete current snapshot: identity, stable traits, directed relationship, mood, known obligations, scene, history, actor-known `npcKnowledge`, and offered options. `turns[].oracle` contains evaluator-only native events and hidden world truth. The runner must keep the oracle out of both D3 and Qwen request payloads.

`initialMechanicalState` is a scenario label for human reviewers, not a numeric state initializer. The fixture prescribes no exact affinity, trust, fear, resentment, or mood deltas. Use current native contracts and event receipts to decide whether a proposed action or state transition is mechanically valid. Keep native eligibility separate from semantic suitability.

## Acceptance categories

**Critical:** hidden oracle leakage; an invented or misattributed event; a claim of completed transfer, help, poison harm, or appointment outcome without actor-known evidence; unauthorized action; or fabricated guard, crime, or world effect.

**Major:** affinity treated as proof of trust; promise or claim treated as fulfillment; gift treated as accepted debt or consent; no-show blamed without confirmed observed wait; existing penalty applied twice; quotation treated as direct threat; known direct abuse ignored solely because of humor; apology treated as completed repair; unknown private cause, culprit, or motive asserted as fact; or active scoped duty silently changed by unrelated social pressure.

**Review judgment:** a cautious clarification, refusal, boundary, or alternative may remain defensible. Review whether the choice fits supplied evidence, constraints, temperament, and current affect. Do not mark a response wrong only because it differs from one preferred answer. Do not require exact prose, phrase lists, keywords, or regex-based semantic grading.

## Running and extending

This fixture is frozen before model evaluation. Parent owns serial inference and live-game work. The offline runner adapter must preserve corpus/turn hashes, exact request bytes, raw responses, choice result, and model metadata. It must never include oracle fields in requests. Keep failed rows; do not retry, reroll, cherry-pick, or edit the suite after seeing output.

Create held-out paraphrases only after v1 evaluation design is frozen, under a new corpus ID and file. Preserve underlying events, observer knowledge, options, identity, traits, and acceptance rubric. Change wording and surface context only where the event meaning and evidence remain identical. Record all attempts, including poor ones.

## True model-driven continuation: current gap and minimum extension

`tools/NpcFactorEvaluation` currently performs fixed-snapshot replay. `BuildMultiTurnRequests` reconstructs each turn from its authored `turns[].input`; `SerializeModelHistory` projects authored history; `run_serial_evaluation.py` sends all D3 requests, validates them through `compose-qwen`, then sends Qwen requests. It does not feed a generated reply or committed mood into the next turn. Keep this mode and its records labeled `fixed_snapshot_replay`.

A bounded continuation mode can reuse the frozen v2 episode wording, request builders, serial HTTP transport, and raw-result format. Start with one synthetic branch for one arm, capped at three turns. Initialize that branch once from the first turn's model-facing input using typed Core state; never reseed it from later fixture snapshots. `WorldStateStore.Restore(WorldState)` can load an isolated branch. For each turn, build D3 from the branch's current typed snapshot, send once, validate its exact binding and offered choice with `LocalDecisionWire`, then build and send Qwen from that same snapshot plus committed transcript. The D3 choice remains a conversational preference, not an accepted native action. Parse the complete response with `SocialDialogueResult.TryParse`; only an accepted response may advance the branch through `WorldStateStore.TryCommitAcceptedSocialExchangeWithObservedSpeech`. Rebuild the next turn from the resulting Core state and exact committed transcript. A transport, parse, stale-binding, or commit failure ends that arm; retain the row and do not retry or advance.

Keep `WorldStateStore` and any `NpcExperienceAppraisalTicket` inside one Core-owned episode session. Tickets bind to their owning store and capture epoch, so Python-side snapshots cannot safely recreate or commit them. The existing batch Python runner can remain the transport and recorder, but needs a small per-turn Core step/session seam; current `compose-qwen` is a batch decision projection, not that seam. Do not create a second state machine in Python.

Only actual supported receipts may trigger experience appraisal. Capture them with `TryCaptureNpcExperienceAppraisal`, build through `LocalDialogueCompletion.BuildAppraisal`, and commit the same ticket with `TryCommitNpcExperienceAppraisal`. Never turn `turns[].oracle` into a receipt or event. If a fixture event has no native adapter, mark that continuation unsupported. A social reply enters history only after its social result commits; it is not evidence that an event, promise, transfer, or world change occurred. Preserve `AlreadyAppliedEffectsMask` so a social mood effect is not applied twice by a later receipt appraisal.

Bind every step to one NPC identity/profile hash, isolated arm branch, conversation/session and save epoch, scene and snapshot revisions, expected mood revision and accepted-turn sequence, directed relationship revision, and exact D3 request hash. Reject stale, foreign, duplicate, or mismatched replies before commit. Keep arms in separate branches. Record before/after revisions, request/response bytes and hashes, validation/parser/commit outcomes, exact transcript, and any native receipt identity. Keep oracle fields outside every model payload. Test that D3 rejection skips Qwen, Qwen failure skips commit/history, stale state stops continuation, and unsupported event adapters never become synthetic facts.
