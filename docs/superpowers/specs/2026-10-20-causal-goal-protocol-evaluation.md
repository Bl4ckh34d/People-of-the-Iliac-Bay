# Causal goal protocol follow-up — 20 October 2026

## What changed

The reusable offline harness now includes action-specific goal extraction, deterministic proposal binding, a frozen goal-suite runner, and a causal language-to-Core pilot. This advances the [approved architecture](2026-10-20-npc-agent-architecture.md); it does not install the architecture or certify all current and planned NPC capabilities.

`tools/NpcArchitectureEval/goal_protocol.py` removes model-controlled item-source indices. Wait/Follow carry duration only; MoveTo/Interact carry a target reference; Purchase/Transfer carry quantity. For the supported outgoing handover, the binder derives Core's one-based item dependency from a unique earlier purchase with the exact item and quantity. Duplicate consumption, ambiguous sources, incomplete target shapes, malformed JSON field types, unsupported quantities, missing exact-quantity quotes and unsupported funding produce non-actionable outcomes. Other transfer directions and general spatial planning remain outside this binder.

Independent review found and reproduced quantity/price mismatch, incomplete target binding and unhashable JSON types. Regression tests were observed failing, then passing after fixes. The protocol also prevents repeated handover through aliases for the same owned-item fixture ID. Authored fixture markers and target coordinates remain synthetic evidence; they are not a live survey or inventory freshness check.

## Prompt regression and controlled diagnosis

The first v3 model run regressed despite passing structural tests. On the twenty new roots, only 7/20 per option order passed all exercised stages. Many outputs contained correct steps but `intent: none`; the binder rejected these contradictions. Only four proposals reached Core, all valid. Zero false proposals in this run is primarily abstention, not evidence of a better model.

A 2×2 development diagnostic held four already-observed cases constant and crossed the previous/new interpreter prompt with the previous/new step schema. The previous prompt gave the expected intent in 8/8 calls; the shortened v3 prompt gave 2/8. Three direct requests changed to `none` under the shortened prompt with either schema; the negated control remained `none`. This isolates an effect of the prompt as a whole on this sample. It does not identify one causal word or establish generalization.

Version `goal-v3.1` restores the more explicit field instructions, keeps action-specific schema constraints, and clarifies exact live-offer and funding semantics. No language keyword or regex router was added. All failed runs remain preserved.

## v3.1 development results

The twelve old roots and twenty newly authored roots were each tested in original and reversed route-option order. The twenty-root set was fresh for v3, but already observed when v3.1 was developed. These are development comparisons, not a fresh held-out score for v3.1.

| Set, per order | Route correct | Exact pipeline outcome | All exercised stages exact | Effect-shaped semantic errors |
|---|---:|---:|---:|---:|
| Old twelve roots | 9/12 | 8/12 | 7/12 | 1 |
| Twenty goal roots | 18/20 | 14/20 | 13/20 | 2 |

Both orders give these counts. Of thirty generated proposals, actual Core validates twenty-eight. The two rejected proposals incorrectly replace Follow→Wait with Wait→Wait and violate Core's later-wait position binding. Core validation alone still accepts the unsupported-gate substitution and a dagger purchase substituted for an unavailable silver ring.

A separate D3 post-plan faithfulness test assesses the actual thirty generated proposals, without a menu of gold plans. It rejects four of six semantically wrong proposals and retains all twenty-four correct ones. The two wrong proposals it retains are precisely those rejected by Core. Combining both gates leaves twenty-four correct proposals and no semantically wrong accepted proposal **within this development sample**. This does not prove perfect meaning recognition, willingness, or native action safety. The auditor and interpreter share the same authored candidate facts, and further independent cases remain necessary.

Scoring distinguishes an unobserved interpreter on the social route from a fabricated `none` response. Strict route/intent/outcome agreement is separate from semantic false-proposal counts. Signatures cover kind, candidate reference, duration, quantity, item-source dependency and cancellation commitment ID. They do not establish complete fidelity of every contextual field, concrete coordinate, funding interpretation or natural-language reply.

## Actual-Core causal pilot

Four scenarios run in both option orders, each with a new real Core session. D3 selects read-only own-inventory and seller-stock tools. Qwen generates conceptual steps from the returned observations; the binder creates a proposal; D3 assesses the proposal against a synthetic willingness fixture. An accepted supported proposal is passed into Core without replacement by a gold task. Only then does the simulator supply explicit synthetic arrival, purchase and handover receipts.

- The first direct purchase/handover wording completes in both orders. Actual Core accepts the model-generated steps, spends 9 of the buyer's 30 gold, restores its serialized state between effects, resolves the acquired UID 43 and hands that exact UID to the intended recipient. The task reaches Completed.
- The second direct wording produces the correct bound plan in both orders, but D3 requests clarification. No task is accepted.
- The hypothetical is incorrectly routed as an action request in both orders and ends in clarification rather than ordinary discussion. No task is accepted.
- The reported historical request correctly produces no action in both orders.
- Both completed replies report task status, gold, UID and owner correctly in structured fields. The prose exposes internal IDs and fixture terminology; this is a factual-grounding pass, **not** an immersive-dialogue pass.

Thus two of four positive arms complete, two abstain, and all four control arms produce no effects. Only the two historical controls match the desired social behavior fully. Technical completion of all eight harness arms must not be presented as eight successful NPC conversations. Willingness, consent, prices, arrival and receipts are synthetic fixture inputs; Unity movement, player inventory and general shop/container authenticity are not observed.

## Evidence and remaining work

Preserved under `work/architecture-probe-20261020/`: `event-first-v3-run1`, `prompt-schema-factorial-v1`, `event-first-v3p1-run1`, `proposal-audit-v3p1`, `causal-goal-v3-run1`, and `causal-goal-v3p1-run1`. Manifests freeze scripts/corpora and, for Core runs, both executed assemblies before inference. Exact requests and responses remain journaled; no failed call was silently retried. The reusable twenty-root fixture is `tools/NpcArchitectureEval/fixtures/goal-wiring-v3.json`, cases-array SHA-256 `80ae13c25e29b9976639169f87904b1a3928cb12bb12d04aa178ba068456d0c8`.

Parent verification: 54/54 Python harness tests, 1188/1188 Core tests, clean RuntimeCompile, and legacy/closed-loop/session CoreBridge self-tests. Mini's owned test service was stopped; the selected dialogue service remains on port 8080. No package was built or installed from this protocol slice.

Next: connect general planner constraints and position binding, strengthen offer/cancellation/unknown-target semantics on independent cases, provide a natural dialogue projection without internal IDs, then implement the bounded runtime decision adapter and shadow lifecycle tests. Broader capabilities, combat/perception, native effects and the existing one-second navigation stall remain separate open work.
