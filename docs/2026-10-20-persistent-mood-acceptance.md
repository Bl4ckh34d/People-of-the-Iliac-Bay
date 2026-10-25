# Persistent conversation mood acceptance — 20 October 2026

The installed development package passed a bounded native check for persistent conversation mood. Installed bundle SHA-256 is `E5402CF0DFBCB46F4148112EB0BAFB451D9A21EB8219ED60F7B60C7707D34FE6`; deployment contains 654 runtime assets.

## Verified results

- Core suite: 1289/1289 passed. Adapter suite: 77/77 passed.
- RuntimeCompile completed with 0 warnings and 0 errors. Unity build and integration checks passed, and the bundle installed.
- The native probe sent one real production `SubmitSocialText` request and received one fresh typed completion. That model result had `moodDelta=0`; mood stayed `0`.
- Controlled accepted callbacks applied `+3`, `-2`, and `0`. Invalid, replayed, stale-session, and verifier-replaced results did not mutate mood.
- Reopening the same person carried current mood into the next request. Disk reload restored a checkpoint with mood value `1` and accepted sequence `5`; rollback restored the fixture baseline and deleted the checkpoint.

## Scope limits

The native probe did not assert full `SubmitFreeText` routing. It did not establish a model-generated nonzero mood delta, broad emotional tone quality, or mood-aware action wording. IMM-09 remains partial. The next bounded extension is receipt-grounded action wording with the existing action receipt remaining authoritative; see the [action mood extension plan](../work/persistent-mood-20261020/action-mood-next.md).

## Evidence

- [`green-run.log`](../work/persistent-mood-20261020/green-run.log): native acceptance markers and cleanup.
- [`green-native.log`](../work/persistent-mood-20261020/green-native.log): Unity native run output.
- [`build-green.log`](../work/persistent-mood-20261020/build-green.log): 654-asset deployment and Unity build/install pass.
- [`runtime-parent.log`](../work/persistent-mood-20261020/runtime-parent.log): RuntimeCompile result.
- [`core-parent.log`](../work/persistent-mood-20261020/core-parent.log) and [`adapter-parent.log`](../work/persistent-mood-20261020/adapter-parent.log): suite results.

An earlier saved RED run found a production integration defect: the runtime cloned request set `AvoidDuplicateReply` to an empty string, which is invalid for social requests. The corrected runtime passes `null`. See [`social-clone-red-run.log`](../work/persistent-mood-20261020/social-clone-red-run.log) and [`social-clone-red-native.log`](../work/persistent-mood-20261020/social-clone-red-native.log).

A separate full-routing attempt produced zero prose reply requests after social classification. A valid spontaneous-offer branch can bypass prose generation; investigation has not established whether that was the cause. This is an unresolved coverage finding, not a confirmed routing defect or a successful end-to-end test. Evidence: [`routed-no-reply-native.log`](../work/persistent-mood-20261020/routed-no-reply-native.log).
