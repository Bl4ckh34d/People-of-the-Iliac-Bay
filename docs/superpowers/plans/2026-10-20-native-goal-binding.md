# Native Goal Extraction and Binding Implementation Plan

> **For agentic workers:** Implement each task in order. Use test-first changes and review each boundary before moving to the next task.

**Goal:** Extract a bounded conceptual action plan from the exact player request, bind every executable parameter to current native evidence, and send only a complete bound proposal to D3 willingness.

**Architecture:** Keep language extraction, native binding, willingness, and execution as separate stages. The extractor preserves requested meaning and uncertainty; the binder resolves only current verified options and enforces supported parameter limits; existing D3, consent, freshness, stores, executors, and receipts retain authority. Migrate only the existing action-request path first.

**Tech Stack:** C# 7.3 / `netstandard2.0` Core; existing local dialogue constrained-output path; current Core and mod action/task contracts.

**Spec:** `docs/superpowers/specs/2026-10-20-npc-agent-architecture.md`; evidence: `docs/superpowers/specs/2026-10-20-native-wait-chain-evaluation.md`, `docs/superpowers/specs/2026-10-20-causal-goal-protocol-evaluation.md`, and `work/native-willingness-20261020/native-live-duration-mismatch.log`.

## User clarification during implementation

The LLM translates natural language directly into typed amounts and units. Do not use regex, keyword intent rules, or a handcrafted phrase/number-word parser for NPC requests or task values. The briefly introduced phrase parser was removed. Native code validates structured fields, references and bounds, performs arithmetic, and executes accepted plans. **Every duration uses in-game time, never real time.** The duration schema is nullable `{amount, unit: game_seconds|game_minutes|game_hours, source}`; quantity is nullable `{amount, source}`. Source quotes prove provenance, not numeric meaning: a model can still translate "ten seconds" incorrectly. D3 sees the full original request and proposed values; occasional semantic mistakes remain accepted limitations.

## Current acceptance

The subsequent [native acceptance report](../specs/2026-10-20-native-goal-binding-acceptance.md) supersedes the rollout status below: package `025C14C9` is installed, Core 1231/1231 and native accepted-Wait mechanics pass, history is windowable, and the normal hybrid launcher enables extraction. Live refusal and unavailable-target results remain separate from deterministic accepted execution. The earlier checkpoint below is preserved as historical evidence.

## Earlier implementation checkpoint, 2026-10-20

The typed Core contracts, strict JSON reader, binder, extraction transport and opt-in runtime bridge are implemented. The handwritten natural-language value converter and its tests are absent. Current checks pass: Core 1230/1230, adapter 77/77, RuntimeCompile, Unity build and owned native GoalBindingProbe. Installed bundle SHA-256 is `D77AC312EC5E6F39D16D1B979F5A0873C5A5145973BF4693B60C1C0CC70701ED` (640 runtime assets).

The native probe rejects a fabricated parameter quote before willingness and without saving an action or task. The live request extracts eighteen as 18 game seconds, resolves a current Wait target, and reaches real D3 willingness. D3 declines; no action/task starts. This run verifies the decline path, not accepted execution of the new extraction path. Prior willingness execution evidence remains separate. The normal launcher does not yet enable the extraction flag; promotion and remaining integration coverage are still open.

Fresh diagnostics preserve model failures rather than replacing them with text rules. All five outputs pass structural parsing, but the model misclassifies a Follow as Wait, treats a reported instruction as a request, and drops MoveTo in a history-dependent request. Two cases match the expected action kinds. This small set is diagnostic evidence, not a reliability estimate. The initial analysis accidentally supplied a null request binding through PowerShell; `fresh-extraction-reparsed.json` corrects that setup error without rerunning model requests.

Evidence: `work/goal-binding-20261020/core-final.log`, `adapter-final.log`, `final-build.log`, `native-final.log`, `fresh-extraction-reparsed.json`, and `work/town-test.log`. The full implementation goal remains active.

## Global Constraints

- Preserve the exact current utterance, attributed history, request binding, and source spans through interpretation and D3.
- Keep `NpcTaskContract.MaximumSteps` at eight; reject or clarify unsupported or incomplete goals instead of truncating them.
- Model output may name only current opaque candidate references. Native code owns native target IDs, coordinates, item IDs, unit conversion, arithmetic, and dependency indices.
- Preserve conditions, exclusions, and missing or unsupported fields. Any unresolved requirement blocks an executable plan.
- Do not add regex, keyword routing or handcrafted natural-language value parsing. A strict JSON reader validates model fields only. Typed game-time conversion is arithmetic, not interpretation of player text.
- D3 willingness sees the complete bound proposal and original request. D3 selection alone is not consent or execution authority.
- Preserve native consent, current snapshot/scene/save checks, target/resource/schedule revalidation, existing effects, idempotency, and receipt-backed replies.
- Do not claim current D3 willingness acceptance is complete. The preserved live duration mismatch remains an open failure.
- Keep accepted-action replies deterministic and receipt-grounded. Natural-language rendering is outside this slice.

## Files and Interfaces

- Create `src/PeopleOfTheIliacBay.Core/NpcGoalContracts.cs` for proposal, bound-goal, source-span, duration, reference, and unresolved-field types.
- Create `src/PeopleOfTheIliacBay.Core/NpcGoalChat.cs` for the extraction prompt, bounded output grammar/schema, and strict parse entry point.
- Create `src/PeopleOfTheIliacBay.Core/NpcGoalBinding.cs` for deterministic resolution against current `NpcActionOption` sets and conversion to existing `NpcActionDecision` values.
- Add `mod/Scripts/PeopleOfTheIliacBayMod.GoalActions.cs` and hook it from `DialogueActions.cs` before ordinary new-action interpretation; retain existing offer, commitment, task save and dispatch code.
- Register dedicated `NpcGoalExtractionTests`, `NpcGoalBindingTests` and `NpcGoalCompletionTests` in the existing test runner.
- Keep offline diagnostics under `tools/NpcArchitectureEval/`; do not treat Python fixture binding as production implementation.

Proposed Core surface:

```csharp
NpcGoalChat.Prompt(string originalText, string attributedHistory, string candidateContext)
NpcGoalChat.Parse(string output, ConversationContextBinding binding, string originalText)
NpcGoalBinding.TryBind(NpcGoalProposal proposal, IList<IList<NpcActionOption>> candidates,
    IList<int> selectedOptionIds, out NpcBoundGoal bound, out string reason)
```

`NpcGoalChat.Parse` returns `NpcGoalExtractionResult` with status (`Valid`, `Invalid`, or `Unavailable`), optional proposal, and bounded reason. `NpcGoalProposal` carries the binding, original utterance, request intent, ordered steps, goal-level conditions/exclusions, and unresolved fields. Each step carries its action kind, exact original-text span, unresolved surface target and typed model-produced parameters. `NpcGoalDuration` retains decimal amount, a game-time unit and exact parameter quote; `NpcGoalQuantity` retains integer amount and quote. The proposal distinguishes null/unspecified from explicit missing fields. `NpcBoundGoal` contains copied native decisions plus the binding and original text; it does not mutate state.

## Task 1: Add a bounded conceptual proposal contract

**Files:** Create `NpcGoalContracts.cs` and `NpcGoalChat.cs`; add cases to `NpcActionSequenceChatTests.cs`.

**Interfaces:** `NpcGoalChat.Parse` returns a typed proposal or a typed invalid/unavailable result. It must not return an executable `NpcActionDecision`. Extraction sees no candidate menu. Target mentions remain unresolved exact text with source spans; a later LLM binding call selects an offered opaque candidate ID.

- [ ] Add tests for ordered spans, up to eight steps, supported and unsupported action kinds, exact target references, condition/exclusion preservation, and explicit missing/ambiguous duration or quantity.
- [ ] Add parser tests for extra fields, wrong field types, invalid or out-of-range spans, more than eight steps, and contradictory intent plus steps. Unknown candidate references remain explicit unresolved fields for the binder; they never become native IDs.
- [ ] Add duration-provenance regressions: an invented quote "60 seconds" is rejected when absent from the original source; a typed amount 60 with the authentic quote "ten seconds" remains structurally valid and is an explicit model semantic-error exposure, not something native phrase parsing may repair.
- [ ] Run `dotnet run --project tests/PeopleOfTheIliacBay.Tests` and confirm new tests fail before implementation.
- [ ] Implement the strict output schema and typed parser. The LLM supplies amounts and game-time units; never substitute a candidate default for an explicit model parameter.
- [ ] Re-run the Core suite. Keep existing prompt and parser behavior unchanged outside the new APIs.

The live evidence in `work/native-willingness-20261020/native-live-duration-mismatch.log` records D3 `ACCEPT` with a requested 10-second Wait but a 60-second native schedule and 60.839-second observed duration. Preserve that semantic failure. Structured extraction separates model-produced meaning from candidate defaults, but cannot guarantee model interpretation. Native binding must preserve the typed values exactly, not silently reinterpret the player's words.

## Task 2: Bind proposals to current native options

**Files:** Create `NpcGoalBinding.cs`; add cases to `NpcTaskDependencyAcceptanceTests.cs` and `NpcActionSequenceChatTests.cs`.

**Interfaces:** `TryBind` accepts the proposal and per-step current candidate lists. It returns a complete `NpcBoundGoal` only when every requested step and constraint is represented by existing native contracts. It returns a reason and no executable decisions when any part remains unresolved.

- [ ] Test target references that resolve to exactly one option, missing or duplicate references, invented/unavailable references, mismatched action kinds, changed candidate targets, and unsupported native action kinds.
- [ ] Test parameter bounds from existing contracts: duration 10–900 game seconds for timed actions, quantity 1–1,000,000 and the option's `MaximumQuantity`, with native unit conversion only after extraction.
- [ ] Test that null duration is distinct from explicit typed duration and uses the existing documented 300-game-second default only for unspecified output. Preserve explicit amount/unit through checked game-time conversion; reject invalid units, overflow and native bounds before willingness.
- [ ] Test that all steps remain in order; no step is dropped; item dependencies are derived by native code from prior verified steps; duplicate, ambiguous, wrong-quantity, or unavailable item sources fail closed.
- [ ] Test that any condition, exclusion, payer/budget, or reference not supported by existing native task contracts remains unresolved and blocks a full plan. Do not broaden executor capabilities in this task.
- [ ] Run Core tests and verify generated decisions copy exact targets and quantity/duration values without mutating candidate inputs.

## Task 3: Migrate the action-request path in layers

**Files:** Modify `PeopleOfTheIliacBayMod.DialogueSemanticTasks.cs`; keep existing `DialogueActionTargets.cs`, `PeopleOfTheIliacBayMod.Willingness.cs`, stores, executors, and reply code as authorities.

**Interfaces:** After existing route selection classifies a current turn as an action request, call `NpcGoalChat.Prompt/Parse`, then `NpcGoalBinding.TryBind`, then existing `ResolveProposedActionWillingness(actor, ..., bound.Decisions, ...)`. Do not call willingness for `none`, invalid, unsupported, ambiguous, or unresolved output.

- [ ] Preserve route, social, offer-response, and commitment-update paths. Do not widen routing or add phrase-specific branches.
- [ ] Keep extraction and binding read-only. No task save, action acceptance, effect, callback, or acknowledgment occurs in either stage.
- [ ] Send D3 the complete bound proposal and unchanged original text. Retain existing context stage tokens, callback ownership, snapshot freshness, and target/resource rechecks before commit.
- [ ] Only after D3 agrees, use existing native consent and schedule/capability checks, then existing task/action stores and execution. Keep once-only effects and authoritative receipts unchanged.
- [ ] If extraction, binding, D3, freshness, consent, or native validation fails, save no action/task and use existing clarification, unavailable, or receipt paths as appropriate.
- [ ] Add an integration regression for an invented parameter quote: no D3 request or native save may occur when source provenance is invalid. Record actual model-produced values separately from structural validity and native integrity. Retain the prior semantic mismatch log unchanged.

## Verification and rollout boundary

Run `dotnet run --project tests/PeopleOfTheIliacBay.Tests` after each Core task. Then run the existing mod `RuntimeCompile` and willingness/consent/snapshot probe suite after Task 3. Replay the preserved duration mismatch and the existing Follow-to-Wait, conditional, unsupported-target, and option-order cases. Require no dropped requested steps, no silently changed explicit parameters, no unresolved field reaching D3, no stale callback commit, and no state mutation before D3 plus native gates.

Do not set a perfect model-score threshold. Review a small fresh, source-reviewed diagnostic set for false plans, missed valid requests, unresolved cases, and parameter fidelity. Keep the new path opt-in until native provenance, consent and snapshot checks pass; deployment remains covered by the user's active implementation goal. This slice does not add new action adapters, wider task support, or natural-language accepted-reply generation.
