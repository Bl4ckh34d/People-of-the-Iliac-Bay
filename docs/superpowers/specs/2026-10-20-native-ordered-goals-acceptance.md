# Native ordered goals and cancellation acceptance

## Installed result

Installed and dist SHA-256 match: `36843F0E78CE05DE9AC5259879746561B1E30515B38648B5AF78B74DBA6FCDB8`, 642 runtime assets. Normal hybrid gameplay retains the typed goal path enabled by the preceding milestone.

The new structural candidate filter repairs a native integration problem: after a MoveTo, Qwen could select the ordinary Wait at the old location even though the task contract requires the subsequent Wait at the exact MoveTo destination. Native binding rejected that choice correctly, but the invalid option should not have been offered.

`NpcGoalBinding.FilterCandidates` now checks each offered ID through the existing typed binder against the full prepared catalog and previously bound steps. This preserves duplicate-ID rejection, quantity/duration limits and step dependencies. It returns defensive copies with original option metadata. Runtime selection still uses Qwen, including when only one option remains; D3 willingness and native revalidation still precede commitment. There is no regex, keyword rule or interpretation of player text in this filter.

The shared movement fixture also now uses typed native candidates and task validation, replacing legacy phrase-based fixture construction.

## Verification

- **1234/1234 Core tests**, including three candidate-mask cases for old-location Wait rejection, typed quantity bounds, defensive copies, duplicate IDs across kinds and invalid prior inputs. The missing API first fails compilation, then the implementation passes.
- **77/77 decision adapter tests**, clean RuntimeCompile and Unity build/install.
- New runner modes reject missing owned fixtures and conflicting probe modes before launching a game.
- Both owned native runners exit zero on the final package: `-GoalSequenceProbe` and `-GoalCancellationProbe`, using `PeopleOfTheIliacBayTest-9a5f197b` / `People - Gothway Garden`.

### Ordered physical task

The probe injects authentic typed extraction for MoveTo Doctor Savery's Gear, then Wait there for eighteen seconds, and a controlled D3 ACCEPT. Qwen target binding, current native candidates, complete task commitment, movement adapters, timing and receipts remain production code.

Before Talk closes, exactly one willingness dispatch precedes a persisted two-step task and its truthful acceptance receipt. No movement or second action occurs while Talk remains open. After close, the resident physically walks **43.10 m**, with maximum sampled frame movement **0.131 m**, reaches the destination within **0.190 m** of the **0.250 m** arrival range, then waits **18.234 game seconds** against exactly **18.000 saved game seconds** at clock scale 2. Wait drift is **0.000 m**. Both step effect receipts and terminal task progress reconcile normally; routine-return ownership then releases. No actor teleport, custom route or collision bypass is added.

This proves ordered execution mechanics with injected extraction and willingness, not universal live-model planning or agreement.

### Pending-goal cancellation

Real Qwen target binding reaches a controlled willingness transport. While blocked, no goal action/task or reply is saved. Closing actual native Talk cancels the request token. The transport then deliberately returns a valid late ACCEPT, and main-thread delivery is pumped. No stale goal action, task or reply appears. The NPC moves afterward as part of normal routine recovery; that legitimate motion is not treated as evidence of a stale action.

## Preserved failures and fixes

- Initial ordered runs reject an unbindable target. Added precise diagnostics preserve selected ID and native reason. A retained run selects Wait ID 2 at the old location; the structural eligibility filter addresses this generally without changing the request wording or adding language rules.
- The first cancellation run finds a reflection helper incorrectly using static flags for the fixture's instance Actor field. That probe helper is corrected.
- Two ordered probe checks initially inspect the parent task before the next ordinary reconciliation tick records the completed child Wait receipt. The corrected probe captures arrival immediately, then yields up to ten seconds for normal reconciliation and strictly requires terminal task progress and exact effect receipts. It never forces state transitions. Physical movement and Wait evidence from those failed runs remains preserved.

Independent review covers the candidate filter, full-catalog duplicate checks, continued model/consent gates and bounded reconciliation/routine-release checks.

## Evidence and remaining scope

`work/ordered-goals-20261020/`: `core-final.log`, `adapter-final.log`, `runtime-build-final.log`, `unity-build-final.log`, `candidate-mask-red.log`, `candidate-mask-green.log`, `sequence-final.log`, `sequence-final-run-3.log`, `cancellation-final.log`, `cancellation-final-run.log`, and preserved `sequence-failed-1` through `sequence-failed-4` / `cancellation-failed-1` logs.

Complex model omissions remain possible. This does not verify purchases/handover through the new extractor, actual disk reload during a task, every scene interruption, broad action adapters or natural-language accepted acknowledgments. The full roadmap remains active. No game/editor remains after the owned runs; local model services remain available. No shutdown is requested.
