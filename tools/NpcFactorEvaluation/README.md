# NPC Factor Evaluation

## Bound speech classification (22 October 2026)

Observed-speech appraisals now require the production Nano classification before Qwen appraisal or commit. `RunPostChatAppraisalAndReopenAsync` records both stages. Selected continuity sessions first return `episode-speech-ready`; execute the emitted D3 request through the receipt-bound runner, then submit `classifyEpisode` with the current `episodeId` and verified response receipts. That operation binds the classification to the exact current ticket and emits the subsequent Qwen request. Native event appraisals skip this speech stage. Never fabricate a classification to make a live run commit; self-test labels are controlled fixtures only. Preserve each raw request/response and journal rather than replacing the classifier result with an expected answer.

## Matched selector substitution diagnostic

`run_selector_comparison.py` compares D3 selection against Qwen selection from the same exact prepared D3-visible snapshot and option descriptions. Both selections pass through the existing production Core validation and the same Qwen wording formatter. This isolates selector substitution; it is **not** the full production Qwen-only route, a complex task-plan test, or native gameplay acceptance. Later turns remain authored fixed snapshots.

```powershell
python tools/NpcFactorEvaluation/run_selector_comparison.py --prepared <fresh-multi-turn-v2.jsonl> --output-directory <new-directory> --qwen-model <active-model-id> --limit 12
```

Without `--execute`, this validates inputs without network calls or creating the output directory. An episode cannot be split by the limit. Add `--execute` for one serial attempt per stage. The parent operator must first verify the current healthy loopback servers and actual loaded model paths, ensure no human game session is active, and build the current Debug evaluator. The runner does not switch models or services. Use a new directory for every run.

The `*.raw.jsonl` files preserve each actual selector request/response before conversion. For the Qwen arm, the legacy serial runner's `d3` evidence slot contains the **converted wire envelope**, not raw D3 inference. The wire parser requires a probability mapping, so Qwen's selected ID is encoded as a deterministic one-hot mapping over exactly the offered IDs. These numbers are adapter encoding, **not model probabilities or confidence**. Use the actual raw sidecar for model review; never compare that synthetic vector against D3 confidence.

Initial package106 omitted this required mapping. Its original failures remain in `work/npc-factor-evaluation/paired-selector-106/live-01`. `wire-recovery-01` re-encodes the same 12 captured first Qwen choices and runs only their previously unattempted wording stage. There were no selector rerolls. The recovery script verifies exact original request parity before reuse. See `parent-review.md` and `structural-summary.json` in that evidence directory.

This small .NET entrypoint prepares the frozen NPC event contrast corpora for later, serial local inference. It references production Core to construct bounded `NpcDecisionRequest` and `DialogueRequest` projections, and links the production `mod/Scripts/LocalDecisionWire.cs` implementation for exact D3 bytes, hashing, and response parsing. It does not load the game, mutate a world, contact a model, or infer semantic quality.

The checked-in `fixtures/paired-scenarios-v1.json` and `fixtures/paired-scenarios-v2.json` are frozen copies of the review corpora. V2 preserves v1's 29 pairs and adds six targeted contrasts. Synthetic facts and expected eligibility are offline review oracles. New v2 cases use `planned_adapter_only`; this must not be read as current native implementation support. `fixtures/appraisal-acceptance-v1.json` remains frozen. `fixtures/appraisal-acceptance-v2.json` adds focused inputs for the current parser, affinity reducer, and detached relationship-ledger reducer.

`fixtures/multi-turn-interactions-v1.json` remains the frozen qualitative predecessor. It is not eligible for serial evaluation because its qualitative fields cannot populate the current typed Core snapshot without invented values. See [`docs/2026-10-21-npc-multi-turn-interaction-suite.md`](../../docs/2026-10-21-npc-multi-turn-interaction-suite.md) for coverage and review rules.

`fixtures/multi-turn-interactions-v2.json` preserves the matched episodes while replacing qualitative mood/relationship labels with authored numeric snapshots matching current Core types. The prepared rows use the production `ConversationContextFormatter`, include only model-facing input, and keep the oracle in a separate file. They are fixed-snapshot replays: later turn inputs are not derived from generated replies or fixture oracles.

## Commands

Run the offline validator and production-projection self-test:

```powershell
dotnet run --project tools/NpcFactorEvaluation -- self-test
```

Prepare JSONL requests, without inference:

```powershell
dotnet run --project tools/NpcFactorEvaluation -- prepare --corpus v2 --output work/npc-factor-evaluation/run/requests.jsonl
```

Prepare the 32 synthetic appraisal prompts through production Core factories. Labels remain review metadata, each exact prompt has a SHA-256, and these rows cannot commit native state:

```powershell
dotnet run --project tools/NpcFactorEvaluation -- prepare-appraisal --output work/npc-factor-evaluation/run/appraisal-live-scenarios.jsonl
```

After producing the JSONL, validate exact prompt hashes and confirm review labels and scenario IDs stay outside prompt bytes:

```powershell
python tools/NpcFactorEvaluation/validate_appraisal_preparation.py --prepared work/npc-factor-evaluation/run/appraisal-live-scenarios.jsonl
python tools/NpcFactorEvaluation/test_appraisal_preparation_contract.py
```

`fixtures/appraisal-live-scenarios-v1.json` remains frozen. Its two cause-unknown rows (`native-injury-health-episode` and `native-status-onset-unknown-cause`) were incorrectly expanded as known player-attributed events; do not score those eight profile/scenario rows as causal-attribution evidence. V2 copies the 32-case design into a new fixture and records `responsibleActorAttribution` and `responsibleActorId` as evidence fields. Injury and status onset are now explicitly unattributed; confirmed gifts/help and appointment absence retain their known player actor. The reviewer label remains outside the production prompt.

After the parent builds the updated evaluator serially, prepare and validate the corrected fixture into new paths. V1 remains the default if `--fixture-version` is omitted:

```powershell
dotnet run --no-build --project tools/NpcFactorEvaluation -- prepare-appraisal --fixture-version v2 --output work/npc-factor-evaluation/run/appraisal-cause-corrected-v2-prepared.jsonl
python tools/NpcFactorEvaluation/validate_appraisal_preparation.py --fixture-version v2 --prepared work/npc-factor-evaluation/run/appraisal-cause-corrected-v2-prepared.jsonl
dotnet run --no-build --project tools/NpcFactorEvaluation -- validate-appraisal-prepared --fixture-version v2 --prepared work/npc-factor-evaluation/run/appraisal-cause-corrected-v2-prepared.jsonl
```

The Core self-test checks the v2 prompt projection for unknown injury/status attribution and absence of a player relationship target. `record-appraisal` accepts the same `--fixture-version v2` selector when reviewing a future separately authorized v2 response file.

V3 is an explicit 38-case corpus: it keeps the v2 32-case prompts unchanged and appends six typed motivation/knowledge context cases. `fixtures/appraisal-live-scenarios-v3.json` is a manifest that pins the v2 base and context-pair fixture by SHA-256. Prepare and validate it through the manifest selector:

```powershell
dotnet run --no-build --project tools/NpcFactorEvaluation -- prepare-appraisal --fixture-version v3 --output work/npc-factor-evaluation/run/appraisal-v3-prepared.jsonl
python tools/NpcFactorEvaluation/validate_appraisal_preparation.py --fixture-version v3 --prepared work/npc-factor-evaluation/run/appraisal-v3-prepared.jsonl
dotnet run --no-build --project tools/NpcFactorEvaluation -- validate-appraisal-prepared --fixture-version v3 --prepared work/npc-factor-evaluation/run/appraisal-v3-prepared.jsonl
```

The v3 context rows remain synthetic Core projections, not native captures. Pair labels and the hidden public-topic value are review metadata; the prompt receives the typed need/duty or public topic epistemic label only. V1/v2 remain separate 32-row runs. The serial driver accepts `--fixture-version v3` when a model run is separately authorized and still sends each exact prepared row once.

```powershell
python tools/NpcFactorEvaluation/run_appraisal_evaluation.py --fixture-version v3 --prepared work/npc-factor-evaluation/run/appraisal-v3-prepared.jsonl --output work/npc-factor-evaluation/run/appraisal-v3-raw.jsonl --validated-output work/npc-factor-evaluation/run/appraisal-v3-validated.jsonl
```

The serial driver also accepts an explicit fixture version. If a separate v2 model run is authorized later, give it new paths:

```powershell
python tools/NpcFactorEvaluation/run_appraisal_evaluation.py --fixture-version v2 --prepared work/npc-factor-evaluation/run/appraisal-cause-corrected-v2-prepared.jsonl --output work/npc-factor-evaluation/run/appraisal-cause-corrected-v2-raw.jsonl --validated-output work/npc-factor-evaluation/run/appraisal-cause-corrected-v2-validated.jsonl
```

The serial appraisal driver validates prepared rows against current Core before any model call, sends each exact Core-built request once to loopback, and then invokes `record-appraisal` for strict Core parsing. It never retries or commits state. It writes raw response evidence and a separate Core-validated result file:

```powershell
python tools/NpcFactorEvaluation/run_appraisal_evaluation.py --prepared work/npc-factor-evaluation/run/appraisal-32-prepared.jsonl --output work/npc-factor-evaluation/run/appraisal-32-raw.jsonl --validated-output work/npc-factor-evaluation/run/appraisal-32-validated.jsonl
python tools/NpcFactorEvaluation/test_run_appraisal_evaluation.py
```

### Isolated ChatML framing experiment

The v1 framing check uses only the 24 cause-valid rows. It excludes both cause-unknown scenarios across all four profiles. Each variant wraps the unchanged Core prompt as a Qwen ChatML user message followed by an assistant prefix. The prompt text inside the wrapper stays byte-for-byte unchanged. Completion grammar, temperature, token limit, stops, and pinned Qwen model stay paired to the finished v1 baseline. Preparation makes no model call and refuses an existing output directory:

```powershell
python tools/NpcFactorEvaluation/run_appraisal_chatml_experiment.py --prepared work/npc-factor-evaluation/overnight-appraisal-20261021/requests.jsonl --baseline-raw work/npc-factor-evaluation/overnight-appraisal-20261021/raw-results.jsonl --output-root work/overnight-appraisal-chatml-20261021
```

This writes `chatml-requests.jsonl`, per-case `paired-manifest.jsonl` with original and variant hashes, and `experiment.json`. A separate explicit `--execute` invocation sends each variant once, preserves raw request/response bytes, and calls the experimental Core response-only recorder. It does not invoke `record-appraisal`, because that production command correctly requires exact production completion-body bytes. The experimental recorder verifies that only the prompt field changed, then calls the same Core response parser; it cannot commit native state. Parent owns the build and any inference run.

After the parent rebuilds the evaluator and authorizes the serial run, execute against the same source and baseline paths by adding `--execute` to the preparation command:

```powershell
python tools/NpcFactorEvaluation/run_appraisal_chatml_experiment.py --prepared work/npc-factor-evaluation/overnight-appraisal-20261021/requests.jsonl --baseline-raw work/npc-factor-evaluation/overnight-appraisal-20261021/raw-results.jsonl --output-root work/overnight-appraisal-chatml-20261021 --execute
```

See [`APPRAISAL-32-RUN-CARD.md`](APPRAISAL-32-RUN-CARD.md) for source provenance limits, the native acceptance command, and manual scorecard.

The combined native acceptance checker requires both the framework markers and streaming evidence from the same owned fixture and exact built mod assembly. Its schema and offline failure cases are documented in [`fixtures/native-framework-streaming-acceptance-v1.md`](fixtures/native-framework-streaming-acceptance-v1.md). Run the checker tests offline:

```powershell
python tools/NpcFactorEvaluation/test_native_combined_acceptance.py
```

The streaming probe requests exactly two short sentences and records monotonic first-sentence and final-result times. The combined acceptance checker requires at least two sentence callbacks and a first callback timestamp earlier than final structured completion; this demonstrates callback ordering, not audio audibility.

Prepare all 20 typed v2 full-snapshot requests and keep evaluator-only oracle evidence physically separate:

```powershell
dotnet run --project tools/NpcFactorEvaluation -- prepare --corpus multi-turn-v2 --output work/npc-factor-evaluation/run/multi-turn-v2-requests.jsonl --oracle-output work/npc-factor-evaluation/run/multi-turn-v2-oracle.jsonl
```

For the first matched episode only (four turn rows), add `--episode-limit 1`. This keeps the first paired episode intact while the inference owner reviews its sequence:

```powershell
dotnet run --project tools/NpcFactorEvaluation -- prepare --corpus multi-turn-v2 --episode-limit 1 --output work/npc-factor-evaluation/run/multi-turn-v2-requests.jsonl --oracle-output work/npc-factor-evaluation/run/multi-turn-v2-oracle.jsonl
```

When the model owner authorizes a run, use the Python driver to send one D3 request per row to loopback `8093`, validate each response through the production Core parser/binding gate, and only then build/send Qwen requests to loopback `8080`:

```powershell
python tools/NpcFactorEvaluation/run_serial_evaluation.py --prepared work/npc-factor-evaluation/run/multi-turn-v2-requests.jsonl --output work/npc-factor-evaluation/run/serial-results.jsonl --limit 4
```

The default Core bridge uses `dotnet run --no-build`; build the updated evaluator serially before starting a run. The runner makes one request attempt per stage, never retries or rerolls, stores exact request/response text and response bytes (base64), and records latency, HTTP status, model metadata, and failures. A failed or stale D3 result skips Qwen for that row. Qwen's prompt receives the validated D3 selection as an abstract conversational preference, explicitly not as native willingness, an accepted task, or an executed game effect. The result records `contextReplayMode: fixed_snapshot_replay` and does not treat earlier generated replies as state updates.

Each multi-turn request row has a stable `requestId` (`episode/arm/turn`), source fixture and input hashes, full model-facing snapshot, exact production `LocalDecisionWire` bytes/hash, and Core decision binding. The Qwen request is deliberately absent until the separate D3 result passes Core validation. Qwen construction then uses the typed `ConversationContextFormatter.ForSocialReply` snapshot and `LocalDialogueCompletion`. No oracle is copied into the request file. The separate oracle file is for human review only. The suite supplies complete per-turn snapshots; preparation does not derive later world effects from model answers or rerun any turn. The older paired-scenario preparation still records its offline Qwen completion body as part of its separate review format.

The offline self-test constructs appraisal requests from Core and checks known/unknown gift attribution, confirmed rescue versus speech claim, the observed-wait gate, zero-credit severe negative affinity, and detached-ledger branch/replay/stale rejection. V2 also contrasts the existing `EmotionalStability` values while holding `Manner` fixed. It names unsupported humor, thin-skinned, and insecurity fields explicitly; humor may shape wording through existing manner and context, but is not a new saved trait. The self-test does not grade whether a model correctly distinguishes a direct threat from a quotation. The current speech factory still requires a claimed world-event kind; the fixture records this gap instead of mislabeling speech as a real injury. `NpcRelationshipLedger.TryApplyAppraisal` creates a detached relationship candidate; it does not alone commit mood, affinity, relationship ledger, and pending inbox consumption in one authoritative store transaction. The current `WorldStateStore` capture/commit method declarations in `NpcExperienceCommit.cs` are intentional stubs that return false. The output has one row per arm. It includes a D3 choice payload, the production Core request binding, and the production voice completion body/prompt built offline. Model-facing projections retain only the NPC's stable/personality/relationship/mood fields, explicit role/authority field, game time and scene, `npcKnowledge`, player text, history, tools, and offered choices. The oracle-only `nativeFacts` and grading labels are not included in either model payload.

A serial inference owner may write one response JSONL row per prepared arm, in any order:

```json
{"armId":"trust.evidence-with-same-affinity.a","rawRequest":"exact request bytes sent","rawResponse":"exact response bytes received","selectedOptionId":"ask_terms","modelMetadata":{"model":"local-model","status":200}}
```

Then preserve raw evidence and apply only the mechanical fixture gate (for multi-turn requests, pass `requestId` on both files; no eligible-option gate is defined for that corpus):

```powershell
dotnet run --project tools/NpcFactorEvaluation -- record --prepared work/npc-factor-evaluation/run/requests.jsonl --responses work/npc-factor-evaluation/run/responses.jsonl --output work/npc-factor-evaluation/run/review.jsonl
```

Create fresh run paths. `prepare` and `record` refuse to overwrite output files. Keep failed inference attempts as rows with their raw request/response and null `selectedOptionId`; do not retry, reroll, or remove poor outputs. The gate reports whether an extracted choice was offered and listed as fixture-eligible. It is not current native validation and does not authorize an action.

## Manual review

Review the mechanical gate separately from semantic quality. For Qwen, assess grounded claims, actor-knowledge boundaries, uncertainty, personality/mood fit, and the distinction between appraisal and action. For D3, compare the selected option with the broad `acceptableD3Choices` set, then separately check the fixture-only native eligibility oracle. Multiple choices may be defensible. Do not require one exact answer or use regex/keyword scoring for meaning. The output keeps `manualSemanticReview` pending; a human reviewer records the decision and note.

Use `fixtures/REVIEW-AND-INTEGRATION.md` for detailed evidence, revision, relationship, and no-show rules. Existing appointment outcomes already own a no-show penalty; later appraisal must not apply it again. `interrupted` and `npc-absent` appointments produce no player-blame event. V2's authority/guard case is explicitly compound. Noble identity/rank is explicit synthetic authored evidence tied to the existing native-person ID; it is not inferred from wealth, outfit, or eloquence. Actual player rank must be verified before a status-based appraisal. A Main Quest speaker may block access only through actual supported native quest state; dialogue review never invents or flips a quest flag.

## Core-owned social continuation

`continue-social` adds an opt-in closed-loop path for one frozen v2 arm. Without `--execute`, it only validates the selected arm and prints preflight metadata. Live mode requires `--execute`, a new `--output` path, and exact expected D3/Qwen model paths. Before any inference it reads loopback `/health` and `/props`, requires a healthy server and matching `model_path`, and records the raw metadata. Each response envelope must also report the expected model identity. It posts one D3 request, validates that response with `LocalDecisionWire` and `NpcDecisionResult.ValidateResponse`, then posts Qwen. It parses Qwen with `SocialDialogueResult.TryParse` and commits only accepted complete responses through `WorldStateStore.TryCommitAcceptedSocialExchangeWithObservedSpeech`. It stops on the first failed stage, returns a nonzero exit code, and never retries.

The episode store is synthetic and Core-owned for the full run. For `trust-help.verified`, the runner starts at mood `0` (neutral) with trust unknown; the arm label does not create an accepted trust outcome. It seeds mood and first-turn actor knowledge from the first authored model-facing snapshot. It copies typed trust, confidence, evidence count, resentment, and fear as synthetic baseline values, then normalizes the Core relationship row to revision `0` and `lastGameTime=-1` with no accepted outcome rows. The header records source and seeded relationship revision/time. This supports authored appointment, threat, and poison context without importing later fixture state or inventing ledger outcomes. Initial authored history must remain empty; Core-committed mood and transcript supply later history. Fixture arms currently provide two turns. `--extra-player-text` may add one caller-authored social utterance, reusing last turn's offered options, for a three-turn probe; this adds no world event.

Offline preflight:

```powershell
dotnet run --no-build --project tools/NpcFactorEvaluation -- continue-social --episode-id interaction.trust-help-and-gratitude --arm-id trust-help.verified
```

Live execution is explicit and pins exact active model paths reported by `/props`:

```powershell
dotnet run --no-build --project tools/NpcFactorEvaluation -- continue-social --episode-id interaction.trust-help-and-gratitude --arm-id trust-help.verified --extra-player-text 'How do you feel about our conversation now?' --output work/npc-factor-evaluation/closed-loop/run-01.jsonl --expect-d3-model '<exact D3 model path from /props>' --expect-qwen-model '<exact Qwen model path from /props>' --execute
```

The run writes one header and one row per attempted turn. Rows preserve exact request/response text and response bytes, model IDs, latency, request hash and Core binding, full serialized Core state and hashes before/after, parser/validation/commit outcomes, and committed transcript. Both endpoints must use unauthenticated loopback HTTP. This synthetic social dialogue test performs no native game action and appraises no unsupported world event.

### Post-chat appraisal and reopen probe

`continue-social-appraise-reopen` is a separate opt-in phase. It uses the frozen `interaction.threat-quotation-and-repeated-abuse` / `speech.direct-threat-repeated` arm and requires a caller-authored third utterance intended to threaten the observer. The evaluator does not verify that wording; Core's parser remains the mechanical gate, and a human must review the model's semantic classification. It commits three social turns into one `WorldStateStore`, then simulates the runtime idle barrier. During that simulated idle phase it captures each exact receipt-bound speech inbox entry with `TryCaptureNpcExperienceAppraisal`, posts the exact `LocalDialogueCompletion.BuildAppraisal` body once, calls the production Core response parser and `TryCommitNpcExperienceAppraisal`, and stops on the first transport, model, parse, commit, or cancellation failure. No failure retries and no reopen follows a failed appraisal. The run adds no native help receipt and cannot establish gratitude-based trust.

The evaluator does not claim the native Unity scheduler accepted the work: its idle barrier is simulated. The run uses the same Core store across chat, appraisal, and one fresh reopened D3/Qwen turn. The synthetic store starts from the first model-facing effective `npc.disposition` as `ConversationAttitude.NativeBaseline` with `Offset=0`; later disposition comes from `TryGetActualConversationDisposition`. It does not import later fixture snapshots, oracle fields, or world events. Speech appraisal cannot grant positive trust. The reopened social turn's new observed-speech inbox row remains pending for a later phase.

Before each appraisal completion, the runner posts the exact Core prompt to the already model-pinned Qwen server's `/tokenize` endpoint as `{"content":"<prompt>","add_special":true}`. The Qwen server must report `n_ctx=4096`. The prompt must fit 3072 tokens, with the fixed 1024-token response reserve; a malformed tokenizer response, transport error, or over-budget prompt stops the phase before completion and reopen. Each row stores exact tokenizer request and any returned response text/bytes, SHA-256 hashes, token count when valid, context budget, and failure reason.

Preflight is offline and makes no network calls:

```powershell
dotnet run --no-build --project tools/NpcFactorEvaluation -- continue-social-appraise-reopen --extra-player-text 'I will hurt you if you refuse me.' --reopen-player-text 'Can we talk again?'
```

The live command requires exact active model paths from the server probes and creates a new append-only JSONL file. Ctrl+C cancels the active loopback request; a returned response after cancellation is journaled but never committed:

On Windows, pin the exact `model` string returned by the active server, including backslash separators. Metadata preflight accepts equivalent normalized paths, but completion-envelope identity checks are exact. For a known Windows file path, `[IO.Path]::GetFullPath('C:/.../model.gguf')` produces the canonical separator form before passing `--expect-d3-model` and `--expect-qwen-model`. A mismatch stops the run without a commit; retain that failed invocation separately.

```powershell
dotnet run --no-build --project tools/NpcFactorEvaluation -- continue-social-appraise-reopen --extra-player-text 'I will hurt you if you refuse me.' --reopen-player-text 'Can we talk again?' --output work/npc-factor-evaluation/closed-loop/post-chat-appraisal/run-01.jsonl --expect-d3-model '<exact D3 model path from /props>' --expect-qwen-model '<exact Qwen model path from /props>' --execute
```

Appraisal rows preserve exact tokenizer and completion request/response bodies and bytes, prompt/body hashes, model identity, elapsed time, evidence binding, expected revisions, mask, parser and commit results, inbox status/revision, and full Core state with hashes before and after. The offline fake-transport self-test checks exact `/tokenize` body, 3072-token boundary acceptance, 3073-token rejection, malformed tokenizer response halt before completion, exact appraisal grammar and mood mask, three ordered commits, Core disposition carry-forward into a fourth request, malformed-appraisal halt, and cancellation without commit. It does not grade whether the model's speech-act appraisal is semantically correct.

