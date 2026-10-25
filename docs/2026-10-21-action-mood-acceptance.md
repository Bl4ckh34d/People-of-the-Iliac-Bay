# Action Mood Acceptance (Provisional)

**Status:** Action mood, bounded full routing, transport cancellation pressure and explicit outage/recovery have native acceptance. IMM-09 is not complete.

## Final transport and outage checkpoint

Installed bundle: `364B459FF1B663195E769C6E95C0BCBDB9F7E909BBE484BC74E2EA35ACD99526`, 655 assets (`build-final-transport.log`). Core 1293/1293 (`core-transport.log`); RuntimeCompile and Unity integration pass.

`LocalDialogueDispatch` now synchronizes version-bound abort ownership. Each HTTP stage registers its actual request abort callback; cancellation aborts in-flight requests and prevents stale workers starting the next stage or delivering results. Old workers cannot clear a newer request's abort callback. Component destruction cancels requests and prewarm work. Payload construction precedes dispatch ownership, preventing constructor failures from stranding the busy slot. Transport failures retain their stage and are no longer mislabeled as invalid model output.

`cancellation-run.log` and `cancellation-native.log` pass real full routing both before and after controlled canceled requests. The latter receives one fresh typed social completion, displays and journals it once, and returns idle. No `DIALOGUE_FAILURE`, timeout, send failure or receive failure occurs in that run. This is a bounded native regression, not a claim that every possible service failure is eliminated.

`outage-fixed-run.log` and `outage-fixed-native.log` pass four actual model replies, one explicitly injected connection failure, one actual recovery reply, six exact persisted exchanges, disk reload and exact baseline restoration/checkpoint deletion. This probe uses production `SubmitSocialText` directly; the separate cancellation probe covers full routing. Inventory, wallet and action/task state remain unchanged.

The historical failures below remain recorded. No schema or prompt tuning was justified by their transport errors.

## Subsequent parent verification

- The stricter action probe passes in `strict-run.log` and `strict-native.log`, including source-independent checks against duplicate/rejected journal rows.
- Full `SubmitFreeText` passes in `routing-ordered-run.log` and `routing-ordered-native.log` through a validated spontaneous offer: one typed event, two visible rows, one journal exchange, and no stuck busy state. The separate direct social completion returns a valid neutral mood result; controlled callbacks and disk rollback also pass.
- Installed at this checkpoint: `CCEA4916BADFA86268DBF160B9156056C8D707055A6E194DC689ADDDBD0FCC97`, 655 assets. `build-outage.log` records installation. The stricter action run preceded the outage wording change; full routing ran afterward.
- Technical failures now use `No new reply was received.` with unavailable provenance rather than fictional NPC refusal. Core remains 1291/1291.
- Preserve the earlier failed full-route run in `routing-run.log` and `routing-native.log`: classification failed after transport timeouts. Moving the live route before deliberately canceled control requests passed. `LocalDialogueClient.Cancel()` currently invalidates dispatch but does not abort HTTP work; fixing that underlying transport issue is the next step, not claiming that changing wording fixes it.
- The old availability probe could not start on the explicit fixture because its harness still selected a legacy save name (`outage-run.log`). It is being repaired separately; outage recovery is not yet newly verified.

## Current evidence

- Core suite: **1291/1291 passed**, recorded in `work/action-mood-20261021/core-final.log`.
- Runtime compilation: **0 warnings, 0 errors** in `work/action-mood-20261021/runtime-final.log`. The associated integration run reports success in `work/action-mood-20261021/build-final.log`.
- First native action-mood pass: `work/action-mood-20261021/run.log` and `native.log` report receipt-grounded action mood commit, typed output, one final delta commit, invalid and typed-sentinel no-mutation, duplicate-rephrase single commit, changed-receipt stale-delta rejection, stale-completion rejection, disk rollback, and baseline restoration.
- The native log also has a tokenizer-timeout warning after the pass marker. The stricter probe and full-routing run are still needed before final acceptance.

## Behavior and authority

Mood is enabled only for a stable-person snapshot whose persistent mood is captured and whose active memory owner matches that person. The model receives an immutable `ConversationActionReply` receipt as authoritative outcome. Its generated reply can add wording and a bounded mood delta; it cannot accept, reject, alter, or execute the action.

Action and social mood updates share the same per-person accepted-turn sequence and atomic mood-plus-memory transaction. A stale sequence cannot commit. The successful mood transaction records the exchange once; runtime then adds the visible dialogue pair without a second memory write.

A duplicate first rendering triggers one rephrase request. The first response’s mood delta is discarded; only a final distinct reply with an unchanged receipt can commit. Invalid typed output, `Unavailable` and `ContextLimit` sentinels, stale completion, or a changed receipt do not apply the generated delta. A changed receipt uses its current canonical wording.

## Remaining acceptance

Broader emotional model quality, native-only reply mood, and other IMM-09 requirements remain open. Unavailable messages remain necessary for an actual service outage or rejected response; this change fixes the identified abandoned-request and stranded-dispatch causes rather than hiding every possible error.
