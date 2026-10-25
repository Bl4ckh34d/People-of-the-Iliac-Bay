using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Saves the service pause before the runtime releases ordinary work. A saved pause owned by this
        /// exact episode may be retried without charging or advancing the ledger a second time.
        /// Native alarm, quest movement ownership and treatment feasibility remain runtime preflights.
        /// </summary>
        public bool TryEnsureNpcServiceHealthCarePause(NpcServiceContractWriteTicket ticket,
            string contractId, string personId, string careEpisodeId, long expectedCareRevision,
            NpcArchetype archetype, long expectedVitalsRevision, double expectedVitalsCapturedAt,
            long expectedMotivationRevision, long expectedGoalStartRevision, double now,
            out bool newlyPaused, out string error)
        {
            newlyPaused = false;
            error = "Service, care, goal or native health observation is stale.";
            if (!IsCurrentNpcServiceContractWriteTicket(ticket) ||
                !ServiceHealthTime(now) || !NpcHealthCare.ValidEpisodeId(careEpisodeId) ||
                !TryReadServiceHealthCareContext(personId, careEpisodeId, expectedCareRevision,
                    archetype, expectedVitalsRevision, expectedVitalsCapturedAt, now,
                    out var care, out var vitals) ||
                care.Phase != NpcHealthCarePhase.Seeking &&
                    (care.Phase != NpcHealthCarePhase.Blocked || now < care.RetryAt) ||
                care.TaskId != null || vitals.CurrentHealth >= vitals.MaximumHealth)
                return false;

            var motivation = ReadNpcMotivationState(personId);
            if (motivation == null || motivation.OwnerPersonId != personId ||
                motivation.Revision != expectedMotivationRevision || now < motivation.LastGameTime)
                return false;
            NpcMotivationGoalView recovery = null;
            foreach (var goal in motivation.Goals)
                if (goal.Id == NpcHealthNeedLifecycle.RecoveryGoalId) { recovery = goal; break; }
            if (recovery == null || recovery.Kind != NpcMotivationGoalKind.Recovery ||
                recovery.Status != NpcMotivationGoalStatus.Active ||
                recovery.KnowledgeState != NpcMotivationKnowledgeState.Known ||
                recovery.EvidenceKind != NpcMotivationStateEvidenceKind.NativeObservation ||
                recovery.GoalStartRevision != expectedGoalStartRevision)
                return false;

            var source = ticket.SourceLedger;
            var contract = source.Read(contractId);
            if (contract == null || contract.NpcId != personId || now >= contract.BlockEndAt)
                return false;
            int activeContracts = 0;
            int ownedHealthPauses = 0;
            foreach (var item in source.Contracts)
                if (item.NpcId == personId)
                {
                    if (item.Status == NpcServiceContractStatus.Active) activeContracts++;
                    if (item.Status == NpcServiceContractStatus.Suspended &&
                        item.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare) ownedHealthPauses++;
                }
            if (contract.Status == NpcServiceContractStatus.Suspended &&
                contract.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare &&
                contract.SuspensionOwnerId == careEpisodeId && activeContracts == 0 && ownedHealthPauses == 1)
            { error = null; return true; }
            if (contract.Status != NpcServiceContractStatus.Active || activeContracts != 1 || ownedHealthPauses != 0 ||
                !care.HasAcceptedTreatment &&
                    (recovery.Priority < NpcMotivationPriorityLevel.Urgent ||
                     !NpcHealthNeedLifecycle.ShouldInterruptWork(vitals))) return false;

            if (!contract.TrySuspendForHealthCare(contract.Revision, now, careEpisodeId, out var paused) ||
                !NpcServiceContractLedger.TryReplace(source, ticket.ExpectedLedgerRevision, paused, out var candidate))
                return false;
            if (!TryCommitNpcServiceContracts(ticket, candidate, Array.Empty<NpcServiceTransferReceipt>(),
                Array.Empty<NpcServiceRenewalAuthorization>(), out error)) return false;
            newlyPaused = true;
            return true;
        }

        /// <summary>Resumes the exact saved health pause after sufficient native recovery and care handoff.</summary>
        public bool TryResumeNpcServiceFromHealthCare(NpcServiceContractWriteTicket ticket,
            string contractId, string personId, string careEpisodeId, long expectedCareRevision,
            NpcArchetype archetype, long expectedVitalsRevision, double expectedVitalsCapturedAt,
            double now, out string error)
        {
            error = "Service pause or sufficient native recovery is stale or unavailable.";
            if (!IsCurrentNpcServiceContractWriteTicket(ticket) ||
                !ServiceHealthTime(now) || !NpcHealthCare.ValidEpisodeId(careEpisodeId) ||
                !TryReadServiceHealthCareContext(personId, careEpisodeId, expectedCareRevision,
                    archetype, expectedVitalsRevision, expectedVitalsCapturedAt, now,
                    out var care, out var vitals) ||
                care.Phase != NpcHealthCarePhase.Recovered || care.TaskId != null ||
                !NpcHealthNeedLifecycle.HasRecoveredForWork(vitals) || ActiveNpcTask(personId) != null)
                return false;
            var action = ActiveNpcAction(personId);
            if (action != null && (!NpcActionExecutor.IsTerminal(action) || action.ReturningToRoutine)) return false;

            var source = ticket.SourceLedger;
            var contract = source.Read(contractId);
            if (contract == null || contract.NpcId != personId ||
                contract.Status != NpcServiceContractStatus.Suspended ||
                contract.SuspensionOwnerKind != NpcServiceSuspensionOwnerKind.HealthCare ||
                contract.SuspensionOwnerId != careEpisodeId || now >= contract.BlockEndAt) return false;
            foreach (var item in source.Contracts)
                if (item.NpcId == personId && item.ContractId != contractId &&
                    (item.Status == NpcServiceContractStatus.Active ||
                     item.Status == NpcServiceContractStatus.Suspended &&
                     item.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare)) return false;

            if (!contract.TryResumeFromHealthCare(contract.Revision, now, careEpisodeId, out var resumed) ||
                !NpcServiceContractLedger.TryReplace(source, ticket.ExpectedLedgerRevision, resumed, out var candidate))
                return false;
            return TryCommitNpcServiceContracts(ticket, candidate, Array.Empty<NpcServiceTransferReceipt>(),
                Array.Empty<NpcServiceRenewalAuthorization>(), out error);
        }

        /// <summary>
        /// A second injury may replace a Recovered care row before its physical return releases the old
        /// service pause. Rebinds that already unpaid pause to the current episode; it never resumes billing.
        /// </summary>
        public bool TryTransferNpcServiceHealthPauseToNewCare(NpcServiceContractWriteTicket ticket,
            string contractId, string personId, string expectedOldCareEpisodeId, string newCareEpisodeId,
            long expectedNewCareRevision, NpcArchetype archetype, long expectedVitalsRevision,
            double expectedVitalsCapturedAt, long expectedMotivationRevision,
            long expectedGoalStartRevision, double now, out string error)
        {
            error = "The old pause or new native care episode is stale or unavailable.";
            if (!IsCurrentNpcServiceContractWriteTicket(ticket) ||
                !ServiceHealthTime(now) || !NpcHealthCare.ValidEpisodeId(expectedOldCareEpisodeId) ||
                expectedOldCareEpisodeId == newCareEpisodeId ||
                !TryReadServiceHealthCareContext(personId, newCareEpisodeId, expectedNewCareRevision,
                    archetype, expectedVitalsRevision, expectedVitalsCapturedAt, now,
                    out var care, out var vitals) ||
                care.Phase != NpcHealthCarePhase.Seeking &&
                    (care.Phase != NpcHealthCarePhase.Blocked || now < care.RetryAt) ||
                care.TaskId != null || vitals.CurrentHealth >= vitals.MaximumHealth)
                return false;

            var motivation = ReadNpcMotivationState(personId);
            if (motivation == null || motivation.OwnerPersonId != personId ||
                motivation.Revision != expectedMotivationRevision || now < motivation.LastGameTime)
                return false;
            NpcMotivationGoalView recovery = null;
            foreach (var goal in motivation.Goals)
                if (goal.Id == NpcHealthNeedLifecycle.RecoveryGoalId) { recovery = goal; break; }
            if (recovery == null || recovery.Kind != NpcMotivationGoalKind.Recovery ||
                recovery.Status != NpcMotivationGoalStatus.Active ||
                recovery.KnowledgeState != NpcMotivationKnowledgeState.Known ||
                recovery.EvidenceKind != NpcMotivationStateEvidenceKind.NativeObservation ||
                recovery.GoalStartRevision != expectedGoalStartRevision)
                return false;

            var source = ticket.SourceLedger;
            var contract = source.Read(contractId);
            if (contract == null || contract.NpcId != personId ||
                contract.Status != NpcServiceContractStatus.Suspended ||
                contract.SuspensionOwnerKind != NpcServiceSuspensionOwnerKind.HealthCare ||
                contract.SuspensionOwnerId != expectedOldCareEpisodeId ||
                now >= contract.BlockEndAt || care.StartedAt < contract.LastServiceUpdateAt)
                return false;
            int activeContracts = 0;
            int ownedHealthPauses = 0;
            foreach (var item in source.Contracts)
                if (item.NpcId == personId)
                {
                    if (item.Status == NpcServiceContractStatus.Active) activeContracts++;
                    if (item.Status == NpcServiceContractStatus.Suspended &&
                        item.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare) ownedHealthPauses++;
                }
            if (activeContracts != 0 || ownedHealthPauses != 1 ||
                !contract.TryTransferHealthCareSuspensionOwner(contract.Revision,
                    expectedOldCareEpisodeId, newCareEpisodeId, out var transferred) ||
                !NpcServiceContractLedger.TryReplace(source, ticket.ExpectedLedgerRevision,
                    transferred, out var candidate)) return false;
            return TryCommitNpcServiceContracts(ticket, candidate, Array.Empty<NpcServiceTransferReceipt>(),
                Array.Empty<NpcServiceRenewalAuthorization>(), out error);
        }

        private bool TryReadServiceHealthCareContext(string personId, string careEpisodeId,
            long expectedCareRevision, NpcArchetype archetype, long expectedVitalsRevision,
            double expectedVitalsCapturedAt, double now, out NpcHealthCareRecord care, out NpcVitalsView vitals)
        {
            care = null;
            vitals = null;
            if (string.IsNullOrWhiteSpace(personId) || !Enum.IsDefined(typeof(NpcArchetype), archetype) ||
                !ServiceHealthTime(expectedVitalsCapturedAt) || expectedVitalsRevision <= 0)
                return false;
            var resident = ResolveResident(personId);
            if (resident == null || !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,
                resident.AgeGroup == ResidentAgeGroup.Adult, resident.IsQuestProtected)) return false;
            care = ReadNpcHealthCare(personId);
            if (care == null || care.PersonId != personId || care.Id != careEpisodeId ||
                care.Revision != expectedCareRevision || now < care.UpdatedAt) return false;
            try { vitals = ReadNpcVitals(personId, archetype); }
            catch (ArgumentException) { return false; }
            return vitals != null && vitals.IsPersisted && vitals.IsAlive &&
                vitals.Revision == expectedVitalsRevision && vitals.CapturedAt == expectedVitalsCapturedAt &&
                vitals.CurrentHealth > 0 && vitals.CurrentHealth <= vitals.MaximumHealth &&
                now >= vitals.CapturedAt;
        }

        private static bool ServiceHealthTime(double now)
        { return !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0; }
    }
}
