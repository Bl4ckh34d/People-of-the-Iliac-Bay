using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Derives an ordinary resident's health need from saved native vitals.</summary>
    public static class NpcHealthNeedLifecycle
    {
        public const string HealthNeedId = "need:native-health";
        public const string RecoveryGoalId = "goal:native-recovery";

        private const string EvidencePrefix = "native-health:v1:";
        private const string OperationPrefix = "motivation:native-health:v1:";

        public static bool Observe(WorldStateStore store, string personId, NpcArchetype archetype, double now)
        {
            if (store == null || string.IsNullOrWhiteSpace(personId) || !ValidTime(now) ||
                !Enum.IsDefined(typeof(NpcArchetype), archetype)) return false;

            Resident resident = store.ResolveResident(personId);
            if (resident == null || !resident.IsAlive || resident.IsQuestProtected ||
                resident.AgeGroup != ResidentAgeGroup.Adult) return false;

            NpcVitalsView vitals;
            try { vitals = store.ReadNpcVitals(personId, archetype); }
            catch (ArgumentException) { return false; }
            if (vitals == null || !vitals.IsAlive || vitals.MaximumHealth < 1 || vitals.CurrentHealth < 1 ||
                vitals.CurrentHealth > vitals.MaximumHealth || now < vitals.CapturedAt) return false;

            NpcMotivationStateView state = store.ReadNpcMotivationState(personId);
            if (state == null || now < state.LastGameTime || state.Revision == long.MaxValue)
                return false;

            NpcMotivationNeedView existingNeed = FindNeed(state);
            NpcMotivationGoalView existingGoal = FindGoal(state);
            if ((existingNeed != null && existingNeed.Kind != NpcMotivationNeedKind.Health) ||
                (existingGoal != null && existingGoal.Kind != NpcMotivationGoalKind.Recovery)) return false;

            bool injured = vitals.CurrentHealth < vitals.MaximumHealth;
            bool activeRecovery = existingGoal != null && existingGoal.Status == NpcMotivationGoalStatus.Active;
            int intensity = injured ? HealthNeedIntensity(vitals.MaximumHealth, vitals.CurrentHealth) : 0;
            NpcMotivationPriorityLevel priority = injured
                ? Priority(vitals.MaximumHealth, vitals.CurrentHealth)
                : NpcMotivationPriorityLevel.Routine;

            bool needRequired = injured || activeRecovery || HasPositiveNeed(existingNeed);
            bool needChanged = needRequired && !NeedMatches(existingNeed, intensity);
            NpcMotivationGoalStatus goalStatus = injured
                ? NpcMotivationGoalStatus.Active
                : NpcMotivationGoalStatus.Fulfilled;
            bool goalRequired = injured || activeRecovery;
            bool goalChanged = goalRequired && !GoalMatches(existingGoal, goalStatus, priority);

            int requiredUpdates = (needChanged ? 1 : 0) + (goalChanged ? 1 : 0);
            if (requiredUpdates == 0 || state.Revision > long.MaxValue - requiredUpdates)
                return false;

            if (needChanged && goalChanged)
                return TryCommitNeedAndGoal(store, state, personId, archetype, vitals, now, intensity, goalStatus, priority);
            if (needChanged) return TryCommitNeed(store, state, personId, archetype, vitals, now, intensity);
            return TryCommitGoal(store, state, personId, archetype, vitals, now, goalStatus, priority);
        }

        public static bool ShouldInterruptWork(NpcVitalsView vitals)
        {
            return vitals != null && vitals.IsAlive && vitals.MaximumHealth > 0 && vitals.CurrentHealth > 0 &&
                vitals.CurrentHealth <= vitals.MaximumHealth && !HasRecoveredForWork(vitals);
        }

        // Resume ordinary work after immediate care; leave the remaining injury for rest.
        public static bool HasRecoveredForWork(NpcVitalsView vitals)
        {
            return vitals != null && vitals.IsAlive && vitals.MaximumHealth > 0 &&
                vitals.CurrentHealth <= vitals.MaximumHealth &&
                (long)vitals.CurrentHealth * 3 >= (long)vitals.MaximumHealth * 2;
        }

        private static bool TryCommitNeed(WorldStateStore store, NpcMotivationStateView state, string owner, NpcArchetype archetype,
            NpcVitalsView vitals, double now, int intensity)
        {
            NpcMotivationUpdateTicket ticket;
            if (!store.TryCaptureNpcMotivationUpdate(owner, out ticket) || ticket.ExpectedRevision != state.Revision)
                return false;
            return store.TryCommitNativeHealthMotivationUpdate(ticket, archetype, NeedProposal(owner, state.Revision, vitals, now, intensity));
        }

        private static bool TryCommitGoal(WorldStateStore store, NpcMotivationStateView state, string owner, NpcArchetype archetype,
            NpcVitalsView vitals, double now, NpcMotivationGoalStatus status, NpcMotivationPriorityLevel priority)
        {
            NpcMotivationUpdateTicket ticket;
            if (!store.TryCaptureNpcMotivationUpdate(owner, out ticket) || ticket.ExpectedRevision != state.Revision)
                return false;
            return store.TryCommitNativeHealthMotivationUpdate(ticket, archetype, GoalProposal(owner, state.Revision, vitals, now, status, priority));
        }

        private static bool TryCommitNeedAndGoal(WorldStateStore store, NpcMotivationStateView state,
            string owner, NpcArchetype archetype, NpcVitalsView vitals, double now, int intensity,
            NpcMotivationGoalStatus status, NpcMotivationPriorityLevel priority)
        {
            NpcMotivationUpdateTicket ticket;
            if (!store.TryCaptureNpcMotivationUpdate(owner, out ticket) || ticket.ExpectedRevision != state.Revision)
                return false;
            return store.TryCommitNativeHealthMotivationUpdatePair(ticket, archetype,
                NeedProposal(owner, state.Revision, vitals, now, intensity),
                GoalProposal(owner, state.Revision + 1, vitals, now, status, priority));
        }

        private static NpcMotivationStateUpdateProposal NeedProposal(string owner, long expected,
            NpcVitalsView vitals, double now, int intensity)
        {
            var evidence = Evidence(owner, EvidenceId("need", vitals), expected + 1, now);
            return NpcMotivationStateUpdateProposal.ForNeed(owner, expected, OperationId("need", vitals), now,
                HealthNeedId, NpcMotivationNeedKind.Health, NpcMotivationKnowledgeState.Known, intensity, evidence);
        }

        private static NpcMotivationStateUpdateProposal GoalProposal(string owner, long expected,
            NpcVitalsView vitals, double now, NpcMotivationGoalStatus status, NpcMotivationPriorityLevel priority)
        {
            var evidence = Evidence(owner, EvidenceId("goal", vitals), expected + 1, now);
            return NpcMotivationStateUpdateProposal.ForGoal(owner, expected, OperationId("goal", vitals), now,
                RecoveryGoalId, NpcMotivationGoalKind.Recovery, status, priority,
                NpcMotivationKnowledgeState.Known, evidence);
        }

        internal static bool IsExactNativeProposal(NpcMotivationStateUpdateProposal proposal, NpcVitalsView vitals)
        {
            if (proposal == null || vitals == null || !vitals.IsAlive || vitals.Revision < 1 ||
                proposal.GameTime < vitals.CapturedAt) return false;
            bool injured = vitals.CurrentHealth < vitals.MaximumHealth;
            if (proposal.ChangeKind == NpcMotivationStateChangeKind.Need)
            {
                var row = proposal.Need;
                return row != null && proposal.Goal == null && proposal.Priority == null && row.Id == HealthNeedId &&
                    row.Kind == NpcMotivationNeedKind.Health && row.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                    row.Intensity == HealthNeedIntensity(vitals.MaximumHealth, vitals.CurrentHealth) &&
                    ExactEvidence(row.Evidence, proposal.OwnerPersonId, EvidenceId("need", vitals), proposal.ExpectedRevision + 1, proposal.GameTime) &&
                    proposal.OperationId == OperationId("need", vitals);
            }
            if (proposal.ChangeKind == NpcMotivationStateChangeKind.Goal)
            {
                var row = proposal.Goal;
                return row != null && proposal.Need == null && proposal.Priority == null && row.Id == RecoveryGoalId &&
                    row.Kind == NpcMotivationGoalKind.Recovery && row.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                    row.Status == (injured ? NpcMotivationGoalStatus.Active : NpcMotivationGoalStatus.Fulfilled) &&
                    row.Priority == (injured ? Priority(vitals.MaximumHealth, vitals.CurrentHealth) : NpcMotivationPriorityLevel.Routine) &&
                    ExactEvidence(row.Evidence, proposal.OwnerPersonId, EvidenceId("goal", vitals), proposal.ExpectedRevision + 1, proposal.GameTime) &&
                    proposal.OperationId == OperationId("goal", vitals);
            }
            return false;
        }

        internal static bool IsExpectedNativeSequence(NpcMotivationState current, NpcVitalsView vitals,
            NpcMotivationStateUpdateProposal[] proposals)
        {
            if (current == null || vitals == null || proposals == null) return false;
            var need = current.Needs.Find(x => x.Id == HealthNeedId);
            var goal = current.Goals.Find(x => x.Id == RecoveryGoalId);
            if (need != null && need.Kind != NpcMotivationNeedKind.Health ||
                goal != null && goal.Kind != NpcMotivationGoalKind.Recovery) return false;
            bool injured = vitals.CurrentHealth < vitals.MaximumHealth;
            bool activeRecovery = goal != null && goal.Status == NpcMotivationGoalStatus.Active;
            int intensity = injured ? HealthNeedIntensity(vitals.MaximumHealth, vitals.CurrentHealth) : 0;
            var priority = injured ? Priority(vitals.MaximumHealth, vitals.CurrentHealth) : NpcMotivationPriorityLevel.Routine;
            var status = injured ? NpcMotivationGoalStatus.Active : NpcMotivationGoalStatus.Fulfilled;
            bool needRequired = injured || activeRecovery || need != null && need.Intensity.HasValue && need.Intensity.Value > 0;
            bool goalRequired = injured || activeRecovery;
            bool needChanged = needRequired && (need == null || need.KnowledgeState != NpcMotivationKnowledgeState.Known ||
                need.Intensity != intensity || need.Evidence == null || need.Evidence.Kind != NpcMotivationStateEvidenceKind.NativeObservation);
            bool goalChanged = goalRequired && (goal == null || goal.Status != status || goal.Priority != priority ||
                goal.KnowledgeState != NpcMotivationKnowledgeState.Known || goal.Evidence == null ||
                goal.Evidence.Kind != NpcMotivationStateEvidenceKind.NativeObservation);
            int count = (needChanged ? 1 : 0) + (goalChanged ? 1 : 0);
            if (count == 0 || count != proposals.Length) return false;
            int index = 0;
            if (needChanged && proposals[index++].ChangeKind != NpcMotivationStateChangeKind.Need) return false;
            return !goalChanged || proposals[index].ChangeKind == NpcMotivationStateChangeKind.Goal;
        }

        private static bool ExactEvidence(NpcMotivationStateEvidence evidence, string owner, string id, long revision, double time)
        { return evidence != null && evidence.ObserverId == owner && evidence.EvidenceId == id &&
            evidence.Kind == NpcMotivationStateEvidenceKind.NativeObservation && evidence.Revision == revision &&
            evidence.ObservedGameTime == time; }

        private static NpcMotivationNeedView FindNeed(NpcMotivationStateView state)
        {
            foreach (NpcMotivationNeedView need in state.Needs)
                if (string.Equals(need.Id, HealthNeedId, StringComparison.Ordinal)) return need;
            return null;
        }

        private static NpcMotivationGoalView FindGoal(NpcMotivationStateView state)
        {
            foreach (NpcMotivationGoalView goal in state.Goals)
                if (string.Equals(goal.Id, RecoveryGoalId, StringComparison.Ordinal)) return goal;
            return null;
        }

        private static bool HasPositiveNeed(NpcMotivationNeedView need)
        { return need != null && need.Intensity.HasValue && need.Intensity.Value > 0; }

        private static bool NeedMatches(NpcMotivationNeedView need, int intensity)
        {
            return need != null && need.Kind == NpcMotivationNeedKind.Health &&
                need.KnowledgeState == NpcMotivationKnowledgeState.Known && need.Intensity == intensity &&
                need.EvidenceKind == NpcMotivationStateEvidenceKind.NativeObservation;
        }

        private static bool GoalMatches(NpcMotivationGoalView goal, NpcMotivationGoalStatus status,
            NpcMotivationPriorityLevel priority)
        {
            return goal != null && goal.Kind == NpcMotivationGoalKind.Recovery && goal.Status == status &&
                goal.Priority == priority && goal.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                goal.EvidenceKind == NpcMotivationStateEvidenceKind.NativeObservation;
        }

        private static NpcMotivationStateEvidence Evidence(string owner, string evidenceId, long revision, double now)
        {
            return new NpcMotivationStateEvidence(owner, evidenceId,
                NpcMotivationStateEvidenceKind.NativeObservation, revision, now);
        }

        private static int HealthNeedIntensity(int maximumHealth, int currentHealth)
        {
            long missing = (long)maximumHealth - currentHealth;
            if (missing <= 0) return 0;
            return (int)Math.Min(100, (100L * missing + maximumHealth - 1) / maximumHealth);
        }

        private static NpcMotivationPriorityLevel Priority(int maximumHealth, int currentHealth)
        {
            if ((long)currentHealth * 4 <= maximumHealth) return NpcMotivationPriorityLevel.Emergency;
            if ((long)currentHealth * 2 <= maximumHealth) return NpcMotivationPriorityLevel.Urgent;
            return NpcMotivationPriorityLevel.Important;
        }

        private static string EvidenceId(string row, NpcVitalsView vitals)
        {
            return EvidencePrefix + row + ":" + vitals.Revision.ToString(CultureInfo.InvariantCulture) + ":" +
                vitals.CurrentHealth.ToString(CultureInfo.InvariantCulture) + ":" +
                vitals.MaximumHealth.ToString(CultureInfo.InvariantCulture);
        }

        private static string OperationId(string row, NpcVitalsView vitals)
        {
            return OperationPrefix + row + ":" + vitals.Revision.ToString(CultureInfo.InvariantCulture);
        }

        private static bool ValidTime(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
    }
}
