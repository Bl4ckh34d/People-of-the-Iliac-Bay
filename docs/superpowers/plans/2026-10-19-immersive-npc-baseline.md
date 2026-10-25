# Immersive NPC Baseline Implementation Plan

> **For agentic workers:** Execute this milestone task by task with independent review and evidence before completion. Follow the installed executing-plans skill when continuing from this document.

**Goal:** Complete IMM-01 with a defensible local-model comparison and actual production-harness compatibility evidence before changing the NPC harness.

**Architecture:** Preserve the frozen paired corpora, model settings and production source. Separate social-policy replay, experimental tool fixtures, accepted raw-completion/parser replay, and native-game behavior. Use the comparison to choose a candidate, then verify that candidate in the game before integrating IMM-02/03.

**Tech Stack:** PowerShell 7, llama.cpp, local Qwen GGUF models, accepted LivingCities.Core assembly, Daggerfall Unity native probes.

**Spec:** `docs/ROADMAP.md`, “Immersive NPC architecture follow-up”, IMM-01 and the approved delivery order.

## Global Constraints

- Root has assessed the comparison and released the source freeze for evidence-based repairs and approved IMM-02 implementation. Keep matrix corpora, configs, and runners frozen; preserve existing evidence and failed traces.
- All in-game text remains English. No borrowing/lending feature is introduced.
- Delegate only to Luna/high-or-lower workers. The user's later authorization allows five workers alongside the primary agent (six total concurrency slots).
- Run one owned local model server at a time. Do not switch a model during a user game session.
- Never infer semantic quality or native action success from HTTP/parser completion.
- Preserve failed, ambiguous and interrupted cases. Do not reroll bad answers or overwrite results.
- Retain Qwen2.5 7B as the current accepted baseline. The full action/assent native probe failed; do not describe full action acceptance as passed.

### Task 1: Finish frozen paired inference

**Files:** `work/model-comparison/Continue-FrozenComparison.ps1`, both freeze manifests, eight `*-matrix-*.json` results.

- [x] Revalidate current process and frozen hashes; retain the existing active job.
- [x] Finish grounding and natural-conversation runs for both models and both modes.
- [x] Record successful answers, failed records, unattempted turns and exact failure causes separately. Quarantine the three 9B grounding-tools trajectories and their twelve unattempted turns; no rerolls.
- [x] Confirm native-template/token budgets, model hashes and source provenance for all runs in the frozen matrix artifacts and protocol summaries.

### Task 2: Review meaning independently of transport

**Files:** `work/model-comparison/Export-TrajectoryReview.ps1`, exported review artifacts, per-category assessments.

- [x] Export completed 7B production held-out evidence with prompt and tool metadata.
- [x] Complete matched memory and time/place reviews using calibrated severity.
- [x] Expand review to role, schedule/presence, actions, and natural conversations; retain script/split denominators.
- [x] Review disputed findings against actual histories and authoritative context; preserve sample limits and differing count schemas.
- [x] Report category-level tradeoffs and representative examples; do not automatically select a winner from averages.

### Task 3: Verify the actual accepted transport and parsers

**Files:** `work/model-comparison/Invoke-AcceptedHarnessReplay.ps1`, accepted fixtures and assembly, distinct per-model replay results.

- [x] Independently verify fake-HTTP request fidelity, parsers and context-budget blocking.
- [x] Run the exact accepted raw `/completion` fixtures against each model sequentially.
- [x] Inspect grammar/parser failures, truncation, fallback selection and whether required context was actually present.
- [x] Keep raw-template compatibility failures distinct from poor conversation reasoning.

### Task 4: Cover production personality/mood inputs

**Files:** `work/model-comparison/Build-PersonalityMoodSuite.ps1`, supplemental runner/tests, separate supplemental results.

- [x] Build five source-backed/explicitly controlled trajectories with exact accepted helper output.
- [x] Verify one initial system message, authoritative mood replacement, retained history, and bounded checkpoint retries.
- [x] Verify the exported turn-eight request is accepted by Qwen 9B `/apply-template` without inference.
- [x] Run both models through the supplemental cases with unchanged settings; human-review their behavior. The parent-read review is illustrative and has no exhaustive critical-turn count.
- [x] State that these helper tests do not implement conversation-driven persistent mood or NPC-specific outdoors behavior.

### Task 5: Native candidate acceptance

**Files:** Existing native dialogue probes, `docs/TESTING.md`, model launcher configuration only after selection.

- [x] Choose a candidate based on matched semantic evidence and accepted-transport compatibility: retain 7B under the current accepted protocol, not as a claim of general superiority.
- [x] With DFU closed, run owned native conversations using actual roles, clock, memories and current activity. Three focused probes passed; the separate full action/assent probe failed as recorded below.
- [x] Measure complete user-turn latency across all routing, validation and reply calls. Five native 7B turns measured 761.723 ms median (642.889–851.068 ms range) on bundle 918; this is not a 7B-vs-9B comparison.
- [ ] Verify consent, refusal, false premises, scene transitions, callbacks, reopening and one actual action outcome.
- [x] Keep the previous model available; retain 7B as the current baseline. The known action/assent failure remains an open acceptance gate.

### Task 6: Publish evidence and unlock the next slice

**Files:** `docs/TESTING.md`, `docs/ROADMAP.md`, project Obsidian note.

- [x] Record results, limitations, model choice and unresolved tradeoffs with reproducible artifact paths in `work/model-comparison/IMM-01-assessment.md` and `docs/TESTING.md`.
- [ ] Mark IMM-01 complete only after its full acceptance scope is proven.
- [ ] Begin the separately testable IMM-02 shared-context snapshot and IMM-03 live-read-tool integration using the verified candidate.

### Follow-up: native action/assent acceptance

The full owned action/assent command on bundle 918 exited 1. `ACTION_PROBE_PASS`, `ACTION_FEEDBACK_PASS`, and `SIMPLE_ACTIONS_PASS` are partial subcheck evidence; the latter explicitly says no model assent was tested. “Would you mind waiting here?” produced zero decomposition spans and no task attempt. The friendly reply is not an executed native Wait task. Preserve the original trace at `work/model-comparison/native-7b-20261019-183030/action-assent-town-test.log` (SHA-256 `CA2CD313897F2EB8B4B93E14A185500658BE6FD5CAF908916C968752ACAAC53E`).

- [ ] Diagnose the no-decomposition/no-task result; do not infer consent or execution from the verbal response.
- [ ] If a repair is made, add a targeted regression and rerun the owned action/assent probe against bundle 918. Preserve the full failed and repaired evidence and satisfy the native acceptance-plan checks for action chat, task progress and disk persistence.
- [ ] Keep IMM-01 incomplete until this known gap and its remaining acceptance scope are proven. This follow-up does not block approved IMM-02 implementation and adds no approval step.

## Current execution checkpoint

Root assessed the comparison and retained 7B under the accepted protocol; this is not a general superiority claim. All eight frozen grounding/natural matrix runs, both accepted raw replays, all four supplemental mood runs, and the semantic reviews have terminal/reviewed artifacts. Three 9B grounding-tools trajectories failed `invalid_tool_arguments`, with twelve follow-up turns unattempted; quarantine them without rerolls or scores. The 9B `<think>` output selects the accepted prose fallback, and its map/threat fixtures show specific routing errors.

On unchanged installed bundle 918, the semantic-conversation, commerce-chat, and family-threat-refusal probes passed. Five native 7B turns measured 761.723 ms median from immediately before `SubmitFreeText` through persisted visible Talk exchange and `conversationBusy=false`; timing instrumentation was rolled back and 459 deployed sources match the accepted snapshot. A later full action/assent probe exited 1: it emitted partial MoveTo/action-feedback/simple-action markers, but “Would you mind waiting here?” decomposed to zero spans and no task attempt. Preserve `work/model-comparison/native-7b-20261019-183030/action-assent-town-test.log`; do not claim action/assent acceptance. IMM-01 remains incomplete until this known acceptance gap and all remaining acceptance scope are resolved.

The comparison prerequisite is assessed. Root has released the source freeze for evidence-based general repairs and approved IMM-02 implementation using 7B as the current baseline. This sequencing decision does not mark IMM-01 complete or weaken native acceptance. The separately testable IMM-02/03 work remains tracked in its own plan.
