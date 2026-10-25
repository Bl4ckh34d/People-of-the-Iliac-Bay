using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcOperationalHistoryPressure { None, DeferredScan, AllPinned, NoRemovableRows, ValidationRejected }
    public enum NpcOperationalHistoryPressureTarget { None, Tasks, Actions }

    public sealed class NpcOperationalHistoryRetentionResult
    {
        public int Examined, RemovedTaskGroups, RemovedStandaloneActions, RemovedStepActions;
        public int Pinned, RecentWindow, TooYoung, ReferenceRowsInspected, FullValidationPasses;
        public NpcOperationalHistoryPressure Pressure;
    }

    public sealed partial class WorldStateStore
    {
        private const double OperationalHistoryAgeSeconds = 30d * 86400d;
        private const int RecentTerminalTasksPerPerson = 8;
        private const int RecentStandaloneActionsPerPerson = 32;
        private sealed class OperationalSweepCursor
        {
            internal WorldState Owner;
            internal long Signature;
            internal int TaskCount, ActionCount;
            internal int TaskIndex, ActionIndex, TaskExamined, ActionExamined, Pinned;
            internal bool ActionNext;
        }
        private OperationalSweepCursor ordinaryOperationalSweep = new OperationalSweepCursor();
        private OperationalSweepCursor taskPressureSweep = new OperationalSweepCursor();
        private OperationalSweepCursor actionPressureSweep = new OperationalSweepCursor();

        /// <summary>
        /// Examines a bounded number of append-order ledger rows. A published batch validates a full
        /// world copy, so ReferenceRowsInspected and FullValidationPasses expose its separate cost.
        /// Pressure may evict recent settled rows; live dependencies always pin their owners.
        /// </summary>
        public NpcOperationalHistoryRetentionResult MaintainNpcOperationalHistory(double gameSeconds,
            int maximumExamined, int maximumGroups, bool underCapacityPressure = false,
            NpcOperationalHistoryPressureTarget target = NpcOperationalHistoryPressureTarget.None)
        {
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds < 0 ||
                maximumExamined < 1 || maximumExamined > 64 || maximumGroups < 1 || maximumGroups > 32 ||
                (int)target < 0 || (int)target > 2 || !underCapacityPressure && target != NpcOperationalHistoryPressureTarget.None)
                throw new ArgumentOutOfRangeException("Invalid operational-history maintenance budget.");
            var result = new NpcOperationalHistoryRetentionResult();
            if (state.Version != WorldState.CurrentVersion)
            {
                result.Pressure = NpcOperationalHistoryPressure.ValidationRejected;
                return result;
            }

            var actionsById = new Dictionary<string, NpcActionJob>(StringComparer.Ordinal);
            var latestActionByResident = new Dictionary<string, string>(StringComparer.Ordinal);
            long signature = 17;
            foreach (var action in state.NpcActions)
            {
                actionsById.Add(action.Id, action);
                latestActionByResident[action.ResidentId] = action.Id;
                signature = HashSweep(signature, action.Id, action.Revision, (int)action.Phase,
                    action.ReturningToRoutine ? 1 : 0, action.TreatmentOutcomeRevision);
                signature = HashSweep(signature, action.InteractionProjectionReason,
                    (long)action.InteractionProjectionDisposition,
                    action.ProjectedItemTransferReceipt == null ? 0 : 1,
                    action.FollowTransition == null ? 0 : 1, action.RoutineRecoveryTransition == null ? 0 : 1);
                result.ReferenceRowsInspected++;
            }
            var stepActionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in state.NpcTasks)
            {
                for (int step = 0; step < task.Steps.Count; step++)
                    stepActionIds.Add(task.Id + ":step:" + step);
                signature = HashSweep(signature, task.Id, task.Revision, (int)task.Phase,
                    task.FailureReceipt == null ? 1 : task.FailureReceipt.Delivered ? 2 : 3,
                    task.Steps.Count);
                result.ReferenceRowsInspected++;
            }
            foreach (var care in state.NpcHealthCares)
            { signature = HashSweep(signature, care.TaskId, care.Revision, 0, 0, 0); result.ReferenceRowsInspected++; }
            foreach (var inn in state.Lodgings)
                foreach (var booking in inn.Bookings)
                { signature = HashSweep(signature, booking.HealthCareTaskId, BitConverter.DoubleToInt64Bits(booking.EndsAt), 0, 0, 0);
                    result.ReferenceRowsInspected++; }
            var cursor = target == NpcOperationalHistoryPressureTarget.Tasks ? taskPressureSweep :
                target == NpcOperationalHistoryPressureTarget.Actions ? actionPressureSweep : ordinaryOperationalSweep;
            bool replacement = !ReferenceEquals(cursor.Owner, state) ||
                state.NpcTasks.Count < cursor.TaskCount || state.NpcActions.Count < cursor.ActionCount;
            bool changed = cursor.Signature != signature || cursor.TaskCount != state.NpcTasks.Count ||
                cursor.ActionCount != state.NpcActions.Count;
            if (replacement || changed ||
                cursor.TaskExamined == state.NpcTasks.Count &&
                cursor.ActionExamined == (target == NpcOperationalHistoryPressureTarget.Tasks ? 0 : state.NpcActions.Count))
            {
                // Ordinary gameplay changes task/action revisions between ticks. Preserve its
                // position so a live first row cannot starve settled rows behind it.
                if (replacement || target != NpcOperationalHistoryPressureTarget.None)
                { cursor.TaskIndex = cursor.ActionIndex = 0; cursor.ActionNext = false; }
                cursor.Owner = state; cursor.Signature = signature;
                cursor.TaskCount = state.NpcTasks.Count; cursor.ActionCount = state.NpcActions.Count;
                cursor.TaskExamined = cursor.ActionExamined = cursor.Pinned = 0;
            }

            var removedTasks = new HashSet<string>(StringComparer.Ordinal);
            var removedActions = new HashSet<string>(StringComparer.Ordinal);
            while (result.Examined < maximumExamined &&
                result.RemovedTaskGroups + result.RemovedStandaloneActions < maximumGroups &&
                (cursor.TaskExamined < state.NpcTasks.Count ||
                 target != NpcOperationalHistoryPressureTarget.Tasks && cursor.ActionExamined < state.NpcActions.Count))
            {
                bool examineAction = target != NpcOperationalHistoryPressureTarget.Tasks &&
                    (cursor.TaskExamined >= state.NpcTasks.Count ||
                     cursor.ActionExamined < state.NpcActions.Count && cursor.ActionNext);
                cursor.ActionNext = !examineAction;
                if (examineAction)
                {
                    if (cursor.ActionIndex >= state.NpcActions.Count) cursor.ActionIndex = 0;
                    int index = cursor.ActionIndex++;
                    cursor.ActionExamined++;
                    result.Examined++;
                    var action = state.NpcActions[index];
                    if (stepActionIds.Contains(action.Id)) continue;
                    if (!NpcActionExecutor.IsTerminal(action))
                    { if (underCapacityPressure) { result.Pinned++; cursor.Pinned++; } continue; }
                    if (action.UpdatedAt > gameSeconds)
                    { result.Pinned++; cursor.Pinned++; continue; }
                    if (!underCapacityPressure &&
                        MoreRecentStandaloneCount(index, stepActionIds, result) < RecentStandaloneActionsPerPerson)
                    { result.RecentWindow++; continue; }
                    if (!underCapacityPressure && gameSeconds - action.UpdatedAt < OperationalHistoryAgeSeconds)
                    { result.TooYoung++; continue; }
                    if (ActionPinned(action, latestActionByResident, result))
                    { result.Pinned++; cursor.Pinned++; continue; }
                    removedActions.Add(action.Id);
                    result.RemovedStandaloneActions++;
                }
                else
                {
                    if (cursor.TaskIndex >= state.NpcTasks.Count) cursor.TaskIndex = 0;
                    int index = cursor.TaskIndex++;
                    cursor.TaskExamined++;
                    result.Examined++;
                    var task = state.NpcTasks[index];
                    if (!NpcTaskContract.IsTerminal(task))
                    { if (underCapacityPressure) { result.Pinned++; cursor.Pinned++; } continue; }
                    if (task.UpdatedAt > gameSeconds)
                    { result.Pinned++; cursor.Pinned++; continue; }
                    if (target == NpcOperationalHistoryPressureTarget.Actions &&
                        !TaskHasAction(task, actionsById, result)) continue;
                    if (!underCapacityPressure &&
                        MoreRecentTaskCount(index, result) < RecentTerminalTasksPerPerson)
                    { result.RecentWindow++; continue; }
                    if (!underCapacityPressure && gameSeconds - task.UpdatedAt < OperationalHistoryAgeSeconds)
                    { result.TooYoung++; continue; }
                    if (TaskPinned(task, actionsById, latestActionByResident, result))
                    { result.Pinned++; cursor.Pinned++; continue; }
                    removedTasks.Add(task.Id);
                    result.RemovedTaskGroups++;
                    for (int step = 0; step < task.Steps.Count; step++)
                    {
                        string actionId = task.Id + ":step:" + step;
                        if (actionsById.ContainsKey(actionId))
                        { removedActions.Add(actionId); result.RemovedStepActions++; }
                    }
                }
            }

            bool scanComplete = cursor.TaskExamined == state.NpcTasks.Count &&
                (target == NpcOperationalHistoryPressureTarget.Tasks || cursor.ActionExamined == state.NpcActions.Count);
            if (removedTasks.Count == 0 && removedActions.Count == 0)
            {
                if (underCapacityPressure)
                    result.Pressure = !scanComplete ? NpcOperationalHistoryPressure.DeferredScan :
                        cursor.Pinned > 0 ? NpcOperationalHistoryPressure.AllPinned : NpcOperationalHistoryPressure.NoRemovableRows;
                return result;
            }

            try
            {
                // Do not hide malformed payloads merely because they are being deleted.
                // Live cross-ledger authority was validated at restore/admission; retained
                // links are checked again by the final full validation below.
                foreach (var task in state.NpcTasks)
                    if (removedTasks.Contains(task.Id)) NpcTaskContract.Validate(task);
                foreach (string actionId in removedActions)
                    NpcActionExecutor.Validate(actionsById[actionId]);
                // Detach only the ledgers changed by this transaction. The final full validation
                // proves task/action, origin, care, lodging and admission links before publication.
                var candidate = state.CandidateForOperationalRetention();
                candidate.NpcTasks.RemoveAll(x => removedTasks.Contains(x.Id));
                candidate.NpcActions.RemoveAll(x => removedActions.Contains(x.Id));
                ReleaseOrphanedGoalOrigins(candidate, result);
                var validated = CopyValidated(candidate); result.FullValidationPasses++;
                state.NpcTasks = validated.NpcTasks;
                state.NpcActions = validated.NpcActions;
                state.NpcMotivations = validated.NpcMotivations;
                npcTaskIndexOwner = null;
                npcActionIndexOwner = null;
                itemTransferReceiptDrainCursor = 0;
                nextNpcInteractionProjectionIndex = 0;
                treatmentProjectionCursorState = null;
                treatmentProjectionCursor = 0;
                ordinaryOperationalSweep = new OperationalSweepCursor();
                taskPressureSweep = new OperationalSweepCursor();
                actionPressureSweep = new OperationalSweepCursor();
            }
            catch (ArgumentException)
            {
                cursor.Owner = null;
                result.RemovedTaskGroups = result.RemovedStandaloneActions = result.RemovedStepActions = 0;
                result.Pressure = NpcOperationalHistoryPressure.ValidationRejected;
                return result;
            }
            result.Pressure = underCapacityPressure && !scanComplete
                ? NpcOperationalHistoryPressure.DeferredScan : NpcOperationalHistoryPressure.None;
            return result;
        }

        private static long HashSweep(long previous, string id, long revision, long phase, long detail, long source)
        {
            unchecked
            {
                long hash = previous * 31 + (id == null ? 0 : StringComparer.Ordinal.GetHashCode(id));
                hash = hash * 31 + revision; hash = hash * 31 + phase;
                hash = hash * 31 + detail; return hash * 31 + source;
            }
        }

        private static bool TaskHasAction(NpcTaskPlan task, Dictionary<string, NpcActionJob> actionsById,
            NpcOperationalHistoryRetentionResult result)
        {
            for (int step = 0; step < task.Steps.Count; step++)
            {
                result.ReferenceRowsInspected++;
                if (actionsById.ContainsKey(task.Id + ":step:" + step)) return true;
            }
            return false;
        }

        private int MoreRecentTaskCount(int index, NpcOperationalHistoryRetentionResult result)
        {
            int count = 0; var candidate = state.NpcTasks[index];
            foreach (var task in state.NpcTasks)
            {
                result.ReferenceRowsInspected++;
                if (task.ResidentId == candidate.ResidentId && NpcTaskContract.IsTerminal(task) &&
                    MoreRecent(task.UpdatedAt, task.Id, candidate.UpdatedAt, candidate.Id)) count++;
                if (count >= RecentTerminalTasksPerPerson) break;
            }
            return count;
        }

        private int MoreRecentStandaloneCount(int index, HashSet<string> stepActionIds,
            NpcOperationalHistoryRetentionResult result)
        {
            int count = 0; var candidate = state.NpcActions[index];
            foreach (var action in state.NpcActions)
            {
                result.ReferenceRowsInspected++;
                if (action.ResidentId == candidate.ResidentId && NpcActionExecutor.IsTerminal(action) &&
                    !stepActionIds.Contains(action.Id) &&
                    MoreRecent(action.UpdatedAt, action.Id, candidate.UpdatedAt, candidate.Id)) count++;
                if (count >= RecentStandaloneActionsPerPerson) break;
            }
            return count;
        }

        private static bool MoreRecent(double leftTime, string leftId, double rightTime, string rightId)
        { return leftTime > rightTime || leftTime == rightTime && string.CompareOrdinal(leftId, rightId) > 0; }

        private bool TaskPinned(NpcTaskPlan task, Dictionary<string, NpcActionJob> actionsById,
            Dictionary<string, string> latestActionByResident, NpcOperationalHistoryRetentionResult result)
        {
            if (!NpcTaskContract.IsTerminal(task) || task.FailureReceipt != null && !task.FailureReceipt.Delivered)
                return true;
            foreach (var care in state.NpcHealthCares)
            { result.ReferenceRowsInspected++; if (care.TaskId == task.Id) return true; }
            foreach (var inn in state.Lodgings)
                foreach (var booking in inn.Bookings)
                { result.ReferenceRowsInspected++; if (booking.HealthCareTaskId == task.Id) return true; }
            for (int step = 0; step < task.Steps.Count; step++)
            {
                NpcActionJob action;
                if (actionsById.TryGetValue(task.Id + ":step:" + step, out action) &&
                    ActionPinned(action, latestActionByResident, result)) return true;
            }
            return false;
        }

        private static bool ActionPinned(NpcActionJob action, Dictionary<string, string> latestActionByResident,
            NpcOperationalHistoryRetentionResult result)
        {
            result.ReferenceRowsInspected++;
            string latest;
            return !NpcActionExecutor.IsTerminal(action) || action.ReturningToRoutine ||
                action.FollowTransition != null || action.RoutineRecoveryTransition != null ||
                latestActionByResident.TryGetValue(action.ResidentId, out latest) && latest == action.Id ||
                action.ItemTransferReceipt != null && action.ProjectedItemTransferReceipt == null ||
                action.Kind == NpcActionKind.Interact && action.InteractionProjectionDisposition == NpcInteractionProjectionDisposition.Pending ||
                action.Phase == NpcActionPhase.Completed &&
                    (action.Kind == NpcActionKind.UseHealingItem || action.Kind == NpcActionKind.RestToHeal ||
                     action.Kind == NpcActionKind.CastSpell && action.MagicHealing != null) &&
                    action.TreatmentOutcomeRevision == 0;
        }

        private static void ReleaseOrphanedGoalOrigins(WorldState candidate,
            NpcOperationalHistoryRetentionResult result)
        {
            foreach (var motivation in candidate.NpcMotivations)
            {
                var pins = new HashSet<string>(StringComparer.Ordinal);
                foreach (var task in candidate.NpcTasks)
                {
                    result.ReferenceRowsInspected++;
                    if (task.ResidentId == motivation.OwnerPersonId && task.GoalId != null)
                        pins.Add(NpcMotivationState.OriginKey(task.GoalId, task.GoalStartRevision));
                }
                motivation.GoalOriginAnchors.RemoveAll(anchor =>
                    !pins.Contains(NpcMotivationState.OriginKey(anchor.RowId, anchor.Revision)) &&
                    !motivation.Goals.Exists(goal => goal.Id == anchor.RowId && goal.GoalStartRevision == anchor.Revision) &&
                    !motivation.AcceptedUpdates.Exists(receipt =>
                        receipt.ChangeKind == NpcMotivationStateChangeKind.Goal &&
                        receipt.RowId == anchor.RowId && receipt.GoalStartRevision == anchor.Revision));
            }
        }
    }
}
