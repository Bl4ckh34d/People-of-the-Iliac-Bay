# Atomic health need and recovery goal publication

## Scope

Publish the correlated native health need and recovery goal together when both change. Keep schema 46, existing single-update callers, sequential accepted receipts, activation generations, owner/epoch/revision tickets, and native evidence as authority. This is separate from deferred autobiographical compaction and the upcoming operational-retention migration.

## Approach

Add a narrow two-update store operation sharing the validated publication path with the single-update API. Apply the first and second proposals only to detached state, requiring consecutive expected revisions and the same owner. Validate the complete replacement collection before its single assignment. A failed second proposal, stale ticket, exhausted capacity or copy failure leaves the original collection unchanged.

Health observation derives both proposals from one native vitals snapshot. Preserve deterministic need-before-goal receipt order and exact generation rules. Single changed rows continue through the existing single-update API.

## Acceptance

- Valid pair has consecutive receipts and correct goal origin.
- Invalid second proposal leaves both rows and receipts unchanged.
- Replayed/stale/restored tickets and wrong owners fail without mutation.
- One remaining receipt slot cannot admit half of a required pair.
- Actual recovery fulfills the existing episode; later injury creates a new generation.
- Current rows must reference the latest accepted receipt for their row/kind; substituting a complete historical row must fail restore.
- Focused tests, full Core, RuntimeCompile and native owned-potion recovery pass before installed acceptance is claimed.

The latest-row invariant has a reproduced regression: replacing a current goal with its complete older row was accepted by the previous validator. The new test failed before the fix and passes after enforcing latest-receipt ownership (goal binding 10/10).

## Verified delivery

Pair regressions 5/5, binding 10/10, full Core 1692/1692 and RuntimeCompile pass. Final package passes native five-dose recovery, generation retention, physical return and unchanged fixture hashes; see docs/2026-10-23-goal-binding-delivery.md.
