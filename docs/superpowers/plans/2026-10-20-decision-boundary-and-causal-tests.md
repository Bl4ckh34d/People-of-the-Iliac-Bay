# Decision boundary and causal architecture tests

**Goal:** Replace isolated choice-score claims with auditable event-first, actual-Core and loaded-game evidence while advancing the approved hybrid architecture.

**Spec:** [NPC architecture](../specs/2026-10-20-npc-agent-architecture.md), [capability evaluation](../specs/2026-10-20-npc-model-capability-evaluation.md), and the earlier local `work/architecture-probe-20261020/decision-boundary-design.md` contract proposal.

**Architecture:** Models supply proposals; Core owns typed contract validation and state transitions. Offline adapter receipts are explicitly synthetic. No experimental route is installed into production from this slice.

## Constraints

- Preserve the integrated dirty checkout and existing installed package; do not reset, broadly stage or replace unrelated work.
- C# 7.3 / netstandard2.0 for production Core; fixture tools may use net8.0.
- No language keyword/regex routing. Structured numeric/schema validation is permitted.
- Use explicitly selected Luna/high-or-lower workers, parent review and independent verification.
- Preserve failures and distinguish syntax, semantic interpretation, consent, effects, native behavior and performance.
- No universal perfect-model gate. Native integrity remains mandatory; synthetic effect evidence is never native evidence.

## Deliverables and evidence

- [x] Implement immutable `NpcDecisionRequest` / `NpcDecisionResult` / offered-option contracts in Core, with no executor references.
- [x] Test copies, option bounds/identity, probabilities, confidence, stale actor/session/save/scene/options, permitted archive-head advancement and non-actionable failures. TDD RED 1181/1188; GREEN 1188/1188. Independently review and scrub malformed failure metadata.
- [x] Extend `tools/NpcArchitectureEval/CoreBridge` with actual-Core purchase/handover lifecycle tests, explicit synthetic receipts, restore, cancellation and retained partial outcomes. Fix lossy quantity mapping, hardcoded UID output and omitted-consent defaults found in review.
- [x] Add a persistent JSON-lines Core session with current inventory reads, typed task acceptance/dispatch, explicit receipts, reconciliation, cancellation and snapshot restore. Run session, closed-loop and legacy self-tests.
- [x] Freeze and run the event-first Mini router plus candidate-bound Qwen interpreter on twelve development and sixteen fresh diagnostic roots, both route orders. Record strict stage metrics and actual Core rejections; do not promote from an outcome-only score.
- [x] Test D3's post-planning decision stage against the actual generated proposals without gold-plan menus. Preserve its remaining semantic errors.
- [x] Exercise model-selected inventory reads against actual persistent Core state before purchase, after purchase, and after restore/handover. Keep scripted task/effect inputs explicit; this does not test language-to-task generation.
- [x] Measure Mini + selected dialogue model + loaded disposable DFU city fixture and perform actual model requests. Retain both failed native frame gates separately from GPU resource compatibility.
- [ ] Connect actual on-demand tools, free goal interpretation, native proposal generation, willingness, acceptance, executor/simulator and receipt-driven conversation into one broad causal test suite.
- [x] Correct the action-specific interpreter schema: remove irrelevant quantity/dependency slots from Wait/Follow and derive unique exact-item purchase/handover dependencies. Preserve v3 prompt regression, factorial diagnosis and v3.1 development results. [Evidence and limits](../specs/2026-10-20-causal-goal-protocol-evaluation.md).
- [x] Run a bounded language-to-Core causal pilot with model-selected current inventory/stock reads, generated purchase/handover plans, synthetic willingness decisions, explicit receipts, restore and receipt-grounded replies. Two of four positive arms complete; two clarify. Historical controls pass; hypothetical controls still misroute. This is not the broad causal-suite completion above.
- [ ] Extend native proposal planning and validation to position-bound later Wait steps and other inventory directions; do not reinterpret Follow as Wait or silently substitute unavailable targets.
- [ ] Correct live-offer/cancellation handling and independent semantic regressions; preserve abstention, invalid interpretation and wrong action counts separately.
- [ ] Project confirmed state into natural English dialogue without internal actor/task IDs or fixture terminology. Structured factual accuracy does not prove prose quality.
- [x] Implement the runtime decision adapter and cancellation/main-thread dispatch source tests. [Boundary evidence](../specs/2026-10-20-decision-service-boundary.md): 31 adapter tests, 1191 Core tests, clean RuntimeCompile and an actual D3 wire smoke on the revision before the final per-Pump callback cap. No production caller or installed package.
- [x] Resolve the 128-character Purpose/instruction contract: explicit Instructions preserves exact bounded multiline prompt/turn/context under `npc-decision-v2`, with legacy constructor compatibility. Parent Core 1194/1194 and adapter 52/52.
- [x] Implement opt-in non-mutating shadow routing at the real SubmitFreeText snapshot boundary, including current offer/commitment capture, whole-turn history bounds, coalescing/dedup and lifecycle cancellation. [Slice C](../specs/2026-10-20-decision-shadow-routing.md) records installed package `BB77D2AC` and the passing deterministic native callback/production-tap/scene-save-close-hook probe. This is not an actual-model semantic test.
- [x] Evaluate native-context routing against actual models with frozen development and fresh diagnostic roots in both option orders. [640-call evidence](../specs/2026-10-20-native-route-replay-evaluation.md) preserves worse held-out scores after v2 eligibility masks; no semantic-accuracy improvement is claimed. Parent verifies 57 adapter / 1194 Core / 72 Python and native-tested package `6396B570`.
- [ ] Extend actual transition/disk-load/disable coverage and measure responsiveness before any bounded production rollout. Preserve native effect/receipt/consent gates separately from shadow acceptance.
- [x] Test the full native-context route/interpreter/Core Wait lifecycle with a typed fixture completion command and explicit Follow-to-Wait substitution controls. [116-call evidence](../specs/2026-10-20-native-wait-chain-evaluation.md): route 68/70, 18/20 supported completions, twelve wrong synthetic-consent acceptances. Test execution is complete; semantic rollout gate fails. Route legality alone never proves current intent or consent.
- [x] Isolate and repair the saved-city `ReadSteps.MoveNext` one-second stall with narrower measurements; do not relax the frame gate. [Native evidence](../specs/2026-10-20-incremental-loader-and-conceptual-pilot.md): 118-block parity/cancellation; final initial/later/approach maxima 103.51/112.98/56.43 ms; performance, daily itinerary, murder witness and final wrapper pass. This is fixture-specific and does not close broader concurrent Mini/game performance coverage.

## Verification commands

```powershell
dotnet run --project tests/LivingCities.Tests --no-restore
dotnet run --project tests/DecisionAdapter.Tests
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore
dotnet run --project tools/NpcArchitectureEval/CoreBridge/CoreBridge.csproj -- --self-test
dotnet run --project tools/NpcArchitectureEval/CoreBridge/CoreBridge.csproj -- --closed-loop-self-test
dotnet run --project tools/NpcArchitectureEval/CoreBridge/CoreBridge.csproj -- --session-self-test
python -m unittest discover -s tools/NpcArchitectureEval/tests -v
git diff --check
```

Model runs and owned-game measurements have separate hashed manifests and raw logs. Evidence, limits and next actions belong in Testing and the architecture report. Full mod goal remains active.
