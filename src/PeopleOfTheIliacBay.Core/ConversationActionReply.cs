using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum ConversationActionReplyStatus
    {
        Unknown,
        NoChange,
        Rejected,
        AcceptedPending,
        InProgress,
        Completed,
        Failed,
        Cancelled
    }

    public enum ConversationActionReplySource
    {
        None,
        Action,
        Task
    }

    // Immutable reply receipt. It contains only backend status and identifiers;
    // the runtime binds it to the current speaker, request, and callback stage.
    public sealed class ConversationActionReply
    {
        public readonly ConversationActionReplyStatus Status;
        public readonly NpcActionKind? ActionKind;
        public readonly ConversationActionReplySource Source;
        public readonly string SourceId;
        public readonly string PersonId;
        public readonly long SourceRevision;
        public readonly long ActiveActionRevision;
        public readonly NpcActionPhase? ActiveActionPhase;
        public readonly string EffectReceiptId;
        public readonly int CompletedSteps;
        public readonly int TotalSteps;
        public readonly string ReceiptKey;
        public readonly string NpcText;
        public readonly string UnresolvedReason;
        public readonly bool CancellationAttemptFailed;

        private const int MaximumUnresolvedReasonCharacters = 512;

        private ConversationActionReply(ConversationActionReplyStatus status, NpcActionKind? actionKind,
            ConversationActionReplySource source, string sourceId, string personId, long sourceRevision,
            string effectReceiptId, int completedSteps, int totalSteps,
            long activeActionRevision = 0, NpcActionPhase? activeActionPhase = null,
            bool unsuccessfulCancellation = false, string unresolvedReason = null, bool partialTaskFailure = false)
        {
            Status = status;
            ActionKind = actionKind;
            Source = source;
            SourceId = sourceId;
            PersonId = personId;
            SourceRevision = sourceRevision;
            ActiveActionRevision = activeActionRevision;
            ActiveActionPhase = activeActionPhase;
            CancellationAttemptFailed = unsuccessfulCancellation;
            UnresolvedReason = unresolvedReason;
            EffectReceiptId = effectReceiptId;
            CompletedSteps = completedSteps;
            TotalSteps = totalSteps;
            ReceiptKey = source == ConversationActionReplySource.None ? null :
                (source == ConversationActionReplySource.Action ? "action" : "task") +
                ":" + sourceId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" + sourceId + ":" + sourceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" + activeActionRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" + (activeActionPhase.HasValue ? ((int)activeActionPhase.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-");
            NpcText = Format(status, source == ConversationActionReplySource.Task, completedSteps, totalSteps,
                CancellationAttemptFailed);
            if(partialTaskFailure && (status==ConversationActionReplyStatus.AcceptedPending || status==ConversationActionReplyStatus.InProgress))
                NpcText="An earlier step failed. I will continue only the independent work we agreed I could still do.";
        }

        public static ConversationActionReply NoChange()
        {
            return new ConversationActionReply(ConversationActionReplyStatus.NoChange, null,
                ConversationActionReplySource.None, null, null, 0, null, 0, 0);
        }

        public static ConversationActionReply Unresolved(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumUnresolvedReasonCharacters || !ValidUtf16(reason))
                throw new ArgumentException("Native unresolved reason must be valid UTF-16 and at most 512 characters.", nameof(reason));
            return new ConversationActionReply(ConversationActionReplyStatus.NoChange, null,
                ConversationActionReplySource.None, null, null, 0, null, 0, 0,
                unresolvedReason: reason);
        }

        // Backend committed or may have committed work, but its saved receipt
        // cannot currently be verified. This must not imply acceptance or denial.
        public static ConversationActionReply Unverified()
        {
            return new ConversationActionReply(ConversationActionReplyStatus.Unknown, null,
                ConversationActionReplySource.None, null, null, 0, null, 0, 0);
        }

        // Preserve the saved action/task receipt while changing only the reply
        // purpose after a stop request could not be committed.
        public ConversationActionReply ForUnsuccessfulCancellation()
        {
            if (Source == ConversationActionReplySource.None) return Unverified();
            return new ConversationActionReply(Status, ActionKind, Source, SourceId, PersonId, SourceRevision,
                EffectReceiptId, CompletedSteps, TotalSteps, ActiveActionRevision, ActiveActionPhase, true);
        }

        public static ConversationActionReply Rejected(NpcActionKind? kind)
        {
            ValidateKind(kind);
            return new ConversationActionReply(ConversationActionReplyStatus.Rejected, kind,
                ConversationActionReplySource.None, null, null, 0, null, 0, 0);
        }

        public static bool TryFromSavedAction(WorldStateStore store, string actionId, out ConversationActionReply reply)
        {
            reply = null;
            if (store == null || !Identifier(actionId, 220)) return false;
            NpcActionJob job;
            try { job = store.ReadNpcAction(actionId); }
            catch (ArgumentException) { return false; }
            if (job == null || !string.Equals(job.Id, actionId, StringComparison.Ordinal)) return false;
            try { NpcActionExecutor.Validate(job); }
            catch (ArgumentException) { return false; }

            ConversationActionReplyStatus status;
            switch (job.Phase)
            {
                case NpcActionPhase.Accepted: status = ConversationActionReplyStatus.AcceptedPending; break;
                case NpcActionPhase.Approaching:
                case NpcActionPhase.Executing: status = ConversationActionReplyStatus.InProgress; break;
                case NpcActionPhase.Completed:
                    if (!Identifier(job.CompletedEffectId, 240)) return false;
                    status = ConversationActionReplyStatus.Completed;
                    break;
                case NpcActionPhase.Failed: status = ConversationActionReplyStatus.Failed; break;
                case NpcActionPhase.Cancelled: status = ConversationActionReplyStatus.Cancelled; break;
                default: return false;
            }

            reply = new ConversationActionReply(status, job.Kind, ConversationActionReplySource.Action,
                job.Id, job.ResidentId, job.Revision, job.CompletedEffectId,
                status == ConversationActionReplyStatus.Completed ? 1 : 0, 1,
                job.Revision, job.Phase);
            return true;
        }

        public static bool TryFromSavedTask(WorldStateStore store, string taskId, out ConversationActionReply reply)
        {
            reply = null;
            if (store == null || !Identifier(taskId, 190)) return false;
            NpcTaskPlan task;
            try { task = store.ReadNpcTask(taskId); }
            catch (ArgumentException) { return false; }
            if (task == null || !string.Equals(task.Id, taskId, StringComparison.Ordinal)) return false;
            try { NpcTaskContract.Validate(task); }
            catch (ArgumentException) { return false; }

            int confirmedSteps=0;
            for (int index = 0; index < task.CurrentStepIndex; index++)
            {
                var step=task.Steps[index];
                if(step.FailureReason!=null)
                {
                    var failed=store.ReadNpcAction(task.Id+":step:"+index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if(step.Blocked?failed!=null:failed!=null &&
                        (!TryReadTaskStep(store,task,index,out failed) || failed.Phase!=NpcActionPhase.Failed))return false;
                    continue;
                }
                NpcActionJob completed;
                if (!TryReadTaskStep(store, task, index, out completed) ||
                    completed.Phase != NpcActionPhase.Completed ||
                    completed.CompletedEffectId != task.Steps[index].CompletedEffectId)
                    return false;
                confirmedSteps++;
            }

            ConversationActionReplyStatus status;
            long activeActionRevision = 0;
            NpcActionPhase? activeActionPhase = null;
            if (task.Phase == NpcTaskPhase.Ready)
            {
                if (task.ActiveActionId != null) return false;
                status = ConversationActionReplyStatus.AcceptedPending;
            }
            else if (task.Phase == NpcTaskPhase.Running)
            {
                NpcActionJob active;
                int index = task.CurrentStepIndex;
                if (index >= task.Steps.Count ||
                    !TryReadTaskStep(store, task, index, out active) ||
                    active.Id != task.ActiveActionId || active.Kind != task.Steps[index].Kind ||
                    (active.Phase != NpcActionPhase.Accepted && active.Phase != NpcActionPhase.Approaching &&
                     active.Phase != NpcActionPhase.Executing && active.Phase != NpcActionPhase.Completed))
                    return false;
                status = active.Phase == NpcActionPhase.Accepted ? ConversationActionReplyStatus.AcceptedPending :
                    ConversationActionReplyStatus.InProgress;
                activeActionRevision = active.Revision;
                activeActionPhase = active.Phase;
            }
            else if (task.Phase == NpcTaskPhase.Completed)
            {
                if (task.CurrentStepIndex != task.Steps.Count || task.Steps.Count == 0) return false;
                status = ConversationActionReplyStatus.Completed;
            }
            else if (task.Phase == NpcTaskPhase.Failed || task.Phase == NpcTaskPhase.Cancelled)
            {
                if (task.ActiveActionId != null)
                {
                    NpcActionJob terminal;
                    if (!TryReadTaskStep(store, task, task.CurrentStepIndex, out terminal) ||
                        terminal.Id != task.ActiveActionId || terminal.Kind != task.Steps[task.CurrentStepIndex].Kind ||
                        (task.Phase == NpcTaskPhase.Failed && terminal.Phase != NpcActionPhase.Failed) ||
                        (task.Phase == NpcTaskPhase.Cancelled && terminal.Phase != NpcActionPhase.Cancelled))
                        return false;
                    activeActionRevision = terminal.Revision;
                    activeActionPhase = terminal.Phase;
                }
                status = task.Phase == NpcTaskPhase.Failed ? ConversationActionReplyStatus.Failed : ConversationActionReplyStatus.Cancelled;
            }
            else return false;

            string effectReceipt = null;
            if (status == ConversationActionReplyStatus.Completed)
            {
                for (int index = 0; index < task.Steps.Count; index++)
                {
                    NpcActionJob completed;
                    if (!TryReadTaskStep(store, task, index, out completed) ||
                        completed.Phase != NpcActionPhase.Completed ||
                        completed.CompletedEffectId != task.Steps[index].CompletedEffectId)
                        return false;
                }
                effectReceipt = task.Steps[task.Steps.Count - 1].CompletedEffectId;
                if (!Identifier(effectReceipt, 240)) return false;
            }

            reply = new ConversationActionReply(status, null, ConversationActionReplySource.Task,
                task.Id, task.ResidentId, task.Revision, effectReceipt, confirmedSteps, task.Steps.Count,
                activeActionRevision, activeActionPhase, partialTaskFailure:task.FailureReason!=null);
            return true;
        }

        // Re-read saved state immediately before rendering/appending. No-record
        // outcomes must instead be guarded by the runtime request/stage binding.
        public bool MatchesSavedState(WorldStateStore store)
        {
            if (Source == ConversationActionReplySource.None || store == null) return false;
            ConversationActionReply current = null;
            bool found = Source == ConversationActionReplySource.Action
                ? TryFromSavedAction(store, SourceId, out current)
                : Source == ConversationActionReplySource.Task && TryFromSavedTask(store, SourceId, out current);
            return found && current.Source == Source && current.Status == Status &&
                current.SourceId == SourceId && current.PersonId == PersonId &&
                current.SourceRevision == SourceRevision && current.ActionKind == ActionKind &&
                current.EffectReceiptId == EffectReceiptId && current.CompletedSteps == CompletedSteps &&
                current.TotalSteps == TotalSteps && current.ActiveActionRevision == ActiveActionRevision &&
                current.ActiveActionPhase == ActiveActionPhase && current.ReceiptKey == ReceiptKey;
        }

        private static bool TryReadTaskStep(WorldStateStore store, NpcTaskPlan task, int index, out NpcActionJob action)
        {
            action = null;
            if (index < 0 || index >= task.Steps.Count) return false;
            string actionId = task.Id + ":step:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try { action = store.ReadNpcAction(actionId); }
            catch (ArgumentException) { return false; }
            if (action == null || action.Id != actionId || action.ResidentId != task.ResidentId ||
                action.Kind != task.Steps[index].Kind) return false;
            try { NpcActionExecutor.Validate(action); }
            catch (ArgumentException) { return false; }
            return true;
        }

        private static string Format(ConversationActionReplyStatus status, bool task, int completedSteps, int totalSteps,
            bool unsuccessfulCancellation)
        {
            if (unsuccessfulCancellation)
            {
                switch (status)
                {
                    case ConversationActionReplyStatus.AcceptedPending: return "I couldn't stop it. It hasn't started yet.";
                    case ConversationActionReplyStatus.InProgress: return "I couldn't stop it. I'm still working on what we agreed.";
                    case ConversationActionReplyStatus.Completed: return "That has already finished.";
                    case ConversationActionReplyStatus.Failed: return "That could not be completed.";
                    case ConversationActionReplyStatus.Cancelled: return "That has already been stopped.";
                    default: return "I can't confirm that yet.";
                }
            }
            switch (status)
            {
                case ConversationActionReplyStatus.Unknown: return "I can't confirm that yet.";
                case ConversationActionReplyStatus.NoChange: return "I haven't agreed to do anything new.";
                case ConversationActionReplyStatus.Rejected: return "I can't agree to that.";
                case ConversationActionReplyStatus.AcceptedPending:
                    if (task && completedSteps > 0 && completedSteps < totalSteps)
                        return "I completed " + Count(completedSteps) + " of " + Count(totalSteps) + " steps. I haven't started the next step yet.";
                    return "I agree. I'll get started once we've finished talking.";
                case ConversationActionReplyStatus.InProgress:
                    return "I'm still working on what we agreed.";
                case ConversationActionReplyStatus.Completed:
                    return "I've finished what we agreed.";
                case ConversationActionReplyStatus.Failed:
                    return task && completedSteps > 0
                        ? "I completed " + Count(completedSteps) + " of " + Count(totalSteps) + " steps, but couldn't finish the task."
                        : "I couldn't finish what we agreed.";
                case ConversationActionReplyStatus.Cancelled:
                    return task && completedSteps > 0
                        ? "I stopped after completing " + Count(completedSteps) + " of " + Count(totalSteps) + " steps."
                        : "I have stopped what you asked me to do.";
                default: throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        private static string Count(int value)
        { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        private static void ValidateKind(NpcActionKind? kind)
        {
            if (kind.HasValue && !Enum.IsDefined(typeof(NpcActionKind), kind.Value))
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        private static bool Identifier(string value, int maximum)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum; }

        private static bool ValidUtf16(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsHighSurrogate(current))
                {
                    if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
                }
                else if (char.IsLowSurrogate(current)) return false;
            }
            return true;
        }
    }
}
