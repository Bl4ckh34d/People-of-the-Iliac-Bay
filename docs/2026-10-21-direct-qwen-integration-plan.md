# Direct Qwen goal extraction integration plan

Checkpoint145 partially implements this plan: explicit D3-off Qwen willingness is now a separate native stage, with controlled valid-goal binding/consent/save acceptance and no silent D3-outage fallback. Full original extraction acceptance remains open;141's invalid first output and unsupported consecutive-WAIT fixture are preserved. [Current implemented scope and limits](2026-10-21-direct-qwen-willingness.md). The remaining text records the original design and acceptance requirements, not a declaration that every item is deployed or passing.

Checkpoint127 adds only the first-call diagnostic fields under the existing explicit goal-binding probe flag. They retain request JSON, raw response and decoded result separately from repair fields in memory. No new default logging, extraction behavior, consent path or model setting changes. Durable journal export and native capture of these fields still need implementation/verification; the remaining plan below is not deployed behavior.

This document originally described a future, opt-in integration step. Checkpoint145's limited implementation is summarized above; broader rollout remains unverified. The offline comparison supports testing complete-sequence Qwen extraction as a candidate path. It does not establish production readiness: the six-case fixture still contains negation, historical-quotation, and unresolved-request failures.

## Bounded next step

Add one opt-in path for a new action request after the existing top-level dialogue route has selected `ACTION_REQUEST`. Use the existing Qwen model and the existing complete-sequence goal schema to extract the full ordered request in one call. Keep the player's exact current turn and attributed history unchanged. Keep D3 speech-act routing optional; when it is disabled, the existing Qwen route path must still be able to reach this opt-in extraction path.

Do not use the sequential D3-or-Qwen action-kind selector for this path. Do not add keyword rules, regular expressions, repeated-kind bans, or other hand-written semantic interpretation. Do not change model identities, model settings, service ownership, or GPU/TTS configuration as part of this integration step.

The first production seam is `PeopleOfTheIliacBayMod.TrySemanticActionConversation` in `mod/Scripts/PeopleOfTheIliacBayMod.DialogueActions.cs`. Preserve its current priority order: interpret a real pending offer first; handle active commitments through their existing path; consider complete-sequence extraction only for a new request. The existing `GoalExtractionEnabled` gate in `mod/Scripts/PeopleOfTheIliacBayMod.GoalActions.cs` currently depends on the separate decision session and a command-line flag. Make any future opt-in gate explicit without letting extraction imply consent.

Reuse `NpcGoalChat.ExtractionInstructions` and `NpcGoalChat.Parse` in `src/PeopleOfTheIliacBay.Core/NpcGoalChat.cs`. Require exact, ordered source spans and parameter quotes from the current turn. Keep the existing rejection of non-request intent, unresolved conditions, exclusions, missing fields, and unsupported steps. Do not repair or semantically retry the first extraction during the bounded comparison run.

## Binding, willingness, and commit boundary

Bind every extracted step against fresh native candidates using `NpcGoalBinding.FilterCandidates`, `TryBindStep`, and final `TryBind` in `src/PeopleOfTheIliacBay.Core/NpcGoalBinding.cs`. Preserve current actor, conversation, request, and candidate freshness checks in `PeopleOfTheIliacBayMod.GoalActions.cs`. Keep native Core validation as the final structural boundary. A model proposal alone must never save or execute an action.

Keep willingness as a distinct stage after the complete plan has been bound. Require explicit acceptance of the whole proposed plan before calling `TryAcceptConversationTask` or `TryAcceptConversationAction`. A decline, invalid response, unavailable service, changed target, or stale conversation must save no action. Do not treat an extraction result, route selection, or structural Core acceptance as willingness. The current goal path requires `DecisionWillingnessEnabled`; resolve that coupling deliberately before allowing this path when D3 is disabled. Use the same approved Qwen and D3 services already configured by the application; do not introduce a model switch in this step.

Keep `pendingConversationAction` handling ahead of extraction so a current pending offer remains bound to its exact live candidate. Keep active commitment cancellation or modification on its existing route. A new request may supersede an unaccepted offer only through the existing confirmed route behavior.

## Evidence and rollout limits

For the first native capture, preserve the first extraction request JSON, raw response body, decoded extraction content, and hashes before parsing. `LocalDialogueClient.Extract` already exposes these values through its trace callback. The current `GoalActions` trace stores only the prompt and decoded result, so it is insufficient for this evidence requirement. Do not issue a semantic repair or retry before recording the first response. Retain the original response even when parsing or validation rejects it.

Keep the feature opt-in while collecting native evidence. Record extraction status, step count, binding result, separate willingness result, and final Core save result without logging private conversation text by default. Exercise unseen negation, quoted history, unsupported targets, ambiguity, stale candidates, pending offers, and active commitments before considering broader use. The six-case offline result and any initial native capture are not, by themselves, grounds for enabling this path by default.

The bounded implementation surface is `mod/Scripts/PeopleOfTheIliacBayMod.DialogueActions.cs`, `mod/Scripts/PeopleOfTheIliacBayMod.GoalActions.cs`, and the existing willingness integration in `mod/Scripts/PeopleOfTheIliacBayMod.Willingness.cs`. Reuse the current core parser and binder. Do not modify routing semantics, action contracts, native execution, model selection, TTS, or services in this step.
