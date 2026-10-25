using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcGoalWorkDisposition
    {
        NativeOwner = 1, KeepTask = 2, InterruptTask = 3,
        BeginGoalTask = 4, DeferGoal = 5, OrdinaryRoutine = 6, BeginRoutineGoalWork = 7
    }

    public enum NpcGoalMovementOwner
    {
        NativeAlarm = 1, ExistingTask = 2, HealthCare = 3, OrdinaryRoutine = 4, None = 5,
        NativeQuest = 6, ExistingAction = 7
    }

    public enum NpcGoalServiceHealthAdmission
    {
        None = 0, PaidRest = 1, RequiresPause = 2
    }

    /// <summary>Fresh native facts and one detached persisted motivation view for the ordinary resident pilot.</summary>
    public sealed class NpcGoalArbitrationInput
    {
        public string PersonId;
        public NpcMotivationStateView Motivation;
        public bool EligibleResident, NativeAlarmActive;
        public bool NativeQuestOwner, ActiveActionOwner, ActiveWorkDuty;
        public bool ActiveAppointment, ActiveServiceDuty;
        public NpcGoalServiceHealthAdmission ServiceHealthAdmission;
        public bool CareTreatmentPreviouslyAdmitted;
        public bool HealthEligible, HealthTaskFeasible, CareOwnsRoutine, CareRetryPending;
        public NpcTaskPlan ActiveTask;
        public bool ActiveTaskIsCare;
    }

    public sealed class NpcGoalArbitrationResult
    {
        public string GoalId { get; private set; }
        public long GoalStartRevision { get; private set; }
        public long MotivationRevision { get; private set; }
        public NpcGoalWorkDisposition Disposition { get; private set; }
        public NpcGoalMovementOwner MovementOwner { get; private set; }
        public NpcNeedDestinationKind Destination { get; private set; }

        internal NpcGoalArbitrationResult(string goalId, long startRevision, long motivationRevision,
            NpcGoalWorkDisposition disposition, NpcGoalMovementOwner movementOwner,
            NpcNeedDestinationKind destination)
        {
            GoalId = goalId;
            GoalStartRevision = startRevision;
            MotivationRevision = motivationRevision;
            Disposition = disposition;
            MovementOwner = movementOwner;
            Destination = destination;
        }
    }

    /// <summary>Selects one existing movement owner. Native alarm and action execution remain authoritative.</summary>
    public static class NpcGoalArbiter
    {
        /// <summary>Injury admits immediate care until native health reaches two thirds; remaining injury can recover through rest.</summary>
        public static bool HealthEligibleForArbitration(NpcVitalsView vitals, NpcHealthCareRecord care)
        {
            if (vitals == null || !vitals.IsPersisted || !vitals.IsAlive || vitals.MaximumHealth < 1 ||
                vitals.CurrentHealth < 1 || vitals.CurrentHealth >= vitals.MaximumHealth)
                return false;
            return NpcHealthNeedLifecycle.ShouldInterruptWork(vitals) ||
                care != null && (care.Phase == NpcHealthCarePhase.Seeking ||
                                 care.Phase == NpcHealthCarePhase.Acting ||
                                 care.Phase == NpcHealthCarePhase.Blocked);
        }

        public static NpcGoalArbitrationResult Select(NpcGoalArbitrationInput input)
        {
            if (input == null || input.Motivation == null || string.IsNullOrWhiteSpace(input.PersonId) ||
                !string.Equals(input.PersonId, input.Motivation.OwnerPersonId, StringComparison.Ordinal) ||
                input.ActiveTask != null && !string.Equals(input.PersonId, input.ActiveTask.ResidentId, StringComparison.Ordinal))
                throw new ArgumentException("Goal arbitration requires an owner-bound motivation view.", nameof(input));

            long revision = input.Motivation.Revision;
            if (input.NativeAlarmActive)
                return Result(null, 0, revision, NpcGoalWorkDisposition.NativeOwner, NpcGoalMovementOwner.NativeAlarm);
            if (input.NativeQuestOwner)
                return Result(null, 0, revision, NpcGoalWorkDisposition.NativeOwner, NpcGoalMovementOwner.NativeQuest);
            if (!input.EligibleResident)
                return Result(null, 0, revision, NpcGoalWorkDisposition.NativeOwner, NpcGoalMovementOwner.None);
            if (input.ActiveActionOwner)
                return Result(null, 0, revision, NpcGoalWorkDisposition.DeferGoal, NpcGoalMovementOwner.ExistingAction);

            NpcMotivationGoalView recovery = null;
            foreach (NpcMotivationGoalView goal in input.Motivation.Goals)
            {
                if (goal.Id == NpcHealthNeedLifecycle.RecoveryGoalId && goal.Kind == NpcMotivationGoalKind.Recovery &&
                    goal.Status == NpcMotivationGoalStatus.Active && goal.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                    goal.EvidenceKind == NpcMotivationStateEvidenceKind.NativeObservation)
                {
                    recovery = goal;
                    break;
                }
            }

            if (input.ActiveTaskIsCare && input.ActiveTask != null)
            {
                bool linked = recovery != null && input.ActiveTask.GoalId == recovery.Id &&
                    input.ActiveTask.GoalStartRevision == recovery.GoalStartRevision;
                return Result(linked ? recovery.Id : null,
                    linked ? recovery.GoalStartRevision : 0, revision,
                    NpcGoalWorkDisposition.KeepTask, NpcGoalMovementOwner.ExistingTask);
            }
            if (recovery == null || !input.HealthEligible)
                return ExistingRoutine(input, revision);

            bool serviceDefers = input.ActiveServiceDuty &&
                (input.ServiceHealthAdmission == NpcGoalServiceHealthAdmission.None ||
                 input.ServiceHealthAdmission == NpcGoalServiceHealthAdmission.RequiresPause &&
                 recovery.Priority < NpcMotivationPriorityLevel.Urgent &&
                 !input.CareTreatmentPreviouslyAdmitted);
            if (serviceDefers || input.ActiveAppointment && recovery.Priority < NpcMotivationPriorityLevel.Urgent ||
                input.CareRetryPending)
                return Result(recovery.Id, recovery.GoalStartRevision, revision,
                    input.ActiveTask == null ? NpcGoalWorkDisposition.DeferGoal : NpcGoalWorkDisposition.KeepTask,
                    CurrentMovement(input));

            if (!input.HealthTaskFeasible)
                return Result(recovery.Id, recovery.GoalStartRevision, revision,
                    input.ActiveTask == null ? NpcGoalWorkDisposition.DeferGoal : NpcGoalWorkDisposition.KeepTask,
                    CurrentMovement(input));

            return Result(recovery.Id, recovery.GoalStartRevision, revision,
                input.ActiveTask == null ? NpcGoalWorkDisposition.BeginGoalTask : NpcGoalWorkDisposition.InterruptTask,
                input.ActiveTask == null ? NpcGoalMovementOwner.HealthCare : NpcGoalMovementOwner.ExistingTask);
        }

        private static NpcGoalArbitrationResult ExistingRoutine(NpcGoalArbitrationInput input, long revision)
        {
            if (input.ActiveTask != null)
                return Result(null, 0, revision, NpcGoalWorkDisposition.KeepTask, NpcGoalMovementOwner.ExistingTask);
            return Result(null, 0, revision, NpcGoalWorkDisposition.OrdinaryRoutine,
                NpcGoalMovementOwner.OrdinaryRoutine);
        }

        private static NpcGoalMovementOwner CurrentMovement(NpcGoalArbitrationInput input)
        {
            return input.ActiveTask != null ? NpcGoalMovementOwner.ExistingTask :
                NpcGoalMovementOwner.OrdinaryRoutine;
        }

        private static NpcGoalArbitrationResult Result(string goalId, long startRevision, long revision,
            NpcGoalWorkDisposition disposition, NpcGoalMovementOwner movement,
            NpcNeedDestinationKind destination = NpcNeedDestinationKind.None)
        {
            return new NpcGoalArbitrationResult(goalId, startRevision, revision, disposition, movement, destination);
        }
    }
}
