using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>Current accepted native-outcome relationship ledger revision.</summary>
        public long NpcRelationshipLedgerRevision
        {
            get { return state.NpcRelationships.Revision; }
        }

        /// <summary>Reads a saved directed relation without creating one for missing or unknown identities.</summary>
        public NpcDirectedRelationshipView ReadNpcRelationship(string observerId, string targetId)
        {
            bool alive;
            if (!KnownNpcIdentity(state, observerId, out alive)) return null;
            if (!string.Equals(targetId, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal) &&
                !KnownNpcIdentity(state, targetId, out alive)) return null;
            var relationship = state.NpcRelationships.Read(observerId, targetId);
            if (relationship == null || !string.Equals(targetId, NpcDirectedRelationship.PlayerTargetId,
                StringComparison.Ordinal)) return relationship;

            NpcRoleState role = ReadNpcRole(observerId);
            return relationship.WithPlayerFearPolicy(NpcRolePolicy.SuppressPlayerFear(role),
                role == null ? -1 : role.Revision);
        }

        /// <summary>Reads detached relationship view with fear and resentment projected at supplied game time.</summary>
        public NpcDirectedRelationshipView ReadNpcRelationship(string observerId, string targetId, double now)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            var relationship = ReadNpcRelationship(observerId, targetId);
            if (relationship == null) return null;
            var personality = ActiveNpcPersonality(state, observerId);
            return relationship.ProjectRecovery(now, personality, NpcFearRecoveryPolicy.ProvisionalDefault,
                NpcResentmentRecoveryPolicy.ProvisionalDefault);
        }

        /// <summary>
        /// Native caller-only commit boundary. The external event adapter remains responsible for proving that
        /// evidence belongs to this world and for authorizing the appraisal; this store enforces known identities,
        /// revisions, exactly-once outcome IDs, and atomic relationship persistence.
        /// </summary>
        public bool TryCommitNpcRelationship(NpcRelationshipLedgerUpdateProposal proposal, long expectedLedgerRevision)
        {
            if (proposal == null || proposal.ExpectedLedgerRevision != expectedLedgerRevision) return false;
            if (string.Equals(proposal.TargetId, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal) &&
                proposal.RelationshipUpdate != null && proposal.RelationshipUpdate.InterpersonalFearDelta > 0 &&
                NpcRolePolicy.SuppressPlayerFear(ReadNpcRole(proposal.ObserverId))) return false;
            NpcRelationshipLedger updated;
            var personality = proposal == null ? null :
                ActiveNpcPersonality(state, proposal.ObserverId);
            if (!NpcRelationshipLedger.TryApply(state.NpcRelationships, proposal, IsKnownNpcRelationshipIdentity,
                personality, NpcFearRecoveryPolicy.ProvisionalDefault,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated)) return false;
            state.NpcRelationships = updated;
            return true;
        }

        private bool IsKnownNpcRelationshipIdentity(string id)
        {
            bool alive;
            return KnownNpcIdentity(state, id, out alive);
        }

        private static NpcRelationshipLedger CopyNpcRelationships(WorldState source, WorldState validated)
        {
            Require(source.NpcRelationships != null, "Missing NPC relationship ledger.");
            return NpcRelationshipLedger.CopyValidated(source.NpcRelationships,
                id => KnownNpcIdentity(validated, id, out _),
                id => ActiveNpcPersonality(validated, id),
                NpcFearRecoveryPolicy.ProvisionalDefault);
        }
    }
}
