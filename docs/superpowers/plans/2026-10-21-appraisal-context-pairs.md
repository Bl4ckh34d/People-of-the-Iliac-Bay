# Appraisal motivation and knowledge context pairs

**Goal:** Add a small synthetic Core-generated evaluation slice that checks whether appraisal prompts carry bounded observer-owned motivation and epistemic context without turning context into action authority or hidden oracle.

**Architecture:** Keep appraisal fixtures v1/v2 frozen. Add a v3 fixture with six rows arranged as three matched pairs, built through the existing typed Core APIs and `NpcExperienceAppraisalContract.CreateRequest`. Preserve human labels outside the model payload; use exact request bodies and human review for semantic quality.

**Tech Stack:** Existing .NET Core appraisal/knowledge/motivation APIs; `tools/NpcFactorEvaluation` fixture builder and preparation validator; pinned 4096-context Qwen request with `n_predict=1024`.

**Scope:** Evaluation design only. Synthetic fixtures are not native gameplay evidence and cannot commit state.

## Current seam

`AppraisalFixturePreparation.BuildCases` currently makes 32 rows (4 profiles × 8 events) and calls `CreateRequest` without commitments, motivation context, or knowledge snapshot. Keep those 32 rows unchanged. The builder and validators currently accept only fixture v1/v2 and hard-code 32 rows, so v3 needs an explicit fixture path and validator update; do not silently append rows to a frozen run.

The production capture seam is `WorldStateStore.TryCaptureNpcExperienceAppraisal` in `src/PeopleOfTheIliacBay.Core/NpcExperienceCommit.cs`: it captures the observer's sparse `NpcMotivationState`, `NpcKnowledgeSnapshot`, and current service commitments. The motivation capture currently supplies persisted needs/goals/priorities but passes no transient capability/resource/threat/duty facts. Service contracts are projected separately into bounded `NpcExperienceCommitment` rows (reserved→planned, active-in-window→active, max 8). A fixture built with `NpcMotivationDuty` directly tests the request contract/formatter only; it must not be presented as proof that native event capture supplies duties.

`NpcExperienceAppraisalContract.CreateRequest` accepts `NpcMotivationContextSnapshot` and `NpcKnowledgeSnapshot`. Motivation context is built through `NpcMotivationContextSnapshot.Capture` with branch, observer, revision, exact captured clock, profile reference, and typed facts. Known scalar needs require revision-matched evidence of an allowed kind. Knowledge uses `NpcKnowledgeLedger.Create`, typed `NpcKnowledgeUpdate` factories, `TryApply`, and `TryCapture`; native evidence can create `VerifiedTruth`, while `FromAttributedClaim` preserves `Rumor`/belief/claim epistemics. The appraiser checks motivation and knowledge revisions again before commit.

Prompt visibility is deliberately limited. `NpcMotivationContextFormatter.ForModels` emits a bounded projection (default 6000 characters; whole facts may be omitted). Knowledge prompt formatting emits public selectable topic IDs and epistemic labels only; it does not emit fact values or source traces. Therefore the knowledge pair below tests epistemic metadata and non-inference, not recall of a hidden fact value.

## Proposed v3 rows

Add three pairs, six rows total, to the existing 32 for 38 total. Within each pair, keep observer/profile, event evidence, relationship, mood, time, revisions, and all unrelated fields identical. Use opaque evidence/source IDs shared within the pair; keep pair names and labels outside prompt bytes.

| Pair | One intended difference | Typed fixture input | Human review constraint |
| --- | --- | --- | --- |
| Health need high / low | Known health-need intensity, e.g. 75 vs 20 | Same actor and unknown-attribution injury event; one `NpcMotivationNeed(Health, Known, intensity, revision, evidenceRef)` in each snapshot | May acknowledge the supplied need as context. Do not infer injury cause, actor intent, medical capability, or a required mood/fear/trust delta. |
| Appointment duty present / absent | One current duty row vs no duty row | Same waited-through-deadline no-show event; `NpcMotivationDuty(Appointment, Schedule, Active, starts/ends, revision, evidenceRef)` in the present arm; empty duties in the absent arm | May mention an active scheduled obligation when supplied. Do not claim completion, player fault beyond the captured no-show, or permission/action authority. Mark this as synthetic request-projection coverage until native capture supplies transient duties. |
| Verified topic / rumor topic | Verified versus attributed epistemic/provenance package | Same actor, branch, public opaque proposition ID and test value; `FromNativeEvidence(... NativeObservation ...)` produces `VerifiedTruth`, while `FromAttributedClaim(... Rumor ..., AttributedReport, ..., attributionId)` preserves the attributed state. Evidence origin and attribution necessarily differ with the typed path. | Do not treat rumor metadata as verified world truth or infer that either topic explains the injury/status event. Since prompt contains metadata only, do not score for reproducing either hidden value. |

Use one existing profile for each pair and reuse the same generated personality object within that pair. Generate each event once per pair and pass the same event/evidence IDs to both requests; no event names such as `need-high` or `rumor` may appear in receipt, episode, source, proposition, or evidence IDs. Fixture labels and paired-condition keys remain review-only fields. No row uses model output to alter another row's inputs. Keep common actor, branch, capture time, topic, test value and revision matched in the knowledge pair; its evidence origin, epistemic kind and attribution are the required treatment package, not an independently isolated variable.

For context snapshots, bind every evidence ref to the same fixture branch and observer; clock/profile refs and fact refs must be revision-consistent and no later than `capturedGameTime`. Build knowledge snapshots for the same observer and branch, using public protection only for the one test topic. Add a regression that the exact hidden knowledge value and review label are absent from `Request.Prompt`, while the public topic label is present. Existing `NpcExperienceSharedContextTests` already cover detached snapshots, actor isolation, missing topics staying unknown, and stale motivation/knowledge revision rejection; do not duplicate those tests in the model fixture.

## Acceptance checks

Automated fixture checks should assert pair equality for all non-treatment inputs, one intended typed-field difference, correct observer/branch/time binding, non-null expected snapshot revisions, 38 unique case IDs, no label/pair-name leakage, no hidden knowledge-value leakage, and unchanged v1/v2 32-row validation. Verify generated `CreateRequest` prompts include the serialized motivation projection and only permitted public knowledge metadata. Assert the exact completion body still sets `n_predict=1024`.

For the 4096-token context, budget the *final rendered prompt including system/protocol framing* to at most 3072 tokens, leaving 1024 for output. Use the pinned GGUF tokenizer or an exact server tokenizer endpoint in preparation/preflight; character count and the formatter's 6000-character ceiling are not proof of token fit. Keep each context to at most one need, one duty, and one public topic. If a whole fact is omitted or a row exceeds the budget, fail preparation rather than silently making the pair indistinguishable. Do not infer fit from a live completion or reduce the response budget.

Manual review is separate from structural validation. Label each reply grounded/mixed/concern with a short reason. Review factual grounding, whether the one changed context is treated as observer knowledge rather than authority, and whether rumor/unknown status stays distinct. Do not require identical wording, a monotonic high-need response, exact numeric deltas, or keyword/regex grades. Appraisal output remains a proposal; no synthetic fixture can mint a native capture ticket or commit effects.

## Source map

- `tools/NpcFactorEvaluation/AppraisalFixturePreparation.cs` — current 32-row builder; context arguments omitted.
- `tools/NpcFactorEvaluation/fixtures/appraisal-live-scenarios-v2.json` and `.md` — frozen v2 synthetic events and attribution notes.
- `tools/NpcFactorEvaluation/validate_appraisal_preparation.py` and `test_appraisal_preparation_contract.py` — version and 32-row assumptions to extend explicitly for v3.
- `src/PeopleOfTheIliacBay.Core/NpcExperienceAppraisal.cs` — typed request, prompt, commitment and revision bindings.
- `src/PeopleOfTheIliacBay.Core/NpcMotivationContext.cs`, `NpcMotivationContextFormatter.cs`, `NpcMotivationState.cs` — typed snapshot facts and projection.
- `src/PeopleOfTheIliacBay.Core/NpcKnowledgeLedger.cs` — verified versus attributed update paths and public-topic projection.
- `src/PeopleOfTheIliacBay.Core/NpcExperienceCommit.cs` — live capture and pre-commit freshness checks.
- `tests/PeopleOfTheIliacBay.Tests/NpcExperienceSharedContextTests.cs` — existing same-actor, detached, unknown, and stale-context coverage.
