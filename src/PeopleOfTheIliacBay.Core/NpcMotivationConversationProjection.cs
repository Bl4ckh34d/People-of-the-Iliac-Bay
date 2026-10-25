using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Builds motivation context only from sources already bound to one native conversation snapshot.</summary>
    public static class NpcMotivationConversationProjection
    {
        public static bool TryCapture(ConversationContextSnapshot conversation, NpcPersonalityProfile savedProfile,
            out NpcMotivationContextSnapshot motivation)
        {
            return TryCaptureCore(conversation, savedProfile, null, null, null, null, out motivation);
        }

        /// <summary>
        /// Captures persisted agenda and the currently funded service duties into the same bound context as
        /// native conversation commitments. The store owns persisted needs/goals/priorities; this projection
        /// supplies only transient duties that are still current at the captured native game time.
        /// </summary>
        public static bool TryCapture(ConversationContextSnapshot conversation, NpcPersonalityProfile savedProfile,
            WorldStateStore store, out NpcMotivationContextSnapshot motivation)
        {
            if (store == null)
            { motivation = null; return false; }
            NpcServiceContractLedger serviceContracts;
            try { serviceContracts = store.ReadNpcServiceContracts(); }
            catch (InvalidOperationException) { motivation = null; return false; }
            return TryCaptureCore(conversation, savedProfile, store, serviceContracts, null, null, out motivation);
        }

        internal static bool TryCapture(ConversationContextSnapshot conversation, NpcPersonalityProfile savedProfile,
            WorldStateStore store, NpcMotivationDuty verifiedWorkDuty, string verifiedWorkSiteId,
            out NpcMotivationContextSnapshot motivation)
        {
            if (store == null)
            { motivation = null; return false; }
            NpcServiceContractLedger serviceContracts;
            try { serviceContracts = store.ReadNpcServiceContracts(); }
            catch (InvalidOperationException) { motivation = null; return false; }
            return TryCapture(conversation, savedProfile, store, serviceContracts,
                verifiedWorkDuty, verifiedWorkSiteId, out motivation);
        }

        internal static bool TryCapture(ConversationContextSnapshot conversation, NpcPersonalityProfile savedProfile,
            WorldStateStore store, NpcServiceContractLedger serviceContracts,
            out NpcMotivationContextSnapshot motivation)
        {
            if (store == null || serviceContracts == null)
            { motivation = null; return false; }
            return TryCaptureCore(conversation, savedProfile, store, serviceContracts, null, null, out motivation);
        }

        internal static bool TryCapture(ConversationContextSnapshot conversation, NpcPersonalityProfile savedProfile,
            WorldStateStore store, NpcServiceContractLedger serviceContracts, NpcMotivationDuty verifiedWorkDuty,
            string verifiedWorkSiteId, out NpcMotivationContextSnapshot motivation)
        {
            if (store == null || serviceContracts == null)
            { motivation = null; return false; }
            return TryCaptureCore(conversation, savedProfile, store, serviceContracts,
                verifiedWorkDuty, verifiedWorkSiteId, out motivation);
        }

        private static bool TryCaptureCore(ConversationContextSnapshot conversation, NpcPersonalityProfile savedProfile,
            WorldStateStore store, NpcServiceContractLedger serviceContracts, NpcMotivationDuty verifiedWorkDuty,
            string verifiedWorkSiteId,
            out NpcMotivationContextSnapshot motivation)
        {
            motivation = null;
            if (conversation == null || savedProfile == null || conversation.Binding == null ||
                conversation.Binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                string.IsNullOrWhiteSpace(conversation.Binding.PersonId) ||
                !string.Equals(savedProfile.PersonId, conversation.Binding.PersonId, StringComparison.Ordinal))
                return false;

            try
            {
                NpcPersonalityProfile profile = NpcPersonalityProfile.CopyValidated(savedProfile);
                string branch = conversation.MotivationBranchId;
                string observer = conversation.Binding.PersonId;
                double now = conversation.CapturedGameTime;

                // The game clock has no independent ledger revision. Its exact IEEE-754
                // timestamp is the revision token; the evidence ID keeps the readable value.
                long clockRevision = BitConverter.DoubleToInt64Bits(now);
                var clockEvidence = Evidence(branch, observer, "world-clock:" + Number(now),
                    NpcMotivationEvidenceKind.WorldClock, clockRevision, now);
                var clock = new NpcMotivationGameClock(now, clockRevision, clockEvidence);
                var profileEvidence = Evidence(branch, observer,
                    "personality:" + observer + ":v" + profile.Version.ToString(CultureInfo.InvariantCulture),
                    NpcMotivationEvidenceKind.PersonalityProfile, profile.Version, now);
                var profileReference = new NpcMotivationProfileReference(observer, profile.Version, profileEvidence);

                NpcMotivationRelationshipReference relationshipReference = null;
                NpcDirectedRelationshipView relationship = conversation.DirectedRelationship;
                if (relationship != null && string.Equals(relationship.ObserverId, observer, StringComparison.Ordinal))
                {
                    var relationshipEvidence = Evidence(branch, observer,
                        "relationship:" + observer + ":" + relationship.TargetId,
                        NpcMotivationEvidenceKind.RelationshipLedger, relationship.Revision, now);
                    relationshipReference = new NpcMotivationRelationshipReference(observer, relationship.TargetId,
                        relationship.Revision, relationshipEvidence);
                }

                var duties = new List<NpcMotivationDuty>();
                foreach (ConversationContextCommitment commitment in conversation.Commitments)
                {
                    NpcMotivationDuty duty;
                    if (TryBuildDuty(branch, observer, now, commitment, out duty)) duties.Add(duty);
                }

                if (serviceContracts != null)
                    AddCurrentServiceDuties(serviceContracts, branch, observer, now, duties);
                if (IsCurrentKeeperWorkDuty(conversation, observer, now, verifiedWorkDuty, verifiedWorkSiteId))
                    duties.Add(verifiedWorkDuty);

                motivation = store == null
                    ? NpcMotivationContextSnapshot.Capture(branch, observer, conversation.Revision,
                        clock, profileReference, relationshipReference, null, null, null, null, null, null, duties)
                    : store.CaptureNpcMotivationContext(conversation, clock, profileReference, relationshipReference,
                        null, null, null, duties);
                return true;
            }
            catch (ArgumentException) { motivation = null; return false; }
            catch (OverflowException) { motivation = null; return false; }
        }

        private static bool IsCurrentKeeperWorkDuty(ConversationContextSnapshot conversation, string observer,
            double now, NpcMotivationDuty duty, string workSiteId)
        {
            if (conversation == null || conversation.Binding == null || duty == null ||
                conversation.Binding.ActorCategory != ConversationActorCategory.Static ||
                string.IsNullOrWhiteSpace(workSiteId) || workSiteId.Length > 128 ||
                !string.Equals(workSiteId, NativeBuildingSiteId(observer), StringComparison.Ordinal) ||
                !string.Equals(duty.Id, "work:" + observer + ":" + workSiteId, StringComparison.Ordinal) ||
                duty.Kind != NpcMotivationDutyKind.Work ||
                duty.Scope != (NpcMotivationDutyScope.RoutineWork | NpcMotivationDutyScope.Schedule) ||
                duty.Status != NpcMotivationDutyStatus.Active || !duty.EndsAtGameTime.HasValue ||
                duty.StartsAtGameTime > now || now >= duty.EndsAtGameTime.Value)
                return false;

            ConversationDialogueRole role;
            if (conversation.Role == null || conversation.Role.Source != ConversationContextFactSource.NativeActor ||
                conversation.Role.ObservedAt != now || !conversation.Role.TryGetVerified(out role) ||
                (role != ConversationDialogueRole.Shopkeeper && role != ConversationDialogueRole.Innkeeper))
                return false;

            NpcMotivationEvidenceRef evidence = duty.Evidence;
            return evidence != null && evidence.Kind == NpcMotivationEvidenceKind.NativeObservation &&
                evidence.Revision == duty.Revision && evidence.ObservedGameTime == now &&
                string.Equals(evidence.SaveBranchId, conversation.MotivationBranchId, StringComparison.Ordinal) &&
                string.Equals(evidence.ObserverId, observer, StringComparison.Ordinal) &&
                string.Equals(evidence.EvidenceId, "native-work:" + workSiteId, StringComparison.Ordinal);
        }

        private static string NativeBuildingSiteId(string personId)
        {
            if (string.IsNullOrWhiteSpace(personId)) return null;
            string[] parts = personId.Split(':');
            int settlement, building, hash;
            if (parts.Length != 4 || !string.Equals(parts[0], "npc", StringComparison.Ordinal) ||
                !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out settlement) ||
                !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out building) ||
                !int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out hash) ||
                settlement < 0 || building < 0 ||
                !string.Equals(personId, "npc:" + settlement.ToString(CultureInfo.InvariantCulture) + ":" +
                    building.ToString(CultureInfo.InvariantCulture) + ":" + hash.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)) return null;
            return "building:" + settlement.ToString(CultureInfo.InvariantCulture) + ":" +
                building.ToString(CultureInfo.InvariantCulture);
        }

        internal static void AddCurrentServiceDuties(NpcServiceContractLedger ledger, string branch,
            string observer, double now, List<NpcMotivationDuty> duties)
        {
            foreach (NpcServiceContract contract in ledger.Contracts)
            {
                if (contract == null || !string.Equals(contract.NpcId, observer, StringComparison.Ordinal)) continue;

                NpcMotivationDutyStatus status;
                double start;
                double? end;
                if (contract.Status == NpcServiceContractStatus.Reserved)
                {
                    status = NpcMotivationDutyStatus.Planned;
                    start = contract.AcceptedAt;
                    end = null;
                }
                else if (contract.Status == NpcServiceContractStatus.Active ||
                    contract.Status == NpcServiceContractStatus.Suspended)
                {
                    // A saved active/suspended row can outlive its paid block until the native adapter
                    // settles or renews it. Do not present expired service as a current obligation.
                    if (now < contract.BlockStartAt || now >= contract.BlockEndAt) continue;
                    status = contract.Status == NpcServiceContractStatus.Active
                        ? NpcMotivationDutyStatus.Active : NpcMotivationDutyStatus.Suspended;
                    start = contract.BlockStartAt;
                    end = contract.BlockEndAt;
                }
                else continue;

                NpcMotivationDutyScope scope = ScopeFor(contract.Coverage);
                if (scope == NpcMotivationDutyScope.None) continue;
                string id = ServiceDutyId(contract.ContractId);
                var evidence = Evidence(branch, observer, "service-contract:" + contract.ContractId,
                    NpcMotivationEvidenceKind.CommitmentLedger, contract.Revision, now);
                duties.Add(new NpcMotivationDuty(id, NpcMotivationDutyKind.Service, scope,
                    status, start, end, contract.Revision, evidence));
            }
        }

        private static NpcMotivationDutyScope ScopeFor(NpcServiceCoverage coverage)
        {
            NpcMotivationDutyScope scope = NpcMotivationDutyScope.None;
            if ((coverage & (NpcServiceCoverage.Follow | NpcServiceCoverage.Travel)) != 0)
                scope |= NpcMotivationDutyScope.Travel;
            if ((coverage & (NpcServiceCoverage.Combat | NpcServiceCoverage.Expedition)) != 0)
                scope |= NpcMotivationDutyScope.Combat;
            if ((coverage & NpcServiceCoverage.Expedition) != 0)
                scope |= NpcMotivationDutyScope.Travel;
            if ((coverage & NpcServiceCoverage.Rest) != 0)
                scope |= NpcMotivationDutyScope.Schedule;
            if ((coverage & NpcServiceCoverage.Resupply) != 0)
                scope |= NpcMotivationDutyScope.Assistance;
            return scope;
        }

        private static string ServiceDutyId(string contractId)
        {
            const string prefix = "service:";
            string direct = prefix + contractId;
            if (direct.Length <= 128) return direct;
            // Preserve a deterministic distinct ID within the context's bounded identifier size.
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < contractId.Length; i++)
                { hash ^= contractId[i]; hash *= 1099511628211UL; }
                return prefix + hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        internal static bool TryBuildDuty(string branch, string observer, double observedAt,
            ConversationContextCommitment commitment, out NpcMotivationDuty duty)
        {
            duty = null;
            if (commitment == null || !commitment.StartsAtGameTime.HasValue) return false;

            NpcMotivationDutyKind kind;
            NpcMotivationDutyScope scope;
            if (string.Equals(commitment.Kind, "appointment", StringComparison.OrdinalIgnoreCase))
            {
                // Attended meetings remain available through their agreed end,
                // but a saved outcome is not a current duty forever afterward.
                if (EqualsStatus(commitment.Status, "attended") &&
                    commitment.EndsAtGameTime.HasValue &&
                    observedAt > commitment.EndsAtGameTime.Value) return false;
                kind = NpcMotivationDutyKind.Appointment;
                scope = NpcMotivationDutyScope.Schedule;
            }
            else if (string.Equals(commitment.Kind, "task", StringComparison.OrdinalIgnoreCase))
            {
                kind = NpcMotivationDutyKind.Personal;
                scope = NpcMotivationDutyScope.Assistance;
            }
            else if (string.Equals(commitment.Kind, "action-task", StringComparison.OrdinalIgnoreCase) &&
                commitment.ActionKind.HasValue)
            {
                kind = NpcMotivationDutyKind.Personal;
                scope = ScopeFor(commitment.ActionKind.Value);
            }
            else return false;

            NpcMotivationDutyStatus status;
            if (!TryStatus(commitment.Status, commitment.Kind, out status)) return false;
            var source = Evidence(branch, observer,
                "commitment:" + commitment.Kind + ":" + commitment.Id,
                NpcMotivationEvidenceKind.CommitmentLedger, commitment.Revision, observedAt);
            duty = new NpcMotivationDuty(commitment.Id, kind, scope, status,
                commitment.StartsAtGameTime.Value, commitment.EndsAtGameTime, commitment.Revision, source);
            return true;
        }

        private static NpcMotivationDutyScope ScopeFor(NpcActionKind kind)
        {
            switch (kind)
            {
                case NpcActionKind.MoveTo:
                case NpcActionKind.Follow:
                case NpcActionKind.Climb:
                    return NpcMotivationDutyScope.Travel;
                case NpcActionKind.Attack:
                case NpcActionKind.CastSpell:
                    return NpcMotivationDutyScope.Combat;
                case NpcActionKind.Wait:
                case NpcActionKind.TakeSeat:
                case NpcActionKind.ChangeSchedule:
                    return NpcMotivationDutyScope.Schedule;
                case NpcActionKind.Interact:
                case NpcActionKind.Purchase:
                case NpcActionKind.Transfer:
                case NpcActionKind.Lockpick:
                    return NpcMotivationDutyScope.Assistance;
                default:
                    return NpcMotivationDutyScope.None;
            }
        }

        private static bool TryStatus(string status, string kind, out NpcMotivationDutyStatus result)
        {
            result = default(NpcMotivationDutyStatus);
            if (string.Equals(kind, "appointment", StringComparison.OrdinalIgnoreCase))
            {
                if (EqualsStatus(status, "accepted")) result = NpcMotivationDutyStatus.Accepted;
                else if (EqualsStatus(status, "waiting") || EqualsStatus(status, "attended")) result = NpcMotivationDutyStatus.Active;
                else return false;
                return true;
            }
            if (EqualsStatus(status, "proposed")) result = NpcMotivationDutyStatus.Planned;
            else if (EqualsStatus(status, "ready") || EqualsStatus(status, "accepted")) result = NpcMotivationDutyStatus.Accepted;
            else if (EqualsStatus(status, "approaching") || EqualsStatus(status, "executing") || EqualsStatus(status, "running"))
                result = NpcMotivationDutyStatus.Active;
            else if (EqualsStatus(status, "completed")) result = NpcMotivationDutyStatus.Fulfilled;
            else if (EqualsStatus(status, "cancelled")) result = NpcMotivationDutyStatus.Cancelled;
            else return false;
            return true;
        }

        private static bool EqualsStatus(string actual, string expected)
        { return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase); }

        private static NpcMotivationEvidenceRef Evidence(string branch, string observer, string id,
            NpcMotivationEvidenceKind kind, long revision, double observedAt)
        { return new NpcMotivationEvidenceRef(branch, observer, id, kind, revision, observedAt); }

        private static string Number(double value)
        { return value.ToString("R", CultureInfo.InvariantCulture); }
    }
}
