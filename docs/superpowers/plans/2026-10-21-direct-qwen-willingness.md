# Optional direct Qwen willingness implementation plan

**Goal:** Make the existing opt-in complete-goal extraction usable with the optional D3 planner disabled, while preserving independent whole-plan willingness and native acceptance checks.

**Architecture:** Reuse the existing immutable willingness proposal, captured context and exact current turn. D3 remains the willingness backend when enabled. A separate, explicitly opted-in Qwen call can decide ACCEPT or DECLINE only when the planner setting is off. Extraction, willingness, native validation, persistence and wording remain separate stages. An enabled but unavailable D3 must not silently change backend.

**Scope:** Existing `-peopleoftheiliacbay-goal-extraction` plus a new `-peopleoftheiliacbay-qwen-willingness` opt-in. No default enablement, model switch, semantic regex, action schema change, execution bypass or per-case prompt repair. Pending offers and active commitments retain priority. Existing two-pass gates remain unchanged unless the same explicit policy applies.

## Package

- [x] Add a pure bounded Qwen willingness prompt/grammar/parser adapter using `LocalDecisionWillingness.Build` and its exact proposal/context. Parse only the exact offered ACCEPT/DECLINE protocol; malformed output is unconfirmed. Keep the original user text as data. Parent observes RED79/84 then GREEN84/84.
- [x] Add a distinct native conversation stage for the Qwen willingness call, with the same session, actor, snapshot, duplicate callback and proposal-signature checks used by D3. Require healthy local dialogue, explicit flag and planner disabled; never reinterpret extraction as consent. Controlled native145 verifies the completion boundary.
- [x] Relax only the complete-goal opt-in gate for that available explicit backend. Preserve D3 behavior and the existing target/resource revalidation before commit. Close the independently discovered legacy outage bypass, with RED139 and controlled resolver GREEN145.
- [x] Verify actual Qwen binding/consent and one exact saved MoveTo/Wait task from a controlled valid goal; preserve first failed fixtures and original141 model output separately. RuntimeCompile/Unity145 pass and baseline/settings restore succeeds.
- [ ] Compile, build and run owned native acceptance. Require D3-off extraction plus separate willingness, once-only accepted save, decline/unavailable/stale no-save controls and pending-offer/commitment priority. Preserve original model outputs. If the model proposes a wrong plan, record the failure; do not repair semantics to obtain a pass.
- [ ] Review source and evidence independently; document implemented scope versus remaining native/model limits, then create and verify a recovery copy.

Parent owns every build, test, service, model call and game session serially. Workers share the checkout and must freeze edits before builds. No commit, reset or broad staging is authorized by this plan.

Acceptance status145: the full live-extraction requirement remains open. Original141 returned invalid overlapping source spans and also used an unsupported consecutive-WAIT fixture. Native145 deliberately supplies a controlled supported goal and does not claim full original extraction. See `docs/2026-10-21-direct-qwen-willingness.md` for all failures, source/evidence hashes and remaining native regression coverage.

Follow-up146 completes the unchanged one-/two-pass comparison on all six existing cases, with21 first completions retained. Two-pass preserves the three positive chain shapes, but parameters and intent suppression still fail. Native146b reaches actual decomposition/parameters/one repair, then correctly rejects missing explicit durations before binding or consent. Full positive extraction acceptance remains open. The sequence probe now restores its owned baseline on both interrupted and model-failure paths; see `docs/2026-10-22-goal-extraction-comparison.md`. No repeated sampling or prompt change was used to turn the failed model result into a pass.
