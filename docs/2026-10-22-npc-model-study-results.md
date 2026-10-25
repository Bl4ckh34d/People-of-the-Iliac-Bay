# Local NPC model study — 22–23 October 2026

Gemma 4 E4B Q4_K_M is the strongest tested candidate for further integration. It is not production-qualified. The GPU was explicitly released by the user; inference ran locally and serially with Breeze resident and DFU closed. Original Qwen3B/D3 services and active model configuration are preserved/restored, and no new Unity bundle is installed by this study.

## Controlled development comparison

The frozen development fixture contains 11 positive requests (one through five steps) and six non-request controls. Native model templates are used. Expected answers are scored only after inference and never become model repair feedback.

| Model | Original two-pass | Visible schema only | Three full-plan revisions |
| --- | ---: | ---: | ---: |
| Qwen3.5-4B Q4_K_M | 2/11 | 8/11 | 10/11 |
| Gemma 4 E4B Q4_K_M | 8/11 | 10/11 | 5/11 |

The matched schema comparison has identical fixture/Core hashes. The sampler grammar was not visible to the model; explicitly showing nested duration and quantity shapes materially helps. Full-plan revision regresses Gemma and is not integrated. Gemma E2B's original arm scores 6/11 and makes two non-requests executable.

## First held-out comparison

The untouched fixture has 18 positive requests in six correlated lexical families, with 3/4/5-step variants, and twelve controls. Three bounded parameter proposals follow a frozen decomposition. These are not 18 independent scenario families.

| Model | 3 steps | 4 steps | 5 steps | Total | First proposal | Recovered | False-ready controls |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Gemma E4B | 6/6 | 5/6 | 6/6 | 17/18 | 16 | 1 | 0/12 |
| Qwen3.5 | 5/6 | 4/6 | 5/6 | 14/18 | 11 | 3 | 1/12 |

Gemma exactly classifies eleven controls as None and one as Clarify. Qwen exactly classifies five as None, six as Clarify, and wrongly makes a player's own plan executable. Clarify is an exact-classification mismatch but not an executed request.

A separate manually authored numeric-expression oracle audits source quotes after the run. The combined value/source counts remain 17/18 and 14/18. This closes the bounded result-auditing gap without introducing a runtime text parser. The original evaluator does not independently verify semantic numeric-source fidelity on arbitrary fixtures.

The sole positive Gemma failure is `held-4-4`: buying 2 loaves, transferring one loaf, waiting 12 seconds, and going to the Copper Lantern. The first valid partial has correct quantities/time but empty object targets. Repairs confuse singular/plural source quotes, then change existing data. The experimental `targetpatch` arm restricts generation to missing required targets and preserves all other fields before existing Core validation. Gemma then scores 18/18 positives (6/6 at each length), sixteen on the first proposal and two recovered, with zero false-ready controls. Qwen remains 14/18 and one false-ready control. The separate numeric-source audit preserves both counts. These are **regression results after seeing the fixture**, not fresh held-out evidence. See `work/model-study-20261022/targetpatch-regression-v1` and `live-summary.json`. The patch is evaluator-only.

## Speech and broad interaction

| Model | Correct speech choices | False direct threats | Invalid | Median |
| --- | ---: | ---: | ---: | ---: |
| Gemma E4B | 36/36 | 0 | 0 | 858 ms |
| Qwen3.5 | 35/36 | 0 | 0 | 933 ms |
| Gemma E2B | 30/36 | 4 | 0 | 505 ms |
| LFM2.5 with reasoning | 28/36 | 4 | 2 | 4560 ms |

There are eighteen scenarios with two option orders. Earlier Nano results were 31/36, zero false direct threats, roughly 75 ms median. This favors Nano latency but does not show a quality benefit over Gemma. No simultaneous D3+Gemma ablation has been run. Good speech classification does not guarantee a good in-character threat response.

Each candidate also completed 128 synthetic free-response inputs covering 33 areas. Main-assistant review records at least 13 Gemma E4B, 25 E2B, 34 Qwen and 37 LFM issues. These are confirmed issue lower bounds, not pass rates, independent human adjudication, or native acceptance. Common failures include owner/speaker confusion, unknown household data treated as childlessness, invented current environment, missed recalled corrections and retractions, false completion, and technical implementation language in dialogue. Unmarked rows are not certified correct.

## Liquid protocol correction

The specific LiquidAI LFM2.5-2.6B model always reasons. Its initial raw-completion arm incorrectly treated reasoning as output; a later raw-grammar chat arm forced JSON into the reasoning channel and left final content empty. Both are explicitly ineligible for quality ranking. The corrected plan arm uses native chat JSON Schema and a bounded reasoning budget: 5/11 positives, zero recovered by correction, one false-ready control, about 16.7 seconds median cumulative model-call time. Fair speech and 128-case interaction runs use separate final content. At least one final answer still leaks a reasoning closing marker; the diagnostic flag now detects that token as well.

## Resources and architecture

Baseline observed total GPU peaks: Qwen 9135 MiB, Gemma E2B 7915 MiB, Gemma E4B 9587 MiB. LFM reasoning interaction peaks at 9451 MiB. These include Breeze and desktop, with no DFU. They are not model-only allocations or proof of gameplay headroom. Breeze remains resident; concurrent synthesis and loaded-game performance are still untested for these candidates.

Prefer one stronger small LLM with authoritative native facts, typed extraction, bounded correction, and native execution gates. Keep D3 optional and justify it with measured incremental quality/latency. For long-term memory, CPU retrieval/embeddings with speaker, source, time, correction and disclosure filters are a better first candidate than mandatory additional decision inference. Retrieval and compaction remain separate validation work. Uncensored fine-tuning is not evidence of factual or extraction quality.

The prior model-class analysis includes general dense/hybrid models, reasoning models, function-call specialists, span/relationship extractors, embeddings/rerankers, MoE, vision, and ordinary native NPC control. Primary model cards and pinned download revisions/hashes remain in `work/model-study-20261022/cards` and `download-manifest.json`.

## Implemented and verified scope

- `NpcGoalTwoPassChat.ParameterInstructions`: visible full schema before the original turn; precise per-step quoting guidance.
- `NpcGoalChat`: localized field errors. No weakened validators, keyword intent parser, or number-word converter.
- Core 1650/1650; DecisionAdapter 86/86; RuntimeCompile zero warnings/errors. Logs are retained under the study directory.
- Evaluators: native templates, model identity, context/finish validation, oracle separation, thinking final-answer transport, process listener ownership, experimental target patch and post-run source audit.
- No production model promotion or Unity bundle installation. Gemma runtime promotion still needs native template integration rather than only replacing a model path.

The user permits lore-compatible invented past events that do not contradict canonical facts. Shared invented biography involving other NPCs is on the ToDo: validate one canonical event, preserve participant/source/time/branch identities, and make the accepted details available to all participants. No shared biography persistence was implemented here. The historical diagnostic prompt is stricter; compatible anecdotal invention is not automatically a product defect.

## Remaining gates

The 18 native run cards covering 64 roadmap IDs are not run. Prepared paired factor prompts and dynamic correction episodes do not become accepted merely because the 128 static replies completed. Still required: unseen plan generalization, genuine multi-turn clarification, native effects, memory retrieval/compaction over game weeks, natural role-play, concurrent Breeze and DFU latency/VRAM, and invalid/stale/duplicate result rejection through the complete runtime.

User-facing evidence: `C:/Users/Daniel/Documents/Codex/2026-10-22/wir/outputs/dfu-npc-modellstudie-ergebnisse-2026-10-22.html` and the adjacent result-data ZIP. Saved run manifests and raw response files are the primary measurement record; failed protocols are preserved.
