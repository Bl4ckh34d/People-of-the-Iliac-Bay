# Shared goal arbitration and goal/task reconciliation — design, 23 October 2026

Status: implementation design for CORE-01 and CORE-02. No runtime change or gameplay acceptance follows from this document. Current repository and the [core audit](2026-10-23-core-systems-audit.md) are the source baseline; [ROADMAP](ROADMAP.md) retains CORE-03 through CORE-05 as separate work. The installed save schema is 45.

The first code pilot is narrower than the extension contract below: it selects native alarm ownership or a recorded Recovery goal with a freshly discovered health task. It reads active paid-service and appointment constraints. It does not rank multiple independently executable goal families. Adding such a family requires a native producer, typed feasible-task adapter and effect reconciliation through this same selector and overlay; it must not add another movement overlay.

## Decision and scope

Use a narrow, pure Core selector plus one native admission/reconciliation coordinator in `StreetPopulation`. Pilot only the existing `goal:native-recovery` and `goal:native-safety` producers. A selected goal must change the existing task/action overlay's decision; a new context type or model projection alone would not satisfy CORE-01. The selector does not move actors, grant resources, execute actions, complete native episodes, or replace the care policy. It returns an explicit disposition for the current task and one movement owner. The runtime adapter creates feasible health tasks and applies that disposition through existing `WorldStateStore` methods.

Alternative A, more `PrepareNpcHealthCare` special cases, is small initially but leaves safety, duty and future needs with independent conflict rules. Alternative B, a generic utility/planning engine for every role and need, duplicates native action authority and adds policies with no real adapter. The recommended selector has two pilot candidates and a bounded result, but stable contracts that can admit another producer after it supplies native observation, feasibility and reconciliation evidence.

## Existing boundaries that remain authoritative

`NpcHealthNeedLifecycle.Observe` derives health need and recovery goal from saved native vitals. Current priority is Important above 50% HP, Urgent at or below 50%, Emergency at or below 25%; an ordinary care episode begins at or below 75% HP. Keep those thresholds in the pilot. The goal priority ranks eligible work; it does not by itself make an injured NPC eligible for a treatment task above 75% HP. `NpcNativeSafetyGoalLifecycle` records a directly witnessed active hostile episode and closes only after a sealed refuge arrival or abandonment supported by native episode state. `CivilianResponses` and the alarm/refuge path choose and execute flight. They keep ownership of the safety movement; the selector never fabricates a refuge task.

`NpcTaskStore` owns one active task per resident, action/step receipts, completed effect IDs and terminal states. `NpcHealthCare` owns seeking, acting, blocked/retry and recovered care phases. The native inventory, seller, quote, route, bed and vitals adapters determine whether a particular plan works. `TryAcceptNpcTask` and `TryDispatchNpcTaskStep` remain final action gates. Quest-protected and quest-owned actors, dead or absent residents, native alarms, service/guard duties, and existing scene ownership must be checked at the actual overlay and dispatch sites, not inferred from a stale conversation snapshot. No LLM text, priority row or candidate score can authorize stock, money, access, healing or safety.

## Core contract

Add a focused `NpcGoalArbitration.cs` in `src/PeopleOfTheIliacBay.Core`. These are proposed names/signatures, with immutable validated inputs and enum values saved only where specified:

```csharp
public enum NpcGoalWorkDisposition { NativeOwner, KeepTask, InterruptTask, BeginGoalTask, DeferGoal, OrdinaryRoutine }
public enum NpcGoalMovementOwner { NativeAlarm, ExistingTask, HealthCare, OrdinaryRoutine, None }
public enum NpcGoalDeferralReason { None, NativeProtection, NativeAlarm, EssentialDuty, ActiveService,
    AlreadyOccupied, CareThreshold, RetryWindow, NoFreshFeasibleTask, StaleObservation }

public sealed class NpcGoalCandidate {
    public string GoalId;                 // exact persisted goal ID
    public long GoalStartRevision;        // stable activation generation, not latest row revision
    public NpcMotivationGoalKind Kind;
    public NpcMotivationPriorityLevel Priority;
    public string SourceEvidenceId;
    public bool NativeEpisodeActive;      // safety only, bound to its exact episode
    public bool ActionEligible;           // health: persisted vitals <= 75%; safety: active alarm
    public bool HasFeasibleTask;          // health only; from fresh native candidate adapter
    public string FeasibilityKey;         // exact target/stock/route/quote digest, not action authority
}
public sealed class NpcGoalArbitrationInput {
    public string SaveBranchId, PersonId;
    public double GameTime;
    public long MotivationRevision;
    public bool IsAlive, IsAdult, IsQuestProtected, IsQuestOwned, IsAway, NativeAlarmActive;
    public string ActiveAlarmEpisodeId;
    public NpcTaskPlan ActiveTask;        // detached read, never mutated by selector
    public NpcHealthCareRecord Care;      // detached read
    public IReadOnlyList<NpcMotivationDuty> Duties;
    public IReadOnlyList<NpcGoalCandidate> Candidates;
}
public sealed class NpcGoalArbitrationResult {
    public string GoalId, FeasibilityKey;
    public long GoalStartRevision, MotivationRevision;
    public NpcGoalWorkDisposition Disposition;
    public NpcGoalMovementOwner MovementOwner;
    public NpcGoalDeferralReason Reason;
}
public static class NpcGoalArbiter {
    public static NpcGoalArbitrationResult Select(NpcGoalArbitrationInput input);
}
```

Input builder accepts only a current `ReadNpcMotivationState` row with status Active, knowledge Known and native observation evidence for the two pilot IDs. It reads native alarm episode and care/vitals state independently. `NpcMotivationContextSnapshot` remains a useful model projection but is not used as action authority: its transient constraints can be absent or stale. The runtime adapter supplies live duty facts from actual service/guard/quest ledgers and current actor state; an unknown duty or unknown feasibility is not silently treated as available. Require matching branch, person, game time, motivation ledger revision and goal start revision at admission. The goal activation generation stays constant while native health/vitals observations update that row. If a required source cannot be captured, return `DeferGoal/StaleObservation` and leave existing native ownership intact.

Selection is deterministic and side effect free. First reject dead, protected, quest-owned, absent or unsupported people from autonomous task admission. Native alarm wins over every task and duty: return `NativeOwner/NativeAlarm` while its exact episode is active, even if the safety goal write failed. Then retain an already running goal task unless its native source ended, an emergency preempts it, or dispatch became invalid. For remaining candidates, order by goal priority descending, explicit native episode class (Safety before Recovery only at equal priority), then ordinal `GoalId`, then ordinal source evidence ID. Never depend on list enumeration, hash iteration, current wall time or random seed. A goal without a fresh feasible action may stay recorded Active but cannot claim task or movement. `BeginGoalTask` requires health action eligibility and a freshly feasible health task. When no goal wins, keep an unrelated accepted task if its current duty/contract permits it, otherwise return `OrdinaryRoutine`.

Duty order is a policy gate before comparing ordinary priority: authored quest/native ownership and alarm always take precedence. An accepted or active essential guard, combat, appointment or service duty blocks an Important recovery interruption; the duty ledger, not personality prose, identifies that duty and its time/scope. Urgent recovery can interrupt routine work and ordinary appointments after the native service/quest contract checks; it cannot silently breach an active paid or guard service. Emergency recovery can request suspension of such service, but only through its existing explicit pause/exit rules. If those rules reject suspension, return `DeferGoal/ActiveService` and keep the service owner; never quietly cancel its task. An active alarm suspends ordinary care, with the native response deciding guard fight versus civilian flight. Safety goals are not interpreted as permission for a guard to flee: guard combat/retreat policy remains native. After the alarm ends, a fresh arbitration may resume care. A blocked care episode keeps its existing 300-game-second retry gate unless a *new* observed vitals revision establishes a higher emergency; do not busy-loop on the same failing target.

## Actual runtime integration and one mover

Replace the policy portion of `PrepareNpcHealthCare` with `PrepareNpcGoalWork`, called at the existing `StreetPopulation.Actions.cs` line 418 before `AdvanceConversationTask` and before ordinary schedule visibility/placement in both outdoor and indoor loops. Preserve the outer `ApplyNpcActionOverlay` gate at lines 422–426 for quest, alarm, death and away changes. At every call: capture live vitals; run existing health and safety producers; reconcile terminal active actions/tasks; build current candidates/duties/feasible health tasks; call `Select`; apply exactly one disposition; then let the existing action executor or native alarm/schedule branch move the actor. `HealthCare` remains the movement owner only through its task action or its existing stationary `HoldNpcHealthCare` and recovery handoff. Clear `actionMotions` and release seats only after a successful cancellation or existing terminal transition. A failed cancellation means retain old owner for this tick and retry after reconciliation. No second `Move`/route loop is added.

The health adapter continues using `TryCreateHealthRestTask`, `TryCreateOwnedHealthTask`, `TryCreateGuildHealthTask`, `TryCreateShopHealthTask` and `NpcHealthCareChoice.Select`. These build *candidate plans*, not reservations. Re-read inventory/UID, seller supply, price/wallet, route, bed and vitals after selection and immediately before `TryAcceptNpcTask`; repeat the check before each step via current dispatch gates. If candidate key or source revisions changed, rebuild once on next tick rather than accepting a stale target. Unknown route or stock means infeasible. A successful purchase confirms acquired item identity but does not imply healing. Preserve existing `TryConfirmHealthRestReservation` and `DispatchConversationTaskStep` checks.

The adapter must not cancel an incompatible task solely because `care.Phase != Recovered`. Cancel only on `InterruptTask`, after reconciling a terminal action and validating whether that task is quest/service owned. It must not begin a care episode solely from `ShouldInterruptWork`: use the active recovery goal selected by the arbiter plus the existing threshold. `KeepTask` carries its active task and movement; `DeferGoal` leaves duty/task owner untouched; `NativeOwner` yields to alarm/refuge/guard code. `GetState` and `HoldNpcHealthCare` must consult the selected ownership or a genuinely acting care task so a deferred stale care row cannot override schedule position. On unload/reload, run selection before restoring care movement; if native alarm wins, restore alarm position. The design does not alter who can converse, who is present, or native quest flags.

## Goal/task link and reconciliation

Add persistent `GoalStartRevision` to `NpcMotivationGoalState`. Assign the motivation ledger revision of first activation; preserve it across Active/Suspended updates and the terminal update. When a later injury reactivates a Fulfilled/Abandoned goal ID, assign a new start revision. This is the goal episode key. `NpcHealthNeedLifecycle.Observe` updates recovery evidence and current row revision after every changed vitals observation, so current `StateRevision` or `EvidenceId` cannot be the task's durable link. For schema-45 active goals, migrate start revision conservatively to their saved current `StateRevision`; old historical episodes are not inferred.

Add optional `GoalId` and `GoalStartRevision` to `NpcTaskPlan`. New autonomous health tasks require both; legacy and dialogue/ordinary tasks keep both absent. Their saved resident is the owner. `NpcTaskContract.Validate/Copy`, save-copy validation and task acceptance check the pair: Recovery kind, matching Active/Known goal ID and activation generation, and current care episode ID through `NpcHealthCare.MakeTaskId`. Reject a cross-owner or mismatched generation. Once accepted, the task keeps that generation even when partial healing changes the current goal row. Historical terminal tasks validate against the retained accepted-update receipt at their start revision, not a possibly later active goal row with the same ID. Task ID still comes from `NpcHealthCare.MakeTaskId`; association does not replace existing care `TaskId`.

Add one store operation for the health admission edge: `WorldStateStore.TryAcceptGoalTask(NpcGoalAdmissionTicket ticket, NpcTaskPlan plan, out string reason)`. Its ticket is an opaque capability, captured by `WorldStateStore.TryCaptureGoalAdmission(string personId, long goalStartRevision, string careEpisodeId, long careRevision, string feasibilityKey, out NpcGoalAdmissionTicket ticket)`. Capture binds the store epoch/scope/branch, resident, motivation revision, goal generation, care revision, task/action occupancy and adapter's fresh feasibility key. Acceptance rechecks these and publishes task acceptance plus `NpcHealthCare` task binding as one validated save-state assignment. This removes the current accept-then-bind orphan window; do not call the existing two methods independently for new linked tasks. Failure leaves task and care unchanged. Limit this operation to Recovery; avoid a broad transaction framework.

Add `ReconcileGoalTask(personId, taskId, expectedTaskRevision, now, nativeSnapshot)` as a single coordinator entry point, called after terminal action reconciliation, interrupted dispatch, native vitals change, alarm end and on reload. It validates goal/task/care link and exact branch/epoch, then invokes existing task effect and care policy; repeated calls with the same task/action/effect/vitals revisions are no-ops. A completed purchase advances task but does not complete goal. A completed treatment step requires the existing `TreatmentReceipt`, matching `CompletedEffectId` and freshly observed saved vitals. Partial healing keeps Recovery Active, updates Health intensity, and uses `TryContinueNpcHealthCareAfterTreatment` to seek another feasible plan. Full health completes care through `NpcHealthCareRecoveryHandoff.TryComplete` and then `NpcHealthNeedLifecycle.Observe` marks need 0 and goal Fulfilled. Failure/cancellation blocks care with its existing retry policy; interruption by native alarm leaves the health goal Active, records interruption, and returns after alarm resolution. A failed/rejected action never fulfills a goal. Safety reconciliation calls `NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge/TryReconcile` from the existing sealed-arrival/episode sites; no fabricated task effect ID is created.

Goal state and task state are separate ledgers. Reconciliation must treat native state as source of truth when a save has a terminal task but unfinished care/goal publication. Idempotent retry finishes the projection without consuming another item or wage. Never infer a health gain from an accepted task, a completed purchase, a plausible animation, a route estimate or a verbal claim. Do not erase completed effects on cancellation. For the pilot, operation receipts use exact task ID, action effect ID, vitals revision and care episode. A later health injury creates a new native vitals revision and reopens Recovery through the existing producer.

## Save version and adjacent work

Increase `WorldState.CurrentVersion` from 45 to 46 only with the implementation. Version-45 task rows get null goal-link fields; preserve every task/action ID, phase, revision, effect, failure receipt, care row, motivation row and alarm episode. On load, validate linked schema-46 rows strictly. A legacy active care task remains unbound and follows its current reconciliation path until terminal; do not infer a historical association or grant it new arbitration authority. A missing/invalid goal is never fabricated during migration. The version-45 active goal receives only the conservative current-row activation generation described above. Test copying and loading versions 29, 38, 45 and 46 where each relevant ledger first appears, plus branch fork/reload and malformed cross-owner links. Keep the saved schema 45 until code and migration tests land.

CORE-05 must specify an atomic need/goal group and its revision/receipt semantics before implementation if fault injection shows the two-row health publication can split; the current single-row assignment is atomic only per row. This design does not claim that a partial commit has been observed. CORE-04 separately replaces 4096 motivation updates per person, 1000 tasks and 10000 actions globally. Do not trim receipt arrays under schema 46: existing validators require contiguous absolute revisions and replay checks. Pilot tests must exercise near-cap behavior honestly and mark a capacity failure as deferred, never a silently fulfilled need. Later retention design must preserve open tasks, care, obligations, branch identity and replay suppression across multi-year progression. CORE-03 resource reservations remain separate; fresh feasibility plus existing action validation is the pilot's honest guarantee, not a shared reservation system.

## Required verification before delivery claim

Pure Core cases: equal-priority deterministic tie under shuffled candidates; safety versus emergency health; alarm active when safety goal write is absent; guard fight/retreat and quest/essential service precedence; Important versus Urgent versus Emergency recovery; no feasible/unknown route or resource; stale goal/vitals/care revision; blocked retry; occupied linked and unrelated tasks. Test `Select` twice before/after save-copy and confirm identical disposition and movement owner.

Store cases: linked admission and care binding publish together; injected failure leaves both unchanged; stale epoch/branch/revision rejected; wrong owner/goal/episode/evidence rejected; legacy task remains readable; terminal purchase followed by treatment; completed action reconciled before cancellation; duplicate effect/reconcile/reload cannot reconsume or fulfill; partial treatment replans; full recovery returns to routine; failure and alarm interruption preserve retry and completed receipts. Test old-save migration and branch isolation with malformed link rejection.

Runtime cases: one actor with health below 75% interrupts eligible ordinary work and physically executes a feasible task; an active alarm owns movement and safety completion still needs sealed refuge; guard/service/quest ownership holds; changing stock, UID, quote, bed, route or wallet between selection and dispatch defers safely; action/seat/motion cleanup after a valid preemption; save/reload in Seeking, Acting, Blocked, alarm and terminal-before-reconcile states. Assert no duplicate mover, item consumption, currency transfer or HUD completion. Run focused Core and adapter suites, clean `RuntimeCompile`, Unity package/install checks and owned native gameplay probes before claiming installed acceptance. The design document alone passes none of those gates.
