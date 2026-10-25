# Architecture development probe

**Goal:** Test the approved hybrid NPC architecture before changing production dialogue routing. The active full-mod goal remains unchanged.

**Scope:** A disposable offline probe under `work/architecture-probe-20261020`, using actual captured facts where available, explicitly labeled synthetic contrasts, the user's selected abliterated Qwen model and existing Core validation contracts. This is development evidence, not the architecture's 200-case held-out promotion test or native gameplay acceptance.

## Frozen experiment

Executed scope update: the first wiring probe contains twelve synthetic roots, not the originally proposed forty. The wider future-capability experiment adds 72 scenario roots (190 decisions per model over both option orders and turns) and 22 extension roots (44 decisions per model). D3-mini and D3-flash were subsequently authorized, downloaded, verified and tested. Preserve all separate scopes and original failures. Reusable code is now under `tools/NpcArchitectureEval`; results and limits are in `docs/superpowers/specs/2026-10-20-npc-model-capability-evaluation.md`. User clarification removes a universal 100% semantic quality requirement; deterministic native integrity is still tested separately.

1. Prepare approximately 40 semantic cases with the latest player turn separate from attributed history, current facts, pending offers and active commitments. Include direct and indirect requests, social conversation, quotations, reported speech, negation, hypothetical/future conditions, cancellation, modification, unsupported targets, quantities and ordered instructions. Label intent separately from willingness. Freeze corpus before inference.
2. Freeze four-way event routing: `SOCIAL`, `ACTION_REQUEST`, `OFFER_RESPONSE`, `COMMITMENT_UPDATE`. A current unsupported request still requires interpretation and clarification; absence of capability does not erase its intent. Compare event routing with the old flat supplied-action matching only where both inputs can be rendered without expected-label leakage. Do not call an oracle option menu a planner.
3. Run a structured language interpreter from the original input, never from expected route or plan. Preserve duration amount/unit, target references, order, quantities, conditions and missing evidence. Normalize typed numbers in code. Resolve opaque references only through the supplied native facts; unknown stock/targets/permissions stay unknown.
4. Feed materialized proposals into real `NpcTaskContract.Validate` and a disposable `WorldStateStore` acceptance path. Verify refusal of absent consent and duplicate ownership. No Unity effects, purchases, save writes or user-game changes are permitted. Core acceptance does not prove navigation or actual transactions.
5. Record per-stage technical success, intent correctness, exact argument coverage, binder rejection, critical false-action proposals and option-order sensitivity. The combined pipeline uses actual model outputs, not gold labels. Diagnostic independent interpreter calls are reported separately from conditional pipeline latency.

Use one attempt per frozen case/stage/order; keep malformed JSON, HTTP errors and wrong answers. Log insertion-order-preserving UTF-8 request bytes/hash before HTTP and preserve raw response bytes. Sorting criteria keys would invalidate option-order tests. Retain the current user's dialogue-model selection. Do not tune on held-out outputs or promote from this development probe. Existing 34-case results remain historical regressions.

## Ownership and review

- Luna corpus worker: frozen development cases and provenance only.
- Luna contracts worker: existing Core API review, then a separately authorized disposable bridge.
- Luna review/client worker: methodological review and transport client with local HTTP tests.
- Parent: protocol, scoring, model runs, aggregate verification and decisions. Workers do not start or switch models.

## Current operational boundary

The full thread goal is active. The selected dialogue service was found stopped through live process/endpoint inspection and restarted through its normal launcher. Automatic approval review rejected the attempted second Kev server launch without giving a reason. Do not bypass that rejection through a repackaged command or another tool. Continue the development probe with the running dialogue service; leave the new Kev arm explicitly unattempted unless the block is resolved. Historical Kev results do not substitute for that arm.

## Completion evidence for this slice

- [x] Corpus, protocol and runner hashes recorded before inference.
- [x] Transport tests and scorer/binder negative controls pass.
- [x] All attempted model calls have durable attempt/result records; unattempted arms are explicit.
- [x] Real Core contract results accompany the model proposals without claiming gameplay effects.
- [x] Parent reviews raw failures and publishes a scoped report; roadmap and memory capture accompany the checkpoint.

The next production slice remains the interchangeable decision-service boundary and non-mutating shadow routing. Its implementation requires a separate executable plan based on these results; the full roadmap is not complete when this probe finishes.
