# Atomic healthcare task admission

## Objective and boundary

Publish acceptance of a recovery task and its care-episode binding through one store operation. Keep schema 46, goal provenance, legacy admitted-task binding, native resources, and existing effect validation. This addresses the concrete acceptance/binding seam, not general database transactions or paired motivation publication.

## Implementation

- Prepare and validate a detached care transition against the proposed task before task admission.
- Share care-plan validation with the legacy binding API. Keep occupancy checks appropriate to each caller: legacy binding already has its own accepted Ready task; combined admission must not replace an occupied task.
- Task admission remains authoritative for consent, owner, active goal generation, task structure, duplicate proposals, and ledger capacity.
- After successful task admission, publish the prevalidated care row without callbacks or additional fallible validation.
- Switch runtime healthcare to the combined API, removing cancellation solely due to a failed second binding step.

## Evidence required

Test stale care revision, invalid episode/task identity, bad time, unsupported plan, stale goal generation, occupied task/action, duplicate admission, and valid legacy binding. Rejected admission must preserve both task and care ledgers. Successful admission must survive snapshot/restore with exact goal provenance. Run focused healthcare/arbitration/binding suites, Core and RuntimeCompile, then build and repeat native owned-potion recovery acceptance after agents freeze.

Exceptional process termination or out-of-memory between assignments is outside this single-threaded store-operation guarantee. No claim of a general transactional persistence engine is made.

## Verified delivery

Seven focused admission cases, Core 1692, binding 9, arbitration 14 and RuntimeCompile pass. Native owned-potion acceptance passes after runtime integration and again after correcting native civilian level initialization. See docs/2026-10-23-goal-binding-delivery.md for final package hash and evidence.
