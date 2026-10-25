using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>
    /// Persists one observer-owned Safety goal for a directly witnessed hostile incident.
    /// It records native state only; it neither identifies a culprit nor authorizes actions.
    /// </summary>
    public static class NpcNativeSafetyGoalLifecycle
    {
        public const string GoalId = "goal:native-safety";
        private const string OperationPrefix = "motivation:native-safety:";
        private const string EvidencePrefix = "native-safety:v1:";

        public static bool TryObserveDirectThreat(WorldStateStore store, string ownerPersonId,
            CivilianIncident incident, AlarmPerson witness, double now, bool isAlive, bool isAwake,
            bool isQuestProtected)
        {
            if (store == null || string.IsNullOrWhiteSpace(ownerPersonId) || incident == null || witness == null ||
                !ValidTime(now) || !isAlive || !isAwake || isQuestProtected || incident.ThreatId == null ||
                incident.VictimId != null || incident.PlayerAttributed || witness.ResidentId != ownerPersonId ||
                !witness.DirectWitness) return false;

            CivilianIncident active = store.ActiveAlarm(ownerPersonId, now);
            if (active == null || !ReferenceEquals(active, incident) || active.Id != incident.Id || active.ThreatId == null ||
                active.VictimId != null || active.PlayerAttributed)
                return false;
            AlarmPerson activeWitness = active.People.Find(person => person.ResidentId == ownerPersonId);
            if (activeWitness == null || !ReferenceEquals(activeWitness, witness) || !activeWitness.DirectWitness ||
                !CivilianAlarms.PersonActive(active, activeWitness, now)) return false;
            Resident resident = store.ResolveResident(ownerPersonId);
            if (resident == null || !resident.IsAlive || resident.IsQuestProtected) return false;

            string digest = EpisodeDigest(ownerPersonId, active);
            string operationId = OperationId("start", digest);
            if (store.HasNativeSafetyGoal(ownerPersonId, ActiveEvidenceId(digest), NpcMotivationGoalStatus.Active) ||
                store.HasNativeSafetyGoal(ownerPersonId, FulfilledEvidenceId(digest), NpcMotivationGoalStatus.Fulfilled) ||
                store.HasNativeSafetyGoal(ownerPersonId, AbandonedEvidenceId(digest), NpcMotivationGoalStatus.Abandoned)) return false;

            NpcMotivationStateView state = store.ReadNpcMotivationState(ownerPersonId);
            if (state == null || state.Revision == long.MaxValue || now < state.LastGameTime)
                return false;

            if (HasOperation(state, operationId)) return false;

            NpcMotivationGoalView existing = FindGoal(state);
            if (existing != null && existing.Kind != NpcMotivationGoalKind.Safety) return false;
            if (existing != null && existing.Status == NpcMotivationGoalStatus.Active &&
                string.Equals(existing.EvidenceId, ActiveEvidenceId(digest), StringComparison.Ordinal)) return false;
            if (existing != null && existing.Status != NpcMotivationGoalStatus.Active &&
                (string.Equals(existing.EvidenceId, FulfilledEvidenceId(digest), StringComparison.Ordinal) ||
                 string.Equals(existing.EvidenceId, AbandonedEvidenceId(digest), StringComparison.Ordinal))) return false;

            return TryCommit(store, state, ownerPersonId, active, "start", false, operationId, ActiveEvidenceId(digest),
                NpcMotivationGoalStatus.Active, now);
        }

        /// <summary>Fulfills only after the native shelter store sealed a living direct witness's actual arrival.</summary>
        public static bool TryFulfillAtRefuge(WorldStateStore store, string ownerPersonId,
            CivilianIncident incident, AlarmPerson witness, double now, bool isAlive, bool isQuestProtected)
        {
            if (store == null || string.IsNullOrWhiteSpace(ownerPersonId) || incident == null || witness == null ||
                !ValidTime(now) || !isAlive || isQuestProtected || incident.ThreatId == null ||
                incident.VictimId != null || incident.PlayerAttributed || witness.ResidentId != ownerPersonId ||
                !witness.DirectWitness || witness.Reaction != CivilianReaction.Flee || !witness.HasPosition ||
                !witness.ReachedRefuge || !witness.RefugeSealed || witness.BuildingKey < 0) return false;

            CivilianIncident active = store.ActiveAlarm(ownerPersonId, now);
            if (active == null || !ReferenceEquals(active, incident) || active.Id != incident.Id) return false;
            AlarmPerson activeWitness = active.People.Find(person => person.ResidentId == ownerPersonId);
            if (activeWitness == null || !ReferenceEquals(activeWitness, witness) || !activeWitness.DirectWitness ||
                !activeWitness.ReachedRefuge || !activeWitness.RefugeSealed || !CivilianAlarms.PersonActive(active, activeWitness, now))
                return false;
            Resident resident = store.ResolveResident(ownerPersonId);
            if (resident == null || !resident.IsAlive || resident.IsQuestProtected) return false;

            NpcMotivationStateView state = store.ReadNpcMotivationState(ownerPersonId);
            if (!IsActiveGoalForEpisode(state, ownerPersonId, active)) return false;
            string digest = EpisodeDigest(ownerPersonId, active);
            return TryCommit(store, state, ownerPersonId, active, "fulfill", false, OperationId("fulfill", digest),
                FulfilledEvidenceId(digest), NpcMotivationGoalStatus.Fulfilled, now);
        }

        /// <summary>
        /// Reconciles persisted active goals at a bounded native time boundary. Calls with no active goal do no writes.
        /// </summary>
        public static bool TryReconcile(WorldStateStore store, string ownerPersonId, double now,
            bool isAlive, bool isQuestProtected)
        {
            if (store == null || string.IsNullOrWhiteSpace(ownerPersonId) || !ValidTime(now)) return false;
            if (!store.HasActiveNpcSafetyGoal(ownerPersonId)) return false;
            NpcMotivationStateView state = store.ReadNpcMotivationState(ownerPersonId);
            NpcMotivationGoalView goal = FindGoal(state);
            if (!IsActiveSafetyGoal(goal) || state.Revision == long.MaxValue || now < state.LastGameTime)
                return false;

            Resident resident = store.ResolveResident(ownerPersonId);
            bool residentUnavailable = resident == null || !resident.IsAlive || resident.IsQuestProtected ||
                !isAlive || isQuestProtected;
            string digest;
            if (!TryReadActiveDigest(goal.EvidenceId, out digest)) return false;
            if (!residentUnavailable && store.HasActiveNpcSafetyEpisode(ownerPersonId, digest, now)) return false;
            return TryCommit(store, state, ownerPersonId, null, "abandon", residentUnavailable, OperationId("abandon", digest),
                AbandonedEvidenceId(digest), NpcMotivationGoalStatus.Abandoned, now);
        }

        private static bool TryCommit(WorldStateStore store, NpcMotivationStateView state, string owner,
            CivilianIncident incident, string phase, bool sourceUnavailable,
            string operationId, string evidenceId, NpcMotivationGoalStatus status, double now)
        {
            if (state == null || state.Revision == long.MaxValue || now < state.LastGameTime)
                return false;
            NpcMotivationUpdateTicket ticket;
            if (!store.TryCaptureNpcMotivationUpdate(owner, out ticket) || ticket.ExpectedRevision != state.Revision)
                return false;

            long sourceRevision = state.Revision + 1;
            var evidence = new NpcMotivationStateEvidence(owner, evidenceId,
                NpcMotivationStateEvidenceKind.NativeObservation, sourceRevision, now);
            var proposal = NpcMotivationStateUpdateProposal.ForGoal(owner, state.Revision, operationId, now,
                GoalId, NpcMotivationGoalKind.Safety, status, NpcMotivationPriorityLevel.Emergency,
                NpcMotivationKnowledgeState.Known, evidence);
            return store.TryCommitNativeSafetyMotivationUpdate(ticket, incident, phase, sourceUnavailable, proposal);
        }

        private static bool IsActiveGoalForEpisode(NpcMotivationStateView state, string owner, CivilianIncident incident)
        {
            if (state == null || state.OwnerPersonId != owner || incident == null) return false;
            NpcMotivationGoalView goal = FindGoal(state);
            return IsActiveSafetyGoal(goal) && string.Equals(goal.EvidenceId,
                ActiveEvidenceId(EpisodeDigest(owner, incident)), StringComparison.Ordinal);
        }

        private static bool IsActiveSafetyGoal(NpcMotivationGoalView goal)
        {
            return goal != null && goal.Kind == NpcMotivationGoalKind.Safety &&
                goal.Status == NpcMotivationGoalStatus.Active && goal.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                goal.EvidenceKind == NpcMotivationStateEvidenceKind.NativeObservation;
        }

        private static NpcMotivationGoalView FindGoal(NpcMotivationStateView state)
        {
            if (state == null || state.Goals == null) return null;
            foreach (NpcMotivationGoalView goal in state.Goals)
                if (goal.Id == GoalId) return goal;
            return null;
        }

        private static bool HasOperation(NpcMotivationStateView state, string operationId)
        {
            foreach (NpcAcceptedMotivationUpdateView update in state.AcceptedUpdates)
                if (string.Equals(update.OperationId, operationId, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static string EpisodeDigest(string owner, CivilianIncident incident)
        {
            string key = owner + "\n" + incident.Id + "\n" + incident.StartedAt.ToString("R", CultureInfo.InvariantCulture);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static string OperationId(string phase, string digest)
        { return OperationPrefix + phase + ":" + digest; }

        private static string ActiveEvidenceId(string digest)
        { return EvidencePrefix + digest + ":active"; }

        private static string FulfilledEvidenceId(string digest)
        { return EvidencePrefix + digest + ":fulfilled"; }

        private static string AbandonedEvidenceId(string digest)
        { return EvidencePrefix + digest + ":abandoned"; }

        private static bool TryReadActiveDigest(string evidenceId, out string digest)
        {
            digest = null;
            if (string.IsNullOrEmpty(evidenceId) || !evidenceId.StartsWith(EvidencePrefix, StringComparison.Ordinal) ||
                !evidenceId.EndsWith(":active", StringComparison.Ordinal)) return false;
            string value = evidenceId.Substring(EvidencePrefix.Length, evidenceId.Length - EvidencePrefix.Length - ":active".Length);
            if (value.Length != 64) return false;
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            digest = value;
            return true;
        }

        internal static bool IsExactNativeCommit(WorldStateStore store, string owner, NpcMotivationState current,
            CivilianIncident incident, string phase, bool sourceUnavailable, NpcMotivationStateUpdateProposal proposal)
        {
            if (store == null || current == null || proposal == null || proposal.ChangeKind != NpcMotivationStateChangeKind.Goal ||
                proposal.Need != null || proposal.Priority != null || proposal.Goal == null ||
                proposal.OwnerPersonId != owner || proposal.ExpectedRevision != current.Revision ||
                proposal.GameTime < current.LastGameTime) return false;
            var row = proposal.Goal;
            if (row.Id != GoalId || row.Kind != NpcMotivationGoalKind.Safety ||
                row.KnowledgeState != NpcMotivationKnowledgeState.Known ||
                row.Priority != NpcMotivationPriorityLevel.Emergency || row.Evidence == null ||
                row.Evidence.ObserverId != owner || row.Evidence.Kind != NpcMotivationStateEvidenceKind.NativeObservation ||
                row.Evidence.Revision != current.Revision + 1 || row.Evidence.ObservedGameTime != proposal.GameTime)
                return false;
            string digest;
            if (phase == "start" || phase == "fulfill")
            {
                Resident resident;
                try { resident = store.ResolveResident(owner); }
                catch (ArgumentException) { return false; }
                if (sourceUnavailable || resident == null || !resident.IsAlive || resident.IsQuestProtected ||
                    incident == null || incident.People == null || incident.GenerationSerial < 1 || incident.ThreatId == null ||
                    incident.VictimId != null || incident.PlayerAttributed ||
                    !ReferenceEquals(store.ActiveAlarm(owner, proposal.GameTime), incident)) return false;
                var witness = incident.People.Find(x => x.ResidentId == owner);
                if (witness == null || !witness.DirectWitness || !CivilianAlarms.PersonActive(incident, witness, proposal.GameTime)) return false;
                digest = EpisodeDigest(owner, incident);
                if (phase == "start")
                {
                    if (incident.GenerationSerial <= current.LastStartedSafetySerial ||
                        row.Status != NpcMotivationGoalStatus.Active) return false;
                }
                else
                {
                    var previous = current.Goals.Find(x => x.Id == GoalId);
                    if (incident.GenerationSerial != current.LastStartedSafetySerial || previous == null ||
                        previous.Status != NpcMotivationGoalStatus.Active || previous.Evidence == null ||
                        previous.Evidence.EvidenceId != ActiveEvidenceId(digest) ||
                        row.Status != NpcMotivationGoalStatus.Fulfilled || !witness.ReachedRefuge || !witness.RefugeSealed ||
                        witness.Reaction != CivilianReaction.Flee || !witness.HasPosition || witness.BuildingKey < 0) return false;
                }
            }
            else if (phase == "abandon")
            {
                if (incident != null || row.Status != NpcMotivationGoalStatus.Abandoned) return false;
                var previous = current.Goals.Find(x => x.Id == GoalId);
                Resident resident;
                try { resident = store.ResolveResident(owner); }
                catch (ArgumentException) { resident = null; }
                bool unavailable = resident == null || !resident.IsAlive || resident.IsQuestProtected;
                if (previous == null || previous.Status != NpcMotivationGoalStatus.Active || previous.Evidence == null ||
                    !TryReadActiveDigest(previous.Evidence.EvidenceId, out digest) ||
                    !unavailable && !sourceUnavailable && store.HasActiveNpcSafetyEpisode(owner, digest, proposal.GameTime)) return false;
            }
            else return false;
            string expectedEvidence = phase == "start" ? ActiveEvidenceId(digest) :
                phase == "fulfill" ? FulfilledEvidenceId(digest) : AbandonedEvidenceId(digest);
            return proposal.OperationId == OperationId(phase, digest) && row.Evidence.EvidenceId == expectedEvidence;
        }

        private static bool ValidTime(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>Checks a native Safety phase without copying the owner's accepted-update history.</summary>
        internal bool HasNativeSafetyGoal(string ownerPersonId, string evidenceId, NpcMotivationGoalStatus status)
        {
            if (string.IsNullOrWhiteSpace(ownerPersonId) || string.IsNullOrWhiteSpace(evidenceId)) return false;
            NpcMotivationState saved = FindNpcMotivation(ownerPersonId);
            if (saved == null || saved.Goals == null) return false;
            foreach (NpcMotivationGoalState goal in saved.Goals)
                if (goal != null && goal.Id == NpcNativeSafetyGoalLifecycle.GoalId &&
                    goal.Kind == NpcMotivationGoalKind.Safety && goal.Status == status &&
                    goal.KnowledgeState == NpcMotivationKnowledgeState.Known && goal.Evidence != null &&
                    goal.Evidence.Kind == NpcMotivationStateEvidenceKind.NativeObservation &&
                    string.Equals(goal.Evidence.EvidenceId, evidenceId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Checks the one native Safety row without copying the owner's accepted-update history.</summary>
        internal bool HasActiveNpcSafetyGoal(string ownerPersonId)
        {
            if (string.IsNullOrWhiteSpace(ownerPersonId)) return false;
            NpcMotivationState saved = FindNpcMotivation(ownerPersonId);
            if (saved == null || saved.Goals == null) return false;
            foreach (NpcMotivationGoalState goal in saved.Goals)
                if (goal != null && goal.Id == NpcNativeSafetyGoalLifecycle.GoalId &&
                    goal.Kind == NpcMotivationGoalKind.Safety && goal.Status == NpcMotivationGoalStatus.Active &&
                    goal.KnowledgeState == NpcMotivationKnowledgeState.Known && goal.Evidence != null &&
                    goal.Evidence.Kind == NpcMotivationStateEvidenceKind.NativeObservation) return true;
            return false;
        }

        /// <summary>Checks the exact source episode for a persisted safety goal, not an unrelated active alarm.</summary>
        internal bool HasActiveNpcSafetyEpisode(string ownerPersonId, string episodeDigest, double now)
        {
            if (string.IsNullOrWhiteSpace(ownerPersonId) || string.IsNullOrWhiteSpace(episodeDigest) ||
                double.IsNaN(now) || double.IsInfinity(now) || now < 0 || state.AlarmIncidents == null) return false;
            foreach (CivilianIncident incident in state.AlarmIncidents)
            {
                if (incident == null || incident.ThreatId == null || incident.VictimId != null || incident.PlayerAttributed ||
                    !string.Equals(NpcNativeSafetyGoalLifecycle.EpisodeDigest(ownerPersonId, incident), episodeDigest, StringComparison.Ordinal) ||
                    incident.People == null) continue;
                AlarmPerson witness = incident.People.Find(person => person != null && person.ResidentId == ownerPersonId);
                if (witness != null && witness.DirectWitness && CivilianAlarms.PersonActive(incident, witness, now)) return true;
            }
            return false;
        }
    }
}
