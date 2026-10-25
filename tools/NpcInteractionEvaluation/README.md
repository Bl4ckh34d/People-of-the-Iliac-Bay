# Whole-mod NPC interaction evaluation preparation

This package prepares English-only model diagnostics and native acceptance procedures. It does not claim the model or the entire mod passes. The active GPU remains reserved for the other agent's TTS generation until explicitly released.

## Prepared layers

| Layer | Material | Meaning |
|---|---|---|
| Free responses | `fixtures/all-aspects-v1.json`: 128 inputs, 64 paired scenario roots, 33 areas | Synthetic permitted snapshots, questions, independent review criteria; no actual game/retrieval execution |
| Complete plans | Existing 17-case development fixture plus `../NpcArchitectureEval/fixtures/goal-attempts-heldout-20261022.json`: 30 cases | 18 positive held-out instances at 3/4/5 steps and 12 controls; six correlated positive families, not 18 independent roots |
| Clarification | `fixtures/clarification-dialogues-v1.json`: 10 episodes | Native/closed-loop run cards for genuinely missing facts, revisions, cancellation, speaker changes and restore; not an implemented automated episode adapter |
| Factor/continuity | Existing `../NpcFactorEvaluation` v2 contrasts, multi-turn-v2 and appraisal-v3 | 70 paired arms, 20 fixed-snapshot turns and 38 production-Core appraisal prompts; existing continuation runner separately tests generated-turn continuity |
| Speech acts | Existing contrast and held-out English suites in `../NpcArchitectureEval/fixtures` | Exact production classification and appraisal guards; quotes, reports, hypotheticals, negation and conditional real threats |
| Native acceptance | `fixtures/native-run-cards-v1.json`: 18 cards | Concrete setup, sequence, assertions and existing source seams; no assertion that the complete cards are automated |
| Roadmap traceability | `fixtures/roadmap-register-v1.json` | All 64 roadmap IDs mapped to native cards; 125 older requirement rows retained as historical inventory, not individually certified predicates |

Run cards include clock/navigation, census/housing, schedules, guards/crime, mortality, services, quest control, companions, skill progression, packaging and voice/portrait behavior. Some are native systems for which choosing a better language model cannot replace implementation. Preserve their planned/deferred status.

## CPU-only preparation and checks

From repository root:

```powershell
python -m unittest discover -s tools/NpcInteractionEvaluation -p 'test_*.py'
python tools/GoalAttemptEvaluation/test_transport.py
python tools/NpcInteractionEvaluation/prepare.py --suite tools/NpcInteractionEvaluation/fixtures/all-aspects-v1.json --output work/FRESH-preparation
python tools/NpcInteractionEvaluation/run.py --prepared work/FRESH-preparation
```

Default runner mode validates hashes and exact projected inputs without network, model, GPU or game calls. Preparation refuses an existing destination. `requests.jsonl` excludes oracles, roadmap labels and hidden metadata. `review.jsonl` stores independent required/forbidden/allowed facts and native assertions. The native-template transport also checks model identity, context budget and response truncation. Tests use fake local HTTP only.

## Live run after GPU release

Keep Breeze available, confirm the other GPU job is finished, close DFU for offline runs and establish a single intended model server with bounded context and reasoning disabled. Do not use simultaneous candidate inference. The runner does not stop/start services.

```powershell
python tools/NpcInteractionEvaluation/run.py --prepared work/FRESH-preparation --endpoint http://127.0.0.1:8080 --model '<exact /v1/models ID>' --output work/FRESH-model-run --execute --gpu-released
dotnet run --project tools/GoalAttemptEvaluation -c Release -- tools/NpcArchitectureEval/fixtures/goal-attempts-heldout-20261022.json work/FRESH-plan-run '<exact /v1/models ID>' native 8080
```

Use each model's embedded GGUF template, not hardcoded Qwen control tokens. The plan comparison isolates template differences while retaining the old system instructions. It still exercises the existing frozen-decomposition repair protocol; changing the template alone does not fix its semantic limitations. The free-response diagnostic uses its own generic grounded-roleplay prompt and must not be described as a replay of production social prompts. Model-specific recommended sampling can be an additional separately named arm; never silently mix settings into one score. Do not use a development run as a held-out result.

## Scoring and promotion

Record every initial/correction output. For plans, report complete-plan accuracy after attempts 1/2/3, step count/order, exact source evidence, target/reference, quantity, duration, condition/exclusion retention and false executable interpretations. The current C# scorer requires original units; report unit-equivalent semantic matches separately during review. A formally valid source span or nonempty target is not proof that all meaning was captured. No gold-derived errors enter repairs.

For genuinely missing user information, the correct outcome is clarification with no execution. A model may not fill a missing recipient or duration just to pass after three attempts. User clarification is a new source turn, distinct from model self-correction of already supplied facts.

Free prose remains `pending-human-review` after transport success. Review factual support, source/owner/time attribution, false world assertions, appropriate uncertainty/disclosure, intent, personality/mood consistency, relationship continuity and conversational naturalness. Permit creative subjective flavor and explicitly authorized intentional lies; never relabel an unsupported fact as intentional deception after generation. Record verbatim supporting/offending spans and independent reviewer judgments. Wrong factual claims and false punitive/action effects are separate severe failures, not averaged away by fluent smalltalk.

For recall, measure evidence retrieval first and answer support second. Supplied-memory diagnostics cannot prove retrieval or compaction. Native receipts, real disk restart, divergent branches and the 35-day lifecycle require their own acceptance. Proposed strict plan gate: every tested unambiguous complete plan correct by attempt three and zero false executable control plans; any remaining failures stay visible. Finite fixtures cannot guarantee arbitrary future language.

The existing broader coverage specification's large promotion holdout remains a separate unmet gate. These new cases are not a replacement for its actor/context/order controls or an automatic full-mod promotion.

## Resource measurements

Run candidates serially, first offline and then on an owned loaded-game fixture. Compare LLM-only versus D3+LLM with identical cases, and separately reserve Breeze. Record full-turn call count/latency, time to first text/audio, repair overhead, p50/p95, peak VRAM/RAM, game frame p99/max, cold/warm behavior, cancellation and OOM. File size, active MoE parameter count and advertised benchmark speed do not establish playable headroom on this GPU.
