# NPC model capability evaluation — 20 October 2026

## Decision

Latest [causal goal follow-up](2026-10-20-causal-goal-protocol-evaluation.md) adds action-specific argument binding and actual language-generated tasks reaching Core through read tools, explicit synthetic receipts, snapshot restore and exact UID handover. It preserves the failed shortened-prompt run and controlled diagnosis. Reused development strict matches remain 7/12 and 13/20; a separate post-plan audit plus Core rejects all six wrong proposals in thirty while retaining all twenty-four correct ones in that sample. This is bounded causal evidence, not exhaustive model/feature validation or native gameplay acceptance.

Follow-up: see [decision boundary and causal tests](../plans/2026-10-20-decision-boundary-and-causal-tests.md). Actual loaded-game memory measurements now supersede the earlier unverified Mini headroom statement within one city fixture: Mini + selected dialogue model + DFU used 10,640–10,748 MiB, with 1,534 MiB remaining at the sampled peak; all eight requests to each model succeeded. Both native performance runs still failed their one-second frame gate. This does not establish broad graphics/mod compatibility or a general performance pass.

Continue the hybrid architecture with interchangeable decision models. User's hardware constraint excludes D3-flash from the planned simultaneous game/dialogue runtime: Flash alone occupies about 8,566 MiB, while the current dialogue service alone occupies about 5,976 MiB. Their aggregate residency is expected to exceed 12,282 MiB before the game, although shared baseline memory means those samples must not be treated as an exact additive allocation. D3-mini remains the runtime/shadow candidate, subject to loaded-game headroom verification. Flash remains an offline comparison reference, not an installed runtime dependency. Neither candidate receives game-action authority from these results. Keep the selected abliterated Qwen dialogue model available. No experimental routing or perception system was installed into the game.

User clarification supersedes a universal perfect-model gate. Report useful decision percentages, errors by severity/category, legitimate alternatives, abstention and recovery. Deterministic native integrity still requires consent, correct identities, current revisions, cancellation and once-only effects. A wrong model proposal must not become an unchecked effect.

## Reusable harness

Code and frozen fixtures: [tools/NpcArchitectureEval](../../../tools/NpcArchitectureEval/README.md). The base contains 72 synthetic scenario roots, including 15 multi-turn roots. Expanding those turns and testing both option orders produces 190 decisions per model. An optional 22-root extension produces another 44 decisions per model. Future states are explicitly synthetic; they are not evidence of implemented game capabilities.

The runner hashes code, fixtures and model configuration before inference, records durable exact request/response journals, validates candidate references, and keeps expected labels outside model input. Each model/order has its own actual-choice history. Failed calls abort the affected conversation arm without fabricated history or retries. Deep factual turn updates are controlled fixtures, not a general game simulator. D3 uses the native `/v1/systemone` choice head; Qwen uses constrained JSON completion with the same decision instructions and facts. This format difference remains a limitation.

Coverage includes social continuity, attribution, memory, tools, personality/mood, gradual friendship constraints, appointments, agenda recovery, markets, weather-dependent seating, role/capability limits, inventory, purchases, deliveries, ordered tasks, cancellation, quest restrictions and state-based combat decisions. The extension covers perception/stealth evidence, maps, weather, memory portability and elapsed-time simulation. The [coverage specification](npc-capability-test-coverage.md) records uncovered requirements. Choice fixtures cannot establish deterministic or native acceptance for those requirements.

## Frozen comparison

| Model | Base decisions matching allowed set | Severity-weighted quality | Extension | Base latency p50 / p95 |
|---|---:|---:|---:|---:|
| D3-mini Q4_K_M | 165/190 (86.84%) | 87.33% | 40/44 (90.91%) | 84 / 99 ms |
| D3-flash Q4_K_M | 175/190 (92.11%) | 92.17% | 42/44 (95.45%) | 114 / 140 ms |
| Qwen2.5-7B-Instruct-abliterated-v2 Q4_K_M | 161/190 (84.74%) | 85.25% | 40/44 (90.91%) | 148 / 199 ms |

All 702 base/extension model calls returned usable choices without technical failures. These are development scores, not held-out estimates. Paired orders and repeated conversation turns are correlated. Timings include local cache/warm-up; Mini and Qwen were simultaneously loaded, while Flash ran alone. No game was running.

Base multi-turn agreement was 54/76 for Mini and Qwen, and 66/76 for Flash. State-only combat was 35/36 for Mini and 34/36 for Flash/Qwen. These small category counts do not establish combat competence or a statistically general ranking. Some gold sets were found too narrow after execution; raw scores remain unchanged, and the independent review records those limitations instead of rerolling or silently relabeling results.

All 144 first-turn Mini/Flash request bodies match exactly; both score 137/144 there (Qwen 131/144). Flash's aggregate advantage is concentrated in later turns. Eleven later request bodies differ because each model retains its own actual choices/history. A separate advisory exclusion of the same four defective roots leaves Mini 151/166, Flash 157/166 and Qwen 144/166; those are filtered development scores, not replacements for the original raw scores. Preserve both the independent review and the unchanged run journals.

The separate historical 34-root request corpus, both orders, used byte-identical logical requests:

| Model | Expected selections | Critical false-action proposals | Option-order changes |
|---|---:|---:|---:|
| D3-mini | 50/68 (73.53%) | 18 | 0 |
| D3-flash | 47/68 (69.12%) | 17 | 1 |
| Historical Kev-4B | 35/68 (51.47%) | 19 | 6 |
| Historical original Qwen-7B | 39/68 (57.35%) | 25 | 4 |

Kev combines an initial run with four configuration-only admission repairs, across two batch configurations. Historical original Qwen differs from the abliterated model in the broad suite. Do not merge the suites into one model ranking. Quoted/reported/hypothetical instructions, player plans, vague duration and assent without a pending offer remain problematic.

## What the tests do and do not prove

The decision suites select among supplied options. They do not demonstrate arbitrary goal decomposition, free-form itinerary generation, actual context-tool execution, prose quality, movement, purchases or persistent world effects. A separate twelve-root structured-interpreter wiring probe achieved only 6/12 exact pipeline outcomes in either router order. Four actual model-produced plans passed real Core validation and consent/duplicate checks; the other failures remain preserved. Broad decision percentages do not erase that interpreter/planner gap.

An image smoke test sent two hand-reviewed player screenshots, one snowy exterior and one furnished inn interior, in both option orders. Mini and Flash each classified 4/4 correctly. Mini request latency was 324–616 ms; Flash 566–649 ms. This verifies the local image API/projector path on those stills only. It does not validate NPC-eye capture, stealth detection, hostility recognition, combat consequences, video or real-time performance. Image interpretation cannot reveal occluded events or replace native Stealth checks.

Independent review found ambiguous or defective gold in replanning, a fresh follow request after cancellation, a conditional container read, and an unsupported lending request with no suitable refusal option. Preserve the original fixtures/results. Any later corpus revision needs a new version and explicit comparison; do not claim a newly corrected score as the original frozen result.

## Hardware and provenance

RTX 4070 SUPER, 12,282 MiB reported VRAM. D3-mini plus Qwen loaded at about 10,402–10,519 MiB with no game. Flash alone loaded at about 8,566 MiB. This does not prove that either configuration can coexist comfortably with the modded game. Flash-plus-Qwen concurrent serving was not attempted. Measure loaded-game headroom before selecting a production arrangement; do not assume the larger model fits alongside dialogue and gameplay.

- [Official Mini GGUF](https://huggingface.co/ggml-org/vllm-d3-mini-GGUF), revision `e7230e2309c186c80182fbeed6fef70680b20c64`. Q4_K_M: 3,707,523,552 bytes, SHA-256 `ab3d4378c268f7f3238a63607af40c0cb516e4482278b01c2f48e9d0698ce522`. Q8_0 projector: 366,894,720 bytes, SHA-256 `bb79ba9f091df890684388f559f3145649007b5715d03760f99b767de41d9eda`.
- [Official Flash GGUF](https://huggingface.co/ggml-org/vllm-d3-flash-GGUF), revision `50a0b7585fb8732fce67a921fab83ed2ac6e2fcb`. Q4_K_M: 6,603,024,864 bytes, SHA-256 `60bb6a420048731c64315fc4f1959845ae98cda6e718a2029dfd5c90fd1d8b7c`. Q8_0 projector: 624,229,792 bytes, SHA-256 `0291125ad889adb09a0097cc7ba081ce2cfbd2eaf4f253160559da14de93e14c`.
- llama.cpp `b11680-b797b9ca8`; decision servers context 4096, one slot, batch/ubatch 2048, GPU layers 99. Serial requests avoid concurrent decision batching as a confounder. No Artemis variant was required.

Evidence directories: `work/npc-capability-eval-20261020/{broad-run-v1,broad-flash-v1,gaps-run-v1,gaps-flash-v1}`, `work/d3-mini-trial-20261020/{legacy-run-v1,legacy-flash-v1,vision-mini-v1,vision-flash-v1}`, and `work/architecture-probe-20261020/wiring-run-v1`. Manifests and raw journals remain local ignored artifacts; reusable code, fixtures and this report live in repository source.

## Verification and next slice

Later causal checkpoint: Core decision contracts now exist, but there is still no production decision adapter/caller. Parent Core verification is 1188/1188 and RuntimeCompile is clean. The real-Core bridge now includes persistent sessions, explicit receipt-driven lifecycle tests, exact-UID handover, restore, cancellation and error isolation. Independent review corrected omitted consent defaults, silent quantity remapping, hardcoded UID reporting and false no-acceptance reporting after partial effects. Its supported effect fixture remains one item and NPC-to-NPC delivery, not general player/native transactions.

The more faithful event-first prototype runs Mini routing before optional Qwen interpretation. Twelve development cases produce 7/12 strict all-stage matches in each route order; sixteen fresh diagnostic cases produce 8/16 and 9/16. It submits 30 actual model plans to Core, of which only 20 validate. Ten contain invalid item dependencies, showing why an exit code of zero or syntactically valid JSON cannot count as full plan success. Four roots produce eight Core-valid but semantically false plans. A separate development post-planning Mini audit rejects eight of twelve wrong proposals while retaining all eighteen semantically correct proposals; four hypothetical-request proposals remain accepted by that audit. With Core validation also applied, twelve correct and four wrong proposals remain. Do not enable action authority from these results.

The earlier 6/12 wiring score was outcome-only: only three of those six also have correct route and intent. It also called interpretation before routing and reused it across route orders. Preserve that experiment, but do not call it a complete event-first architecture test. The new binary was not frozen with the v2 model manifest; the review records the later bridge binary hash and limits the claim accordingly.

A separate actual-Core tool pilot reaches all three correct inventory answers through model-selected on-demand reads: 30 gold/no items, 21 gold/one acquired item, and after real Core snapshot restore plus fixture handover, 21 gold/no items. Read-only queries leave the store unchanged; UID 12345 moves to the recipient and the task reaches Completed. The task and explicit synthetic receipts are fixture-controlled, not generated from player language. This proves the scoped tool/state path, not arbitrary goal planning, native effects or all planned features. Local evidence: `work/architecture-probe-20261020/{event-first-v2-run1,proposal-audit-v1,core-tool-pilot-v1}`.

Parent independently ran 22 Python harness tests (including mocked image transport), the real CoreBridge self-test (six JSON cases plus direct structural boundaries), the four actual interpreter plans through Core acceptance, and all 1181 Core tests successfully. No Unity runtime code changed for this experiment; no new game bundle or native acceptance is claimed.

Next production work is the disabled-by-default decision-service boundary and shadow comparison, with event/snapshot binding, actor capabilities, cancellation and bounded errors. Continue language-interpreter and task-planning evaluation separately. Add closed-loop simulated consequences and real native lifecycle acceptance before claiming comprehensive agent behavior. The full mod goal remains active.
