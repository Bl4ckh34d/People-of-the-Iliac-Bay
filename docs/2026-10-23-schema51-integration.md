# Schema 51 integration checkpoint

Status: schema51 package built, installed and accepted by the native healthcare scenario. See the final Native healthcare acceptance section for exact package hash and logs. Earlier entries below preserve the diagnostic history and are superseded by that acceptance. Shared core-system completion is not claimed.

## Functional scope

Observed player speech now receives a source-owned per-person ordinal and a ticket bound to its conversation session, exact submitted text and request. This includes registered static NPCs. Closing the session, beginning another request or restoring a save invalidates stale live tickets. Accepting the same request's action command does not invalidate its speech. Accepted pending speech persists independently of its live UI. Historical speech fixtures retain their original provenance without deleting source cursors.

The action overlay also respects the already observed native alarm reaction before care planning or Ready-step dispatch. Existing effect reconciliation and failure delivery continue; native danger response retains movement ownership. The active-service-contract care veto is a separate unresolved policy/integration issue.

## Evidence so far

- First focused source suite: 8/8 passed (`work/life-core-20261023/temp/speech-source-schema51-first.log`), covering replay after detail removal/restore, exact source binding, stale requests, same-request command acceptance, ordinal gaps, pending retry and migration.
- Final focused source suite: 12/12 passed (`work/life-core-20261023/temp/speech-source-schema51-final.log`), including the ordinary Mood-bit gate and legacy global versus new per-person family-threat receipt numbering.
- Main suite passed 1695/1695 before those final gates. The subsequent parent run (`schema51-core-final.log`) reported 1691/1695: four positive masked-speech fixtures still used ordinary post-append admission with Mood already applied. They must use the atomic mood path while retaining their original mask/provenance assertions. Final main acceptance remains open until corrected and rerun.
- Those four fixtures now use the real atomic social/mood commit, preserving their mask, provenance, appraisal and replay checks. The fresh final main run passed 1695/1695 (`work/life-core-20261023/temp/schema51-core-mood-testfix.log`). The intermediate compile failure and failed pre-fix run remain in their logs as diagnostic evidence; they are superseded by this successful run.
- MemorySalience migration passed 109 assertions. Evaluation selftests passed, with 41/41 permitted arms exported and 24/24 Python contract checks; QW-P21-T remains explicitly held.
- Combined runtime compilation: zero warnings/errors (`work/life-core-20261023/temp/schema51-runtime-second.log`). The first compile exposed a missing UI namespace; corrected before the successful run.
- Reviewed runtime compilation also passed with zero warnings/errors (`schema51-runtime-reviewed.log`), including captured deferred speech sources and source-free presentation of already committed atomic replies.
- The native framework persistence probe now issues actual speech tickets for its controlled positive cases, checks persisted source cursors, and separately imports explicitly historical unknown-provenance evidence.
- The native duplicate-reply probe now supplies issued tickets for controlled requests; its negative mismatch changes only the receipt while retaining the original issued ticket.

## Earlier release work (completed for healthcare; retained as history)

Complete main-suite/evaluation migration and verification, retest the legacy family-threat receipt-domain correction, review deferred runtime callbacks and run native acceptance after all writers freeze. Extend the existing healthcare native scenario to cover danger interrupting care and subsequent recovery/routine return. Neither the speech UI path nor the alarm interaction has new native acceptance yet. No package build or installation is claimed for this checkpoint.

## Alarm integration review

The final alarm changes passed RuntimeCompile with zero warnings and errors. Independent read-only review found no build blocker. The native healthcare probe now checks a running treatment interrupted by a real LOS-triggered alarm, unchanged inventory and health during flight, and subsequent recovery after alarm expiry. TownTest requires the corresponding markers; its PowerShell parser check passed. Native execution remains pending. The package build log is work/life-core-20261023/schema51-package-build.log.


Package build succeeded outside the sandbox and installed SHA256 45708165B059DA00F9D2EAF12F71494D5F6CD3C4B391DB9BED2A710DAA6600E2 (dist and installed hashes match). Initial native healthcare run failed in its isolated schema40 fear-migration fixture: future observed speech source data remained in the downgraded copy. Alarm acceptance was not reached. Logs preserved as schema51-healthcare-first-town.log and schema51-healthcare-first-player.log. This is not native acceptance.


The corrected legacy fixture passed the native schema51 persistence check, including speech source cursors and stale-ticket rejection. In the second native run, the production LOS alarm interrupted treatment without potion consumption and native movement was observed. Recovery then remained Blocked after the resident returned to a Home schedule and unloaded. This is an actual runtime retry/materialization gap, not full healthcare acceptance. The bounded existing HoldNpcHealthCare path is the intended repair seam; treatment effects must still execute natively.


The recovery repair now preserves the actually observed flight position and uses the existing bounded materialization path for due Blocked care with no competing task. Failed materialization leaves routine ownership free. Independent source review passed; focused NpcHealthCare regression passed 16/16 and final RuntimeCompile passed with zero warnings/errors. Native acceptance is still pending the rebuilt package.


The rebuilt recovery fix advanced the native scenario beyond the prior timeout: a native Healing bundle executed, then the combined potion/receipt/inventory/progress assertion failed. The exact failing subcondition is not yet isolated. Preserve this distinction from the earlier zero-treatment timeout. Logs: schema51-healthcare-clock-failure-town.log and schema51-healthcare-clock-failure-player.log (historical filenames). All 13 baseline save hashes remained unchanged. The probe now observes the actual Blocked transition before advancing over its saved retry deadline; final native acceptance remains open.


Native diagnostic rerun isolated the failed predicate: source UID, transaction/effect IDs, HP 15 to 29, exactly one consumed unit, inventory revision 2 to 3, and healing progress 0 to 14 all match. Only the asserted damage-progress increase fails (0 to 0). This narrows the remaining issue to injury accounting or its fixture expectation; potion execution is now directly evidenced. Logs preserved as schema51-healthcare-receipt-failure-town.log and -player.log. Overall healthcare acceptance remains open.


The damage-counter failure is a fixture expectation defect: FlushNpcHealthExperiences projects the injury after its quiet window and clears per-episode counters. The subsequent healing episode correctly has damage=0. The probe must verify the original captured injury and its exact persisted projection, rather than require prior damage in the new healing episode. Pure service health-pause Core now passes 8 focused cases, including legacy contract migration and settlement-time/end-reason validation; runtime service pause/resume and native settlement are still unimplemented.


## Native healthcare acceptance

Final run exited 0. Actual hostile LOS interrupted treatment without potion consumption; native flight retained movement. Care resumed after alarm expiry, healed the resident from 15 to 76/76 through actual potion effects, released care ownership and returned to ordinary routine without further consumption. Exact fixture baseline reloaded and all 13 original save hashes remain unchanged. Native schema51 serialization, speech-source persistence and legacy migration also passed. This does not prove the player-dialogue UI or paid-service runtime integration.

Installed and dist SHA256: 25A3A118B8E8C954A655E8EE61CF789E46FEF804037DF3DB81B1CFAC0373489D. Backup: work/life-core-20261023/schema51-accepted.dfmod. Logs: schema51-healthcare-accepted-town.log and schema51-healthcare-accepted-player.log. Build: schema51-package-injury-evidence.log. Main suite1695/1695; health regression16/16; service pause regression8/8; runtime compile0 warnings/errors.

