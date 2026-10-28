using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Opaque update capability captured from one live WorldStateStore binding.</summary>
    public sealed class NpcMotivationUpdateTicket
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal string ScopeId;
        public string OwnerPersonId { get; internal set; }
        public long ExpectedRevision { get; internal set; }
    }

    public sealed partial class WorldStateStore
    {
        private enum MotivationCommitSource { Unclassified, NativeHealth, NativeSafety }
        /// <summary>
        /// Reads a detached immutable view. Missing state is represented by an ephemeral empty ledger and is not
        /// allocated into the save. Unknown identities return null.
        /// </summary>
        public NpcMotivationStateView ReadNpcMotivationState(string personId)
        {
            if (!IsKnownNpcExperienceIdentity(personId)) return null;
            NpcMotivationState saved = FindNpcMotivation(personId);
            return (saved == null ? NpcMotivationState.Create(personId) : NpcMotivationState.CopyValidated(saved)).ReadOnlyView();
        }

        /// <summary>Captures an owner-, scope-, epoch- and revision-bound write ticket without allocating an empty row.</summary>
        public bool TryCaptureNpcMotivationUpdate(string personId, out NpcMotivationUpdateTicket ticket)
        {
            ticket = null;
            if (!IsKnownNpcExperienceIdentity(personId) || string.IsNullOrWhiteSpace(NpcFrameworkScopeId)) return false;
            NpcMotivationState saved = FindNpcMotivation(personId);
            NpcMotivationState detached;
            try { detached = saved == null ? NpcMotivationState.Create(personId) : NpcMotivationState.CopyValidated(saved); }
            catch (ArgumentException) { return false; }
            ticket = new NpcMotivationUpdateTicket { Owner = this, Epoch = bindingEpoch, ScopeId = NpcFrameworkScopeId,
                OwnerPersonId = personId, ExpectedRevision = detached.Revision };
            return true;
        }

        /// <summary>
        /// Commits one typed owner-authored state update. All copies and validation finish before the save list is
        /// replaced, so stale tickets, replays, invalid provenance and copy failures leave it unchanged.
        /// </summary>
        public bool TryCommitNpcMotivationUpdate(NpcMotivationUpdateTicket ticket, NpcMotivationStateUpdateProposal proposal)
        { return TryCommitNpcMotivationUpdates(ticket, new[] { proposal }, MotivationCommitSource.Unclassified, default(NpcArchetype), null, null); }

        /// <summary>
        /// Publishes exactly two ordered updates together. Both candidate states and every saved row are
        /// validated before the motivation list changes; a rejected second update leaves the first unapplied.
        /// </summary>
        public bool TryCommitNpcMotivationUpdatePair(NpcMotivationUpdateTicket ticket,
            NpcMotivationStateUpdateProposal first, NpcMotivationStateUpdateProposal second)
        { return TryCommitNpcMotivationUpdates(ticket, new[] { first, second }, MotivationCommitSource.Unclassified, default(NpcArchetype), null, null); }

        public bool TryCommitNativeHealthMotivationUpdate(NpcMotivationUpdateTicket ticket, NpcArchetype archetype,
            NpcMotivationStateUpdateProposal proposal)
        { return TryCommitNpcMotivationUpdates(ticket, new[] { proposal }, MotivationCommitSource.NativeHealth, archetype, null, null); }

        public bool TryCommitNativeHealthMotivationUpdatePair(NpcMotivationUpdateTicket ticket, NpcArchetype archetype,
            NpcMotivationStateUpdateProposal first, NpcMotivationStateUpdateProposal second)
        { return TryCommitNpcMotivationUpdates(ticket, new[] { first, second }, MotivationCommitSource.NativeHealth, archetype, null, null); }

        internal bool TryCommitNativeSafetyMotivationUpdate(NpcMotivationUpdateTicket ticket, CivilianIncident incident,
            string phase, bool sourceUnavailable, NpcMotivationStateUpdateProposal proposal)
        { return TryCommitNpcMotivationUpdates(ticket, new[] { proposal }, MotivationCommitSource.NativeSafety, default(NpcArchetype), incident, phase, sourceUnavailable); }

        private bool TryCommitNpcMotivationUpdates(NpcMotivationUpdateTicket ticket,
            NpcMotivationStateUpdateProposal[] proposals, MotivationCommitSource source,
            NpcArchetype archetype, CivilianIncident incident, string phase, bool sourceUnavailable = false)
        {
            if (ticket == null || proposals == null || proposals.Length < 1 || proposals.Length > 2 ||
                ticket.Owner != this || !ReferenceEquals(ticket.Epoch, bindingEpoch) ||
                !string.Equals(ticket.ScopeId, NpcFrameworkScopeId, StringComparison.Ordinal) ||
                !IsKnownNpcExperienceIdentity(ticket.OwnerPersonId)) return false;
            for (int i = 0; i < proposals.Length; i++)
                if (proposals[i] == null ||
                    !string.Equals(ticket.OwnerPersonId, proposals[i].OwnerPersonId, StringComparison.Ordinal) ||
                    proposals[i].ExpectedRevision != ticket.ExpectedRevision + i) return false;

            NpcMotivationState current = FindNpcMotivation(ticket.OwnerPersonId);
            if (current == null) current = NpcMotivationState.Create(ticket.OwnerPersonId);
            else
            {
                try { current = NpcMotivationState.CopyValidated(current); }
                catch (ArgumentException) { return false; }
            }
            if (current.Revision != ticket.ExpectedRevision || current.Revision > long.MaxValue - proposals.Length ||
                (current.Version == NpcMotivationState.VersionCurrent && source == MotivationCommitSource.Unclassified)) return false;
            NpcVitalsView vitals = null;
            if (source == MotivationCommitSource.NativeHealth)
            {
                if (!Enum.IsDefined(typeof(NpcArchetype), archetype)) return false;
                Resident resident;
                try { resident = ResolveResident(ticket.OwnerPersonId); }
                catch (ArgumentException) { return false; }
                if (resident == null || !resident.IsAlive || resident.IsQuestProtected ||
                    resident.AgeGroup != ResidentAgeGroup.Adult) return false;
                try { vitals = ReadNpcVitals(ticket.OwnerPersonId, archetype); }
                catch (ArgumentException) { return false; }
                if (vitals == null || !vitals.IsAlive || vitals.Revision < 1 ||
                    !NpcHealthNeedLifecycle.IsExpectedNativeSequence(current, vitals, proposals)) return false;
                foreach (var proposal in proposals)
                {
                    if (!NpcHealthNeedLifecycle.IsExactNativeProposal(proposal, vitals)) return false;
                    if (proposal.ChangeKind == NpcMotivationStateChangeKind.Need && vitals.Revision <= current.LastHealthNeedVitalsRevision ||
                        proposal.ChangeKind == NpcMotivationStateChangeKind.Goal && vitals.Revision <= current.LastHealthGoalVitalsRevision)
                        return false;
                }
            }
            if (source == MotivationCommitSource.NativeSafety &&
                (proposals.Length != 1 || !NpcNativeSafetyGoalLifecycle.IsExactNativeCommit(this, ticket.OwnerPersonId,
                    current, incident, phase, sourceUnavailable, proposals[0]))) return false;
            NpcMotivationState updated = current;
            var pins = new HashSet<string>(StringComparer.Ordinal);
            if (state.NpcTasks != null)
                foreach (var task in state.NpcTasks)
                    if (task != null && task.ResidentId == ticket.OwnerPersonId && task.GoalId != null && task.GoalStartRevision > 0)
                        pins.Add(NpcMotivationState.OriginKey(task.GoalId, task.GoalStartRevision));
            foreach (var proposal in proposals)
            {
                NpcMotivationState candidate;
                if (!NpcMotivationState.TryApply(updated, proposal, pins, out candidate)) return false;
                if (source == MotivationCommitSource.NativeHealth)
                {
                    if (proposal.ChangeKind == NpcMotivationStateChangeKind.Need) candidate.LastHealthNeedVitalsRevision = vitals.Revision;
                    else candidate.LastHealthGoalVitalsRevision = vitals.Revision;
                }
                if (source == MotivationCommitSource.NativeSafety)
                {
                    if (phase == "start")
                    {
                        candidate.LastStartedSafetySerial = incident.GenerationSerial;
                        candidate.LastStartedSafetyDigest = NpcNativeSafetyGoalLifecycle.EpisodeDigest(ticket.OwnerPersonId, incident);
                    }
                    else candidate.LastClosedSafetyDigest = current.LastStartedSafetyDigest;
                }
                try { updated = NpcMotivationState.CopyValidated(candidate); }
                catch (ArgumentException) { return false; }
            }

            List<NpcMotivationState> next;
            try
            {
                next = CopyNpcMotivationRowsForCommit(state.NpcMotivations);
                int index = next.FindIndex(x => string.Equals(x.OwnerPersonId, ticket.OwnerPersonId, StringComparison.Ordinal));
                if (index < 0) next.Add(updated);
                else next[index] = updated;
            }
            catch (ArgumentException) { return false; }

            // Single publication point. No callback or fallible operation follows this assignment.
            state.NpcMotivations = next;
            return true;
        }

        /// <summary>
        /// Captures persistent agenda state into the same source-bound model context used by the conversation.
        /// Branch and time come from that exact captured snapshot; all transient constraints are supplied by the
        /// current native adapter and are not persisted in the motivation ledger.
        /// </summary>
        public NpcMotivationContextSnapshot CaptureNpcMotivationContext(ConversationContextSnapshot conversation,
            NpcMotivationGameClock clock, NpcMotivationProfileReference profile,
            NpcMotivationRelationshipReference relationship,
            IEnumerable<NpcMotivationCapabilityConstraint> capabilities,
            IEnumerable<NpcMotivationResourceConstraint> resources,
            IEnumerable<NpcMotivationThreatObservation> threats,
            IEnumerable<NpcMotivationDuty> duties)
        {
            if (conversation == null || conversation.Binding == null || conversation.Binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                !IsKnownNpcExperienceIdentity(conversation.Binding.PersonId) || clock == null ||
                clock.GameTimeSeconds != conversation.CapturedGameTime || profile == null ||
                !string.Equals(profile.PersonId, conversation.Binding.PersonId, StringComparison.Ordinal))
                throw new ArgumentException("Motivation capture requires a known stable conversation identity and matching captured clock/profile.");

            NpcMotivationState stateRow = FindNpcMotivation(conversation.Binding.PersonId);
            if (stateRow == null) stateRow = NpcMotivationState.Create(conversation.Binding.PersonId);
            else stateRow = NpcMotivationState.CopyValidated(stateRow);
            return NpcMotivationState.CaptureContext(stateRow, conversation.MotivationBranchId, clock, profile,
                relationship, capabilities, resources, threats, duties);
        }

        private NpcMotivationState FindNpcMotivation(string personId)
        {
            if (state.NpcMotivations == null) return null;
            return state.NpcMotivations.Find(x => x != null && string.Equals(x.OwnerPersonId, personId, StringComparison.Ordinal));
        }

        private static List<NpcMotivationState> CopyNpcMotivationRowsForCommit(List<NpcMotivationState> rows)
        {
            if (rows == null) throw new ArgumentException("Missing NPC motivation ledger list.");
            var copy = new List<NpcMotivationState>(rows.Count);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var item = NpcMotivationState.CopyValidated(row);
                if (!identities.Add(item.OwnerPersonId)) throw new ArgumentException("Duplicate NPC motivation owner.");
                copy.Add(item);
            }
            return copy;
        }

        /// <summary>WorldState save-copy hook, called only after identity indexes exist.</summary>
        private static List<NpcMotivationState> CopyNpcMotivations(WorldState source, WorldState validated)
        {
            var result = new List<NpcMotivationState>();
            Require(source.NpcMotivations != null, "Missing NPC motivation state list.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in source.NpcMotivations)
            {
                Require(saved != null, "Null NPC motivation state.");
                Require(!saved.Needs.Exists(n=>n.Id=="need:observed-meal-due") && !saved.Goals.Exists(g=>g.Id=="goal:meal"),
                    "This development state contains a removed need; start with fresh mod data.");
                var item = NpcMotivationState.CopyValidated(saved);
                Require(item.Version == NpcMotivationState.VersionCurrent,
                    "Motivation state version does not match save schema.");
                var pins = new HashSet<string>(StringComparer.Ordinal);
                if (source.NpcTasks != null)
                    foreach (var task in source.NpcTasks)
                        if (task != null && task.ResidentId == item.OwnerPersonId && task.GoalId != null && task.GoalStartRevision > 0)
                            pins.Add(NpcMotivationState.OriginKey(task.GoalId, task.GoalStartRevision));
                NpcMotivationState.ValidateOriginAnchors(item, pins);
                foreach (var incident in validated.AlarmIncidents)
                {
                    if (incident.ThreatId == null) continue;
                    string digest = NpcNativeSafetyGoalLifecycle.EpisodeDigest(item.OwnerPersonId, incident);
                    string startOperation = "motivation:native-safety:start:" + digest;
                    string evidencePrefix = "native-safety:v1:" + digest + ":";
                    bool retainedStart = item.AcceptedUpdates.Exists(x => x.OperationId == startOperation) ||
                        item.LatestRowAnchors.Exists(x => x.OperationId == startOperation) ||
                        item.GoalOriginAnchors.Exists(x => x.OperationId == startOperation);
                    bool currentEpisode = item.Goals.Exists(x => x.Id == NpcNativeSafetyGoalLifecycle.GoalId &&
                        x.Evidence != null && x.Evidence.EvidenceId.StartsWith(evidencePrefix, StringComparison.Ordinal));
                    Require(!retainedStart && !currentEpisode || item.LastStartedSafetySerial >= incident.GenerationSerial,
                        "Native safety cursor predates its retained incident.");
                }
                Require(identities.Add(item.OwnerPersonId), "Duplicate NPC motivation owner.");
                bool alive;
                Require(KnownNpcIdentity(validated, item.OwnerPersonId, out alive), "NPC motivation state references an unknown NPC.");
                result.Add(item);
            }
            return result;
        }

    }
}
