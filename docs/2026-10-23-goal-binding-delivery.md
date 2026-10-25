# Goal episode binding and recovery arbitration

Schema 46 records a goal's activation generation independently of subsequent evidence revisions. Tasks retain their originating goal and generation. Admission requires that generation to remain active and known; save validation preserves historical provenance after recovery, cancellation, or a later injury. Schema 45 migration leaves existing tasks unbound rather than inventing their origin.

The runtime recovery coordinator now uses the shared arbitration contract before interrupting work. Native alarm ownership remains authoritative. Recorded recovery priority distinguishes ordinary appointments from urgent treatment. Actual paid service contracts remain protected; ordinary merchant work is not classified as a paid service contract. Unavailable treatment cannot take over routine movement. This is a recovery pilot, not a complete arbitrary-goal planner.

## Verified CPU evidence

- Full Core: 1692/1692 passed, with TEMP/TMP directed to the writable test workspace.
- Goal binding: 9/9 passed, including stale admission, repeated injury episodes, legacy migration, fabricated origin receipts, and rewinding the current goal to an older lifetime.
- Arbitration: 14/14 passed, including emergency interruption of ordinary work, protected service duty, unavailable treatment, native danger, owner isolation, stale generation handling, and continued treatment above the initial interruption threshold.
- RuntimeCompile: zero warnings and errors.
- Unity build and installation passed after both implementation agents froze their changes.
- Final dist and installed package SHA256: `7F9CF14A72E4A98823AA9A64BD14804A2C5510708A138568D0B0CA491A124C64` (includes paired need/goal publication and latest-row validation as well as combined care admission and native level initialization).

The first schema 46 run failed its full-recovery assertion after 240 seconds; the exact original fixture was reloaded and all 13 fixture file hashes remained unchanged. Evidence is retained in `work/core-goals-20261023/native-first-failure.log`. Review identified that the new selector reapplied the initial 75% work-interruption threshold to continuing recovery. The corrected shared eligibility helper distinguishes starting an interruption from finishing an established treatment episode; two focused regressions exercise that helper through selection.

## Native acceptance

The corrected package passes `TownTest.ps1 -FixtureCharacter PeopleOfTheIliacBayTest-9a5f197b -FixtureSaveName 'People - Gothway Garden' -NpcHealthCareProbe`. Evidence: `work/core-goals-20261023/native-pass.log`.

The resident healed from 15 to 76 HP through 13 actual owned potion units. Every treatment task retained recovery generation 2. The observed health need fell from 81 to 0; the same goal was fulfilled only after full native recovery. Care ownership released, the actor physically returned to routine, and no extra potion was consumed. Native effect rollback and stale replay checks passed. The original fixture was loaded again; all 13 saved file hashes remained unchanged. The owned game and Unity processes exited.

This proves the owned-potion recovery path. It does not prove every inn, guild, shop, duty-conflict or arbitrary-goal scenario.

## Remaining work

The subsequent Core change replaces accept-then-bind rollback with `TryAcceptAndBindNpcHealthCareTask`: care prevalidation finishes before task admission, then the prepared care row is published without callbacks or further validation. Legacy binding uses the same validation. Seven focused cases pass, including rejected proposals leaving both ledgers unchanged, occupancy, retry boundary, duplicate admission and goal-bound restore. Core 1692, binding 9, arbitration 14 and RuntimeCompile pass again. Package `2C73CFF96D8BE30CC87D4402E2F9FD2FD3177B2DBAF246DD15761786796CBF0D` passes native acceptance again with all original assertions, including 13 treatments under generation 2. Evidence: `work/core-goals-20261023/native-atomic-pass.log`; fixture hashes remain unchanged.

General multi-goal planning, bounded operational history, shared resource reservations and broader native service scenarios remain open. Paired health motivation publication is now implemented as described below; broader grouped lifecycle use still needs integration. Active paid-service interruption policy requires further integration with survival and contract lifecycle. No completion of CORE-01 through CORE-05 is claimed.

The active paid-service gate currently refers to companion contracts (`NpcServiceCoverage`), not noble guard allegiance. Emergency healthcare must eventually suspend or otherwise reconcile that contract according to its rest/billing terms, rather than treating ordinary paid service as a permanent survival veto. Existing `TrySuspend`, `TryResume`, ledger `TryReplace` and `TryCommitNpcServiceContracts` provide the transition primitives; native duty/guard combat exceptions remain separate. This path is not covered by the owned-potion fixture.

## Potion magnitude follow-up

The user questioned the 13-dose result. Native source confirms ordinary healing is `5 + 9 * casterLevel` and true healing is `5 + 19 * casterLevel`; the observed 5 HP per dose came from a level-zero civilian body. `CivilianEntity.SetEntityDefaults()` only sets health, while the mod's vitals binding previously did not initialize level. The adapter now initializes an invalid level below one to one and preserves positive native/custom levels. This is not a level progression system or a forced two-to-three-dose balance rule.

Final native acceptance passes in `work/core-goals-20261023/native-level-pass.log`: native level 1, minimum and actual normal-potion magnitude 14, five consumed units to heal 15 to 76 HP, generation 2 preserved, observed goal fulfillment, physical routine return, and zero extra consumption. The original fixture reload and all 13 file hashes pass again. RuntimeCompile and Unity build/install pass; owned processes exited. Historical 13-dose evidence above is retained to explain the discovered initialization defect, not as the final balance result.

## Paired motivation publication and latest-row validation

Health observation now commits changed need and goal through `TryCommitNpcMotivationUpdatePair`. Both proposals are applied to detached state with consecutive revisions; publication occurs only after both and the complete replacement collection validate. Invalid second proposals, stale/replayed/restored tickets, wrong owners and a single remaining capacity slot cannot publish half of an injury. Five focused cases pass, plus health-need nine and safety seven. Existing single-update callers use the same bounded publication path.

A separate reproduced save defect allowed substituting the complete old goal row while retaining newer receipts. The original start-only rewind test missed this. The new regression failed before the fix; current rows now must reference the latest receipt for their row/kind. Goal-binding regression is now 10/10. Current full Core remains 1692/1692; RuntimeCompile has zero warnings/errors.

The final installed package passes native acceptance again in `work/core-goals-20261023/native-pair-pass.log`: five actual treatments, stable generation 2, full recovery, physical return, no extra consumption, exact fixture reload and all 13 file hashes unchanged. Owned test/build processes exited. The proposed role-specific level ranges are not implemented; see `2026-10-23-npc-level-balancing-proposal.md`.
